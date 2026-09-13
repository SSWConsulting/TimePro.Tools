using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Features.Rates;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Mcp.Tools;

[McpServerToolType]
public class TimesheetMcpTools
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;
    private readonly TimesheetCreateService _creates;
    private readonly TimesheetUpdateService _updates;
    private readonly TimesheetAcceptService _accepts;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public TimesheetMcpTools(
        ITimeProApiClient api,
        IConfigService config,
        TimesheetCreateService creates,
        TimesheetUpdateService updates,
        TimesheetAcceptService accepts)
    {
        _api = api;
        _config = config;
        _creates = creates;
        _updates = updates;
        _accepts = accepts;
    }

    [McpServerTool]
    [Description("Get timesheets for a date or date range. Use empId to read another employee's timesheets; employeeId is accepted as an alias.")]
    public async Task<string> GetTimesheets(
        [Description("Single date or start date (yyyy-MM-dd)")] string date,
        [Description("End date for range (yyyy-MM-dd). If omitted, returns single day.")] string? endDate = null,
        [Description("empId to read. Defaults to the current user's empId.")] string? empId = null,
        [Description("Alias for empId.")] string? employeeId = null,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in. Run 'tp login --tenant <id>' first."}""";

        var targetEmpId = ResolveEmpId(empId, employeeId, tenant.EmployeeId);

        var start = DateOnly.ParseExact(date, "yyyy-MM-dd");
        var end = endDate is not null
            ? DateOnly.ParseExact(endDate, "yyyy-MM-dd")
            : start;

        // Weekends are skipped, so a weekend-only range answers with an empty array and no call.
        var days = await TimesheetLookup.ForRangeAsync(
            _api, targetEmpId, start, end, WeekendPolicy.Skip, ct);

        var allTimesheets = days.SelectMany(day => day.Entries.Select(t => new
        {
            t.TimeId, t.EmpId, t.EmpName, t.Client, t.ClientId, t.Project, t.ProjectId,
            date = day.Date.ToString("yyyy-MM-dd"),
            t.StartTime, t.EndTime, t.TotalTime,
            t.Location, t.BillableId, t.IsSuggested,
            t.Notes, t.IsLocked, t.InvoiceId
        })).ToList();

        return JsonSerializer.Serialize(allTimesheets, JsonOpts);
    }

    [McpServerTool]
    [Description("Create a new timesheet entry. Projects that use iterations require one, by name or ID; a missing or unknown iteration fails with the available ones listed.")]
    public async Task<string> CreateTimesheet(
        [Description("Client ID")] string clientId,
        [Description("Project ID")] string projectId,
        [Description("Date (yyyy-MM-dd)")] string date,
        [Description("Start time (HH:mm)")] string startTime = "09:00",
        [Description("End time (HH:mm)")] string endTime = "17:00",
        [Description("Notes/description")] string? description = null,
        [Description("Location (e.g., Office, Home)")] string? location = null,
        [Description("Billable type: B (billable), BPP (prepaid), W (write-off)")] string billableId = "B",
        [Description("Category ID (e.g., TRAIN, PresDe)")] string? categoryId = null,
        [Description("Iteration/sprint ID. Prefer 'iteration', which also takes the name.")] int? iterationId = null,
        [Description("Iteration/sprint, by name or ID. Required for projects that use iterations (e.g., 1I776Q).")] string? iteration = null,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in"}""";

        try
        {
            var prepared = await _creates.PrepareAsync(
                tenant.EmployeeId,
                new TimesheetCreateOptions(
                    ClientId: clientId,
                    ProjectId: projectId,
                    Date: date,
                    Start: startTime,
                    End: endTime,
                    Description: description,
                    Location: location,
                    Category: categoryId,
                    Iteration: iteration ?? iterationId?.ToString(),
                    Billable: billableId),
                ct);

            // Creating a rate is a deliberate act with its own approval, so the agent is told how
            // to set one rather than having one written on its behalf.
            if (prepared.NoActiveRate)
                return NoActiveRateError(clientId);

            var result = await _creates.ApplyAsync(prepared.Plan!, ct);

            // Serialised with the CLI's options so both surfaces answer with the same document.
            return OutputHelper.SerializeJson(result);
        }
        catch (TimesheetValidationException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts);
        }
    }

    private static string NoActiveRateError(string clientId) => JsonSerializer.Serialize(new
    {
        error = RateGuard.NoActiveRateMessage(clientId),

        // No recommendation lookup: an agent is told how to set a rate, not offered an amount that
        // would take a second API call to produce.
        recovery = RateGuard.BuildRecovery(clientId, new RateRecommendation(0m, 0m, RateSource.None))
    }, JsonOpts);

    [McpServerTool]
    [Description("List iterations/sprints for a project. Returns empty list if the project doesn't use iterations. If the list is non-empty, an iteration ID is required when creating timesheets for this project.")]
    public async Task<string> ListIterations(
        [Description("Project ID (e.g., 1I776Q)")] string projectId,
        CancellationToken ct = default)
    {
        var iterations = await _api.GetIterationsAsync(projectId, ct);
        return JsonSerializer.Serialize(iterations, JsonOpts);
    }

    [McpServerTool]
    [Description("Update an existing timesheet. Only specify fields you want to change; everything else is preserved. Returns the entry as saved.")]
    public async Task<string> UpdateTimesheet(
        [Description("Timesheet ID")] int timesheetId,
        [Description("New location")] string? location = null,
        [Description("New notes/description")] string? description = null,
        [Description("New billable type: B, BPP, W")] string? billableId = null,
        [Description("New iteration/sprint, by name or ID. Use ListIterations to see the options.")] string? iteration = null,
        [Description("Date the timesheet is on (yyyy-MM-dd). Speeds up the lookup; otherwise recent weeks are searched.")] string? date = null,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in"}""";

        try
        {
            var result = await _updates.UpdateAsync(
                timesheetId,
                tenant.EmployeeId,
                new TimesheetUpdateOptions(
                    Location: location,
                    Description: description,
                    Billable: billableId,
                    Iteration: iteration,
                    Date: date),
                ct);

            return JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (TimesheetValidationException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts);
        }
    }

    [McpServerTool]
    [Description("Delete a timesheet entry. Suggestions cannot be deleted; accept one first.")]
    public async Task<string> DeleteTimesheet(
        [Description("Timesheet ID")] int timesheetId,
        [Description("Date the timesheet is on (yyyy-MM-dd). Speeds up the lookup; otherwise recent weeks are searched.")] string? date = null,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in"}""";

        try
        {
            await TimesheetLookup.EnsureDeletableAsync(_api, tenant.EmployeeId, timesheetId, date, ct);
        }
        catch (TimesheetValidationException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts);
        }

        await _api.DeleteTimesheetAsync(timesheetId, ct);
        return JsonSerializer.Serialize(new { success = true, timesheetId }, JsonOpts);
    }

    [McpServerTool]
    [Description("Validate a week of timesheets for gaps and issues (leave-aware). Returns per-day coverage with hours, leave, issues, plus allCovered and pendingSuggestions. week: 0=this week (default), -1=last week. The single most useful tool for confirming a week is complete before submitting.")]
    public async Task<string> CheckWeek(
        [Description("Week offset. 0=this week (default), -1=last week.")] int week = 0,
        [Description("empId to check. Defaults to the current user's empId.")] string? empId = null,
        [Description("Alias for empId.")] string? employeeId = null,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in. Run 'tp login --tenant <id>' first."}""";

        var targetEmpId = ResolveEmpId(empId, employeeId, tenant.EmployeeId);

        // Shared orchestration with `tp ts check` — fetch + leave-merge + per-day eval.
        var coverage = await WeekCoverageService.EvaluateWeekAsync(_api, targetEmpId, week, ct);

        return JsonSerializer.Serialize(WeekCheckResult.From(coverage), JsonOpts);
    }

    [McpServerTool]
    [Description("Get suggested timesheets for a date. Refreshes suggestions first.")]
    public async Task<string> GetSuggestedTimesheets(
        [Description("Date (yyyy-MM-dd)")] string date,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in"}""";

        var day = await TimesheetLookup.RefreshAndReadSuggestedAsync(
            _api, tenant.EmployeeId, DateOnly.ParseExact(date, "yyyy-MM-dd"), ct);

        return JsonSerializer.Serialize(day.Entries, JsonOpts);
    }

    [McpServerTool]
    [Description("Accept a suggested timesheet, converting it into a real timesheet. Returns the entry as saved.")]
    public async Task<string> AcceptSuggestedTimesheet(
        [Description("Suggested timesheet ID")] int suggestedId,
        [Description("Override location")] string? location = null,
        [Description("Override notes")] string? notes = null,
        [Description("Iteration/sprint for the accepted timesheet, by name or ID. Required for projects that use iterations.")] string? iteration = null,
        [Description("Date the suggestion is on (yyyy-MM-dd). Speeds up the lookup; otherwise recent weeks are searched.")] string? date = null,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in"}""";

        try
        {
            var result = await _accepts.AcceptAsync(
                suggestedId,
                tenant.EmployeeId,
                new TimesheetAcceptOptions(location, notes, iteration, date),
                ct);

            return JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (TimesheetValidationException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts);
        }
    }

    private static string ResolveEmpId(string? empId, string? employeeId, string defaultEmpId)
    {
        var requestedEmpId = !string.IsNullOrWhiteSpace(empId) ? empId : employeeId;
        return string.IsNullOrWhiteSpace(requestedEmpId) ? defaultEmpId : requestedEmpId.Trim();
    }
}
