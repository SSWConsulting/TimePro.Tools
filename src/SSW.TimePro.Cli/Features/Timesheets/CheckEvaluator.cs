using System.Globalization;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Timesheets;

/// <summary>
/// Pure, side-effect-free logic for the weekly timesheet check. Kept separate from
/// <see cref="CheckCommand"/> so the leave-aware coverage rules can be unit-tested
/// without HTTP / Spectre.Console.
/// </summary>
public static class CheckEvaluator
{
    private const decimal FullDayHours = 8m;

    /// <summary>A single approved-leave entry reduced to a date range + day coverage.</summary>
    public sealed record LeaveDay(
        DateOnly Start,
        DateOnly End,
        bool AllDay,
        decimal Length,
        string? LeaveTypeName)
    {
        public bool Covers(DateOnly d) => d >= Start && d <= End;

        /// <summary>True when this looks like a public holiday rather than personal leave.</summary>
        public bool IsHoliday =>
            LeaveTypeName is not null &&
            LeaveTypeName.Contains("Holiday", StringComparison.OrdinalIgnoreCase);
    }

    public sealed record Issue(string Severity, string Message);

    public sealed record DayCheck(
        DateOnly Date,
        decimal TotalHours,
        int TimesheetCount,
        int SuggestedCount,
        decimal LeaveHours,
        string? LeaveType,
        bool Covered,
        string CoverReason,
        IReadOnlyList<Issue> Issues)
    {
        public bool HasError => Issues.Any(i => i.Severity == "error");
    }

    /// <summary>
    /// Reduce a raw list of leave entries to approved, date-ranged day records.
    /// Dedupes by Id and keeps only <c>StatusName == "Approved"</c> entries.
    /// Uses the offset-free local date portion (no timezone math), falling back to
    /// <see cref="LeaveEntry.StartDate"/>/<see cref="LeaveEntry.EndDate"/>.
    /// </summary>
    public static List<LeaveDay> ToLeaveDays(IEnumerable<LeaveEntry> entries)
    {
        var result = new List<LeaveDay>();
        var seen = new HashSet<string>();

        foreach (var e in entries)
        {
            if (!string.IsNullOrEmpty(e.Id) && !seen.Add(e.Id))
                continue;

            if (e.StatusName != "Approved")
                continue;

            var start = ParseDate(e.StartDateLocal) ?? ParseDate(e.StartDate);
            var end = ParseDate(e.EndDateLocal) ?? ParseDate(e.EndDate) ?? start;
            if (start is null)
                continue;

            result.Add(new LeaveDay(start.Value, end!.Value, e.AllDay, e.Length, e.LeaveType?.Name));
        }

        return result;
    }

    /// <summary>
    /// Evaluate a single weekday given its real timesheets, suggested count, total logged hours,
    /// and the (already approved) leave applicable to that day.
    /// </summary>
    public static DayCheck EvaluateDay(
        DateOnly date,
        IReadOnlyList<TimesheetItem> realTimesheets,
        int suggestedCount,
        IReadOnlyList<LeaveDay> approvedLeave)
    {
        var loggedHours = realTimesheets.Sum(t => t.TotalTime);
        var onDay = approvedLeave.Where(l => l.Covers(date)).ToList();

        var fullDay = onDay.FirstOrDefault(l => l.AllDay);
        var partial = onDay.Where(l => !l.AllDay).ToList();

        decimal leaveHours;
        string? leaveType;
        bool fullyLeaveCovered;

        if (fullDay is not null)
        {
            leaveHours = FullDayHours;
            leaveType = fullDay.IsHoliday ? "Public Holiday" : (fullDay.LeaveTypeName ?? "Leave");
            fullyLeaveCovered = true;
        }
        else if (partial.Count > 0)
        {
            leaveHours = Math.Min(FullDayHours, partial.Sum(l => l.Length));
            // Prefer a holiday label if any partial entry is a holiday, else first type name.
            var holiday = partial.FirstOrDefault(l => l.IsHoliday);
            leaveType = holiday is not null ? "Public Holiday" : (partial[0].LeaveTypeName ?? "Leave");
            fullyLeaveCovered = false;
        }
        else
        {
            leaveHours = 0m;
            leaveType = null;
            fullyLeaveCovered = false;
        }

        var issues = new List<Issue>();

        bool covered;
        string coverReason;

        if (fullyLeaveCovered)
        {
            // Fully covered by leave / holiday — never an error.
            if (leaveType == "Public Holiday")
            {
                issues.Add(new Issue("info", "Public Holiday"));
                coverReason = "holiday";
            }
            else
            {
                issues.Add(new Issue("info", $"On leave ({leaveType})"));
                coverReason = "leave-full";
            }
            covered = true;

            // A full-day leave with extra logged time over 10h is still worth flagging.
            if (loggedHours > 10m)
                issues.Add(new Issue("warning", $"Over 10 hours ({loggedHours:0.0}h)"));
        }
        else
        {
            covered = (loggedHours + leaveHours) >= FullDayHours;

            if (realTimesheets.Count == 0 && leaveHours == 0m)
            {
                // No timesheets and no leave — a genuine gap.
                issues.Add(new Issue("error", "No timesheets entered"));
                coverReason = "missing";
            }
            else if (partial.Count > 0)
            {
                // Partial leave: expected = the remainder of an 8h day.
                var expected = FullDayHours - leaveHours;
                if (loggedHours < expected)
                    issues.Add(new Issue("warning", $"Under {expected:0.0}h after leave ({loggedHours:0.0}h logged, {leaveHours:0.0}h leave)"));
                coverReason = "leave-partial";
            }
            else
            {
                // No leave, has timesheets: keep the original 7.5h under-hours warning.
                if (loggedHours < 7.5m)
                    issues.Add(new Issue("warning", $"Under 8 hours ({loggedHours:0.0}h)"));
                coverReason = "logged";
            }

            if (loggedHours > 10m)
                issues.Add(new Issue("warning", $"Over 10 hours ({loggedHours:0.0}h)"));

            // Overlap detection.
            for (int i = 0; i < realTimesheets.Count; i++)
                for (int j = i + 1; j < realTimesheets.Count; j++)
                    if (TimesOverlap(realTimesheets[i].StartTime, realTimesheets[i].EndTime,
                                     realTimesheets[j].StartTime, realTimesheets[j].EndTime))
                        issues.Add(new Issue("error", $"Overlap: #{realTimesheets[i].TimeId} and #{realTimesheets[j].TimeId}"));

            // Missing descriptions.
            foreach (var ts in realTimesheets.Where(t => !t.HasNotes || string.IsNullOrWhiteSpace(t.Notes)))
                issues.Add(new Issue("warning", $"#{ts.TimeId} has no description"));
        }

        // Unaccepted suggested timesheets (applies regardless of cover).
        if (suggestedCount > 0)
            issues.Add(new Issue("info", $"{suggestedCount} suggested timesheet(s) not accepted"));

        return new DayCheck(
            date,
            loggedHours,
            realTimesheets.Count,
            suggestedCount,
            leaveHours,
            leaveType,
            covered,
            coverReason,
            issues);
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // Take the date portion only — ignore any time / offset.
        var span = value.AsSpan();
        var tIndex = span.IndexOfAny('T', ' ');
        var datePart = tIndex >= 0 ? span[..tIndex] : span;

        if (DateOnly.TryParse(datePart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            return dateOnly;

        // Fall back to full parse, then drop the time.
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            return DateOnly.FromDateTime(dto.DateTime);

        return null;
    }

    private static bool TimesOverlap(string? start1, string? end1, string? start2, string? end2)
    {
        if (start1 is null || end1 is null || start2 is null || end2 is null) return false;
        try
        {
            var s1 = DateTime.Parse(start1, CultureInfo.InvariantCulture);
            var e1 = DateTime.Parse(end1, CultureInfo.InvariantCulture);
            var s2 = DateTime.Parse(start2, CultureInfo.InvariantCulture);
            var e2 = DateTime.Parse(end2, CultureInfo.InvariantCulture);
            return s1 < e2 && s2 < e1;
        }
        catch
        {
            return false;
        }
    }
}
