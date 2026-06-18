using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Leave;

[Description("Show leave stats for an employee (days since last leave, leave taken in the last 12 months)")]
public class BalanceCommand : AsyncCommand<BalanceCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--emp-id|--employee-id|--employee <EMP_ID>")]
        [Description("empId. Defaults to the current user")]
        public string? EmpId { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public BalanceCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant is null)
        {
            if (settings.Json)
                OutputHelper.WriteJsonError("Not logged in. Run 'tp login --tenant <id>' first.");
            else
                OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        var empId = string.IsNullOrWhiteSpace(settings.EmpId)
            ? tenant.EmployeeId
            : settings.EmpId.Trim();

        if (string.IsNullOrWhiteSpace(empId))
        {
            if (settings.Json)
                OutputHelper.WriteJsonError("No empId available. Pass --emp-id <ID> or log in again.");
            else
                OutputHelper.WriteError("No empId available. Pass --emp-id <ID> or log in again.");
            return 1;
        }

        try
        {
            var stats = await _api.GetLeaveStatsAsync(empId, cancellationToken);
            if (stats is null)
            {
                if (settings.Json)
                    OutputHelper.WriteJsonError($"No leave stats found for {empId}.", 404);
                else
                    OutputHelper.WriteWarning($"No leave stats found for {empId}.");
                return 1;
            }

            OutputHelper.Render(stats, settings.Json, s =>
            {
                var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
                table.AddRow("[bold]Employee[/]", Markup.Escape(empId));
                table.AddRow("[bold]Days since last leave[/]",
                    s.DaysSinceLastLeave?.ToString() ?? "-");
                table.AddRow("[bold]Leave taken (last 12 months)[/]",
                    $"{s.LeaveTakenInLast12Months:0.##}h");
                AnsiConsole.Write(table);
            });

            return 0;
        }
        catch (ApiException ex)
        {
            if (settings.Json)
                OutputHelper.WriteJsonError($"API error: {ex.Message}", ex.StatusCode);
            else
                OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }
}
