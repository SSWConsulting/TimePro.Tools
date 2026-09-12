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
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        try
        {
            var found = await TimesheetLookup.FindAsync(
                _api, tenant.EmployeeId, settings.TimesheetId, settings.Date, cancellationToken);

            if (found?.Item.IsSuggested == true)
            {
                var message = $"Timesheet {settings.TimesheetId} is a suggestion and cannot be deleted. Accept it first: tp ts accept {settings.TimesheetId}";
                if (settings.Json)
                    OutputHelper.WriteJsonError(message);
                else
                    OutputHelper.WriteError(message);
                return 1;
            }

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
