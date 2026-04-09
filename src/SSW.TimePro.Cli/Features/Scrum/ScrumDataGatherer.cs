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
    private readonly GhCli _gh;

    public ScrumDataGatherer(ITimeProApiClient api, IConfigService config, GhCli gh)
    {
        _api = api;
        _config = config;
        _gh = gh;
    }

    public async Task<ScrumModel> BuildAsync(
        string employeeId,
        DateOnly today,
        string? projectFilter,
        bool? forceInternal,
        CancellationToken ct)
    {
        var global = _config.LoadGlobalConfig();
        var mappings = _config.LoadRepoMappings();

        // 1. Today's timesheets — tells us which project(s) we're on today
        var todaysSheets = await _api.GetTimesheetsAsync(employeeId, today, ct);
        var realToday = todaysSheets.Where(t => !t.IsSuggested && !IsLeave(t)).ToList();

        // Optional project filter
        var todaysProjects = (projectFilter is not null
                ? realToday.Where(t => string.Equals(t.ProjectId, projectFilter, StringComparison.OrdinalIgnoreCase))
                : realToday)
            .Select(t => (t.ClientId, t.ProjectId, t.Client, t.Project))
            .Where(t => t.ProjectId is not null)
            .Distinct()
            .ToList();

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
            PrimaryClientName = todaysProjects.FirstOrDefault().Client
        };

        // 3. Today bullets — timesheet notes + current open PRs in each project's repo
        foreach (var proj in todaysProjects)
        {
            var note = realToday
                .Where(t => t.ProjectId == proj.ProjectId && !string.IsNullOrWhiteSpace(t.Notes))
                .Select(t => t.Notes!.Trim())
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(note))
            {
                model.Today.Add(new ScrumItem
                {
                    Kind = "PBI",
                    Title = TruncateNote(note)
                });
            }

            // Open PRs by me in the issues repo (if mapped)
            var issuesRepo = ResolveIssuesRepo(mappings, proj.ClientId, proj.ProjectId);
            if (issuesRepo is not null)
            {
                var prs = _gh.ListMyPullRequests(issuesRepo);
                foreach (var pr in prs.Where(p => string.Equals(p.State, "OPEN", StringComparison.OrdinalIgnoreCase)))
                {
                    model.Today.Add(new ScrumItem
                    {
                        Kind = "PBI",
                        Title = pr.Title,
                        Url = pr.Url,
                        Reference = $"#{pr.Number}"
                    });
                }
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
                var note = yesterdaySheets
                    .Where(t => t.ProjectId == proj.ProjectId && !string.IsNullOrWhiteSpace(t.Notes))
                    .Select(t => t.Notes!.Trim())
                    .FirstOrDefault();
                if (!string.IsNullOrEmpty(note))
                {
                    model.Yesterday.Add(new ScrumItem
                    {
                        Status = "✅ Done",
                        Kind = "",
                        Title = TruncateNote(note)
                    });
                }

                // Bleed-back: merged PRs by me since the last-last project day, up to yesterday.
                var issuesRepo = ResolveIssuesRepo(mappings, proj.ClientId, proj.ProjectId);
                if (issuesRepo is not null)
                {
                    // Window start: the day BEFORE the previous-previous project day, or 7 days back.
                    var windowStart = today.AddDays(-7);
                    var prs = _gh.ListMyPullRequests(issuesRepo);
                    var mergedInWindow = prs
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
        // (skipping org-wide wildcards like "github.com/asfaudits/*").
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

    private static string TruncateNote(string note)
    {
        // Strip auto-generated leave/EasyLeave prefix if any, collapse whitespace.
        var cleaned = note.Replace("\r", "").Trim();
        var firstLine = cleaned.Split('\n')[0].Trim();
        return firstLine.Length > 220 ? firstLine[..217] + "…" : firstLine;
    }
}
