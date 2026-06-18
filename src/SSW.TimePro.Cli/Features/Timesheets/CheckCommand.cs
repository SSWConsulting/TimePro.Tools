using System.ComponentModel;
using System.Text.Json.Serialization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Validate timesheets for a week — check for gaps and issues (leave-aware)")]
public class CheckCommand : AsyncCommand<CheckCommand.Settings>
{
    private const int LeavePageSize = 200;

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
            // Fetch leave ONCE for the run — the checked week may be entirely past
            // (e.g. --week -1) or a Mon–Thu leave may already be "past" by Friday,
            // so query both UPCOMING and PAST and merge.
            var approvedLeave = await LoadApprovedLeaveAsync(empId, cancellationToken);

            var dayResults = new List<DayJson>();
            var dayChecks = new List<CheckEvaluator.DayCheck>();
            int errors = 0, warnings = 0, infos = 0;

            for (var d = monday; d <= friday; d = d.AddDays(1))
            {
                var timesheets = await _api.GetTimesheetsAsync(empId, d, cancellationToken);
                var real = timesheets.Where(t => !t.IsSuggested).ToList();
                var suggested = timesheets.Where(t => t.IsSuggested).ToList();

                var check = CheckEvaluator.EvaluateDay(d, real, suggested.Count, approvedLeave);
                dayChecks.Add(check);

                errors += check.Issues.Count(i => i.Severity == "error");
                warnings += check.Issues.Count(i => i.Severity == "warning");
                infos += check.Issues.Count(i => i.Severity == "info");

                dayResults.Add(new DayJson
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
                });
            }

            var allCovered = dayChecks.All(c => c.Covered);

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

            return errors > 0 ? 1 : 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Loads approved leave for the employee across both UPCOMING and PAST filters,
    /// dedupes by Id, and reduces to date-ranged day records.
    /// </summary>
    private async Task<List<CheckEvaluator.LeaveDay>> LoadApprovedLeaveAsync(string empId, CancellationToken ct)
    {
        var entries = new List<LeaveEntry>();

        foreach (var filter in new[] { "UPCOMING", "PAST" })
        {
            var response = await _api.GetLeaveAsync(filter, 1, LeavePageSize, empId, ct);
            var items = response?.Leaves?.Items;
            if (items is not null)
                entries.AddRange(items);
        }

        return CheckEvaluator.ToLeaveDays(entries);
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
