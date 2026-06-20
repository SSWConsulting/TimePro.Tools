using FluentAssertions;
using SSW.TimePro.Cli.Features.Scrum;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Scrum;

/// <summary>
/// Unit tests for <see cref="ScrumItemSelector"/> — the pure selection logic
/// that classifies GitHub PRs/issues into Yesterday / Today / Blockers buckets.
///
/// All fixtures use the Northwind placeholder project (see CLAUDE.md).
/// </summary>
public class ScrumItemSelectorTests
{
    // ── Reference dates ──────────────────────────────────────────────────

    private static readonly DateOnly Tuesday = new(2026, 3, 31);  // arbitrary Tuesday
    private static readonly DateOnly Monday = new(2026, 3, 30);   // day before Tuesday
    private static readonly DateOnly Friday = new(2026, 3, 27);   // day before Monday

    // ── PreviousWorkDay ──────────────────────────────────────────────────

    [Fact]
    public void PreviousWorkDay_OnTuesday_ReturnsMonday()
    {
        ScrumItemSelector.PreviousWorkDay(Tuesday).Should().Be(Monday);
    }

    [Fact]
    public void PreviousWorkDay_OnMonday_ReturnsFriday()
    {
        var monday = new DateOnly(2026, 3, 30);
        ScrumItemSelector.PreviousWorkDay(monday).Should().Be(new DateOnly(2026, 3, 27));
    }

    [Fact]
    public void PreviousWorkDay_OnSunday_ReturnsFriday()
    {
        // Sunday → skip Saturday and Sunday → Friday
        var sunday = new DateOnly(2026, 3, 29);
        ScrumItemSelector.PreviousWorkDay(sunday).Should().Be(new DateOnly(2026, 3, 27));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static DateTimeOffset LocalDt(DateOnly date, int hour = 0) =>
        new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Local));

    private static ScrumItemSelector.GitHubPr OpenPr(
        int number = 42,
        string title = "Product search feature",
        DateTimeOffset? updatedAt = null,
        IReadOnlyList<string>? labels = null,
        bool isDraft = false)
        => new(
            Number: number,
            Title: title,
            Url: $"https://github.com/Northwind/traders-app/pull/{number}",
            State: "OPEN",
            IsDraft: isDraft,
            MergedAt: null,
            UpdatedAt: updatedAt,
            Labels: labels ?? []);

    private static ScrumItemSelector.GitHubPr MergedPr(
        int number = 108,
        string title = "Checkout API fix",
        DateTimeOffset? mergedAt = null,
        IReadOnlyList<string>? labels = null)
        => new(
            Number: number,
            Title: title,
            Url: $"https://github.com/Northwind/traders-app/pull/{number}",
            State: "MERGED",
            IsDraft: false,
            MergedAt: mergedAt ?? LocalDt(Monday, 14),
            UpdatedAt: mergedAt ?? LocalDt(Monday, 14),
            Labels: labels ?? []);

    private static ScrumItemSelector.GitHubIssue OpenIssue(
        int number = 7,
        string title = "Order history page",
        DateTimeOffset? updatedAt = null,
        IReadOnlyList<string>? labels = null)
        => new(
            Number: number,
            Title: title,
            Url: $"https://github.com/Northwind/traders-app/issues/{number}",
            State: "OPEN",
            ClosedAt: null,
            UpdatedAt: updatedAt,
            Labels: labels ?? []);

    private static ScrumItemSelector.GitHubIssue ClosedIssue(
        int number = 13,
        string title = "Search results pagination",
        DateTimeOffset? closedAt = null)
        => new(
            Number: number,
            Title: title,
            Url: $"https://github.com/Northwind/traders-app/issues/{number}",
            State: "CLOSED",
            ClosedAt: closedAt ?? LocalDt(Monday, 11),
            UpdatedAt: closedAt ?? LocalDt(Monday, 11),
            Labels: []);

    // ── Today selection ───────────────────────────────────────────────────

    [Fact]
    public void Select_OpenPr_IsInToday()
    {
        var pr = OpenPr();
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr], []);

        result.Today.Should().ContainSingle(i => i.Reference == "#42" && i.Kind == "PR");
    }

    [Fact]
    public void Select_OpenIssue_IsInToday()
    {
        var issue = OpenIssue();
        var result = ScrumItemSelector.Select(Tuesday, Monday, [], [issue]);

        result.Today.Should().ContainSingle(i => i.Reference == "#7" && i.Kind == "PBI");
    }

    [Fact]
    public void Select_MergedPr_IsNotInToday()
    {
        var pr = MergedPr();
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr], []);

        result.Today.Should().BeEmpty();
    }

    // ── Yesterday selection ───────────────────────────────────────────────

    [Fact]
    public void Select_MergedPrOnPreviousWorkDay_IsInYesterday()
    {
        // Merged at 14:00 on Monday; today is Tuesday → in yesterday window.
        var pr = MergedPr(mergedAt: LocalDt(Monday, 14));
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr], []);

        result.Yesterday.Should().ContainSingle(i => i.Reference == "#108" && i.Status == "✅ Done");
    }

    [Fact]
    public void Select_MergedPrTooOld_IsNotInYesterday()
    {
        // Merged two days before yesterday — too old.
        var pr = MergedPr(mergedAt: LocalDt(new DateOnly(2026, 3, 25), 10));
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr], []);

        result.Yesterday.Should().BeEmpty();
    }

    [Fact]
    public void Select_MergedPrMergedTodayMidnight_IsNotInYesterday()
    {
        // Merged at exactly todayMidnight (00:00 Tuesday) — should NOT be in yesterday
        // (the window is [prevMidnight, todayMidnight), exclusive on the right).
        var todayMidnight = LocalDt(Tuesday, 0);
        var pr = MergedPr(mergedAt: todayMidnight);
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr], []);

        result.Yesterday.Should().BeEmpty();
    }

    [Fact]
    public void Select_OpenPrUpdatedRecently_IsInYesterday()
    {
        // Open PR last updated yesterday afternoon — still in-flight, so yesterday.
        var pr = OpenPr(updatedAt: LocalDt(Monday, 15));
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr], []);

        result.Yesterday.Should().ContainSingle(i => i.Reference == "#42");
    }

    [Fact]
    public void Select_OpenPrUpdatedBeyondLookback_IsNotInYesterday()
    {
        // Open PR untouched for 20 days is beyond the in-progress lookback (14d):
        // it's today's work to resume, but not claimed as yesterday's.
        var pr = OpenPr(updatedAt: LocalDt(Tuesday.AddDays(-20), 15));
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr], []);

        result.Today.Should().ContainSingle(i => i.Reference == "#42");
        result.Yesterday.Should().BeEmpty();
    }

    [Fact]
    public void Select_ClosedIssueOnPreviousWorkDay_IsInYesterday()
    {
        var issue = ClosedIssue(closedAt: LocalDt(Monday, 11));
        var result = ScrumItemSelector.Select(Tuesday, Monday, [], [issue]);

        result.Yesterday.Should().ContainSingle(i => i.Reference == "#13" && i.Status == "✅ Done");
    }

    // ── Blocker selection ─────────────────────────────────────────────────

    [Fact]
    public void Select_OpenPrWithBlockedLabel_IsInBlockers()
    {
        var pr = OpenPr(labels: ["blocked", "bug"]);
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr], []);

        result.Blockers.Should().ContainSingle(i => i.Reference == "#42");
    }

    [Fact]
    public void Select_OpenPrWithBlockedLabel_IsAlsoInToday()
    {
        // Blockers should also appear in Today so the scrum email shows them.
        var pr = OpenPr(labels: ["Blocked"]);
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr], []);

        result.Today.Should().ContainSingle(i => i.Reference == "#42");
        result.Blockers.Should().ContainSingle(i => i.Reference == "#42");
    }

    [Fact]
    public void Select_DraftPr_IsInBlockers()
    {
        var pr = OpenPr(isDraft: true);
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr], []);

        result.Blockers.Should().ContainSingle(i => i.Reference == "#42");
    }

    [Fact]
    public void Select_OpenIssueWithBlockedLabel_IsInBlockers()
    {
        var issue = OpenIssue(labels: ["BLOCKED"]);
        var result = ScrumItemSelector.Select(Tuesday, Monday, [], [issue]);

        result.Blockers.Should().ContainSingle(i => i.Reference == "#7");
    }

    [Fact]
    public void Select_ClosedPrNotBlocked_HasNoBlockers()
    {
        var pr = MergedPr();
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr], []);

        result.Blockers.Should().BeEmpty();
    }

    // ── Deduplication ─────────────────────────────────────────────────────

    [Fact]
    public void Select_SamePr_NotDuplicatedInToday()
    {
        // Two calls with same PR should not duplicate in today.
        var pr = OpenPr();
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr, pr], []);

        result.Today.Should().HaveCount(1);
    }

    [Fact]
    public void Select_OpenPrInYesterday_IsAlsoInToday()
    {
        // Open (in-flight) PRs go into both Yesterday and Today (AutoScrum rule).
        var pr = OpenPr(updatedAt: LocalDt(Monday, 15));
        var result = ScrumItemSelector.Select(Tuesday, Monday, [pr], []);

        result.Yesterday.Should().ContainSingle(i => i.Reference == "#42");
        result.Today.Should().ContainSingle(i => i.Reference == "#42");
    }

    // ── Composite scenario ────────────────────────────────────────────────

    [Fact]
    public void Select_TypicalTuesdayScrum_CorrectBuckets()
    {
        // Product search PR: open, updated yesterday → Today + Yesterday.
        // Checkout API PR: merged yesterday → Yesterday only.
        // Order history issue: open, no label → Today only.
        // Blocked PR: open with "blocked" label → Blockers + Today.
        var prs = new[]
        {
            OpenPr(number: 42, title: "Product search feature", updatedAt: LocalDt(Monday, 15)),
            MergedPr(number: 108, title: "Checkout API fix", mergedAt: LocalDt(Monday, 14)),
            OpenPr(number: 55, title: "Payment gateway integration", updatedAt: LocalDt(Monday, 9), labels: ["blocked"])
        };
        var issues = new[]
        {
            OpenIssue(number: 7, title: "Order history page")
        };

        var result = ScrumItemSelector.Select(Tuesday, Monday, prs, issues);

        // Yesterday: PR42 (in-flight), PR108 (merged), PR55 (blocked, still open, updated yesterday)
        result.Yesterday.Should().HaveCount(3);
        result.Yesterday.Should().Contain(i => i.Reference == "#42");
        result.Yesterday.Should().Contain(i => i.Reference == "#108" && i.Status == "✅ Done");
        result.Yesterday.Should().Contain(i => i.Reference == "#55");

        // Today: PR42, PR55, Issue7
        result.Today.Should().HaveCount(3);
        result.Today.Should().Contain(i => i.Reference == "#42");
        result.Today.Should().Contain(i => i.Reference == "#55");
        result.Today.Should().Contain(i => i.Reference == "#7");

        // Blockers: PR55
        result.Blockers.Should().ContainSingle(i => i.Reference == "#55");
    }
}
