using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Query;

[Description("Query timesheets across employees, clients, and projects")]
public class QueryCommand : AsyncCommand<QueryCommand.Settings>
{
    private readonly ITimeProApiClient _api;

    public class Settings : CommandSettings
    {
        [CommandOption("--from <DATE>")]
        [Description("Start date (yyyy-MM-dd). Defaults to start of current FY (Jul 1)")]
        public string? From { get; set; }

        [CommandOption("--to <DATE>")]
        [Description("End date (yyyy-MM-dd). Defaults to today")]
        public string? To { get; set; }

        [CommandOption("--client <IDS>")]
        [Description("Client ID(s), comma-separated")]
        public string? ClientIds { get; set; }

        [CommandOption("--project <IDS>")]
        [Description("Project ID(s), comma-separated")]
        public string? ProjectIds { get; set; }

        [CommandOption("--emp-id|--employee-id|--employee|--emp <IDS>")]
        [Description("empId(s), comma-separated")]
        public string? EmpIds { get; set; }

        [CommandOption("--category <IDS>")]
        [Description("Category ID(s), comma-separated")]
        public string? CategoryIds { get; set; }

        [CommandOption("--group-by <FIELD>")]
        [Description("Group results by: employee (default), project, client, none")]
        public string GroupBy { get; set; } = "employee";

        [CommandOption("--limit <N>")]
        [Description("Max rows to display in table (default: 50)")]
        public int Limit { get; set; } = 50;

        [CommandOption("--page <N>")]
        [Description("Page number when using --limit (default: 1)")]
        public int Page { get; set; } = 1;

        [CommandOption("--json")]
        [Description("Output raw timesheet entries as JSON (no grouping, no limit)")]
        public bool Json { get; set; }
    }

    public QueryCommand(ITimeProApiClient api) => _api = api;

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var (start, end) = ResolveDateRange(settings);

        var filter = new TimesheetSummaryFilter
        {
            StartDate = start.ToString("yyyy-MM-dd"),
            EndDate = end.ToString("yyyy-MM-dd"),
            ClientIds = ParseCsv(settings.ClientIds),
            ProjectIds = ParseCsv(settings.ProjectIds),
            EmployeeIds = ParseCsv(settings.EmpIds),
            CategoryIds = ParseCsv(settings.CategoryIds)
        };

        try
        {
            var entries = await _api.QueryTimesheetsAsync(filter, CancellationToken.None);

            // --json: raw export, no grouping, no limit
            if (settings.Json)
            {
                OutputHelper.WriteJson(entries);
                return 0;
            }

            if (entries.Count == 0)
            {
                OutputHelper.WriteInfo("No timesheets found matching the query");
                return 0;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule(
                $"[bold]Query: {start:MMM d yyyy} - {end:MMM d yyyy}[/] [dim]({entries.Count} entries)[/]")
                .LeftJustified().RuleStyle("dim"));
            AnsiConsole.WriteLine();

            switch (settings.GroupBy.ToLowerInvariant())
            {
                case "employee":
                    RenderGroupedByEmployee(entries);
                    break;
                case "project":
                    RenderGroupedByProject(entries);
                    break;
                case "client":
                    RenderGroupedByClient(entries);
                    break;
                case "none":
                    RenderFlat(entries, settings.Limit, settings.Page);
                    break;
                default:
                    OutputHelper.WriteError($"Unknown --group-by value: '{settings.GroupBy}'. Use: employee, project, client, none");
                    return 1;
            }

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

    private static void RenderGroupedByEmployee(List<TimesheetSummaryEntry> entries)
    {
        var groups = entries
            .GroupBy(e => e.EmployeeName ?? e.EmpId ?? "Unknown")
            .Select(g => new { Name = g.Key, Hours = g.Sum(e => e.TotalHours) })
            .OrderByDescending(g => g.Hours)
            .ToList();

        var total = groups.Sum(g => g.Hours);

        var table = new Table().NoBorder()
            .AddColumn(new TableColumn("Employee").NoWrap())
            .AddColumn(new TableColumn("Hours").RightAligned())
            .AddColumn(new TableColumn("%").RightAligned());

        foreach (var g in groups)
        {
            var pct = total > 0 ? g.Hours / total * 100 : 0;
            table.AddRow(Markup.Escape(g.Name), $"{g.Hours:0.0}h", $"{pct:0.0}%");
        }

        table.AddEmptyRow();
        table.AddRow("[bold]Total[/]", $"[bold]{total:0.0}h[/]", "");

        AnsiConsole.Write(table);
    }

    private static void RenderGroupedByProject(List<TimesheetSummaryEntry> entries)
    {
        var groups = entries
            .GroupBy(e => (e.ProjectName ?? e.ProjectId ?? "?", e.ClientName ?? e.ClientId ?? "?"))
            .Select(g => new
            {
                Project = g.Key.Item1,
                Client = g.Key.Item2,
                Hours = g.Sum(e => e.TotalHours),
                Employees = g.Select(e => e.EmployeeName ?? e.EmpId).Distinct().Count()
            })
            .OrderByDescending(g => g.Hours)
            .ToList();

        var total = groups.Sum(g => g.Hours);

        foreach (var g in groups)
        {
            var pct = total > 0 ? g.Hours / total * 100 : 0;
            AnsiConsole.MarkupLine(
                $"  {Markup.Escape(g.Project),-45} {g.Hours,8:0.0}h  {g.Employees,2} people  {pct,5:0.0}%");
        }

        AnsiConsole.Write(new Rule().RuleStyle("dim"));
        AnsiConsole.MarkupLine($"  [bold]{"Total",-45} {total,8:0.0}h[/]");
        AnsiConsole.WriteLine();
    }

    private static void RenderGroupedByClient(List<TimesheetSummaryEntry> entries)
    {
        var groups = entries
            .GroupBy(e => e.ClientName ?? e.ClientId ?? "Unknown")
            .Select(g => new
            {
                Name = g.Key,
                Hours = g.Sum(e => e.TotalHours),
                Projects = g.Select(e => e.ProjectName ?? e.ProjectId).Distinct().Count(),
                Employees = g.Select(e => e.EmployeeName ?? e.EmpId).Distinct().Count()
            })
            .OrderByDescending(g => g.Hours)
            .ToList();

        var total = groups.Sum(g => g.Hours);

        foreach (var g in groups)
        {
            var pct = total > 0 ? g.Hours / total * 100 : 0;
            AnsiConsole.MarkupLine(
                $"  {Markup.Escape(g.Name),-30} {g.Hours,8:0.0}h  {g.Projects,2} proj  {g.Employees,2} people  {pct,5:0.0}%");
        }

        AnsiConsole.Write(new Rule().RuleStyle("dim"));
        AnsiConsole.MarkupLine($"  [bold]{"Total",-30} {total,8:0.0}h[/]");
        AnsiConsole.WriteLine();
    }

    private static void RenderFlat(List<TimesheetSummaryEntry> entries, int limit, int page)
    {
        var sorted = entries.OrderByDescending(e => e.TimesheetDate).ToList();
        var totalPages = (int)Math.Ceiling(sorted.Count / (double)limit);
        var paged = sorted.Skip((page - 1) * limit).Take(limit).ToList();

        var table = new Table()
            .AddColumn("Date")
            .AddColumn("Employee")
            .AddColumn("Client")
            .AddColumn("Project")
            .AddColumn(new TableColumn("Hours").RightAligned())
            .AddColumn("Billable")
            .AddColumn("Category");

        foreach (var e in paged)
        {
            var date = e.TimesheetDate?.Split('T')[0] ?? "?";
            var billable = e.BillableId switch
            {
                "B" => "[green]B[/]",
                "BPP" => "[blue]PP[/]",
                "W" => "[dim]W[/]",
                _ => Markup.Escape(e.BillableId ?? "?")
            };

            table.AddRow(
                date,
                Markup.Escape(e.EmployeeName ?? e.EmpId ?? "?"),
                Markup.Escape(e.ClientName ?? e.ClientId ?? "?"),
                Markup.Escape(e.ProjectName ?? e.ProjectId ?? "?"),
                $"{e.TotalHours:0.0}h",
                billable,
                Markup.Escape(e.CategoryName ?? ""));
        }

        AnsiConsole.Write(table);

        if (totalPages > 1)
            AnsiConsole.MarkupLine($"\n[dim]Page {page}/{totalPages} ({sorted.Count} total). Use --page N to navigate.[/]");
    }

    private static (DateOnly start, DateOnly end) ResolveDateRange(Settings settings)
    {
        if (settings.From is not null && settings.To is not null)
        {
            return (
                DateOnly.ParseExact(settings.From, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly.ParseExact(settings.To, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        // Default: current financial year (Jul 1 - Jun 30)
        var today = DateTime.Today;
        var fyStart = today.Month >= 7
            ? new DateOnly(today.Year, 7, 1)
            : new DateOnly(today.Year - 1, 7, 1);

        var start = settings.From is not null
            ? DateOnly.ParseExact(settings.From, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : fyStart;
        var end = settings.To is not null
            ? DateOnly.ParseExact(settings.To, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(today);

        return (start, end);
    }

    private static List<string> ParseCsv(string? input) =>
        string.IsNullOrEmpty(input)
            ? []
            : input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
