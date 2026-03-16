using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Summary;

[Description("Show project hours breakdown for a period")]
public class SummaryCommand : AsyncCommand<SummaryCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--from <DATE>")]
        [Description("Start date (yyyy-MM-dd)")]
        public string? From { get; set; }

        [CommandOption("--to <DATE>")]
        [Description("End date (yyyy-MM-dd)")]
        public string? To { get; set; }

        [CommandOption("--week [OFFSET]")]
        [Description("Show week summary. Offset: 0=this week, -1=last week")]
        [DefaultValue(null)]
        public FlagValue<int>? Week { get; set; }

        [CommandOption("--month [OFFSET]")]
        [Description("Show month summary. Offset: 0=this month, -1=last month")]
        [DefaultValue(null)]
        public FlagValue<int>? Month { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public SummaryCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        var (start, end) = ResolveDateRange(settings);

        try
        {
            var items = await _api.GetProjectsSummaryAsync(tenant.EmployeeId, start, end, CancellationToken.None);

            var result = new
            {
                startDate = start.ToString("yyyy-MM-dd"),
                endDate = end.ToString("yyyy-MM-dd"),
                projects = items,
                totalHours = items.Sum(i => i.Sum),
                billableHours = items.Where(i => i.IsBillable).Sum(i => i.Sum)
            };

            OutputHelper.Render(result, settings.Json, data =>
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[bold]Summary: {start:MMM d} - {end:MMM d, yyyy}[/]").LeftJustified().RuleStyle("dim"));

                var table = new Table().NoBorder()
                    .AddColumn(new TableColumn("Project").NoWrap())
                    .AddColumn("Client")
                    .AddColumn(new TableColumn("Hours").RightAligned())
                    .AddColumn("Billable");

                foreach (var item in items.OrderByDescending(i => i.Sum))
                {
                    var parts = item.ProjectName?.Split(" - ") ?? [];
                    var project = parts.Length > 0 ? parts[0].Trim() : "?";
                    var client = parts.Length > 1 ? parts[1].Trim() : (item.ClientID ?? "?");
                    var billable = item.IsBillable ? "[green]Yes[/]" : "[dim]No[/]";

                    table.AddRow(
                        Markup.Escape(project),
                        Markup.Escape(client),
                        $"{item.Sum:0.0}h",
                        billable);
                }

                AnsiConsole.Write(table);

                var total = data.totalHours;
                var billableTotal = data.billableHours;
                var pct = total > 0 ? billableTotal / total * 100 : 0;
                AnsiConsole.Write(new Rule().RuleStyle("dim"));
                AnsiConsole.MarkupLine($" Total: [bold]{total:0.0}h[/]  |  Billable: [bold]{billableTotal:0.0}h[/] ({pct:0.0}%)");
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

    private static (DateOnly start, DateOnly end) ResolveDateRange(Settings settings)
    {
        if (settings.From is not null && settings.To is not null)
        {
            return (
                DateOnly.ParseExact(settings.From, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly.ParseExact(settings.To, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (settings.Week is not null && settings.Week.IsSet)
        {
            var offset = settings.Week.Value;
            var today = DateOnly.FromDateTime(DateTime.Today);
            var monday = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday + (offset * 7));
            if (today.DayOfWeek == DayOfWeek.Sunday)
                monday = monday.AddDays(-7);
            return (monday, monday.AddDays(4));
        }

        if (settings.Month is not null && settings.Month.IsSet)
        {
            var offset = settings.Month.Value;
            var today = DateTime.Today;
            var month = today.AddMonths(offset);
            var first = new DateOnly(month.Year, month.Month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            return (first, last);
        }

        // Default: current month
        var now = DateTime.Today;
        return (new DateOnly(now.Year, now.Month, 1),
                DateOnly.FromDateTime(now));
    }
}
