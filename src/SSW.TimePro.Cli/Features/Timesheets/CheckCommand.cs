using System.ComponentModel;
using System.Text.Json.Serialization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Validate timesheets for a week — check for gaps and issues (leave-aware)")]
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

        try
        {
            // Shared orchestration with the MCP CheckWeek tool — fetch timesheets +
            // approved leave and evaluate each day in one place.
            var coverage = await WeekCoverageService.EvaluateWeekAsync(_api, empId, offset, cancellationToken);

            var dayChecks = coverage.Days;
            var errors = coverage.Errors;
            var warnings = coverage.Warnings;
            var infos = coverage.Infos;
            var allCovered = coverage.AllCovered;
            var monday = coverage.Monday;
            var friday = coverage.Friday;

            var dayResults = dayChecks.Select(check => new DayJson
            {
                Date = check.Date.ToString("yyyy-MM-dd"),
                DayOfWeek = check.Date.DayOfWeek.ToString(),
                TotalHours = check.TotalHours,
                TimesheetCount = check.TimesheetCount,
                SuggestedCount = check.SuggestedCount,
                LeaveHours = check.LeaveHours,
                LeaveType = check.LeaveType,
                Covered = check.Covered,
                CoverReason = check.CoverReason,
                Issues = check.Issues.Select(i => new IssueJson(i.Severity, i.Message)).ToList()
            }).ToList();

            var result = new
            {
                empId,
                weekStart = monday.ToString("yyyy-MM-dd"),
                weekEnd = friday.ToString("yyyy-MM-dd"),
                errors,
                warnings,
                infos,
                allCovered,
                days = dayResults
            };

            OutputHelper.Render(result, settings.Json, _ =>
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[bold]Week Check: {monday:MMM d} - {friday:MMM d, yyyy}[/]").LeftJustified().RuleStyle("dim"));

                foreach (var check in dayChecks)
                {
                    string icon;
                    if (check.Issues.Any(i => i.Severity == "error"))
                        icon = "[red]x[/]";
                    else if (check.Issues.Any(i => i.Severity == "warning"))
                        icon = "[yellow]![/]";
                    else
                        icon = "[green]v[/]";

                    var marker = check.CoverReason switch
                    {
                        "holiday" => " [blue]PH[/]",
                        "leave-full" => " [blue]Leave[/]",
                        "leave-partial" => $" [blue]Leave {check.LeaveHours:0.0}h[/]",
                        _ => ""
                    };

                    var statusLabel = check.Issues.Count == 0
                        ? "[green]OK[/]"
                        : (!check.HasError && check.Covered ? "[green]OK[/]" : "");

                    AnsiConsole.MarkupLine($" {icon} {check.Date:ddd dd}   {check.TotalHours,5:0.0}h{marker}   {statusLabel}");

                    foreach (var issue in check.Issues)
                    {
                        var color = issue.Severity switch
                        {
                            "error" => "red",
                            "warning" => "yellow",
                            "info" when check.CoverReason is "holiday" or "leave-full" => "blue",
                            _ => "dim"
                        };
                        AnsiConsole.MarkupLine($"              [{color}]{Markup.Escape(issue.Message)}[/]");
                    }
                }

                AnsiConsole.Write(new Rule().RuleStyle("dim"));

                if (errors == 0 && warnings == 0)
                    OutputHelper.WriteSuccess(allCovered ? "All clear — every day covered" : "All clear — no issues found");
                else
                    AnsiConsole.MarkupLine($" [red]{errors} error(s)[/], [yellow]{warnings} warning(s)[/], [dim]{infos} info(s)[/]");

                AnsiConsole.WriteLine();
            });

            // --json mode: the check ran fine, so a week with gaps is data (errors/allCovered
            // in the payload), not a process failure. Human mode keeps non-zero for shell gating.
            return settings.Json ? 0 : (errors > 0 ? 1 : 0);
        }
        catch (ApiException ex)
        {
            if (settings.Json)
                OutputHelper.WriteJsonError($"API error: {ex.Message}", ex.StatusCode);
            else
                OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

    private static string ResolveEmpId(string? requestedEmpId, string defaultEmpId) =>
        string.IsNullOrWhiteSpace(requestedEmpId) ? defaultEmpId : requestedEmpId.Trim();

    /// <summary>Per-day JSON shape. <see cref="LeaveType"/> is always emitted (null when no leave).</summary>
    private sealed class DayJson
    {
        public string Date { get; init; } = string.Empty;
        public string DayOfWeek { get; init; } = string.Empty;
        public decimal TotalHours { get; init; }
        public int TimesheetCount { get; init; }
        public int SuggestedCount { get; init; }
        public decimal LeaveHours { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? LeaveType { get; init; }

        public bool Covered { get; init; }
        public string CoverReason { get; init; } = string.Empty;
        public IReadOnlyList<IssueJson> Issues { get; init; } = [];
    }

    private sealed record IssueJson(string Severity, string Message);
}
