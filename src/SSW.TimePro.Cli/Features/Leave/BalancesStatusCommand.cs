using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Leave;

[Description("Show when leave balances were last imported from Xero and whether they are stale")]
public class BalancesStatusCommand : AsyncCommand<BalancesStatusCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public BalancesStatusCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (_config.LoadActiveTenantConfig() is null)
        {
            const string message = "Not logged in. Run 'tp login --tenant <id>' first.";
            if (settings.Json)
                OutputHelper.WriteJsonError(message);
            else
                OutputHelper.WriteError(message);
            return 1;
        }

        try
        {
            var status = await _api.GetLeaveBalanceStatusAsync(cancellationToken);
            if (status?.LastImportedAt is null)
            {
                // Nothing imported yet is a valid state, not a failure.
                if (settings.Json)
                    OutputHelper.WriteJson(new { imported = false });
                else
                    OutputHelper.WriteWarning("No leave balances have been imported yet.");
                return 0;
            }

            OutputHelper.Render(status, settings.Json, s =>
            {
                var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
                table.AddRow("[bold]Balances as at[/]", s.AsAtDate?.ToString("yyyy-MM-dd") ?? "-");
                table.AddRow("[bold]Last imported[/]",
                    s.LastImportedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "never");
                table.AddRow("[bold]Employees[/]", s.EmployeeCount.ToString());
                table.AddRow("[bold]Stale[/]", s.IsStale ? "[yellow]yes[/]" : "no");
                AnsiConsole.Write(table);

                if (s.IsStale)
                    OutputHelper.WriteWarning("Balances are stale. Re-import the latest Xero export with 'tp leave balances import <path>'.");
            });

            return 0;
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
}
