using System.ComponentModel;
using System.Globalization;
using System.Text;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Export timesheets to CSV")]
public class ExportCommand : AsyncCommand<ExportCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--from <DATE>")]
        [Description("Start date (yyyy-MM-dd). Defaults to 3 months ago")]
        public string? From { get; set; }

        [CommandOption("--to <DATE>")]
        [Description("End date (yyyy-MM-dd). Defaults to today")]
        public string? To { get; set; }

        [CommandOption("--output <FILE>")]
        [Description("Output file path. Defaults to timesheets-export.csv")]
        public string Output { get; set; } = "timesheets-export.csv";

        [CommandOption("--emp-id|--employee-id|--employee <EMP_ID>")]
        [Description("Export only this employee's rows. Defaults to the current user")]
        public string? EmpId { get; set; }

        [CommandOption("--all")]
        [Description("Keep every employee's rows (the raw server export)")]
        public bool All { get; set; }

        public override ValidationResult Validate() =>
            All && !string.IsNullOrWhiteSpace(EmpId)
                ? ValidationResult.Error("--all exports every employee, so it cannot be combined with --emp-id.")
                : ValidationResult.Success();
    }

    public ExportCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tenant = _config.LoadActiveTenantConfig();

        // Resolved before the fetch: a blank configured employee id must not reach the scoper,
        // where it would silently widen the export to every employee.
        var empId = settings.All
            ? null
            : (string.IsNullOrWhiteSpace(settings.EmpId) ? tenant?.EmployeeId?.Trim() : settings.EmpId.Trim());

        if (!settings.All && string.IsNullOrWhiteSpace(empId))
        {
            OutputHelper.WriteError("No employee id to export. Run 'tp login --tenant <id>' first, or pass --emp-id / --all.");
            return 1;
        }

        var from = settings.From is not null
            ? DateOnly.ParseExact(settings.From, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.Today.AddMonths(-3));

        var to = settings.To is not null
            ? DateOnly.ParseExact(settings.To, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.Today);

        try
        {
            // The export endpoint takes only a date range, so it always returns every
            // employee — scoping to one has to happen on the returned file.
            var csvBytes = await _api.ExportTimesheetsCsvAsync(from, to, cancellationToken);
            var csv = Decode(csvBytes, out var hadBom);
            var scoped = empId is null
                ? TimesheetCsvScoper.Unscoped(csv)
                : TimesheetCsvScoper.Scope(csv, empId);

            // An unfiltered export is written back byte-for-byte: a decode/encode round trip
            // would replace anything that is not valid UTF-8, so it would not be the raw file.
            await File.WriteAllBytesAsync(
                settings.Output,
                scoped.Filtered ? Encode(scoped.Csv, hadBom) : csvBytes,
                cancellationToken);

            if (scoped.Warning is not null)
                OutputHelper.WriteWarning(scoped.Warning);
            else if (!scoped.Filtered)
                OutputHelper.WriteWarning($"Exported every employee's timesheets ({scoped.TotalRows:N0} rows). Drop --all to export only your own.");

            var size = new FileInfo(settings.Output).Length;
            OutputHelper.WriteSuccess($"Exported to {settings.Output} ({size:N0} bytes)");
            OutputHelper.WriteInfo(scoped.Filtered
                ? $"Date range: {from:yyyy-MM-dd} to {to:yyyy-MM-dd} | {empId}: {scoped.KeptRows:N0} of {scoped.TotalRows:N0} rows"
                : $"Date range: {from:yyyy-MM-dd} to {to:yyyy-MM-dd} | {scoped.TotalRows:N0} rows");
            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteApiError(ex, useJson: false);
            return 1;
        }
    }

    private static string Decode(byte[] bytes, out bool hadBom)
    {
        hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return hadBom
            ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
            : Encoding.UTF8.GetString(bytes);
    }

    private static byte[] Encode(string csv, bool withBom)
    {
        var body = Encoding.UTF8.GetBytes(csv);
        if (!withBom)
            return body;

        var result = new byte[body.Length + 3];
        result[0] = 0xEF;
        result[1] = 0xBB;
        result[2] = 0xBF;
        body.CopyTo(result, 3);
        return result;
    }
}
