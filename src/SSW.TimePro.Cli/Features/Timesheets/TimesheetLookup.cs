using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Timesheets;

public sealed record TimesheetDay(DateOnly Date, IReadOnlyList<TimesheetItem> Entries);

/// <summary>Whether a date range read covers Saturday and Sunday. The two surfaces answer differently.</summary>
public enum WeekendPolicy
{
    Include,
    Skip
}

/// <summary>
/// Finding an entry and reading one back after a write. Both the read-merge update path and the
/// empty-body write responses need a day's worth of entries, so the lookups are shared.
/// </summary>
public static class TimesheetLookup
{
    private const int SearchDays = 28;

    /// <summary>
    /// Reads every day in <paramref name="start"/>..<paramref name="end"/> in order. Callers pass
    /// their own <paramref name="weekends"/> policy: <c>ts get</c> shows weekend work, the MCP
    /// GetTimesheets tool skips it.
    /// </summary>
    public static async Task<List<TimesheetDay>> ForRangeAsync(
        ITimeProApiClient api,
        string empId,
        DateOnly start,
        DateOnly end,
        WeekendPolicy weekends,
        CancellationToken ct = default)
    {
        var days = new List<TimesheetDay>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (weekends == WeekendPolicy.Skip && d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            days.Add(new TimesheetDay(d, await api.GetTimesheetsAsync(empId, d, ct)));
        }

        return days;
    }

    /// <summary>
    /// The suggestion read behind <c>ts suggest</c> and the MCP GetSuggestedTimesheets tool.
    /// The refresh is a server-side write that regenerates the day's suggestions, so it happens
    /// exactly once here rather than once per surface.
    /// </summary>
    public static async Task<TimesheetDay> RefreshAndReadSuggestedAsync(
        ITimeProApiClient api,
        string empId,
        DateOnly date,
        CancellationToken ct = default)
    {
        await api.RefreshSuggestedTimesheetsAsync(empId, date, ct);

        var entries = await api.GetTimesheetsAsync(empId, date, ct);
        return new TimesheetDay(date, entries.Where(t => t.IsSuggested).ToList());
    }

    public static async Task<(TimesheetItem Item, TimesheetDay Day)?> FindAsync(
        ITimeProApiClient api,
        string empId,
        int timesheetId,
        string? dateHint,
        CancellationToken ct = default)
    {
        if (dateHint is not null)
        {
            var date = ParseDate(dateHint);
            var entries = await api.GetTimesheetsAsync(empId, date, ct);
            var hit = entries.FirstOrDefault(t => t.TimeId == timesheetId);
            return hit is null ? null : (hit, new TimesheetDay(date, entries));
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var d = today; d >= today.AddDays(-SearchDays); d = d.AddDays(-1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            var entries = await api.GetTimesheetsAsync(empId, d, ct);
            var match = entries.FirstOrDefault(t => t.TimeId == timesheetId);
            if (match is not null)
                return (match, new TimesheetDay(d, entries));
        }

        return null;
    }

    /// <summary>
    /// Suggestions are rejected by the delete endpoint with a bare 400, so both the CLI and the MCP
    /// delete surfaces check first. An entry that cannot be found is left to the API.
    /// </summary>
    public static async Task EnsureDeletableAsync(
        ITimeProApiClient api,
        string empId,
        int timesheetId,
        string? dateHint,
        CancellationToken ct = default)
    {
        var found = await FindAsync(api, empId, timesheetId, dateHint, ct);
        if (found?.Item.IsSuggested == true)
            throw new TimesheetValidationException(
                $"Timesheet {timesheetId} is a suggestion and cannot be deleted. Accept it first: tp ts accept {timesheetId}");
    }

    public static async Task<TimesheetItem?> ReadByIdAsync(
        ITimeProApiClient api,
        string empId,
        DateOnly date,
        int timesheetId,
        CancellationToken ct = default)
    {
        try
        {
            var entries = await api.GetTimesheetsAsync(empId, date, ct);
            return entries.FirstOrDefault(t => t.TimeId == timesheetId);
        }
        catch (ApiException)
        {
            // The write already succeeded; a failed read-back must not turn it into an error.
            return null;
        }
    }

    /// <summary>
    /// Locates a freshly created entry when the API answered with an empty body, matching the day's
    /// entries on project, description and start time.
    /// </summary>
    public static async Task<TimesheetItem?> ReadCreatedAsync(
        ITimeProApiClient api,
        string empId,
        DateOnly date,
        TimesheetRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var entries = await api.GetTimesheetsAsync(empId, date, ct);
            return entries
                .Where(t => !t.IsSuggested)
                .Where(t => string.Equals(t.ProjectId, request.ProjectId, StringComparison.OrdinalIgnoreCase))
                .Where(t => string.Equals(t.Notes?.Trim() ?? "", request.Note?.Trim() ?? "", StringComparison.Ordinal))
                .Where(t => TimeMatches(t.StartTime, request.TimeStart))
                .MaxBy(t => t.TimeId);
        }
        catch (ApiException)
        {
            return null;
        }
    }

    public static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly? DateOf(TimesheetItem item)
    {
        var raw = item.Date?.Split('T')[0] ?? item.StartTime?.Split('T')[0];
        return raw is not null && DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>Compares two API timestamps to the minute, ignoring whether a date part is present.</summary>
    public static bool SameClockTime(string? left, string? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return HourMinute(left) == HourMinute(right);
    }

    private static bool TimeMatches(string? actual, string? requested)
    {
        if (requested is null)
            return true;
        if (actual is null)
            return false;

        return HourMinute(actual) == HourMinute(requested);
    }

    private static string HourMinute(string value)
    {
        var timePart = value.Contains('T') ? value.Split('T')[1] : value;
        var pieces = timePart.Split(':');
        return pieces.Length >= 2 ? $"{pieces[0]}:{pieces[1]}" : timePart;
    }
}
