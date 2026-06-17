using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Leave;

[Description("Cancel a leave request")]
public class CancelCommand : AsyncCommand<CancelCommand.Settings>
{
    private readonly ITimeProApiClient _api;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("Leave entry ID")]
        public string LeaveId { get; set; } = string.Empty;

        [CommandOption("--reason <REASON>")]
        [Description("Cancellation reason")]
        public string? Reason { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public CancelCommand(ITimeProApiClient api) => _api = api;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!settings.Yes && !settings.Json)
        {
            if (!AnsiConsole.Confirm($"Cancel leave request {settings.LeaveId}?", false))
                return 1;
        }

        try
        {
            await _api.CancelLeaveAsync(
                settings.LeaveId,
                new CancelLeaveRequest
                {
                    LeaveId = settings.LeaveId,
                    CancellationReason = settings.Reason ?? string.Empty
                },
                CancellationToken.None);

            if (settings.Json)
                OutputHelper.WriteJson(new { success = true, leaveId = settings.LeaveId });
            else
                OutputHelper.WriteSuccess($"Leave request cancelled");

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }
}
