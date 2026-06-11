using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Shared;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Mcp.Tools;

[McpServerToolType]
public class TimesheetMcpTools
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public TimesheetMcpTools(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
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

        var allTimesheets = new List<object>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            var dayTimesheets = await _api.GetTimesheetsAsync(targetEmpId, d, ct);
            allTimesheets.AddRange(dayTimesheets.Select(t => new
            {
                t.TimeId, t.EmpId, t.EmpName, t.Client, t.ClientId, t.Project, t.ProjectId,
                date = d.ToString("yyyy-MM-dd"),
                t.StartTime, t.EndTime, t.TotalTime,
                t.Location, t.BillableId, t.IsSuggested,
                t.Notes, t.IsLocked, t.InvoiceId
            }));
        }

        return JsonSerializer.Serialize(allTimesheets, JsonOpts);
    }

    [McpServerTool]
    [Description("Create a new timesheet entry. Some projects require an iteration ID — use ListIterations to check and find the correct ID.")]
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
        [Description("Iteration/sprint ID. Required for projects that use iterations (e.g., SSWTRN). Use ListIterations to find the ID.")] int? iterationId = null,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in"}""";

        // Resolve location from WFH defaults if not specified
        if (string.IsNullOrEmpty(location))
        {
            var global = _config.LoadGlobalConfig();
            var dateOnly = DateOnly.ParseExact(date, "yyyy-MM-dd");
            var dayName = dateOnly.DayOfWeek.ToString();
            location = global.WfhDays.Contains(dayName, StringComparer.OrdinalIgnoreCase)
                ? "Home" : LocationResolver.Resolve(global.DefaultLocation);
        }
        else
        {
            location = LocationResolver.Resolve(location);
        }

        var request = new TimesheetRequest
        {
            EmpId = tenant.EmployeeId,
            ClientId = clientId,
            ProjectId = projectId,
            IterationId = iterationId,
            DateCreated = date,
            TimeStart = $"{date}T{startTime}:00",
            TimeEnd = $"{date}T{endTime}:00",
            Note = description,
            LocationId = location,
            CategoryId = categoryId,
            BillableId = billableId
        };

        var response = await _api.CreateTimesheetAsync(request, ct);
        return JsonSerializer.Serialize(response, JsonOpts);
    }

    [McpServerTool]
    [Description("List iterations/sprints for a project. Returns empty list if the project doesn't use iterations. If the list is non-empty, an iteration ID is required when creating timesheets for this project.")]
    public async Task<string> ListIterations(
        [Description("Project ID (e.g., SSWTRN)")] string projectId,
        CancellationToken ct = default)
    {
        var iterations = await _api.GetIterationsAsync(projectId, ct);
        return JsonSerializer.Serialize(iterations, JsonOpts);
    }

    [McpServerTool]
    [Description("Update an existing timesheet. Only specify fields you want to change.")]
    public async Task<string> UpdateTimesheet(
        [Description("Timesheet ID")] int timesheetId,
        [Description("New location")] string? location = null,
        [Description("New notes/description")] string? description = null,
        [Description("New billable type: B, BPP, W")] string? billableId = null,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in"}""";

        var request = new TimesheetRequest
        {
            TimeId = timesheetId,
            EmpId = tenant.EmployeeId,
            LocationId = location,
            Note = description,
            BillableId = billableId
        };

        var response = await _api.UpdateTimesheetAsync(request, ct);
        return JsonSerializer.Serialize(response, JsonOpts);
    }

    [McpServerTool]
    [Description("Delete a timesheet entry.")]
    public async Task<string> DeleteTimesheet(
        [Description("Timesheet ID")] int timesheetId,
        CancellationToken ct = default)
    {
        await _api.DeleteTimesheetAsync(timesheetId, ct);
        return JsonSerializer.Serialize(new { success = true, timesheetId }, JsonOpts);
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

        var dateOnly = DateOnly.ParseExact(date, "yyyy-MM-dd");
        await _api.RefreshSuggestedTimesheetsAsync(tenant.EmployeeId, dateOnly, ct);

        var all = await _api.GetTimesheetsAsync(tenant.EmployeeId, dateOnly, ct);
        var suggested = all.Where(t => t.IsSuggested).ToList();

        return JsonSerializer.Serialize(suggested, JsonOpts);
    }

    [McpServerTool]
    [Description("Accept a suggested timesheet, converting it into a real timesheet.")]
    public async Task<string> AcceptSuggestedTimesheet(
        [Description("Suggested timesheet ID")] int suggestedId,
        [Description("Override location")] string? location = null,
        [Description("Override notes")] string? notes = null,
        CancellationToken ct = default)
    {
        var response = await _api.AcceptSuggestedTimesheetAsync(
            suggestedId, location, notes, null, ct);
        return JsonSerializer.Serialize(response, JsonOpts);
    }

    private static string ResolveEmpId(string? empId, string? employeeId, string defaultEmpId)
    {
        var requestedEmpId = !string.IsNullOrWhiteSpace(empId) ? empId : employeeId;
        return string.IsNullOrWhiteSpace(requestedEmpId) ? defaultEmpId : requestedEmpId.Trim();
    }
}
