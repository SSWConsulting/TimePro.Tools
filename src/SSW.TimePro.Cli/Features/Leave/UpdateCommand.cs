using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Leave;

[Description("Update an existing leave request")]
public class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    private readonly LeaveUpdateService _updateService;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("Leave entry ID")]
        public string LeaveId { get; set; } = string.Empty;

        [CommandOption("--start <DATE>")]
        [Description("New start date (yyyy-MM-dd)")]
        public string? Start { get; set; }

        [CommandOption("--end <DATE>")]
        [Description("New end date (yyyy-MM-dd)")]
        public string? End { get; set; }

        [CommandOption("--type <TYPE>")]
        [Description("New leave type ID or active leave type name")]
        public string? Type { get; set; }

        [CommandOption("--note <NOTE>")]
        [Description("New leave note/reason")]
        public string? Note { get; set; }

        [CommandOption("--approved-by <EMAIL>")]
        [Description("New approver email address")]
        public string? ApprovedBy { get; set; }

        [CommandOption("--clear-approved-by")]
        [Description("Remove the current approver")]
        public bool ClearApprovedBy { get; set; }

        [CommandOption("--cc <EMAILS>")]
        [Description("Replace CC recipients with a comma-separated email list")]
        public string? Cc { get; set; }

        [CommandOption("--clear-cc")]
        [Description("Remove all current CC recipients")]
        public bool ClearCc { get; set; }

        [CommandOption("--half-day")]
        [Description("Change the request to partial-day leave")]
        public bool HalfDay { get; set; }

        [CommandOption("--full-day")]
        [Description("Change the request to full-day leave")]
        public bool FullDay { get; set; }

        [CommandOption("--start-time <TIME>")]
        [Description("Employee workday start time (HH:mm)")]
        public string? StartTime { get; set; }

        [CommandOption("--end-time <TIME>")]
        [Description("Employee workday end time (HH:mm)")]
        public string? EndTime { get; set; }

        [CommandOption("--timezone <TIMEZONE_ID>")]
        [Description("Timezone for changed dates (IANA or Windows ID)")]
        public string? TimeZoneId { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation")]
        public bool Yes { get; set; }

        [CommandOption("--dry-run")]
        [Description("Validate and preview the update without applying it")]
        public bool DryRun { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public UpdateCommand(LeaveUpdateService updateService, IConfigService config)
    {
        _updateService = updateService;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
        {
            WriteValidationError(settings.Json, "Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        if (settings.HalfDay && settings.FullDay)
        {
            WriteValidationError(settings.Json, "Use either --half-day or --full-day, not both");
            return 1;
        }

        try
        {
            var plan = await _updateService.PrepareAsync(
                settings.LeaveId,
                tenant.EmployeeId,
                new LeaveUpdateOptions(
                    Start: settings.Start,
                    End: settings.End,
                    Type: settings.Type,
                    Note: settings.Note,
                    ApprovedBy: settings.ApprovedBy,
                    ClearApprovedBy: settings.ClearApprovedBy,
                    Cc: settings.Cc,
                    ClearCc: settings.ClearCc,
                    AllDay: settings.HalfDay ? false : settings.FullDay ? true : null,
                    StartTime: settings.StartTime,
                    EndTime: settings.EndTime,
                    TimeZoneId: settings.TimeZoneId),
                cancellationToken);

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine($"[bold]Updating leave {Markup.Escape(plan.Request.Id)}:[/]");
                foreach (var change in plan.Changes)
                    AnsiConsole.MarkupLine($"  {Markup.Escape(change)}");
                AnsiConsole.WriteLine();
            }

            if (!settings.Yes && !settings.Json && !settings.DryRun && !AnsiConsole.Confirm("Apply these changes?"))
                return 1;

            if (settings.DryRun)
            {
                if (settings.Json)
                {
                    OutputHelper.WriteJson(new
                    {
                        dryRun = true,
                        leaveId = plan.Request.Id,
                        changes = plan.Changes,
                        request = plan.Request
                    });
                }
                else
                {
                    OutputHelper.WriteInfo("Dry run: update validated; no changes were applied");
                }

                return 0;
            }

            await _updateService.ApplyAsync(plan, cancellationToken);

            if (settings.Json)
                OutputHelper.WriteJson(new { success = true, leaveId = plan.Request.Id, changes = plan.Changes });
            else
                OutputHelper.WriteSuccess("Leave request updated");

            return 0;
        }
        catch (LeaveUpdateValidationException ex)
        {
            WriteValidationError(settings.Json, ex.Message);
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

    private static void WriteValidationError(bool json, string message)
    {
        if (json)
            OutputHelper.WriteJsonError(message);
        OutputHelper.WriteError(message);
    }
}
