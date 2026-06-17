using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Timesheets;

[Description("Copy timesheets from one day to another")]
public class CopyCommand : AsyncCommand<CopyCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--from <DATE>")]
        [Description("Source date (yyyy-MM-dd)")]
        public string From { get; set; } = string.Empty;

        [CommandOption("--to <DATE>")]
        [Description("Target date (yyyy-MM-dd)")]
        public string To { get; set; } = string.Empty;

        [CommandOption("--keep-description")]
        [Description("Copy descriptions from source timesheets")]
        public bool KeepDescription { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation prompt")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public CopyCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.From) || string.IsNullOrEmpty(settings.To))
        {
            OutputHelper.WriteError("--from and --to are required");
            return 1;
        }

        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        var fromDate = DateOnly.ParseExact(settings.From, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = DateOnly.ParseExact(settings.To, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        try
        {
            // Fetch source timesheets
            var source = await _api.GetTimesheetsAsync(tenant.EmployeeId, fromDate, CancellationToken.None);
            var real = source.Where(t => !t.IsSuggested && !t.IsLeave).ToList();

            if (real.Count == 0)
            {
                OutputHelper.WriteError($"No timesheets found on {fromDate:yyyy-MM-dd} to copy");
                return 1;
            }

            // Resolve location for target date
            var global = _config.LoadGlobalConfig();
            var toDayName = toDate.DayOfWeek.ToString();
            var targetLocation = global.WfhDays.Contains(toDayName, StringComparer.OrdinalIgnoreCase)
                ? "Home" : LocationResolver.Resolve(global.DefaultLocation);

            // Preview
            if (!settings.Json)
            {
                AnsiConsole.MarkupLine($"[bold]Copying {real.Count} timesheet(s) from {fromDate:ddd MMM d} to {toDate:ddd MMM d}:[/]");
                AnsiConsole.WriteLine();
                foreach (var ts in real)
                {
                    var desc = settings.KeepDescription ? (ts.Notes ?? "[dim](empty)[/]") : "[dim](blank)[/]";
                    AnsiConsole.MarkupLine($"  {Markup.Escape(ts.Client ?? "?")} | {Markup.Escape(ts.Project ?? "?")}  {ts.TotalTime:0.0}h  {Markup.Escape(targetLocation)}");
                    if (settings.KeepDescription && !string.IsNullOrWhiteSpace(ts.Notes))
                        AnsiConsole.MarkupLine($"    [dim]{Markup.Escape(ts.Notes.Split('\n')[0])}[/]");
                }
                AnsiConsole.WriteLine();
            }

            if (!settings.Yes && !settings.Json)
            {
                if (!AnsiConsole.Confirm("Create these timesheets?"))
                    return 1;
            }

            // Resolve iteration IDs for projects that require them.
            // The GET response includes iteration names but not IDs,
            // so we look up the ID by matching the name.
            var iterationCache = new Dictionary<string, List<IterationItem>>();
            async Task<int?> ResolveIterationIdAsync(string projectId, string? iterationName)
            {
                if (string.IsNullOrEmpty(iterationName)) return null;

                if (!iterationCache.TryGetValue(projectId, out var iterations))
                {
                    iterations = await _api.GetIterationsAsync(projectId, CancellationToken.None);
                    iterationCache[projectId] = iterations;
                }

                // Empty list means the project doesn't use iterations
                if (iterations.Count == 0) return null;

                return iterations
                    .FirstOrDefault(i => string.Equals(i.IterationName, iterationName, StringComparison.OrdinalIgnoreCase))
                    ?.IterationId;
            }

            var created = new List<object>();
            foreach (var ts in real)
            {
                var iterationId = ts.IterationId
                    ?? await ResolveIterationIdAsync(ts.ProjectId ?? "", ts.Iteration);

                var request = new TimesheetRequest
                {
                    EmpId = tenant.EmployeeId,
                    ClientId = ts.ClientId ?? "",
                    ProjectId = ts.ProjectId ?? "",
                    IterationId = iterationId,
                    DateCreated = toDate.ToString("yyyy-MM-dd"),
                    TimeStart = ReplaceDate(ts.StartTime, toDate),
                    TimeEnd = ReplaceDate(ts.EndTime, toDate),
                    TimeLess = ts.Less > 0 ? ts.Less : null,
                    Note = settings.KeepDescription ? ts.Notes : null,
                    LocationId = targetLocation,
                    BillableId = ts.BillableId ?? "B",
                    CategoryId = ts.Category
                };

                var response = await _api.CreateTimesheetAsync(request, CancellationToken.None);
                created.Add(new { response?.TimesheetId, ts.Client, ts.Project, ts.TotalTime });

                if (!settings.Json)
                {
                    if (response?.Success == true)
                        OutputHelper.WriteSuccess($"  Created #{response.TimesheetId} — {ts.Client} | {ts.Project}");
                    else
                        OutputHelper.WriteError($"  Failed: {response?.Message ?? "Unknown error"}");
                }
            }

            if (settings.Json)
                OutputHelper.WriteJson(new { copied = created.Count, from = settings.From, to = settings.To, timesheets = created });

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

    private static string? ReplaceDate(string? dateTime, DateOnly newDate)
    {
        if (dateTime is null) return null;
        try
        {
            var dt = DateTime.Parse(dateTime);
            return $"{newDate:yyyy-MM-dd}T{dt:HH:mm:ss}";
        }
        catch
        {
            return dateTime;
        }
    }
}
