using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Leave;

[Description("Create a leave request")]
public class CreateCommand : AsyncCommand<CreateCommand.Settings>
{
    private readonly LeaveCreateService _createService;
    private readonly ITenantProvider _tenantProvider;

    public class Settings : CommandSettings
    {
        [CommandOption("--start <DATE>")]
        [Description("Start date (yyyy-MM-dd)")]
        public string Start { get; set; } = string.Empty;

        [CommandOption("--end <DATE>")]
        [Description("End date (yyyy-MM-dd)")]
        public string End { get; set; } = string.Empty;

        [CommandOption("--type <TYPE>")]
        [Description("Leave type ID or name (e.g., 1, 'Annual Leave')")]
        public string Type { get; set; } = string.Empty;

        [CommandOption("--note <NOTE>")]
        [Description("Leave note/reason")]
        public string? Note { get; set; }

        [CommandOption("--approved-by <EMAIL>")]
        [Description("Approver's email address")]
        public string? ApprovedBy { get; set; }

        [CommandOption("--cc <EMAILS>")]
        [Description("Comma-separated list of emails to notify (optional employees)")]
        public string? OptionalEmp { get; set; }

        [CommandOption("--half-day")]
        [Description("Request a half-day leave (start and end date must be the same)")]
        public bool HalfDay { get; set; }

        [CommandOption("--start-time <TIME>")]
        [Description("Start time override (HH:mm, default: 09:00)")]
        public string? StartTime { get; set; }

        [CommandOption("--end-time <TIME>")]
        [Description("End time override (HH:mm, default: 18:00)")]
        public string? EndTime { get; set; }

        [CommandOption("--timezone <TIMEZONE_ID>")]
        [Description("Timezone override (IANA or Windows ID); takes priority over the TimePro user profile timezone")]
        public string? TimeZoneId { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation")]
        public bool Yes { get; set; }

        [CommandOption("--dry-run")]
        [Description("Validate and preview the request without creating leave")]
        public bool DryRun { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public CreateCommand(LeaveCreateService createService, ITenantProvider tenantProvider)
    {
        _createService = createService;
        _tenantProvider = tenantProvider;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tenant = _tenantProvider.GetCurrentTenant();
        if (tenant is null || string.IsNullOrEmpty(tenant.EmployeeId))
        {
            WriteValidationError(
                settings.Json,
                "No active tenant or employee ID configured. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        try
        {
            var plan = await _createService.PrepareAsync(
                tenant.EmployeeId,
                new LeaveCreateOptions(
                    Start: settings.Start,
                    End: settings.End,
                    Type: settings.Type,
                    Note: settings.Note,
                    ApprovedBy: settings.ApprovedBy,
                    Cc: settings.OptionalEmp,
                    HalfDay: settings.HalfDay,
                    StartTime: settings.StartTime,
                    EndTime: settings.EndTime,
                    TimeZoneId: settings.TimeZoneId),
                cancellationToken);

            if (!settings.Json && (!settings.Yes || settings.DryRun))
            {
                RenderPreview(plan, settings.DryRun);
                if (!settings.Yes && !settings.DryRun && !AnsiConsole.Confirm("Submit this leave request?"))
                    return 1;
            }

            if (settings.DryRun)
            {
                if (settings.Json)
                    OutputHelper.WriteJson(new { dryRun = true, request = plan.Request });
                else
                    OutputHelper.WriteInfo("Dry run: request validated; no leave request was created");
                return 0;
            }

            await _createService.ApplyAsync(plan, cancellationToken);

            if (settings.Json)
                OutputHelper.WriteJson(new { success = true });
            else
                OutputHelper.WriteSuccess("Leave request created");

            return 0;
        }
        catch (LeaveCreateValidationException ex)
        {
            WriteValidationError(settings.Json, ex.Message);
            return 1;
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

    private static void RenderPreview(LeaveCreatePlan plan, bool dryRun)
    {
        var request = plan.Request;
        AnsiConsole.MarkupLine(dryRun
            ? "[bold]Proposed leave request (dry run):[/]"
            : "[bold]Creating leave request:[/]");
        AnsiConsole.MarkupLine($"  Employee: {Markup.Escape(request.RequestedEmpId)}");
        AnsiConsole.MarkupLine($"  Start:    {Markup.Escape(request.StartDate)}");
        AnsiConsole.MarkupLine($"  End:      {Markup.Escape(request.EndDate)}");
        AnsiConsole.MarkupLine($"  Type:     {request.LeaveTypeId} ({Markup.Escape(plan.TypeLabel)})");
        AnsiConsole.MarkupLine($"  Note:     {Markup.Escape(request.Note ?? "")}");
        AnsiConsole.MarkupLine($"  Workday:  {Markup.Escape(request.UserStartTime)}-{Markup.Escape(request.UserEndTime)}");
        AnsiConsole.MarkupLine($"  All day:  {request.AllDay}");
        AnsiConsole.MarkupLine($"  CC:       {Markup.Escape(string.Join(", ", request.OptionalEmp))}");
        AnsiConsole.MarkupLine($"  Approved by: {Markup.Escape(request.ApprovedBy ?? "")}");
        AnsiConsole.MarkupLine($"  Time-less override: {(request.TimeLessOverride?.ToString() ?? "")}");
        AnsiConsole.WriteLine();
    }

    private static void WriteValidationError(bool json, string message)
    {
        if (json)
            OutputHelper.WriteJsonError(message);
        OutputHelper.WriteError(message);
    }
}
