using System.Text.Json.Serialization;

namespace SSW.TimePro.Cli.Features.Timesheets;

/// <summary>
/// The week-coverage document shared by <c>ts check --json</c> and the MCP CheckWeek tool, so the
/// two surfaces cannot drift into describing the same coverage differently.
/// </summary>
public sealed record WeekCheckResult(
    string EmpId,
    string WeekStart,
    string WeekEnd,
    int Errors,
    int Warnings,
    int Infos,
    bool AllCovered,
    int PendingSuggestions,
    IReadOnlyList<WeekCheckDay> Days)
{
    public static WeekCheckResult From(WeekCoverageService.WeekCoverage coverage) => new(
        coverage.EmpId,
        coverage.Monday.ToString("yyyy-MM-dd"),
        coverage.Friday.ToString("yyyy-MM-dd"),
        coverage.Errors,
        coverage.Warnings,
        coverage.Infos,
        coverage.AllCovered,
        CheckEvaluator.CountPendingSuggestions(coverage.Days),
        coverage.Days.Select(WeekCheckDay.From).ToList());
}

public sealed record WeekCheckDay(
    string Date,
    string DayOfWeek,
    decimal TotalHours,
    int TimesheetCount,
    int SuggestedCount,
    decimal LeaveHours,
    // Always emitted, null when the day has no leave, so consumers can read it unconditionally.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? LeaveType,
    bool Covered,
    string CoverReason,
    IReadOnlyList<WeekCheckIssue> Issues)
{
    public static WeekCheckDay From(CheckEvaluator.DayCheck check) => new(
        check.Date.ToString("yyyy-MM-dd"),
        check.Date.DayOfWeek.ToString(),
        check.TotalHours,
        check.TimesheetCount,
        check.SuggestedCount,
        check.LeaveHours,
        check.LeaveType,
        check.Covered,
        check.CoverReason,
        check.Issues.Select(i => new WeekCheckIssue(i.Severity, i.Message)).ToList());
}

public sealed record WeekCheckIssue(string Severity, string Message);
