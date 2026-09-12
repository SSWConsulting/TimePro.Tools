using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Leave;

public sealed record LeaveUpdateOptions(
    string? Start = null,
    string? End = null,
    string? Type = null,
    string? Note = null,
    string? ApprovedBy = null,
    bool ClearApprovedBy = false,
    string? Cc = null,
    bool ClearCc = false,
    bool? AllDay = null,
    string? StartTime = null,
    string? EndTime = null,
    string? TimeZoneId = null);

public sealed record LeaveUpdatePlan(
    LeaveEntry Existing,
    UpdateLeaveRequest Request,
    IReadOnlyList<string> Changes);

public sealed class LeaveUpdateValidationException(string message) : Exception(message);

/// <summary>
/// Builds a complete leave update payload while preserving API-returned fields omitted by the caller.
/// TimePro's update endpoint replaces the full leave record rather than applying a patch. Stored
/// workday times are preferred when available; current profile times are the fallback because older
/// list responses omit those fields.
/// </summary>
public sealed class LeaveUpdateService
{
    private readonly ITimeProApiClient _api;
    private readonly LeaveLookup _lookup;

    public LeaveUpdateService(ITimeProApiClient api, LeaveLookup lookup)
    {
        _api = api;
        _lookup = lookup;
    }

    public async Task<LeaveUpdatePlan> PrepareAsync(
        string leaveId,
        string employeeId,
        LeaveUpdateOptions options,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(leaveId, out var parsedLeaveId))
            throw new LeaveUpdateValidationException("Leave ID must be a valid GUID");

        if (options.ClearApprovedBy && options.ApprovedBy is not null)
            throw new LeaveUpdateValidationException("Use either approvedBy or clearApprovedBy, not both");

        if (options.ClearCc && options.Cc is not null)
            throw new LeaveUpdateValidationException("Use either cc or clearCc, not both");

        var normalizedLeaveId = parsedLeaveId.ToString();
        var existing = await _lookup.FindAsync(normalizedLeaveId, employeeId, ct)
            ?? throw new LeaveUpdateValidationException(
                $"Leave {normalizedLeaveId} was not found for employee {employeeId}");

        if (LeaveStatusRules.IsTerminal(existing.LeaveStatus))
            throw new LeaveUpdateValidationException(
                $"Leave {normalizedLeaveId} is {existing.StatusName} and can no longer be updated. "
                + "Create a new leave request instead.");

        var changes = DescribeChanges(options);
        if (changes.Count == 0)
            throw new LeaveUpdateValidationException(
                "No changes specified. Use --start, --end, --type, --note, --approved-by, --cc, --half-day, --full-day, --start-time, or --end-time.");

        var employeeSettings = await _api.GetEmployeeSettingsAsync(ct);
        if (!LeaveRequestParser.TryResolveRequestTimeZone(
                options.TimeZoneId,
                employeeSettings,
                out var requestTimeZone,
                out var timeZoneError))
        {
            throw new LeaveUpdateValidationException(
                timeZoneError ?? "Invalid leave request timezone");
        }

        var startInput = options.Start ?? existing.StartDate;
        var endInput = options.End ?? existing.EndDate;

        // Changing between partial-day and full-day should reset the range to whole local dates.
        if (options.AllDay is not null)
        {
            startInput ??= existing.StartDateLocal;
            endInput ??= existing.EndDateLocal;
            if (options.Start is null)
                startInput = DatePart(existing.StartDateLocal ?? existing.StartDate);
            if (options.End is null)
                endInput = DatePart(existing.EndDateLocal ?? existing.EndDate);
        }

        if (string.IsNullOrWhiteSpace(startInput) || string.IsNullOrWhiteSpace(endInput))
            throw new LeaveUpdateValidationException("The existing leave does not contain a complete date range");

        var allDay = options.AllDay ?? existing.AllDay;

        if (!LeaveRequestParser.TryParseWorkdayTime(
                options.StartTime ?? existing.UserStartTime ?? employeeSettings?.StartTime,
                LeaveRequestParser.DefaultStartTime,
                "start",
                out var userStartTime,
                out var startTimeError))
        {
            throw new LeaveUpdateValidationException(startTimeError!);
        }

        if (!LeaveRequestParser.TryParseWorkdayTime(
                options.EndTime ?? existing.UserEndTime ?? employeeSettings?.EndTime,
                LeaveRequestParser.DefaultEndTime,
                "end",
                out var userEndTime,
                out var endTimeError))
        {
            throw new LeaveUpdateValidationException(endTimeError!);
        }

        if (!allDay && !LeaveRequestParser.TryValidatePartialDayTimes(userStartTime, userEndTime, out var timeError))
            throw new LeaveUpdateValidationException(timeError!);

        if (!LeaveRequestParser.TryParseDateRange(
                startInput,
                endInput,
                requestTimeZone,
                allDay ? null : userStartTime,
                allDay ? null : userEndTime,
                out var startDate,
                out var endDate,
                out var dateError))
        {
            throw new LeaveUpdateValidationException(dateError ?? "Invalid leave date range");
        }

        if (startDate > endDate)
            throw new LeaveUpdateValidationException("Leave start date must not be after the end date");

        if (IsWeekend(startDate) || IsWeekend(endDate))
            throw new LeaveUpdateValidationException("Leave start and end dates must be weekdays");

        if (!allDay && startDate.Date != endDate.Date)
            throw new LeaveUpdateValidationException("Partial-day leave must start and end on the same day");

        var leaveTypeId = options.Type is null
            ? existing.LeaveType?.Id
            : await ResolveLeaveTypeAsync(options.Type, ct);
        if (leaveTypeId is null)
            throw new LeaveUpdateValidationException(
                options.Type is null
                    ? "The existing leave does not contain a leave type"
                    : $"Unknown leave type: '{options.Type}'.");

        var note = options.Note?.Trim() ?? existing.Note?.Trim();
        if (string.IsNullOrWhiteSpace(note))
            throw new LeaveUpdateValidationException(
                "A leave note is required. Provide --note because the existing leave has no note.");

        var approvedBy = options.ClearApprovedBy
            ? null
            : options.ApprovedBy?.Trim() ?? existing.ApprovedBy;
        var optionalEmployees = options.ClearCc
            ? []
            : options.Cc is not null
                ? LeaveRequestParser.ParseOptionalEmployees(options.Cc)
                : existing.OptionalEmp ?? [];

        var request = new UpdateLeaveRequest
        {
            Id = normalizedLeaveId,
            RequestedEmpId = string.IsNullOrWhiteSpace(existing.RequestedEmpId)
                ? employeeId
                : existing.RequestedEmpId,
            StartDate = startDate.ToString("o"),
            EndDate = endDate.ToString("o"),
            LeaveTypeId = leaveTypeId.Value,
            Note = note,
            UserStartTime = userStartTime.ToString("HH:mm:ss"),
            UserEndTime = userEndTime.ToString("HH:mm:ss"),
            AllDay = allDay,
            OptionalEmp = optionalEmployees,
            ApprovedBy = approvedBy,
            TimeLessOverride = existing.TimeLessOverride
        };

        return new LeaveUpdatePlan(existing, request, changes);
    }

    public Task ApplyAsync(LeaveUpdatePlan plan, CancellationToken ct = default) =>
        _api.UpdateLeaveAsync(plan.Request, ct);

    private async Task<int?> ResolveLeaveTypeAsync(string typeInput, CancellationToken ct)
    {
        if (int.TryParse(typeInput, out var id))
            return id;

        var types = await _api.GetLeaveTypesAsync(ct);
        return types.FirstOrDefault(type =>
            type.Name.Equals(typeInput, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static List<string> DescribeChanges(LeaveUpdateOptions options)
    {
        var changes = new List<string>();
        if (options.Start is not null) changes.Add($"Start -> {options.Start}");
        if (options.End is not null) changes.Add($"End -> {options.End}");
        if (options.Type is not null) changes.Add($"Type -> {options.Type}");
        if (options.Note is not null) changes.Add($"Note -> {Truncate(options.Note)}");
        if (options.ClearApprovedBy) changes.Add("Approver -> cleared");
        else if (options.ApprovedBy is not null) changes.Add($"Approver -> {options.ApprovedBy}");
        if (options.ClearCc) changes.Add("CC -> cleared");
        else if (options.Cc is not null) changes.Add($"CC -> {options.Cc}");
        if (options.AllDay is not null) changes.Add(options.AllDay.Value ? "Day mode -> full day" : "Day mode -> partial day");
        if (options.StartTime is not null) changes.Add($"Workday start -> {options.StartTime}");
        if (options.EndTime is not null) changes.Add($"Workday end -> {options.EndTime}");
        return changes;
    }

    private static string? DatePart(string? value) => value?.Split('T')[0];

    private static string Truncate(string value) =>
        value.Length > 50 ? value[..50] + "..." : value;

    private static bool IsWeekend(DateTimeOffset date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
