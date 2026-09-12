using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Leave;

[Description("Cancel a leave request")]
public class CancelCommand : AsyncCommand<CancelCommand.Settings>
{
    private const string AsyncNote =
        "The server finishes cancellations asynchronously: the request reads PendingCancellation "
        + "and becomes Cancelled within a few minutes.";

    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;
    private readonly LeaveLookup _lookup;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("Leave entry ID")]
        public string LeaveId { get; set; } = string.Empty;

        [CommandOption("--reason <REASON>")]
        [Description("Cancellation reason")]
        public string? Reason { get; set; }

        [CommandOption("--wait [SECONDS]")]
        [Description("Poll until the request reads Cancelled (default 120 seconds). The exit code is the "
            + "verdict, not the success field: 0 when it reads Cancelled, 2 on timeout")]
        public FlagValue<int> Wait { get; set; } = new();

        [CommandOption("--yes")]
        [Description("Skip confirmation")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public CancelCommand(ITimeProApiClient api, IConfigService config, LeaveLookup lookup)
    {
        _api = api;
        _config = config;
        _lookup = lookup;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(settings.LeaveId, out var leaveId))
        {
            // The cancel route is constrained to {id:guid}; reject non-GUID values before routing 404s.
            WriteValidationError(settings.Json, "Leave ID must be a valid GUID");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Reason))
        {
            WriteValidationError(settings.Json, "--reason is required: a cancellation reason is mandatory for leave");
            return 1;
        }

        var normalizedLeaveId = leaveId.ToString();
        var cancellationReason = settings.Reason.Trim();
        var employeeId = _config.LoadActiveTenantConfig()?.EmployeeId;

        try
        {
            var existing = await _lookup.FindAsync(normalizedLeaveId, employeeId, cancellationToken);
            if (existing is not null && LeaveStatusRules.IsTerminal(existing.LeaveStatus))
            {
                WriteValidationError(
                    settings.Json,
                    $"Leave {normalizedLeaveId} is already {existing.StatusName}; there is nothing to cancel.");
                return 1;
            }

            if (!settings.Yes && !settings.Json)
            {
                if (!AnsiConsole.Confirm($"Cancel leave request {normalizedLeaveId}?", false))
                    return 1;
            }

            await _api.CancelLeaveAsync(
                normalizedLeaveId,
                new CancelLeaveRequest
                {
                    LeaveId = normalizedLeaveId,
                    CancellationReason = cancellationReason
                },
                cancellationToken);

            if (!settings.Wait.IsSet)
            {
                if (settings.Json)
                    OutputHelper.WriteJson(new
                    {
                        success = true,
                        leaveId = normalizedLeaveId,
                        status = (string?)null,
                        timedOut = false,
                        note = $"{AsyncNote} Re-read it with 'tp leave list', or pass --wait."
                    });
                else
                {
                    OutputHelper.WriteSuccess("Leave cancellation requested");
                    OutputHelper.WriteInfo($"{AsyncNote} Re-read it with 'tp leave list', or pass --wait.");
                }

                return 0;
            }

            var timeout = settings.Wait.Value > 0
                ? TimeSpan.FromSeconds(settings.Wait.Value)
                : LeaveCancelWaiter.DefaultTimeout;

            if (!settings.Json)
                OutputHelper.WriteInfo(
                    $"Waiting up to {timeout.TotalSeconds:0}s for the server to finish the cancellation...");

            var result = await LeaveCancelWaiter.WaitForCancelledAsync(
                _lookup, normalizedLeaveId, employeeId, timeout, ct: cancellationToken);

            var timedOutText = result.LastStatus is null
                ? $"Timed out after {timeout.TotalSeconds:0}s; could not find the request while polling."
                : $"Timed out after {timeout.TotalSeconds:0}s; the request still reads {result.LastStatus}.";

            if (settings.Json)
                OutputHelper.WriteJson(new
                {
                    success = true,
                    leaveId = normalizedLeaveId,
                    status = result.LastStatus,
                    timedOut = !result.Cancelled,
                    note = result.Cancelled
                        ? "The server reports the request as Cancelled."
                        : $"{timedOutText} {AsyncNote}"
                });
            else if (result.Cancelled)
                OutputHelper.WriteSuccess("Leave request cancelled");
            else
                OutputHelper.WriteError($"{timedOutText} {AsyncNote}");

            return result.Cancelled ? 0 : 2;
        }
        catch (ApiException ex)
        {
            // Surface the server's response body so failures (incl. 500s) are diagnosable.
            var detail = ApiErrorParser.ExtractDetail(ex.ResponseBody);
            if (settings.Json)
                OutputHelper.WriteJsonError($"API error: {ex.Message}", ex.StatusCode, detail);
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}"
                + (detail is not null ? $" — {detail}" : ""));
            return 1;
        }
    }

    private static void WriteValidationError(bool json, string message)
    {
        if (json)
            OutputHelper.WriteJsonError(message);
        OutputHelper.WriteError(message);
    }
}
