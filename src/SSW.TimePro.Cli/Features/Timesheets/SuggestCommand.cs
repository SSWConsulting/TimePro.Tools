using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("View suggested timesheets")]
public class SuggestCommand : AsyncCommand<SuggestCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[DATE]")]
        [Description("Date (yyyy-MM-dd). Defaults to today")]
        public string? Date { get; set; }

        [CommandOption("--week [OFFSET]")]
        [Description("Show week. Optional offset: 0=this week")]
        [DefaultValue(null)]
        public FlagValue<int>? Week { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public SuggestCommand(ITimeProApiClient api, IConfigService config)
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

        try
        {
            var dates = new List<DateOnly>();

            if (settings.Week is not null && settings.Week.IsSet)
            {
                var offset = settings.Week.Value;
                var today = DateOnly.FromDateTime(DateTime.Today);
                var monday = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday + (offset * 7));
                if (today.DayOfWeek == DayOfWeek.Sunday)
                    monday = monday.AddDays(-7);
                for (int i = 0; i < 5; i++)
                    dates.Add(monday.AddDays(i));
            }
            else
            {
                var date = settings.Date is not null
                    ? DateOnly.ParseExact(settings.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : DateOnly.FromDateTime(DateTime.Today);
                dates.Add(date);
            }

            var allSuggested = new List<(DateOnly Date, List<TimesheetItem> Items)>();

            foreach (var date in dates)
            {
                // Refresh suggested timesheets first
                await _api.RefreshSuggestedTimesheetsAsync(tenant.EmployeeId, date, CancellationToken.None);

                // Fetch all timesheets and filter to suggested
                var all = await _api.GetTimesheetsAsync(tenant.EmployeeId, date, CancellationToken.None);
                var suggested = all.Where(t => t.IsSuggested).ToList();
                if (suggested.Count > 0)
                    allSuggested.Add((date, suggested));
            }

            if (settings.Json)
            {
                OutputHelper.WriteJson(allSuggested.Select(d => new
                {
                    date = d.Date.ToString("yyyy-MM-dd"),
                    suggested = d.Items
                }));
                return 0;
            }

            if (allSuggested.Count == 0)
            {
                OutputHelper.WriteInfo("No suggested timesheets found");
                return 0;
            }

            foreach (var (date, items) in allSuggested)
            {
                AnsiConsole.Write(new Rule($"[bold]{date:dddd, MMM d}[/]").LeftJustified().RuleStyle("dim"));

                foreach (var ts in items)
                {
                    AnsiConsole.MarkupLine(
                        $"  [bold]#{ts.TimeId}[/]  {Markup.Escape(ts.Client ?? "?")} [dim]|[/] {Markup.Escape(ts.Project ?? "?")}");
                    AnsiConsole.MarkupLine(
                        $"         {ts.StartTime} - {ts.EndTime} ({ts.TotalTime:0.0}h) [dim]|[/] {Markup.Escape(ts.Location ?? "?")}");
                    if (ts.HasNotes && !string.IsNullOrWhiteSpace(ts.Notes))
                        AnsiConsole.MarkupLine($"         [dim]{Markup.Escape(ts.Notes)}[/]");
                    AnsiConsole.WriteLine();
                }
            }

            AnsiConsole.MarkupLine("[dim]Use 'tp ts accept <ID>' to accept a suggested timesheet[/]");
            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }
}
