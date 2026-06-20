namespace SSW.TimePro.Cli.Features.Scrum;

/// <summary>
/// Pure, side-effect-free logic for selecting which GitHub PRs and issues
/// belong in each scrum bucket (Yesterday / Today / Blockers). Mirrors
/// AutoScrum's three-bucket selection algorithm (see docs/plans/autoscrum-logic.md)
/// adapted to GitHub's state model instead of Azure DevOps StateChangeDate.
///
/// Deliberately free of IO — all inputs are plain data. Wire up in
/// <see cref="ScrumDataGatherer"/> which handles API/gh calls.
/// </summary>
public static class ScrumItemSelector
{
    // ── Input DTOs ────────────────────────────────────────────────────────

    /// <summary>A GitHub pull request as seen by the selector.</summary>
    public sealed record GitHubPr(
        int Number,
        string Title,
        string Url,
        string State,          // "OPEN", "CLOSED", "MERGED"
        bool IsDraft,
        DateTimeOffset? MergedAt,
        DateTimeOffset? UpdatedAt,
        IReadOnlyList<string> Labels);

    /// <summary>A GitHub issue as seen by the selector.</summary>
    public sealed record GitHubIssue(
        int Number,
        string Title,
        string Url,
        string State,          // "OPEN", "CLOSED"
        DateTimeOffset? ClosedAt,
        DateTimeOffset? UpdatedAt,
        IReadOnlyList<string> Labels);

    // ── Output DTOs ───────────────────────────────────────────────────────

    public sealed record Selection(
        IReadOnlyList<ScrumItem> Yesterday,
        IReadOnlyList<ScrumItem> Today,
        IReadOnlyList<ScrumItem> Blockers);

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Classify <paramref name="prs"/> and <paramref name="issues"/> into the
    /// three scrum buckets using AutoScrum-inspired heuristics.
    /// </summary>
    /// <param name="today">The reference date for "today" (midnight, local).</param>
    /// <param name="previousWorkDay">
    ///   The most recent working day before <paramref name="today"/>
    ///   (Friday when today is Monday; otherwise today-1). Callers must skip
    ///   weekends; public-holiday awareness is a future enhancement.
    /// </param>
    /// <param name="hadPreviousProjectDay">
    ///   True when the caller found a matching timesheet on <paramref name="previousWorkDay"/>
    ///   for the same project(s) as today. Used by the "still in-progress" yesterday rule.
    /// </param>
    /// <param name="prs">PRs to classify.</param>
    /// <param name="issues">Issues to classify.</param>
    public static Selection Select(
        DateOnly today,
        DateOnly previousWorkDay,
        IEnumerable<GitHubPr> prs,
        IEnumerable<GitHubIssue> issues,
        DateTimeOffset? cutoff = null,
        int inProgressLookbackDays = 14)
    {
        // The cutoff is the local boundary between "yesterday" and "today".
        // Defaults to today midnight; callers pass a configured time (e.g. 09:00)
        // so work completed this morning before stand-up still shows as done today.
        var cutoffLocal = cutoff?.LocalDateTime
            ?? today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        var prevMidnight = previousWorkDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        // In-progress items count as "yesterday" if last touched within this window
        // (default 14 days), independent of whether a timesheet exists for the
        // literal previous work day — this survives gaps and sprint boundaries.
        var lookbackFloor = today.AddDays(-inProgressLookbackDays)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);

        var yesterdayItems = new List<ScrumItem>();
        var todayItems = new List<ScrumItem>();
        var blockerItems = new List<ScrumItem>();

        // Track IDs already placed to avoid duplicates across buckets.
        var inYesterday = new HashSet<string>();
        var inToday = new HashSet<string>();
        var inBlockers = new HashSet<string>();

        // Place a completed (merged/closed) item: before the cutoff -> Yesterday
        // (done); from the cutoff onward (i.e. this morning) -> Today (done today),
        // so a merge just before stand-up is never lost. Older than the previous
        // work day is dropped.
        void ClassifyDone(DateTimeOffset? when, string key, Func<string, ScrumItem> make)
        {
            if (!when.HasValue) return;
            var ts = when.Value.LocalDateTime;
            if (ts >= prevMidnight && ts < cutoffLocal && inYesterday.Add(key))
                yesterdayItems.Add(make("✅ Done"));
            else if (ts >= cutoffLocal && inToday.Add(key))
                todayItems.Add(make("✅ Done"));
        }

        // An in-flight (open) item counts as Yesterday when last touched in
        // [lookbackFloor, cutoff) — recent enough, but before today's cutoff.
        bool TouchedInYesterdayWindow(DateTimeOffset? updatedAt) =>
            updatedAt.HasValue &&
            updatedAt.Value.LocalDateTime >= lookbackFloor &&
            updatedAt.Value.LocalDateTime < cutoffLocal;

        // ── PRs ───────────────────────────────────────────────────────────

        foreach (var pr in prs)
        {
            var key = $"PR#{pr.Number}";
            var isBlocked = IsBlocked(pr.Labels) || pr.IsDraft;

            // Blocker check first — blocked items bubble up regardless of state.
            if (isBlocked && pr.State == "OPEN")
            {
                blockerItems.Add(MakePrItem(pr, "❌ Blocked"));
                inBlockers.Add(key);
                // Also appears in Today (consistent with AutoScrum's blocker rendering).
            }

            if (pr.State == "OPEN")
            {
                // Today: any open PR.
                if (!inToday.Contains(key))
                {
                    todayItems.Add(MakePrItem(pr, status: string.Empty));
                    inToday.Add(key);
                }

                // Yesterday: in-flight within the lookback window (before the cutoff).
                if (TouchedInYesterdayWindow(pr.UpdatedAt) && inYesterday.Add(key))
                    yesterdayItems.Add(MakePrItem(pr, status: string.Empty));
            }
            else if (pr.State is "MERGED" or "CLOSED")
            {
                ClassifyDone(pr.MergedAt, key, status => MakePrItem(pr, status));
            }
        }

        // ── Issues ────────────────────────────────────────────────────────

        foreach (var issue in issues)
        {
            var key = $"Issue#{issue.Number}";
            var isBlocked = IsBlocked(issue.Labels);

            if (isBlocked && issue.State == "OPEN")
            {
                blockerItems.Add(MakeIssueItem(issue, "❌ Blocked"));
                inBlockers.Add(key);
            }

            if (issue.State == "OPEN")
            {
                // Today: any open issue assigned to me.
                if (!inToday.Contains(key))
                {
                    todayItems.Add(MakeIssueItem(issue, status: string.Empty));
                    inToday.Add(key);
                }

                // Yesterday: in-flight within the lookback window (before the cutoff).
                if (TouchedInYesterdayWindow(issue.UpdatedAt) && inYesterday.Add(key))
                    yesterdayItems.Add(MakeIssueItem(issue, status: string.Empty));
            }
            else if (issue.State == "CLOSED")
            {
                ClassifyDone(issue.ClosedAt, key, status => MakeIssueItem(issue, status));
            }
        }

        return new Selection(yesterdayItems, todayItems, blockerItems);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the most-recent working day before <paramref name="today"/>,
    /// skipping Saturday and Sunday. Mirrors AutoScrum's DateService.GetPreviousWorkDay.
    /// </summary>
    public static DateOnly PreviousWorkDay(DateOnly today)
    {
        var candidate = today.AddDays(-1);
        while (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            candidate = candidate.AddDays(-1);
        return candidate;
    }

    private static bool IsBlocked(IReadOnlyList<string> labels) =>
        labels.Any(l => l.Contains("blocked", StringComparison.OrdinalIgnoreCase));

    private static ScrumItem MakePrItem(GitHubPr pr, string status) => new()
    {
        Status = status,
        Kind = "PR",
        Title = pr.Title,
        Url = pr.Url,
        Reference = $"#{pr.Number}"
    };

    private static ScrumItem MakeIssueItem(GitHubIssue issue, string status) => new()
    {
        Status = status,
        Kind = "PBI",
        Title = issue.Title,
        Url = issue.Url,
        Reference = $"#{issue.Number}"
    };
}
