using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Leave;

public sealed record LeaveCreateOptions(
    string Start,
    string End,
    string Type,
    string? Note,
    string? ApprovedBy = null,
    string? Cc = null,
    bool HalfDay = false,
    string? StartTime = null,
    string? EndTime = null,
    string? TimeZoneId = null);

public sealed record LeaveCreatePlan(CreateLeaveRequest Request, string TypeLabel);

/// <summary>
/// Outcome of a leave create, shared by the CLI <c>--json</c> path and the MCP tool.
/// <see cref="Leave"/> is the entry re-read after the write, because POST /api/leave/ answers
/// with an empty body.
/// </summary>
public sealed record LeaveCreateResult
{
    public bool Success { get; init; } = true;
    public string? LeaveId { get; init; }
    public LeaveEntry? Leave { get; init; }
    public string? Warning { get; init; }
}

public sealed class LeaveCreateValidationException(string message) : Exception(message);

/// <summary>
/// Validates leave-create input and prepares the complete API request shared by CLI and MCP.
/// </summary>
public sealed class LeaveCreateService
{
    private readonly ITimeProApiClient _api;
    private readonly LeaveLookup _lookup;

    public LeaveCreateService(ITimeProApiClient api, LeaveLookup lookup)
    {
        _api = api;
        _lookup = lookup;
    }

    public async Task<LeaveCreatePlan> PrepareAsync(
        string employeeId,
        LeaveCreateOptions options,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Start)
            || string.IsNullOrWhiteSpace(options.End)
            || string.IsNullOrWhiteSpace(options.Type))
        {
            throw new LeaveCreateValidationException("--start, --end, and --type are required");
        }

        if (string.IsNullOrWhiteSpace(options.Note))
        {
            throw new LeaveCreateValidationException(
                "--note is required: a reason/description is mandatory for leave");
        }

        var employeeSettings = string.IsNullOrWhiteSpace(options.TimeZoneId)
            ? await _api.GetEmployeeSettingsAsync(ct)
            : null;
        if (!LeaveRequestParser.TryResolveRequestTimeZone(
                options.TimeZoneId,
                employeeSettings,
                out var requestTimeZone,
                out var timeZoneError))
        {
            throw new LeaveCreateValidationException(
                timeZoneError ?? "Invalid leave request timezone");
        }

        var allDay = !options.HalfDay;

        if (!LeaveRequestParser.TryParseWorkdayTime(
                options.StartTime,
                LeaveRequestParser.DefaultStartTime,
                "start",
                out var userStartTime,
                out var startTimeError))
        {
            throw new LeaveCreateValidationException(startTimeError!);
        }

        if (!LeaveRequestParser.TryParseWorkdayTime(
                options.EndTime,
                LeaveRequestParser.DefaultEndTime,
                "end",
                out var userEndTime,
                out var endTimeError))
        {
            throw new LeaveCreateValidationException(endTimeError!);
        }

        if (!allDay && !LeaveRequestParser.TryValidatePartialDayTimes(userStartTime, userEndTime, out var timeError))
            throw new LeaveCreateValidationException(timeError!);

        if (!LeaveRequestParser.TryParseDateRange(
                options.Start,
                options.End,
                requestTimeZone,
                allDay ? null : userStartTime,
                allDay ? null : userEndTime,
                out var startDate,
                out var endDate,
                out var dateError))
        {
            throw new LeaveCreateValidationException(dateError ?? "Invalid leave date range");
        }

        if (startDate > endDate)
            throw new LeaveCreateValidationException("Leave start date must not be after the end date");

        if (IsWeekend(startDate) || IsWeekend(endDate))
            throw new LeaveCreateValidationException("Leave start and end dates must be weekdays");

        if (options.HalfDay && startDate.Date != endDate.Date)
            throw new LeaveCreateValidationException("Partial-day leave must start and end on the same day");

        var leaveTypeId = await ResolveLeaveTypeAsync(options.Type, ct);
        if (leaveTypeId is null)
            throw new LeaveCreateValidationException($"Unknown leave type: '{options.Type}'.");

        var request = new CreateLeaveRequest
        {
            RequestedEmpId = employeeId,
            StartDate = startDate.ToString("o"),
            EndDate = endDate.ToString("o"),
            LeaveTypeId = leaveTypeId.Value,
            Note = options.Note.Trim(),
            UserStartTime = userStartTime.ToString("HH:mm:ss"),
            UserEndTime = userEndTime.ToString("HH:mm:ss"),
            AllDay = allDay,
            OptionalEmp = LeaveRequestParser.ParseOptionalEmployees(options.Cc),
            ApprovedBy = options.ApprovedBy?.Trim(),
            TimeLessOverride = null
        };

        return new LeaveCreatePlan(request, options.Type);
    }

    public async Task<LeaveCreateResult> ApplyAsync(LeaveCreatePlan plan, CancellationToken ct = default)
    {
        await _api.CreateLeaveAsync(plan.Request, ct);

        var matches = await _lookup.FindCreatedAsync(plan.Request, ct);
        if (matches.Count == 1)
            return new LeaveCreateResult { LeaveId = matches[0].Id, Leave = matches[0] };

        // The request was submitted, so the recovery is always to look it up — creating again
        // would duplicate it.
        var count = matches.Count == 0 ? "no entry matches" : $"{matches.Count} entries match";
        return new LeaveCreateResult
        {
            Warning = $"Leave request created, but the new entry could not be identified ({count}). "
                + "Find it with: tp leave list --filter ALL. Do not create it again."
        };
    }

    private async Task<int?> ResolveLeaveTypeAsync(string typeInput, CancellationToken ct)
    {
        if (int.TryParse(typeInput, out var id))
            return id;

        var types = await _api.GetLeaveTypesAsync(ct);
        return types.FirstOrDefault(type =>
            type.Name.Equals(typeInput, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static bool IsWeekend(DateTimeOffset date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
