using System.Text;
using System.Text.Json;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Paths;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Leave;

public sealed class LeaveBalanceImportValidationException(string message) : Exception(message);

public sealed class LeaveBalanceImportUncertainException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Reads a Xero "Leave Balances" CSV export from disk and imports it into TimePro.
///
/// The file is read here rather than passed around as text so the MCP tool can take a path:
/// tool arguments travel through the model's context, where a large CSV is both expensive and
/// liable to be silently truncated. The CSV itself is parsed server-side and never stored.
/// </summary>
public sealed class LeaveBalanceImportService
{
    private const string UncertainImportMessage =
        "The import request was sent and may have been applied, but TimePro did not return a complete result. "
        + "Run 'tp leave balances status' before retrying.";

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Guard against a caller pointing at the wrong file entirely (a database dump, a video).
    /// A real Xero balance export for a company of any plausible size is far below this.
    /// </summary>
    internal const int MaxCsvBytes = 5 * 1024 * 1024;

    private readonly ITimeProApiClient _api;

    public LeaveBalanceImportService(ITimeProApiClient api) => _api = api;

    /// <summary>
    /// Reads and sanity-checks the CSV at <paramref name="csvPath"/>, throwing
    /// <see cref="LeaveBalanceImportValidationException"/> before any network call if it is
    /// missing, empty, oversized, or plainly not a CSV.
    /// </summary>
    public string ReadCsv(string csvPath)
    {
        if (string.IsNullOrWhiteSpace(csvPath))
            throw new LeaveBalanceImportValidationException("A path to the Xero leave balances CSV export is required.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(PathExpander.ExpandHomeDirectory(csvPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new LeaveBalanceImportValidationException($"'{csvPath}' is not a valid file path.");
        }

        if (Directory.Exists(fullPath))
            throw new LeaveBalanceImportValidationException($"'{fullPath}' is a directory. Provide the path to the CSV file itself.");

        if (!File.Exists(fullPath))
            throw new LeaveBalanceImportValidationException($"File not found: {Describe(fullPath, csvPath)}");

        var length = new FileInfo(fullPath).Length;
        if (length == 0)
            throw new LeaveBalanceImportValidationException($"File is empty: {fullPath}");

        if (length > MaxCsvBytes)
        {
            throw new LeaveBalanceImportValidationException(
                $"File is {length / 1024 / 1024}MB, which is larger than the {MaxCsvBytes / 1024 / 1024}MB limit for a leave balances CSV. Check that '{fullPath}' is the Xero export.");
        }

        string content;
        try
        {
            content = File.ReadAllText(fullPath, StrictUtf8);
        }
        catch (DecoderFallbackException)
        {
            throw new LeaveBalanceImportValidationException(
                $"'{fullPath}' is not valid UTF-8. Export the Xero leave balances report as a UTF-8 CSV and try again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LeaveBalanceImportValidationException($"Could not read {fullPath}: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(content))
            throw new LeaveBalanceImportValidationException($"File contains no data: {fullPath}");

        if (content.Contains('\0'))
        {
            throw new LeaveBalanceImportValidationException(
                $"'{fullPath}' looks like a binary file, not a CSV. Export the Xero leave balances report as CSV first.");
        }

        return content;
    }

    /// <summary>
    /// Reads the CSV and sends it to TimePro. The server owns parsing and matching, so its
    /// rejection messages are surfaced verbatim rather than second-guessed here.
    /// </summary>
    public async Task<ImportLeaveBalancesResult> ImportAsync(string csvPath, CancellationToken ct = default)
    {
        var csv = ReadCsv(csvPath);

        try
        {
            var result = await _api.ImportLeaveBalancesAsync(csv, ct);
            if (result is null || result.AsAtDate == default)
                throw new LeaveBalanceImportUncertainException(UncertainImportMessage);

            // System.Text.Json can replace collection initializers with null when the API sends
            // explicit nulls. Normalize here so every CLI/MCP caller receives the same safe shape.
            result.Warnings ??= [];
            result.UnmatchedEmployees ??= [];
            return result;
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            throw new LeaveBalanceImportValidationException(
                "TimePro authentication failed. Run 'tp login --tenant <id>' again.");
        }
        catch (ApiException ex) when (ex.StatusCode == 403)
        {
            throw new LeaveBalanceImportValidationException(
                "Importing leave balances requires leave admin rights in TimePro, and this account does not have them.");
        }
        catch (ApiException ex) when (ex.StatusCode == 422)
        {
            // The endpoint returns the CSV parser's own message as a bare JSON string.
            throw new LeaveBalanceImportValidationException(
                $"TimePro could not read the CSV: {DescribeUnprocessable(ex.ResponseBody)}");
        }
        catch (LeaveBalanceImportUncertainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new LeaveBalanceImportUncertainException(UncertainImportMessage, ex);
        }
    }

    /// <summary>
    /// Names the resolved path, and the original alongside it when the two differ. A shell that
    /// strips backslashes turns an absolute Windows path into a drive-relative one that resolves
    /// somewhere unexpected, which is impossible to spot from the resolved path alone.
    /// </summary>
    private static string Describe(string fullPath, string originalPath)
    {
        var trimmedOriginal = originalPath.Trim();
        return string.Equals(fullPath, trimmedOriginal, StringComparison.Ordinal)
            ? fullPath
            : $"{fullPath} (resolved from '{trimmedOriginal}')";
    }

    /// <summary>
    /// Unwraps the bare JSON string body returned by the endpoint's 422, falling back to the
    /// shared problem+json parsing for anything else.
    /// </summary>
    internal static string DescribeUnprocessable(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return "the file was rejected without a reason.";

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
                return doc.RootElement.GetString() ?? responseBody;
        }
        catch (JsonException)
        {
            // Not JSON at all — fall through to the shared parser.
        }

        return ApiErrorParser.ExtractDetail(responseBody) ?? responseBody;
    }
}
