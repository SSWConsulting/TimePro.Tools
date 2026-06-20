using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Scrum;

/// <summary>
/// Builds a <see cref="ScrumModel"/> by combining TimePro data (timesheets,
/// bookings), repo mappings and GitHub activity from the local <c>gh</c> CLI.
/// Deliberately tolerant: missing data degrades to empty sections rather than
/// erroring out — a thin scrum is still useful.
/// </summary>
public class ScrumDataGatherer
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;
    private readonly IGhCli _gh;

    public ScrumDataGatherer(ITimeProApiClient api, IConfigService config, IGhCli gh)
    {
        _api = api;
        _config = config;
        _gh = gh;
    }

    public async Task<ScrumModel> BuildAsync(
        string employeeId,
        DateOnly today,
        IReadOnlyList<string>? projectOverrides,
        bool? forceInternal,
        CancellationToken ct,
        bool smartSelection = false)
    {
        var global = _config.LoadGlobalConfig();
        var mappings = _config.LoadRepoMappings();

        // 1. Today's timesheets — tells us which project(s) we're on today
        var todaysSheets = await _api.GetTimesheetsAsync(employeeId, today, ct);
        var realToday = todaysSheets.Where(t => !t.IsSuggested && !IsLeave(t)).ToList();

        // Projects to build the scrum around. By default these are auto-detected
        // from today's logged timesheets. When --project is passed it OVERRIDES
        // that: each requested project is included even if nothing is logged for
        // it yet (its client/name come from the repo mapping), so you can scrum
        // projects you work on but haven't logged/suggested.
        var autoDetected = realToday
            .Select(t => (ClientId: (string?)t.ClientId, ProjectId: (string?)t.ProjectId,
                          Client: (string?)t.Client, Project: (string?)t.Project))
            .Where(t => t.ProjectId is not null)
            .Distinct()
            .ToList();

        List<(string? ClientId, string? ProjectId, string? Client, string? Project)> todaysProjects;
        if (projectOverrides is { Count: > 0 })
        {
            todaysProjects = [];
            foreach (var id in projectOverrides)
            {
                var logged = autoDetected.FirstOrDefault(p =>
                    string.Equals(p.ProjectId, id, StringComparison.OrdinalIgnoreCase));
                if (logged.ProjectId is not null)
                {
                    todaysProjects.Add(logged);
                    continue;
                }

                // Not logged today — synthesise from the repo mapping so its
                // GitHub activity can still be gathered.
                var map = mappings.FirstOrDefault(m =>
                    string.Equals(m.ProjectId, id, StringComparison.OrdinalIgnoreCase));
                todaysProjects.Add((map?.ClientId, id, map?.ProjectName, map?.ProjectName));
            }
            todaysProjects = todaysProjects.Distinct().ToList();
        }
        else
        {
            todaysProjects = autoDetected;
        }

        // 2. Classify internal vs external using bookings for today.
        //    External = a non-SSW booking OR a non-SSW timesheet exists.
        //    Internal = everything we see is SSW, and no client booking.
        var appointments = await _api.GetAppointmentsAsync(employeeId, today, today, ct);
        var hasClientBooking = appointments.Any(a => !IsSswClient(a.ClientId));
        var hasClientTimesheet = realToday.Any(t => !IsSswClient(t.ClientId));
        var isInternal = forceInternal ?? (!hasClientBooking && !hasClientTimesheet);

        var model = new ScrumModel
        {
            TodayDate = today,
            IsInternal = isInternal,
            PrimaryClientId = todaysProjects.FirstOrDefault().ClientId,
            PrimaryClientName = todaysProjects.FirstOrDefault().Client
        };

        // 3. Today bullets — timesheet notes are captured as separate metadata
        //    (for agents/skills to read via --json) but intentionally NOT
        //    rendered as bullets. Bullets come from GitHub activity only.
        foreach (var proj in todaysProjects)
        {
            foreach (var note in realToday
                         .Where(t => t.ProjectId == proj.ProjectId && !string.IsNullOrWhiteSpace(t.Notes))
                         .Select(t => t.Notes!.Trim()))
            {
                model.TodayNotes.Add(note);
            }
        }

        // 4. Yesterday = previous working day where I logged the same project(s).
        //    Walk back up to 14 calendar days.
        var (yesterday, yesterdaySheets) = await FindPreviousProjectDayAsync(
            employeeId, today, todaysProjects.Select(p => p.ProjectId!).ToHashSet(), ct);
        model.YesterdayDate = yesterday;

        if (yesterday is not null && yesterdaySheets is not null)
        {
            foreach (var proj in todaysProjects)
            {
                foreach (var note in yesterdaySheets
                             .Where(t => t.ProjectId == proj.ProjectId && !string.IsNullOrWhiteSpace(t.Notes))
                             .Select(t => t.Notes!.Trim()))
                {
                    model.YesterdayNotes.Add(note);
                }
            }
        }

        // 5. GitHub activity — populate Today/Yesterday/Blockers bullets.
        //    --smart: uses ScrumItemSelector (AutoScrum-inspired heuristics).
        //    default: original behaviour (open PRs for today, merged PRs for yesterday).
        var previousWorkDay = ScrumItemSelector.PreviousWorkDay(today);
        // Yesterday/today boundary. Defaults to midnight (null); when a cutoff time
        // is configured (e.g. 09:00) work completed this morning before stand-up
        // still appears as done today.
        DateTimeOffset? cutoff = global.Scrum.CutoffTime is { } cutoffTime
            ? new DateTimeOffset(today.ToDateTime(cutoffTime, DateTimeKind.Local))
            : null;

        foreach (var proj in todaysProjects)
        {
            var issuesRepo = ResolveIssuesRepo(mappings, proj.ClientId, proj.ProjectId);
            if (issuesRepo is null) continue;

            var rawPrs = _gh.ListMyPullRequests(issuesRepo);

            if (smartSelection)
            {
                var rawIssues = _gh.ListMyAssignedIssues(issuesRepo);

                // Map to selector's input types (GhCli doesn't carry labels/draft yet —
                // extend GhCli in a follow-up; for now use empty labels and isDraft=false).
                var selectorPrs = rawPrs.Select(p => new ScrumItemSelector.GitHubPr(
                    Number: p.Number,
                    Title: p.Title,
                    Url: p.Url,
                    State: p.State.ToUpperInvariant(),
                    IsDraft: false,
                    MergedAt: p.MergedAt,
                    UpdatedAt: p.UpdatedAt,
                    Labels: []));

                var selectorIssues = rawIssues.Select(i => new ScrumItemSelector.GitHubIssue(
                    Number: i.Number,
                    Title: i.Title,
                    Url: i.Url,
                    State: "OPEN",   // gh issue list --state open returns open issues only
                    ClosedAt: null,
                    UpdatedAt: null,
                    Labels: []));

                var selection = ScrumItemSelector.Select(
                    today, previousWorkDay, selectorPrs, selectorIssues,
                    cutoff, global.Scrum.YesterdayLookbackDays);

                foreach (var item in selection.Yesterday) model.Yesterday.Add(item);
                foreach (var item in selection.Today) model.Today.Add(item);
                foreach (var item in selection.Blockers) model.Blockers.Add(item);
            }
            else
            {
                // Original behaviour: open PRs → Today; merged PRs in 7-day window → Yesterday.
                foreach (var pr in rawPrs.Where(p => string.Equals(p.State, "OPEN", StringComparison.OrdinalIgnoreCase)))
                {
                    model.Today.Add(new ScrumItem
                    {
                        Kind = "PBI",
                        Title = pr.Title,
                        Url = pr.Url,
                        Reference = $"#{pr.Number}"
                    });
                }

                if (yesterday is not null)
                {
                    // Window start: the day BEFORE the previous-previous project day, or 7 days back.
                    var windowStart = today.AddDays(-7);
                    var mergedInWindow = rawPrs
                        .Where(p => p.MergedAt.HasValue)
                        .Where(p => DateOnly.FromDateTime(p.MergedAt!.Value.LocalDateTime) >= windowStart
                                 && DateOnly.FromDateTime(p.MergedAt!.Value.LocalDateTime) <= yesterday.Value)
                        .OrderByDescending(p => p.MergedAt)
                        .ToList();
                    foreach (var pr in mergedInWindow)
                    {
                        model.Yesterday.Add(new ScrumItem
                        {
                            Status = "✅ Done",
                            Kind = "PBI",
                            Title = pr.Title,
                            Url = pr.Url,
                            Reference = $"#{pr.Number}"
                        });
                    }
                }
            }
        }

        // 5. Internal block
        if (isInternal)
        {
            model.Internal = new InternalBlock
            {
                TrelloUrl = global.Scrum.TrelloUrl,
                DaysUntilNextClientBooking = await FindDaysUntilNextClientBookingAsync(employeeId, today, ct),
                JoinedScrumMeeting = true
            };
        }

        return model;
    }

    // --- Helpers --------------------------------------------------------

    private async Task<(DateOnly? date, List<TimesheetItem>? sheets)> FindPreviousProjectDayAsync(
        string employeeId, DateOnly today, HashSet<string> projectIds, CancellationToken ct)
    {
        if (projectIds.Count == 0) return (null, null);

        for (int back = 1; back <= 14; back++)
        {
            var candidate = today.AddDays(-back);
            // Skip weekends. Public holidays / leave days will just have zero
            // non-leave timesheets and fall through naturally.
            if (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            var sheets = await _api.GetTimesheetsAsync(employeeId, candidate, ct);
            var real = sheets.Where(t => !t.IsSuggested && !IsLeave(t)).ToList();
            if (real.Any(t => t.ProjectId is not null && projectIds.Contains(t.ProjectId)))
                return (candidate, real);
        }
        return (null, null);
    }

    private async Task<int?> FindDaysUntilNextClientBookingAsync(
        string employeeId, DateOnly today, CancellationToken ct)
    {
        try
        {
            var end = today.AddDays(30);
            var appts = await _api.GetAppointmentsAsync(employeeId, today, end, ct);
            var nextClient = appts
                .Where(a => !IsSswClient(a.ClientId))
                .Select(a => ParseApptDate(a.Start))
                .Where(d => d.HasValue && d.Value >= today)
                .OrderBy(d => d)
                .FirstOrDefault();
            if (nextClient is null) return null;
            return (nextClient.Value.DayNumber - today.DayNumber);
        }
        catch
        {
            return null;
        }
    }

    private static DateOnly? ParseApptDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParse(s, out var dt)) return DateOnly.FromDateTime(dt);
        return null;
    }

    private static string? ResolveIssuesRepo(List<RepoMappingEntry> mappings, string? clientId, string? projectId)
    {
        if (clientId is null || projectId is null) return null;
        var candidates = mappings
            .Where(m => string.Equals(m.ClientId, clientId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(m.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) return null;

        // Prefer any mapping that explicitly sets IssuesRepo.
        var explicitMatch = candidates.FirstOrDefault(m => !string.IsNullOrEmpty(m.IssuesRepo));
        if (explicitMatch is not null) return explicitMatch.IssuesRepo;

        // Otherwise fall back to a concrete "github.com/org/repo" remote
        // (skipping org-wide wildcards like "github.com/Northwind/*").
        var remoteMatch = candidates
            .Select(m => m.RemotePattern)
            .FirstOrDefault(r => r is not null && r.Contains("github.com/") && !r.EndsWith("/*"));
        if (remoteMatch is null) return null;
        var idx = remoteMatch.IndexOf("github.com/", StringComparison.Ordinal);
        return remoteMatch[(idx + "github.com/".Length)..].TrimEnd('/');
    }

    private static bool IsSswClient(string? clientId) =>
        string.Equals(clientId, "SSW", StringComparison.OrdinalIgnoreCase);

    private static bool IsLeave(TimesheetItem t) =>
        string.Equals(t.ProjectId, "LEAVE", StringComparison.OrdinalIgnoreCase) || t.IsLeave;

}
