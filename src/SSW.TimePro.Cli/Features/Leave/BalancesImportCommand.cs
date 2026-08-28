using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Leave;

[Description("Import leave balances for all employees from a Xero leave balances CSV export")]
public class BalancesImportCommand : AsyncCommand<BalancesImportCommand.Settings>
{
    private readonly LeaveBalanceImportService _importService;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("Path to the Xero 'Leave Balances' CSV export")]
        public string CsvPath { get; set; } = string.Empty;

        [CommandOption("--yes")]
        [Description("Skip confirmation")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public BalancesImportCommand(LeaveBalanceImportService importService, IConfigService config)
    {
        _importService = importService;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (_config.LoadActiveTenantConfig() is null)
        {
            WriteError(settings.Json, "Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        // Validate the file before prompting so an unusable path fails immediately.
        try
        {
            _importService.ReadCsv(settings.CsvPath);
        }
        catch (LeaveBalanceImportValidationException ex)
        {
            WriteError(settings.Json, ex.Message);
            return 1;
        }

        // The import replaces stored balances for every matched employee and TimePro offers no
        // dry-run, so confirm unless the caller has opted out.
        if (!settings.Yes && !settings.Json)
        {
            AnsiConsole.MarkupLine(
                $"About to import leave balances for [bold]all employees[/] from {Markup.Escape(settings.CsvPath)}.");
            if (!AnsiConsole.Confirm("This replaces the balances currently stored in TimePro. Continue?", false))
                return 1;
        }

        try
        {
            var result = await _importService.ImportAsync(settings.CsvPath, cancellationToken);

            OutputHelper.Render(result, settings.Json, r =>
            {
                var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
                table.AddRow("[bold]Balances as at[/]", r.AsAtDate.ToString("yyyy-MM-dd"));
                table.AddRow("[bold]Created[/]", r.Created.ToString());
                table.AddRow("[bold]Updated[/]", r.Updated.ToString());
                AnsiConsole.Write(table);

                foreach (var warning in r.Warnings)
                    OutputHelper.WriteWarning(warning);

                if (r.UnmatchedEmployees.Count > 0)
                {
                    OutputHelper.WriteWarning(
                        $"{r.UnmatchedEmployees.Count} row(s) were skipped because the name did not match exactly one TimePro employee:");
                    foreach (var name in r.UnmatchedEmployees)
                        AnsiConsole.MarkupLine($"  - {Markup.Escape(name)}");
                }

                OutputHelper.WriteSuccess($"Imported leave balances ({r.Created} created, {r.Updated} updated)");
            });

            return 0;
        }
        catch (LeaveBalanceImportValidationException ex)
        {
            WriteError(settings.Json, ex.Message);
            return 1;
        }
        catch (ApiException ex)
        {
            var detail = ApiErrorParser.ExtractDetail(ex.ResponseBody);
            if (settings.Json)
                OutputHelper.WriteJsonError($"API error: {ex.Message}", ex.StatusCode, detail);
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}"
                + (detail is not null ? $" — {detail}" : ""));
            return 1;
        }
    }

    private static void WriteError(bool json, string message)
    {
        if (json)
            OutputHelper.WriteJsonError(message);
        OutputHelper.WriteError(message);
    }
}
