using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Delete a timesheet entry")]
public class DeleteCommand : AsyncCommand<DeleteCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("Timesheet ID to delete")]
        public int TimesheetId { get; set; }

        [CommandOption("--date <DATE>")]
        [Description("Date the timesheet is on (yyyy-MM-dd). Used to look up the entry. Defaults to searching recent weeks.")]
        public string? Date { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation prompt")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public DeleteCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
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
            await TimesheetLookup.EnsureDeletableAsync(
                _api, tenant.EmployeeId, settings.TimesheetId, settings.Date, cancellationToken);

            if (!settings.Yes && !settings.Json)
            {
                if (!AnsiConsole.Confirm($"Delete timesheet #{settings.TimesheetId}?", false))
                    return 1;
            }

            await _api.DeleteTimesheetAsync(settings.TimesheetId, cancellationToken);

            if (settings.Json)
                OutputHelper.WriteJson(new { success = true, timesheetId = settings.TimesheetId });
            else
                OutputHelper.WriteSuccess($"Timesheet #{settings.TimesheetId} deleted");

            return 0;
        }
        catch (TimesheetValidationException ex)
        {
            if (settings.Json)
                OutputHelper.WriteJsonError(ex.Message);
            else
                OutputHelper.WriteError(ex.Message);
            return 1;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteApiError(ex, settings.Json);
            return 1;
        }
    }
}
