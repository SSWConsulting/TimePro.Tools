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

[Description("Update an existing timesheet entry")]
public class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("Timesheet ID to update")]
        public int TimesheetId { get; set; }

        [CommandOption("--location <LOC>")]
        [Description("New location (SSW, Home, Client, Travel, Other)")]
        public string? Location { get; set; }

        [CommandOption("--description <DESC>")]
        [Description("New notes/description")]
        public string? Description { get; set; }

        [CommandOption("--start <TIME>")]
        [Description("New start time (HH:mm)")]
        public string? Start { get; set; }

        [CommandOption("--end <TIME>")]
        [Description("New end time (HH:mm)")]
        public string? End { get; set; }

        [CommandOption("--client <CLIENT>")]
        [Description("New client ID")]
        public string? ClientId { get; set; }

        [CommandOption("--project <PROJECT>")]
        [Description("New project ID")]
        public string? ProjectId { get; set; }

        [CommandOption("--category <CAT>")]
        [Description("New category ID")]
        public string? Category { get; set; }

        [CommandOption("--billable <TYPE>")]
        [Description("New billable type: B, BPP, or W")]
        public string? Billable { get; set; }

        [CommandOption("--date <DATE>")]
        [Description("Date the timesheet is on (yyyy-MM-dd). Used to look up the existing entry. Defaults to searching recent weeks.")]
        public string? Date { get; set; }

        [CommandOption("--yes")]
        [Description("Skip confirmation prompt")]
        public bool Yes { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public UpdateCommand(ITimeProApiClient api, IConfigService config)
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
            // Find the existing timesheet so we can send a full payload.
            // The SaveTimesheet API requires all fields even for edits.
            var existing = await FindTimesheetAsync(tenant.EmployeeId, settings.TimesheetId, settings.Date);
            if (existing is null)
            {
                OutputHelper.WriteError($"Timesheet #{settings.TimesheetId} not found. Try passing --date to narrow the search.");
                return 1;
            }

            // Parse the date from the existing timesheet (may be yyyy-MM-dd or ISO datetime)
            var existingDateStr = existing.Date?.Split('T')[0] ?? existing.StartTime?.Split('T')[0]
                ?? throw new InvalidOperationException("Cannot determine date for existing timesheet");
            var dateOnly = DateOnly.ParseExact(existingDateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            // The GET endpoint doesn't return CategoryID — resolve it via the query API
            var categoryId = settings.Category
                ?? await ResolveCategoryIdAsync(tenant.EmployeeId, dateOnly, settings.TimesheetId);

            // Build the full request from the existing timesheet, applying overrides
            var request = new TimesheetRequest
            {
                TimeId = settings.TimesheetId,
                EmpId = tenant.EmployeeId,
                ClientId = settings.ClientId ?? existing.ClientId ?? "",
                ProjectId = settings.ProjectId ?? existing.ProjectId ?? "",
                IterationId = existing.IterationId,
                DateCreated = existingDateStr,
                TimeStart = settings.Start is not null
                    ? $"{existingDateStr}T{settings.Start}:00"
                    : existing.StartTime,
                TimeEnd = settings.End is not null
                    ? $"{existingDateStr}T{settings.End}:00"
                    : existing.EndTime,
                TimeLess = existing.Less > 0 ? existing.Less : null,
                Note = settings.Description ?? existing.Notes,
                LocationId = settings.Location is not null
                    ? LocationResolver.Resolve(settings.Location)
                    : existing.LocationId,
                CategoryId = categoryId,
                BillableId = settings.Billable ?? existing.BillableId ?? "B",
                IsBillingTypeOverridden = settings.Billable is not null,
            };

            // Re-fetch the rate for the client
            var rate = await _api.GetClientRateAsync(tenant.EmployeeId, request.ClientId, dateOnly, CancellationToken.None);
            request.SellPrice = rate?.Rate;

            var changes = new List<string>();
            if (settings.Location is not null)
                changes.Add($"Location -> {LocationResolver.Resolve(settings.Location)}");
            if (settings.Description is not null)
                changes.Add($"Description -> {(settings.Description.Length > 50 ? settings.Description[..50] + "..." : settings.Description)}");
            if (settings.Start is not null)
                changes.Add($"Start -> {settings.Start}");
            if (settings.End is not null)
                changes.Add($"End -> {settings.End}");
            if (settings.ClientId is not null)
                changes.Add($"Client -> {settings.ClientId}");
            if (settings.ProjectId is not null)
                changes.Add($"Project -> {settings.ProjectId}");
            if (settings.Category is not null)
                changes.Add($"Category -> {settings.Category}");
            if (settings.Billable is not null)
                changes.Add($"Billable -> {settings.Billable}");

            if (changes.Count == 0)
            {
                OutputHelper.WriteInfo("No changes specified. Use --location, --description, etc.");
                return 0;
            }

            // Show preview
            if (!settings.Json)
            {
                AnsiConsole.MarkupLine($"[bold]Updating timesheet #{settings.TimesheetId}:[/]");
                AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(existing.Client ?? "?")} | {Markup.Escape(existing.Project ?? "?")} ({existingDateStr})[/]");
                foreach (var change in changes)
                    AnsiConsole.MarkupLine($"  {Markup.Escape(change)}");
                AnsiConsole.WriteLine();
            }

            if (!settings.Yes && !settings.Json)
            {
                if (!AnsiConsole.Confirm("Apply these changes?"))
                    return 1;
            }

            var response = await _api.UpdateTimesheetAsync(request, CancellationToken.None);

            if (settings.Json)
            {
                OutputHelper.WriteJson(response);
            }
            else if (response is null || response.Success)
            {
                OutputHelper.WriteSuccess($"Timesheet #{settings.TimesheetId} updated");
            }
            else
            {
                OutputHelper.WriteError(response.Message ?? "Failed to update timesheet");
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

    /// <summary>
    /// Searches for a timesheet by ID. If a date hint is provided, searches that day.
    /// Otherwise searches the last 4 weeks day-by-day until found.
    /// </summary>
    private async Task<TimesheetItem?> FindTimesheetAsync(string empId, int timesheetId, string? dateHint)
    {
        if (dateHint is not null)
        {
            var date = DateOnly.ParseExact(dateHint, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var timesheets = await _api.GetTimesheetsAsync(empId, date, CancellationToken.None);
            return timesheets.FirstOrDefault(t => t.TimeId == timesheetId);
        }

        // Search last 4 weeks
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var d = today; d >= today.AddDays(-28); d = d.AddDays(-1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            var timesheets = await _api.GetTimesheetsAsync(empId, d, CancellationToken.None);
            var match = timesheets.FirstOrDefault(t => t.TimeId == timesheetId);
            if (match is not null)
                return match;
        }

        return null;
    }

    /// <summary>
    /// The GET /api/Timesheets/GetTimesheetListViewModel endpoint does not return
    /// CategoryID. We use the query/summary API to resolve it.
    /// </summary>
    private async Task<string?> ResolveCategoryIdAsync(string empId, DateOnly date, int timesheetId)
    {
        var filter = new TimesheetSummaryFilter
        {
            StartDate = date.ToString("yyyy-MM-dd"),
            EndDate = date.ToString("yyyy-MM-dd"),
            EmployeeIds = [empId]
        };

        var entries = await _api.QueryTimesheetsAsync(filter, CancellationToken.None);
        var match = entries.FirstOrDefault(e => e.TimeId == timesheetId);
        return match?.CategoryId;
    }
}
