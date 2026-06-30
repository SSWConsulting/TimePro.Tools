using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Leave;

[Description("Create a leave request")]
public class CreateCommand : AsyncCommand<CreateCommand.Settings>
{
    private readonly ITimeProApiClient _api;
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

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public CreateCommand(ITimeProApiClient api, ITenantProvider tenantProvider)
    {
        _api = api;
        _tenantProvider = tenantProvider;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.Start) || string.IsNullOrEmpty(settings.End) || string.IsNullOrEmpty(settings.Type))
        {
            OutputHelper.WriteError("--start, --end, and --type are required");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Note))
        {
            WriteValidationError(settings.Json, "--note is required: a reason/description is mandatory for leave");
            return 1;
        }

        var tenant = _tenantProvider.GetCurrentTenant();
        if (tenant is null || string.IsNullOrEmpty(tenant.EmployeeId))
        {
            OutputHelper.WriteError("No active tenant or employee ID configured. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        try
        {
            var settingsDetails = string.IsNullOrWhiteSpace(settings.TimeZoneId)
                ? await _api.GetEmployeeSettingsAsync(cancellationToken)
                : null;
            if (!LeaveRequestParser.TryResolveRequestTimeZone(settings.TimeZoneId, settingsDetails, out var requestTimeZone, out var timeZoneError))
            {
                WriteValidationError(settings.Json, timeZoneError ?? "Invalid leave request timezone");
                return 1;
            }

            if (!LeaveRequestParser.TryParseDateRange(
                    settings.Start,
                    settings.End,
                    requestTimeZone,
                    out var startDate,
                    out var endDate,
                    out var dateError))
            {
                WriteValidationError(settings.Json, dateError ?? "Invalid leave date range");
                return 1;
            }

            if (IsWeekend(startDate) || IsWeekend(endDate))
            {
                // LeaveCommandValidator rejects weekend boundaries; surface it before the API call.
                WriteValidationError(settings.Json, "Leave start and end dates must be weekdays");
                return 1;
            }

            // Resolve type ID
            var leaveTypeId = await ResolveLeaveTypeAsync(settings.Type);
            if (leaveTypeId is null)
            {
                OutputHelper.WriteError($"Unknown leave type: '{settings.Type}'. Use 'tp leave types' or a numeric ID.");
                var types = await _api.GetLeaveTypesAsync(CancellationToken.None);
                foreach (var t in types.Where(t => t.IsActive))
                    AnsiConsole.MarkupLine($"  {t.Id}: {Markup.Escape(t.Name)}");
                return 1;
            }

            var allDay = !settings.HalfDay;
            var userStartTime = LeaveRequestParser.NormalizeTime(settings.StartTime, LeaveRequestParser.DefaultStartTime);
            var userEndTime = LeaveRequestParser.NormalizeTime(settings.EndTime, LeaveRequestParser.DefaultEndTime);
            var optionalEmps = LeaveRequestParser.ParseOptionalEmployees(settings.OptionalEmp);

            if (!settings.Yes && !settings.Json)
            {
                AnsiConsole.MarkupLine("[bold]Creating leave request:[/]");
                AnsiConsole.MarkupLine($"  Employee: {tenant.EmployeeId}");
                AnsiConsole.MarkupLine($"  Start:    {startDate:yyyy-MM-dd}");
                AnsiConsole.MarkupLine($"  End:      {endDate:yyyy-MM-dd}");
                AnsiConsole.MarkupLine($"  Type:     {settings.Type}");
                AnsiConsole.MarkupLine($"  All day:  {allDay}");
                if (!string.IsNullOrEmpty(settings.Note))
                    AnsiConsole.MarkupLine($"  Note:     {Markup.Escape(settings.Note)}");
                if (!string.IsNullOrEmpty(settings.ApprovedBy))
                    AnsiConsole.MarkupLine($"  Approved by: {Markup.Escape(settings.ApprovedBy)}");
                if (optionalEmps.Count > 0)
                    AnsiConsole.MarkupLine($"  CC:       {Markup.Escape(string.Join(", ", optionalEmps))}");
                AnsiConsole.WriteLine();

                if (!AnsiConsole.Confirm("Submit this leave request?"))
                    return 1;
            }

            var request = new CreateLeaveRequest
            {
                RequestedEmpId = tenant.EmployeeId,
                StartDate = startDate.ToString("o"),
                EndDate = endDate.ToString("o"),
                LeaveTypeId = leaveTypeId.Value,
                Note = settings.Note.Trim(),
                UserStartTime = userStartTime,
                UserEndTime = userEndTime,
                AllDay = allDay,
                OptionalEmp = optionalEmps,
                ApprovedBy = settings.ApprovedBy,
                TimeLessOverride = null
            };

            await _api.CreateLeaveAsync(request, CancellationToken.None);

            if (settings.Json)
                OutputHelper.WriteJson(new { success = true });
            else
                OutputHelper.WriteSuccess("Leave request created");

            return 0;
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

    private static bool IsWeekend(DateTimeOffset date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private async Task<int?> ResolveLeaveTypeAsync(string typeInput)
    {
        if (int.TryParse(typeInput, out var id))
            return id;

        var types = await _api.GetLeaveTypesAsync(CancellationToken.None);
        var match = types.FirstOrDefault(t =>
            t.Name.Equals(typeInput, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }
}
