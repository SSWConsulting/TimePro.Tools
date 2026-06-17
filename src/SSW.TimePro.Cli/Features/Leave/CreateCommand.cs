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

        var tenant = _tenantProvider.GetCurrentTenant();
        if (tenant is null || string.IsNullOrEmpty(tenant.EmployeeId))
        {
            OutputHelper.WriteError("No active tenant or employee ID configured. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        try
        {
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

            // Parse dates with timezone offset
            if (!DateTimeOffset.TryParse(settings.Start, out var startDate))
            {
                // Support plain yyyy-MM-dd by adding local timezone offset
                if (!DateOnly.TryParse(settings.Start, out var startDateOnly))
                {
                    OutputHelper.WriteError($"Invalid start date: '{settings.Start}'. Use yyyy-MM-dd format.");
                    return 1;
                }
                startDate = new DateTimeOffset(startDateOnly.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(DateTime.Now));
            }

            if (!DateTimeOffset.TryParse(settings.End, out var endDate))
            {
                if (!DateOnly.TryParse(settings.End, out var endDateOnly))
                {
                    OutputHelper.WriteError($"Invalid end date: '{settings.End}'. Use yyyy-MM-dd format.");
                    return 1;
                }
                endDate = new DateTimeOffset(endDateOnly.ToDateTime(new TimeOnly(23, 59, 0)), TimeZoneInfo.Local.GetUtcOffset(DateTime.Now));
            }
            else if (endDate.TimeOfDay == TimeSpan.Zero)
            {
                // If end date was parsed but has no time component, set to 23:59
                endDate = new DateTimeOffset(endDate.Date.AddHours(23).AddMinutes(59), endDate.Offset);
            }

            var allDay = !settings.HalfDay;
            var userStartTime = settings.StartTime ?? "09:00:00";
            var userEndTime = settings.EndTime ?? "18:00:00";

            // Normalize time format to HH:mm:ss
            if (userStartTime.Length == 5) userStartTime += ":00";
            if (userEndTime.Length == 5) userEndTime += ":00";

            var optionalEmps = string.IsNullOrEmpty(settings.OptionalEmp)
                ? []
                : settings.OptionalEmp.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

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
                Note = settings.Note,
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
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

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
