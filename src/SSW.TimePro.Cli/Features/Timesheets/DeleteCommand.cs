using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Delete a timesheet entry")]
public class DeleteCommand : AsyncCommand<DeleteCommand.Settings>
{
    private readonly ITimeProApiClient _api;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("Timesheet ID to delete")]
        public int TimesheetId { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation prompt")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public DeleteCommand(ITimeProApiClient api)
    {
        _api = api;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!settings.Yes && !settings.Json)
        {
            if (!AnsiConsole.Confirm($"Delete timesheet #{settings.TimesheetId}?", false))
                return 1;
        }

        try
        {
            await _api.DeleteTimesheetAsync(settings.TimesheetId, CancellationToken.None);

            if (settings.Json)
                OutputHelper.WriteJson(new { success = true, timesheetId = settings.TimesheetId });
            else
                OutputHelper.WriteSuccess($"Timesheet #{settings.TimesheetId} deleted");

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }
}
