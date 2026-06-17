using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Validate timesheets for a week — check for gaps and issues")]
public class CheckCommand : AsyncCommand<CheckCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--week [OFFSET]")]
        [Description("Week to check. 0=this week (default), -1=last week")]
        [DefaultValue(null)]
        public FlagValue<int>? Week { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }

        [CommandOption("--emp-id|--employee-id|--employee <EMP_ID>")]
        [Description("empId. Defaults to the current user")]
        public string? EmpId { get; set; }
    }

    public CheckCommand(ITimeProApiClient api, IConfigService config)
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

        var offset = (settings.Week is not null && settings.Week.IsSet) ? settings.Week.Value : 0;
        var empId = ResolveEmpId(settings.EmpId, tenant.EmployeeId);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monday = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday + (offset * 7));
        if (today.DayOfWeek == DayOfWeek.Sunday)
            monday = monday.AddDays(-7);
        var friday = monday.AddDays(4);

        try
        {
            var dayResults = new List<object>();
            int errors = 0, warnings = 0, infos = 0;

            for (var d = monday; d <= friday; d = d.AddDays(1))
            {
                var timesheets = await _api.GetTimesheetsAsync(empId, d, CancellationToken.None);
                var real = timesheets.Where(t => !t.IsSuggested).ToList();
                var suggested = timesheets.Where(t => t.IsSuggested).ToList();
                var totalHours = real.Sum(t => t.TotalTime);

                var issues = new List<(string severity, string message)>();

                if (real.Count == 0)
                {
                    issues.Add(("error", "No timesheets entered"));
                    errors++;
                }
                else if (totalHours < 7.5m)
                {
                    issues.Add(("warning", $"Under 8 hours ({totalHours:0.0}h)"));
                    warnings++;
                }

                if (totalHours > 10m)
                {
                    issues.Add(("warning", $"Over 10 hours ({totalHours:0.0}h)"));
                    warnings++;
                }

                // Check overlapping times
                for (int i = 0; i < real.Count; i++)
                {
                    for (int j = i + 1; j < real.Count; j++)
                    {
                        if (TimesOverlap(real[i].StartTime, real[i].EndTime, real[j].StartTime, real[j].EndTime))
                        {
                            issues.Add(("error", $"Overlap: #{real[i].TimeId} and #{real[j].TimeId}"));
                            errors++;
                        }
                    }
                }

                // Check for missing descriptions
                foreach (var ts in real.Where(t => !t.HasNotes || string.IsNullOrWhiteSpace(t.Notes)))
                {
                    issues.Add(("warning", $"#{ts.TimeId} has no description"));
                    warnings++;
                }

                // Unaccepted suggested timesheets
                if (suggested.Count > 0)
                {
                    issues.Add(("info", $"{suggested.Count} suggested timesheet(s) not accepted"));
                    infos++;
                }

                dayResults.Add(new
                {
                    date = d.ToString("yyyy-MM-dd"),
                    dayOfWeek = d.DayOfWeek.ToString(),
                    totalHours,
                    timesheetCount = real.Count,
                    suggestedCount = suggested.Count,
                    issues = issues.Select(i => new { i.severity, i.message })
                });
            }

            var result = new
            {
                empId,
                weekStart = monday.ToString("yyyy-MM-dd"),
                weekEnd = friday.ToString("yyyy-MM-dd"),
                errors,
                warnings,
                infos,
                days = dayResults
            };

            OutputHelper.Render(result, settings.Json, _ =>
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[bold]Week Check: {monday:MMM d} - {friday:MMM d, yyyy}[/]").LeftJustified().RuleStyle("dim"));

                foreach (var dayObj in dayResults)
                {
                    // Use dynamic to access anonymous type
                    var day = (dynamic)dayObj;
                    string date = day.date;
                    string dow = day.dayOfWeek;
                    decimal hours = day.totalHours;
                    var issueList = ((IEnumerable<dynamic>)day.issues).ToList();

                    string icon;
                    if (issueList.Any(i => (string)i.severity == "error"))
                        icon = "[red]x[/]";
                    else if (issueList.Any(i => (string)i.severity == "warning"))
                        icon = "[yellow]![/]";
                    else
                        icon = "[green]v[/]";

                    var dateOnly = DateOnly.ParseExact(date, "yyyy-MM-dd");
                    AnsiConsole.MarkupLine($" {icon} {dateOnly:ddd dd}   {hours,5:0.0}h   {(issueList.Count == 0 ? "[green]OK[/]" : "")}");

                    foreach (var issue in issueList)
                    {
                        string sev = issue.severity;
                        string msg = issue.message;
                        var color = sev switch { "error" => "red", "warning" => "yellow", _ => "dim" };
                        AnsiConsole.MarkupLine($"              [{color}]{Markup.Escape(msg)}[/]");
                    }
                }

                AnsiConsole.Write(new Rule().RuleStyle("dim"));

                if (errors == 0 && warnings == 0)
                    OutputHelper.WriteSuccess("All clear — no issues found");
                else
                    AnsiConsole.MarkupLine($" [red]{errors} error(s)[/], [yellow]{warnings} warning(s)[/], [dim]{infos} info(s)[/]");

                AnsiConsole.WriteLine();
            });

            return errors > 0 ? 1 : 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

    private static bool TimesOverlap(string? start1, string? end1, string? start2, string? end2)
    {
        if (start1 is null || end1 is null || start2 is null || end2 is null) return false;
        try
        {
            var s1 = DateTime.Parse(start1);
            var e1 = DateTime.Parse(end1);
            var s2 = DateTime.Parse(start2);
            var e2 = DateTime.Parse(end2);
            return s1 < e2 && s2 < e1;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveEmpId(string? requestedEmpId, string defaultEmpId) =>
        string.IsNullOrWhiteSpace(requestedEmpId) ? defaultEmpId : requestedEmpId.Trim();
}
