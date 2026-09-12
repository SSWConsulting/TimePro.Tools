using System.Text.RegularExpressions;

namespace SSW.TimePro.Cli.Infrastructure.Telemetry;

/// <summary>
/// The <c>User-Agent</c> and <c>x-timepro-client-*</c> headers sent on every TimePro API call, so
/// server telemetry can separate CLI from MCP traffic and match a failed <c>tp</c> call to a request.
/// </summary>
public static partial class ClientHeaders
{
    public const string Command = "x-timepro-client-command";
    public const string Surface = "x-timepro-client-surface";
    public const string RequestId = "x-timepro-client-request-id";

    private const string Product = "timepro-cli";

    /// <summary>Response headers checked, in order, for a server-assigned id that wins over ours.</summary>
    private static readonly string[] EchoHeaders = [RequestId, "request-id", "x-request-id"];

    public static string UserAgent(string version) => $"{Product}/{ShortVersion(version)}";

    /// <summary>
    /// Build metadata (<c>+sha</c>) is dropped and anything unusable falls back to <c>0.0.0</c>, so
    /// the User-Agent stays a short, parseable token.
    /// </summary>
    public static string ShortVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "0.0.0";

        var trimmed = version.Trim();
        var plus = trimmed.IndexOf('+');
        if (plus >= 0)
            trimmed = trimmed[..plus];

        return VersionToken().IsMatch(trimmed) ? trimmed : "0.0.0";
    }

    /// <summary>
    /// Prefers the id the server echoed, but only when it is a plain token: it comes from outside
    /// and is printed to the terminal and written to the local log.
    /// </summary>
    public static string ResolveRequestId(HttpResponseMessage response, string fallback)
    {
        foreach (var name in EchoHeaders)
        {
            if (!response.Headers.TryGetValues(name, out var values))
                continue;

            var echoed = values
                .Select(v => v?.Trim())
                .FirstOrDefault(v => !string.IsNullOrEmpty(v) && v.Length <= 128 && RequestIdToken().IsMatch(v));

            if (echoed is not null)
                return echoed;
        }

        return fallback;
    }

    [GeneratedRegex(@"^[A-Za-z0-9.\-]{1,40}$")]
    private static partial Regex VersionToken();

    [GeneratedRegex(@"^[A-Za-z0-9._:\-]+$")]
    private static partial Regex RequestIdToken();
}
