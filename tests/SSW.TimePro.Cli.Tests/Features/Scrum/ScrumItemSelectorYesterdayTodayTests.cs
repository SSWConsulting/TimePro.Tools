using FluentAssertions;
using SSW.TimePro.Cli.Features.Scrum;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Scrum;

/// <summary>
/// Focused tests for the Yesterday/Today selection boundaries — the part of the
/// daily-scrum algorithm that has historically been wrong in AutoScrum:
///   • a Monday scrum must include work that landed over the WEEKEND (Sat/Sun),
///     because the yesterday window starts at the previous work day;
///   • an in-progress item is "yesterday" if last touched within the lookback
///     window (default 14 days) — surviving gaps and sprint boundaries — but
///     something only TOUCHED after the cutoff is today's, not yesterday's;
///   • the cutoff (default midnight; configurable, e.g. 09:00) splits done work:
///     completed before it → Yesterday(done); completed after it → Today(done).
///
/// Reference week: Mon 2026-06-22 (prev work day = Fri 2026-06-19, spanning
/// Sat 06-20 and Sun 06-21). Northwind-only placeholders (see CLAUDE.md).
/// </summary>
public class ScrumItemSelectorYesterdayTodayTests
{
    private static readonly DateOnly Monday = new(2026, 6, 22);
    private static readonly DateOnly Friday = new(2026, 6, 19);   // PreviousWorkDay(Monday)
    private static readonly DateOnly Saturday = new(2026, 6, 20);
    private static readonly DateOnly Sunday = new(2026, 6, 21);
    private static readonly DateOnly Thursday = new(2026, 6, 18);

    private static DateTimeOffset Local(DateOnly d, int hour = 12, int min = 0, int sec = 0) =>
        new(d.ToDateTime(new TimeOnly(hour, min, sec), DateTimeKind.Local));

    private static ScrumItemSelector.GitHubPr OpenPr(int n, DateTimeOffset? updated = null) =>
        new(n, $"PR {n}", $"https://example.test/{n}", "OPEN", IsDraft: false, MergedAt: null, UpdatedAt: updated, Labels: []);

    private static ScrumItemSelector.GitHubPr MergedPr(int n, DateTimeOffset merged) =>
        new(n, $"PR {n}", $"https://example.test/{n}", "MERGED", IsDraft: false, MergedAt: merged, UpdatedAt: merged, Labels: []);

    private static ScrumItemSelector.GitHubIssue ClosedIssue(int n, DateTimeOffset closed) =>
        new(n, $"Issue {n}", $"https://example.test/{n}", "CLOSED", ClosedAt: closed, UpdatedAt: closed, Labels: []);

    // cutoff defaults to null => Monday midnight boundary (standard).
    private static ScrumItemSelector.Selection Run(
        IEnumerable<ScrumItemSelector.GitHubPr>? prs = null,
        IEnumerable<ScrumItemSelector.GitHubIssue>? issues = null,
        DateTimeOffset? cutoff = null) =>
        ScrumItemSelector.Select(Monday, ScrumItemSelector.PreviousWorkDay(Monday),
            prs ?? [], issues ?? [], cutoff);

    // ── The weekend-spanning case (classic AutoScrum bug) ────────────────────

    [Fact]
    public void Monday_PrMergedSaturday_IsInYesterday()
    {
        var result = Run(prs: [MergedPr(20, Local(Saturday))]);
        result.Yesterday.Should().ContainSingle(i => i.Reference == "#20");
    }

    [Fact]
    public void Monday_PrMergedSunday_IsInYesterday()
    {
        var result = Run(prs: [MergedPr(21, Local(Sunday))]);
        result.Yesterday.Should().ContainSingle(i => i.Reference == "#21");
    }

    [Fact]
    public void Monday_ClosedIssueOverTheWeekend_IsInYesterday()
    {
        var result = Run(issues: [ClosedIssue(20, Local(Saturday, 9))]);
        result.Yesterday.Should().ContainSingle(i => i.Reference == "#20");
    }

    // ── Window boundaries: [prevWorkDay 00:00, today 00:00) ──────────────────

    [Fact]
    public void PrMergedExactlyAtPreviousWorkDayMidnight_IsInYesterday()
    {
        // Lower bound is INCLUSIVE.
        var result = Run(prs: [MergedPr(19, Local(Friday, 0))]);
        result.Yesterday.Should().ContainSingle(i => i.Reference == "#19");
    }

    [Fact]
    public void PrMergedJustBeforePreviousWorkDayMidnight_IsNotInYesterday()
    {
        // One second before Friday 00:00 (i.e. Thursday 23:59:59) is outside the window.
        var result = Run(prs: [MergedPr(18, Local(Thursday, 23, 59, 59))]);
        result.Yesterday.Should().BeEmpty();
    }

    [Fact]
    public void PrMergedThisMorning_ShowsAsTodayDone_NotLost()
    {
        // A PR merged this morning is past the (midnight) yesterday window, so it
        // now surfaces under Today as done — it must not silently vanish.
        var result = Run(prs: [MergedPr(22, Local(Monday, 9))]);
        result.Yesterday.Should().BeEmpty();
        result.Today.Should().ContainSingle(i => i.Reference == "#22" && i.Status.Contains("Done"));
    }

    // ── "Touched today" must not be claimed as yesterday ─────────────────────

    [Fact]
    public void OpenPrTouchedToday_IsInToday_NotYesterday()
    {
        // In-flight PR whose last activity is TODAY: it's today's work, not yesterday's,
        // even though there was a matching timesheet on the previous work day.
        var result = Run(prs: [OpenPr(30, updated: Local(Monday, 9))]);

        result.Today.Should().ContainSingle(i => i.Reference == "#30");
        result.Yesterday.Should().BeEmpty();
    }

    [Fact]
    public void OpenPrUpdatedExactlyAtTodayMidnight_IsNotInYesterday()
    {
        // Upper bound is EXCLUSIVE: updated at exactly today 00:00 is not "yesterday".
        var result = Run(prs: [OpenPr(31, updated: Local(Monday, 0))]);

        result.Today.Should().ContainSingle(i => i.Reference == "#31");
        result.Yesterday.Should().BeEmpty();
    }

    [Fact]
    public void OpenPrInFlightOverWeekend_IsInYesterdayAndToday()
    {
        // Last touched Friday, still open Monday → both worked-on-yesterday and on-today.
        var result = Run(prs: [OpenPr(32, updated: Local(Friday, 15))]);

        result.Yesterday.Should().ContainSingle(i => i.Reference == "#32");
        result.Today.Should().ContainSingle(i => i.Reference == "#32");
    }

    // ── 14-day in-progress lookback (sprint-boundary mitigation) ─────────────

    [Fact]
    public void InProgressTouchedTenDaysAgo_IsInYesterday()
    {
        // Even with NO activity on the literal previous work day (e.g. first day of a
        // new sprint), an item touched within the 14-day window still counts as
        // yesterday's ongoing work.
        var result = Run(prs: [OpenPr(34, updated: Local(Monday.AddDays(-10), 12))]);

        result.Yesterday.Should().ContainSingle(i => i.Reference == "#34");
        result.Today.Should().ContainSingle(i => i.Reference == "#34");
    }

    [Fact]
    public void InProgressTouchedBeyondLookback_IsNotInYesterday()
    {
        // Stale in-progress (touched 20 days ago) is today's work to pick up again,
        // but not claimed as yesterday.
        var result = Run(prs: [OpenPr(35, updated: Local(Monday.AddDays(-20), 12))]);

        result.Today.Should().ContainSingle(i => i.Reference == "#35");
        result.Yesterday.Should().BeEmpty();
    }

    [Fact]
    public void InProgressTouchedExactlyAtLookbackFloor_IsInYesterday()
    {
        // Lower bound of the lookback is inclusive: midnight, 14 days before today.
        var result = Run(prs: [OpenPr(36, updated: Local(Monday.AddDays(-14), 0))]);
        result.Yesterday.Should().ContainSingle(i => i.Reference == "#36");
    }

    // ── Configurable cutoff (the user's 09:00 scenario) ──────────────────────

    [Fact]
    public void Cutoff9am_MergeBeforeCutoff_IsYesterday_AfterCutoff_IsTodayDone()
    {
        // Stand-up at 10:00 with a 09:00 cutoff: a PR merged 08:30 is yesterday's;
        // a PR merged 09:30 is today's done. Neither is lost.
        var cutoff = Local(Monday, 9, 0);
        var result = Run(
            prs: [MergedPr(50, Local(Monday, 8, 30)), MergedPr(51, Local(Monday, 9, 30))],
            cutoff: cutoff);

        result.Yesterday.Should().ContainSingle(i => i.Reference == "#50");
        result.Today.Should().ContainSingle(i => i.Reference == "#51" && i.Status.Contains("Done"));
    }

    [Fact]
    public void Cutoff9am_OpenItemTouchedAfterCutoff_IsTodayOnly()
    {
        // An open item touched at 09:30 (after the 09:00 cutoff) is today's, not yesterday.
        var result = Run(prs: [OpenPr(52, updated: Local(Monday, 9, 30))], cutoff: Local(Monday, 9, 0));

        result.Today.Should().ContainSingle(i => i.Reference == "#52");
        result.Yesterday.Should().BeEmpty();
    }

    // ── Realistic Monday-morning composite ───────────────────────────────────

    [Fact]
    public void RealisticMondayScrum_SpansWeekendAndSeparatesTodayFromYesterday()
    {
        var prs = new[]
        {
            OpenPr(40, updated: Local(Friday, 16)),  // in-flight since Friday → Yesterday + Today
            MergedPr(41, Local(Saturday, 10)),       // merged over the weekend → Yesterday
            MergedPr(42, Local(Sunday, 18)),         // merged over the weekend → Yesterday
            OpenPr(43, updated: Local(Monday, 8)),   // started this morning → Today only
        };

        var result = Run(prs: prs);

        result.Yesterday.Select(i => i.Reference).Should()
            .BeEquivalentTo(["#40", "#41", "#42"]);
        result.Today.Select(i => i.Reference).Should()
            .BeEquivalentTo(["#40", "#43"]);
    }
}
