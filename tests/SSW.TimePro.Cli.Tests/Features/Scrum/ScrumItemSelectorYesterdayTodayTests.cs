using FluentAssertions;
using SSW.TimePro.Cli.Features.Scrum;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Scrum;

/// <summary>
/// Focused tests for the Yesterday/Today selection boundaries — the part of the
/// daily-scrum algorithm that has historically been wrong in AutoScrum:
///   • a Monday scrum must include work that landed over the WEEKEND (Sat/Sun),
///     because the yesterday window is [previousWorkDay 00:00, today 00:00);
///   • something you only TOUCHED today must not be claimed as "yesterday";
///   • the window is inclusive at the start (prevWorkDay midnight) and exclusive
///     at the end (today midnight).
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

    private static ScrumItemSelector.Selection Run(
        IEnumerable<ScrumItemSelector.GitHubPr>? prs = null,
        IEnumerable<ScrumItemSelector.GitHubIssue>? issues = null,
        bool hadPreviousProjectDay = true) =>
        ScrumItemSelector.Select(Monday, ScrumItemSelector.PreviousWorkDay(Monday),
            hadPreviousProjectDay, prs ?? [], issues ?? []);

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
    public void PrMergedToday_IsInNeitherBucket()
    {
        // Characterization: a PR merged THIS MORNING (>= today midnight) is past the
        // yesterday window, and merged PRs never go to Today. So it shows in neither.
        // (Documented gap — pre-scrum merges can vanish; revisit if it bites.)
        var result = Run(prs: [MergedPr(22, Local(Monday, 9))]);
        result.Yesterday.Should().BeEmpty();
        result.Today.Should().BeEmpty();
    }

    // ── "Touched today" must not be claimed as yesterday ─────────────────────

    [Fact]
    public void OpenPrTouchedToday_IsInToday_NotYesterday()
    {
        // In-flight PR whose last activity is TODAY: it's today's work, not yesterday's,
        // even though there was a matching timesheet on the previous work day.
        var result = Run(prs: [OpenPr(30, updated: Local(Monday, 9))], hadPreviousProjectDay: true);

        result.Today.Should().ContainSingle(i => i.Reference == "#30");
        result.Yesterday.Should().BeEmpty();
    }

    [Fact]
    public void OpenPrUpdatedExactlyAtTodayMidnight_IsNotInYesterday()
    {
        // Upper bound is EXCLUSIVE: updated at exactly today 00:00 is not "yesterday".
        var result = Run(prs: [OpenPr(31, updated: Local(Monday, 0))], hadPreviousProjectDay: true);

        result.Today.Should().ContainSingle(i => i.Reference == "#31");
        result.Yesterday.Should().BeEmpty();
    }

    [Fact]
    public void OpenPrInFlightOverWeekend_IsInYesterdayAndToday()
    {
        // Last touched Friday, still open Monday → both worked-on-yesterday and on-today.
        var result = Run(prs: [OpenPr(32, updated: Local(Friday, 15))], hadPreviousProjectDay: true);

        result.Yesterday.Should().ContainSingle(i => i.Reference == "#32");
        result.Today.Should().ContainSingle(i => i.Reference == "#32");
    }

    [Fact]
    public void OpenPrInFlight_NotInYesterday_WhenNoPreviousProjectDay()
    {
        // Same PR, but no matching timesheet on the previous work day → no yesterday claim.
        var result = Run(prs: [OpenPr(33, updated: Local(Friday, 15))], hadPreviousProjectDay: false);

        result.Today.Should().ContainSingle(i => i.Reference == "#33");
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

        var result = Run(prs: prs, hadPreviousProjectDay: true);

        result.Yesterday.Select(i => i.Reference).Should()
            .BeEquivalentTo(["#40", "#41", "#42"]);
        result.Today.Select(i => i.Reference).Should()
            .BeEquivalentTo(["#40", "#43"]);
    }
}
