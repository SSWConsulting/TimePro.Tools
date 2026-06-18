using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Projects;

/// <summary>
/// <c>tp project recent</c> — surfaces the user's RECENT/LIKELY timesheet
/// projects, deduped and ranked by why-they're-likely, so an agent can prefetch
/// a small candidate list instead of dumping the full repo map.
///
/// Sources (all read-only): weekly timesheets (actual + suggested + leave),
/// CRM bookings (client-level signal), and repo mappings (decoration). Walks
/// back week-by-week up to <c>--max-weeks</c>, always covering at least
/// <see cref="MinWeeks"/> weeks and stopping early once at least
/// <c>--min-projects</c> distinct projects are found.
///
/// Ranking priority: booked (very high) &gt; suggested (high) &gt; recent actual
/// (medium) &gt; leave (low). Ported from the validated Python prototype.
/// </summary>
[Description("Surface recent/likely projects for timesheet filling")]
public class RecentCommand : AsyncCommand<RecentCommand.Settings>
{
    private const int WindowDays = 30;        // "recent" recency-decay window
    private const int MinWeeks = 4;           // always cover at least this many weeks
    private const int BookingFutureDays = 14; // upcoming bookings count as "likely"

    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--min-projects <N>")]
        [Description("Keep walking back until at least this many distinct projects are found")]
        [DefaultValue(5)]
        public int MinProjects { get; set; } = 5;

        [CommandOption("--max-weeks <N>")]
        [Description("Hard cap on how many weeks back to scan")]
        [DefaultValue(12)]
        public int MaxWeeks { get; set; } = 12;

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public RecentCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        try
        {
            var result = await BuildAsync(tenant.EmployeeId, today, settings, cancellationToken);

            OutputHelper.Render(result, settings.Json, RenderHuman);
            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

    private async Task<RecentProjectsResult> BuildAsync(
        string empId, DateOnly today, Settings settings, CancellationToken ct)
    {
        var maps = _config.LoadRepoMappings();

        // ---- maps: aggregate per (clientId, projectId) ----
        var byProjMap = new Dictionary<(string?, string?), MapAgg>();
        foreach (var m in maps)
        {
            var key = (NullIfEmpty(m.ClientId), NullIfEmpty(m.ProjectId));
            if (!byProjMap.TryGetValue(key, out var g))
            {
                g = new MapAgg();
                byProjMap[key] = g;
            }
            if (!string.IsNullOrEmpty(m.PathPattern))
                g.Paths.Add(m.PathPattern);
            if (string.IsNullOrEmpty(g.Name) && !string.IsNullOrEmpty(m.ProjectName))
                g.Name = m.ProjectName!;
            g.CategoryId ??= m.CategoryId;
            if (!string.IsNullOrEmpty(m.IssuesRepo))
                g.IssuesRepos.Add(m.IssuesRepo!);
        }

        // ---- bookings (clientId only): window 30 days back .. 14 days future ----
        var winStart = today.AddDays(-WindowDays);
        var winEnd = today.AddDays(BookingFutureDays);
        var booked = new Dictionary<string, BookedAgg>();
        // GetAppointmentsAsync end is exclusive in callers (they pass end.AddDays(1)),
        // so widen by a day to make winEnd inclusive.
        var appts = await _api.GetAppointmentsAsync(empId, winStart, winEnd.AddDays(1), ct);
        foreach (var b in appts)
        {
            var d = ParseDate(b.Start);
            var cid = NullIfEmpty(b.ClientId);
            if (d is null || cid is null || d < winStart || d > winEnd)
                continue;
            if (!booked.TryGetValue(cid, out var e))
            {
                e = new BookedAgg();
                booked[cid] = e;
            }
            e.Count++;
            var title = b.Title?.Trim();
            if (!string.IsNullOrEmpty(title))
                e.Title = title;
            if (e.Last is null || d > e.Last)
                e.Last = d;
        }

        // ---- timesheets: walk back week-by-week until enough distinct projects ----
        var proj = new Dictionary<(string?, string?), ProjAgg>();
        var thisMonday = StartOfWeek(today);
        var weeksScanned = 0;
        var maxWeeks = Math.Max(1, settings.MaxWeeks);
        var minProjects = Math.Max(0, settings.MinProjects);

        for (var n = 0; n < maxWeeks; n++)
        {
            var monday = thisMonday.AddDays(-7 * n);
            weeksScanned = n + 1;

            for (var d = monday; d <= monday.AddDays(6); d = d.AddDays(1))
            {
                var sheets = await _api.GetTimesheetsAsync(empId, d, ct);
                foreach (var ts in sheets)
                {
                    var cid = NullIfEmpty(ts.ClientId);
                    var pid = NullIfEmpty(ts.ProjectId);
                    if (cid is null || pid is null)
                        continue;
                    var key = (cid, pid);
                    if (!proj.TryGetValue(key, out var p))
                    {
                        p = new ProjAgg();
                        proj[key] = p;
                    }
                    if (string.IsNullOrEmpty(p.Name) && !string.IsNullOrWhiteSpace(ts.Project))
                        p.Name = ts.Project!.Trim();
                    if (string.IsNullOrEmpty(p.Client) && !string.IsNullOrWhiteSpace(ts.Client))
                        p.Client = ts.Client!.Trim();
                    if (IsLeave(ts))
                        p.Leave++;
                    else if (ts.IsSuggested)
                        p.Suggested++;
                    else
                        p.Actual++;
                    var tsDate = ParseDate(ts.Date);
                    if (tsDate is not null && (p.Last is null || tsDate > p.Last))
                        p.Last = tsDate;
                }
            }

            // Stop once we have enough, but always cover MinWeeks.
            if (weeksScanned >= MinWeeks && proj.Count >= minProjects)
                break;
        }

        // Seed booked clients that have a map but no recent timesheet (still likely).
        foreach (var (key, g) in byProjMap)
        {
            var (cid, _) = key;
            if (cid is not null && booked.ContainsKey(cid) && !proj.ContainsKey(key))
            {
                proj[key] = new ProjAgg { Name = g.Name };
            }
        }

        // ---- score + reasons ----
        var projects = new List<RecentProject>();
        foreach (var ((cid, pid), p) in proj)
        {
            byProjMap.TryGetValue((cid, pid), out var g);
            BookedAgg? b = cid is not null && booked.TryGetValue(cid, out var bv) ? bv : null;

            var score = 0.0;
            var reasons = new List<string>();
            string? tier = null; // very high | high | medium | low

            if (b is not null)
            {
                score += 100 + b.Count * 2 + Recency(b.Last, today) * 10;
                reasons.Add($"booked x{b.Count} (last {FormatDate(b.Last)})");
                tier = "very high";
            }
            if (p.Suggested > 0)
            {
                score += 30 + p.Suggested * 2;
                reasons.Add($"{p.Suggested} suggested ts");
                tier ??= "high";
            }
            if (p.Actual > 0)
            {
                score += p.Actual * 3 + Recency(p.Last, today) * 10;
                reasons.Add($"{p.Actual} recent ts");
                tier ??= "medium";
            }
            if (p.Leave > 0)
            {
                score += p.Leave;
                reasons.Add($"{p.Leave} leave");
                tier ??= "low";
            }
            if (reasons.Count == 0)
                continue;

            var lastUsed = p.Last ?? b?.Last;
            projects.Add(new RecentProject
            {
                ProjectId = pid,
                ProjectName = !string.IsNullOrEmpty(p.Name) ? p.Name : g?.Name ?? string.Empty,
                ClientId = cid,
                Client = p.Client,
                CategoryId = g?.CategoryId,
                LastUsed = lastUsed is not null ? FormatDate(lastUsed) : null,
                Likelihood = tier,
                Score = Math.Round(score, 1),
                Why = string.Join("; ", reasons),
                Paths = g?.Paths ?? new List<string>(),
                IssuesRepos = g is not null && g.IssuesRepos.Count > 0 ? g.IssuesRepos : null,
                Mapped = g is not null && g.Paths.Count > 0
            });
        }

        projects = projects.OrderByDescending(x => x.Score).ToList();

        var mappedClientIds = maps
            .Select(m => NullIfEmpty(m.ClientId))
            .Where(c => c is not null)
            .ToHashSet();

        var unmapped = booked
            .Where(kvp => !mappedClientIds.Contains(kvp.Key))
            .Select(kvp => new BookedClientWithoutMap
            {
                ClientId = kvp.Key,
                Title = kvp.Value.Title,
                Last = FormatDate(kvp.Value.Last),
                Count = kvp.Value.Count
            })
            .ToList();

        return new RecentProjectsResult
        {
            GeneratedFor = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            WeeksScanned = weeksScanned,
            TotalMaps = maps.Count,
            RecentProjects = projects.Count,
            Projects = projects,
            BookedClientsWithoutMap = unmapped
        };
    }

    private static void RenderHuman(RecentProjectsResult result)
    {
        AnsiConsole.MarkupLine(
            $"[bold]Recent projects:[/] {result.RecentProjects} " +
            $"[dim](scanned {result.WeeksScanned} wk, {result.TotalMaps} maps)[/]");
        AnsiConsole.WriteLine();

        if (result.Projects.Count == 0)
        {
            OutputHelper.WriteInfo("No recent projects found.");
        }
        else
        {
            var table = new Table()
                .AddColumn("Likelihood")
                .AddColumn("Client / Project")
                .AddColumn("IDs")
                .AddColumn("Category")
                .AddColumn("Last used")
                .AddColumn("Why")
                .AddColumn("Repos");

            foreach (var p in result.Projects)
            {
                var likelihood = p.Likelihood switch
                {
                    "very high" => "[green]very high[/]",
                    "high" => "[green]high[/]",
                    "medium" => "[yellow]medium[/]",
                    "low" => "[dim]low[/]",
                    _ => "[dim]?[/]"
                };
                var clientProject = $"{Markup.Escape(p.Client ?? p.ClientId ?? "?")} / {Markup.Escape(p.ProjectName ?? "?")}";
                if (!p.Mapped)
                    clientProject += " [dim](unmapped)[/]";

                var repos = p.Paths.Count > 0
                    ? Markup.Escape(string.Join("\n", p.Paths))
                    : "[dim]—[/]";

                table.AddRow(
                    likelihood,
                    clientProject,
                    Markup.Escape($"{p.ClientId}/{p.ProjectId}"),
                    p.CategoryId is not null ? Markup.Escape(p.CategoryId) : "[dim]—[/]",
                    p.LastUsed is not null ? Markup.Escape(p.LastUsed) : "[dim]—[/]",
                    Markup.Escape(p.Why ?? ""),
                    repos);
            }

            AnsiConsole.Write(table);
        }

        if (result.BookedClientsWithoutMap.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Booked clients with NO map (consider `tp map set`):[/]");
            foreach (var u in result.BookedClientsWithoutMap)
            {
                AnsiConsole.MarkupLine(
                    $"  - {Markup.Escape(u.Title ?? u.ClientId ?? "?")} " +
                    $"([dim]{Markup.Escape(u.ClientId ?? "?")}[/]) — booked x{u.Count}, last {Markup.Escape(u.Last ?? "?")}");
            }
        }
    }

    // --- Helpers --------------------------------------------------------

    private static DateOnly StartOfWeek(DateOnly today)
    {
        var monday = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        if (today.DayOfWeek == DayOfWeek.Sunday)
            monday = monday.AddDays(-7);
        return monday;
    }

    private static DateOnly? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // Take the date portion of ISO / "T" / space separated values.
        var datePart = s.Replace("T", " ").Split(' ')[0];
        if (DateOnly.TryParse(datePart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }

    private static string? FormatDate(DateOnly? d) =>
        d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static double Recency(DateOnly? d, DateOnly today)
    {
        if (d is null) return 0.0;
        var ageDays = today.DayNumber - d.Value.DayNumber;
        return Math.Max(0.0, 1.0 - ageDays / (double)WindowDays);
    }

    private static bool IsLeave(TimesheetItem t) =>
        string.Equals(t.ProjectId, "LEAVE", StringComparison.OrdinalIgnoreCase) || t.IsLeave;

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    // --- Aggregation scratch types --------------------------------------

    private sealed class MapAgg
    {
        public List<string> Paths { get; } = new();
        public string Name { get; set; } = string.Empty;
        public string? CategoryId { get; set; }
        public List<string> IssuesRepos { get; } = new();
    }

    private sealed class BookedAgg
    {
        public int Count { get; set; }
        public DateOnly? Last { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    private sealed class ProjAgg
    {
        public string Name { get; set; } = string.Empty;
        public string Client { get; set; } = string.Empty;
        public int Actual { get; set; }
        public int Suggested { get; set; }
        public int Leave { get; set; }
        public DateOnly? Last { get; set; }
    }

    // --- Result DTOs (camelCased by OutputHelper) -----------------------

    public sealed class RecentProjectsResult
    {
        public string GeneratedFor { get; set; } = string.Empty;
        public int WeeksScanned { get; set; }
        public int TotalMaps { get; set; }
        public int RecentProjects { get; set; }
        public List<RecentProject> Projects { get; set; } = new();
        public List<BookedClientWithoutMap> BookedClientsWithoutMap { get; set; } = new();
    }

    public sealed class RecentProject
    {
        public string? ProjectId { get; set; }
        public string? ProjectName { get; set; }
        public string? ClientId { get; set; }
        public string? Client { get; set; }
        public string? CategoryId { get; set; }
        public string? LastUsed { get; set; }
        public string? Likelihood { get; set; }
        public double Score { get; set; }
        public string? Why { get; set; }
        public List<string> Paths { get; set; } = new();
        public List<string>? IssuesRepos { get; set; }
        public bool Mapped { get; set; }
    }

    public sealed class BookedClientWithoutMap
    {
        public string? ClientId { get; set; }
        public string? Title { get; set; }
        public string? Last { get; set; }
        public int Count { get; set; }
    }
}
