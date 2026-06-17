using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("View timesheets for a day or week")]
public class GetCommand : AsyncCommand<GetCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[DATE]")]
        [Description("Date (yyyy-MM-dd). Defaults to today")]
        public string? Date { get; set; }

        [CommandOption("--date <DATE>")]
        [Description("Date (yyyy-MM-dd). Alternative to positional argument")]
        public string? DateOption { get; set; }

        [CommandOption("--week [OFFSET]")]
        [Description("Show week view. Optional offset: 0=this week, -1=last week")]
        [DefaultValue(null)]
        public FlagValue<int>? Week { get; set; }

        [CommandOption("--from <DATE>")]
        [Description("Start date for range query")]
        public string? From { get; set; }

        [CommandOption("--to <DATE>")]
        [Description("End date for range query")]
        public string? To { get; set; }

        [CommandOption("--detailed")]
        [Description("Show detailed view with descriptions")]
        public bool Detailed { get; set; }

        [CommandOption("--emp-id|--employee-id|--employee <EMP_ID>")]
        [Description("empId. Defaults to the current user")]
        public string? EmpId { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public GetCommand(ITimeProApiClient api, IConfigService config)
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

        var empId = ResolveEmpId(settings.EmpId, tenant.EmployeeId);

        try
        {
            // Determine date range
            if (settings.Week is not null && settings.Week.IsSet)
            {
                var offset = settings.Week.Value;
                var today = DateOnly.FromDateTime(DateTime.Today);
                var monday = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday + (offset * 7));
                if (today.DayOfWeek == DayOfWeek.Sunday)
                    monday = monday.AddDays(-7);
                var friday = monday.AddDays(4);

                return await RenderWeek(empId, monday, friday, settings);
            }

            if (settings.From is not null && settings.To is not null)
            {
                var from = DateOnly.ParseExact(settings.From, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var to = DateOnly.ParseExact(settings.To, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                return await RenderWeek(empId, from, to, settings);
            }

            // Single day (--date option takes precedence over positional argument)
            var dateStr = settings.DateOption ?? settings.Date;
            var date = dateStr is not null
                ? DateOnly.ParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : DateOnly.FromDateTime(DateTime.Today);

            return await RenderDay(empId, date, settings);
        }
        catch (FormatException)
        {
            OutputHelper.WriteError("Invalid date format. Use yyyy-MM-dd.");
            return 1;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

    private async Task<int> RenderDay(string empId, DateOnly date, Settings settings)
    {
        var timesheets = await _api.GetTimesheetsAsync(empId, date, CancellationToken.None);

        if (settings.Json)
        {
            OutputHelper.WriteJson(timesheets);
            return 0;
        }

        if (timesheets.Count == 0)
        {
            AnsiConsole.MarkupLine($"[dim]{date:dddd, MMMM d yyyy}[/] - [yellow]No timesheets[/]");
            return 0;
        }

        RenderDayDetailed(date, timesheets);
        return 0;
    }

    private async Task<int> RenderWeek(string empId, DateOnly start, DateOnly end, Settings settings)
    {
        // Fetch all days in range
        var allTimesheets = new Dictionary<DateOnly, List<TimesheetItem>>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var dayTimesheets = await _api.GetTimesheetsAsync(empId, d, CancellationToken.None);
            allTimesheets[d] = dayTimesheets;
        }

        if (settings.Json)
        {
            var jsonData = new
            {
                weekStart = start.ToString("yyyy-MM-dd"),
                weekEnd = end.ToString("yyyy-MM-dd"),
                days = allTimesheets.Select(kvp => new
                {
                    date = kvp.Key.ToString("yyyy-MM-dd"),
                    dayOfWeek = kvp.Key.DayOfWeek.ToString(),
                    timesheets = kvp.Value,
                    totalHours = kvp.Value.Where(t => !t.IsSuggested).Sum(t => t.TotalTime)
                })
            };
            OutputHelper.WriteJson(jsonData);
            return 0;
        }

        if (settings.Detailed)
        {
            RenderWeekDetailed(start, end, allTimesheets);
        }
        else
        {
            RenderWeekCompact(start, end, allTimesheets);
        }

        return 0;
    }

    private void RenderWeekCompact(DateOnly start, DateOnly end, Dictionary<DateOnly, List<TimesheetItem>> allTimesheets)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($" [bold]Week of {start:MMM d} - {end:MMM d, yyyy}[/]");

        var rule = new Rule().RuleStyle("dim");
        AnsiConsole.Write(rule);

        decimal totalHours = 0;
        decimal billableHours = 0;
        var missingDays = new List<string>();

        foreach (var (date, timesheets) in allTimesheets.OrderBy(x => x.Key))
        {
            var realTimesheets = timesheets.Where(t => !t.IsSuggested).ToList();
            var dayTotal = realTimesheets.Sum(t => t.TotalTime);
            totalHours += dayTotal;
            billableHours += realTimesheets.Where(t => t.BillableId == "B" || t.BillableId == "BPP").Sum(t => t.TotalTime);

            var dayLabel = $" {date:ddd dd}";

            if (realTimesheets.Count == 0)
            {
                AnsiConsole.MarkupLine($" {dayLabel,-8} [dim]|[/] [dim]{dayTotal,5:0.0}h[/] [dim]|[/] [yellow]No timesheets[/]");
                missingDays.Add(date.ToString("ddd"));
                continue;
            }

            for (int i = 0; i < realTimesheets.Count; i++)
            {
                var ts = realTimesheets[i];
                var prefix = i == 0 ? $" {dayLabel,-8} [dim]|[/] [dim]{dayTotal,5:0.0}h[/] [dim]|[/]" : $" {"",8} [dim]|[/] {"",6} [dim]|[/]";
                var billable = ts.BillableId switch
                {
                    "B" => "[green]B[/]",
                    "BPP" => "[blue]PP[/]",
                    "W" => "[dim]W[/]",
                    _ => "[dim]?[/]"
                };
                var lockIcon = ts.IsLocked ? " [red]![/]" : "";
                var client = Markup.Escape(ts.Client ?? "?");
                var project = Markup.Escape(ts.Project ?? "?");
                var location = Markup.Escape(ts.Location ?? "?");

                AnsiConsole.MarkupLine(
                    $"{prefix} {client,-5}  {project,-24} {ts.TotalTime,5:0.0}h  {location,-8} {billable}{lockIcon}");
            }
        }

        AnsiConsole.Write(rule);

        var expectedHours = allTimesheets.Count * 8m;
        var missingText = missingDays.Count > 0 ? $"  [dim]|[/]  [yellow]Missing: {string.Join(", ", missingDays)}[/]" : "";
        AnsiConsole.MarkupLine(
            $" Total: [bold]{totalHours:0.0}h[/] / {expectedHours:0.0}h  [dim]|[/]  Billable: [bold]{billableHours:0.0}h[/]{missingText}");
        AnsiConsole.WriteLine();
    }

    private void RenderWeekDetailed(DateOnly start, DateOnly end, Dictionary<DateOnly, List<TimesheetItem>> allTimesheets)
    {
        foreach (var (date, timesheets) in allTimesheets.OrderBy(x => x.Key))
        {
            RenderDayDetailed(date, timesheets);
        }
    }

    private void RenderDayDetailed(DateOnly date, List<TimesheetItem> timesheets)
    {
        var realTimesheets = timesheets.Where(t => !t.IsSuggested).ToList();
        var dayTotal = realTimesheets.Sum(t => t.TotalTime);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold]{date:dddd, MMMM d yyyy}[/] [dim]── {dayTotal:0.0}h[/]")
            .LeftJustified().RuleStyle("dim"));

        if (realTimesheets.Count == 0)
        {
            AnsiConsole.MarkupLine("  [yellow]No timesheets[/]");
            return;
        }

        foreach (var ts in realTimesheets)
        {
            AnsiConsole.WriteLine();
            var billableLabel = ts.BillableId switch
            {
                "B" => "[green]Billable[/]",
                "BPP" => "[blue]Prepaid[/]",
                "W" => "[dim]Write-off[/]",
                _ => "[dim]Unknown[/]"
            };
            var lockIcon = ts.IsLocked ? " [red][locked][/]" : "";
            var client = Markup.Escape(ts.Client ?? "?");
            var project = Markup.Escape(ts.Project ?? "?");
            var location = Markup.Escape(ts.Location ?? "?");
            var startTime = ts.StartTime ?? "?";
            var endTime = ts.EndTime ?? "?";

            AnsiConsole.MarkupLine($"  [bold]#{ts.TimeId}[/]  {client} [dim]|[/] {project}");
            AnsiConsole.MarkupLine($"         {startTime} - {endTime} ({ts.TotalTime:0.0}h) [dim]|[/] {location} [dim]|[/] {billableLabel}{lockIcon}");

            if (ts.HasNotes && !string.IsNullOrWhiteSpace(ts.Notes))
            {
                AnsiConsole.MarkupLine($"         [dim]{Markup.Escape(ts.Notes)}[/]");
            }

            if (ts.InvoiceId.HasValue)
            {
                var invoiceType = ts.InvoiceType switch
                {
                    "Prepaid" => "Prepaid",
                    "Both" or "Time" => "T&M",
                    _ => ts.InvoiceType ?? "?"
                };
                AnsiConsole.MarkupLine($"         [dim]Invoice: #{ts.InvoiceId} ({invoiceType}){lockIcon}[/]");
            }
        }

        // Show suggested timesheets
        var suggested = timesheets.Where(t => t.IsSuggested).ToList();
        if (suggested.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [dim]── Suggested ({suggested.Count}) ──[/]");
            foreach (var ts in suggested)
            {
                AnsiConsole.MarkupLine($"  [dim]  #{ts.TimeId}  {Markup.Escape(ts.Client ?? "?")} | {Markup.Escape(ts.Project ?? "?")}  {ts.TotalTime:0.0}h[/]");
            }
        }
    }

    private static string ResolveEmpId(string? requestedEmpId, string defaultEmpId) =>
        string.IsNullOrWhiteSpace(requestedEmpId) ? defaultEmpId : requestedEmpId.Trim();
}
