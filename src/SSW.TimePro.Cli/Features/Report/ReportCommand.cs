using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Report;

[Description("Monthly summary with billable %, WFH breakdown, and project details")]
public class ReportCommand : AsyncCommand<ReportCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--month [OFFSET]")]
        [Description("Month offset: 0=this month (default), -1=last month")]
        [DefaultValue(null)]
        public FlagValue<int>? Month { get; set; }

        [CommandOption("--from <DATE>")]
        [Description("Start date (yyyy-MM-dd)")]
        public string? From { get; set; }

        [CommandOption("--to <DATE>")]
        [Description("End date (yyyy-MM-dd)")]
        public string? To { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public ReportCommand(ITimeProApiClient api, IConfigService config)
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

        var (start, end, label) = ResolvePeriod(settings);

        try
        {
            // Fetch project summary
            var projects = await _api.GetProjectsSummaryAsync(tenant.EmployeeId, start, end, CancellationToken.None);

            // Fetch day-by-day for location analysis
            var locationCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int daysEntered = 0;
            int workingDays = 0;

            for (var d = start; d <= end; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;
                workingDays++;

                var dayTs = await _api.GetTimesheetsAsync(tenant.EmployeeId, d, CancellationToken.None);
                var real = dayTs.Where(t => !t.IsSuggested).ToList();
                if (real.Count == 0) continue;

                daysEntered++;
                var loc = real.First().Location ?? "Unknown";
                locationCounts.TryGetValue(loc, out var count);
                locationCounts[loc] = count + 1;
            }

            var totalHours = projects.Sum(p => p.Sum);
            var billableHours = projects.Where(p => p.IsBillable).Sum(p => p.Sum);
            var writeOffHours = totalHours - billableHours;
            var billablePct = totalHours > 0 ? billableHours / totalHours * 100 : 0;

            var result = new
            {
                period = label,
                startDate = start.ToString("yyyy-MM-dd"),
                endDate = end.ToString("yyyy-MM-dd"),
                workingDays,
                daysEntered,
                daysMissing = workingDays - daysEntered,
                totalHours,
                billableHours,
                writeOffHours,
                billablePercent = Math.Round(billablePct, 1),
                locations = locationCounts.OrderByDescending(x => x.Value)
                    .Select(x => new { location = x.Key, days = x.Value }),
                projects = projects.OrderByDescending(p => p.Sum)
                    .Select(p => new { p.ProjectName, p.ClientID, p.Sum, p.IsBillable })
            };

            OutputHelper.Render(result, settings.Json, _ =>
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[bold]Report: {label}[/]").RuleStyle("blue"));
                AnsiConsole.WriteLine();

                // Overview
                var overview = new Table().NoBorder().HideHeaders().AddColumn("K").AddColumn("V");
                overview.AddRow("[bold]Working days[/]", $"{workingDays}");
                overview.AddRow("[bold]Days entered[/]", $"{daysEntered}");
                if (workingDays - daysEntered > 0)
                    overview.AddRow("[bold]Days missing[/]", $"[red]{workingDays - daysEntered}[/]");
                overview.AddRow("[bold]Total hours[/]", $"{totalHours:0.0}h");
                overview.AddRow("", "");
                overview.AddRow("[bold]Billable[/]", $"[green]{billableHours:0.0}h[/] ({billablePct:0.0}%)");
                overview.AddRow("[bold]Write-off[/]", $"[dim]{writeOffHours:0.0}h[/] ({(totalHours > 0 ? 100 - billablePct : 0):0.0}%)");
                AnsiConsole.Write(overview);

                // Location
                if (locationCounts.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.Write(new Rule("[bold]Location[/]").LeftJustified().RuleStyle("dim"));
                    foreach (var (loc, days) in locationCounts.OrderByDescending(x => x.Value))
                    {
                        var pct = daysEntered > 0 ? (double)days / daysEntered * 100 : 0;
                        AnsiConsole.MarkupLine($"  {Markup.Escape(loc),-20} {days,3} days  ({pct:0.0}%)");
                    }
                }

                // Projects
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule("[bold]By Project[/]").LeftJustified().RuleStyle("dim"));
                foreach (var p in projects.OrderByDescending(p => p.Sum))
                {
                    var billable = p.IsBillable ? "[green]B[/]" : "[dim]W[/]";
                    AnsiConsole.MarkupLine($"  {Markup.Escape(p.ProjectName ?? "?"),-40} {p.Sum,6:0.0}h  {billable}");
                }

                AnsiConsole.WriteLine();
            });

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

    private static (DateOnly start, DateOnly end, string label) ResolvePeriod(Settings settings)
    {
        if (settings.From is not null && settings.To is not null)
        {
            var s = DateOnly.ParseExact(settings.From, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var e = DateOnly.ParseExact(settings.To, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return (s, e, $"{s:MMM d} - {e:MMM d, yyyy}");
        }

        var offset = (settings.Month is not null && settings.Month.IsSet) ? settings.Month.Value : 0;
        var target = DateTime.Today.AddMonths(offset);
        var first = new DateOnly(target.Year, target.Month, 1);
        var last = offset == 0 ? DateOnly.FromDateTime(DateTime.Today) : first.AddMonths(1).AddDays(-1);
        return (first, last, target.ToString("MMMM yyyy"));
    }
}
