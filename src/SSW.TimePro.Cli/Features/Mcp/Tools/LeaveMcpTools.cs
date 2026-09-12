using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Features.Leave;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Features.Mcp.Tools;

[McpServerToolType]
public class LeaveMcpTools
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;
    private readonly LeaveCreateService _leaveCreateService;
    private readonly LeaveUpdateService _leaveUpdateService;
    private readonly LeaveListService _leaveListService;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public LeaveMcpTools(
        ITimeProApiClient api,
        IConfigService config,
        LeaveCreateService leaveCreateService,
        LeaveUpdateService leaveUpdateService,
        LeaveListService leaveListService)
    {
        _api = api;
        _config = config;
        _leaveCreateService = leaveCreateService;
        _leaveUpdateService = leaveUpdateService;
        _leaveListService = leaveListService;
    }

    [McpServerTool]
    [Description("List EasyLeave entries. Use empId for one person; employeeId is accepted as an alias. Omit both to return all visible leave.")]
    public async Task<string> GetLeaveEntries(
        [Description("Filter: UPCOMING (default), PAST or ALL")] string filter = "UPCOMING",
        [Description("Number of entries to return")] int limit = 10,
        [Description("empId to filter by")] string? empId = null,
        [Description("Alias for empId")] string? employeeId = null,
        CancellationToken ct = default)
    {
        if (_config.LoadActiveTenantConfig() is null)
            return """{"error":"Not logged in. Run 'tp login --tenant <id>' first."}""";

        try
        {
            var response = await _leaveListService.ListAsync(
                filter,
                limit,
                ResolveEmpId(empId, employeeId),
                ct);

            return JsonSerializer.Serialize(response.Leaves?.Items ?? [], JsonOpts);
        }
        catch (LeaveFilterValidationException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts);
        }
    }

    [McpServerTool]
    [Description("Get leave stats for an employee: days since last leave and total leave hours taken in the last 12 months. Defaults to the current user's empId. (TimePro does not expose entitlement/remaining per leave type.)")]
    public async Task<string> GetLeaveBalance(
        [Description("empId to read. Defaults to the current user's empId.")] string? empId = null,
        [Description("Alias for empId.")] string? employeeId = null,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant is null)
            return """{"error": "Not logged in. Run 'tp login --tenant <id>' first."}""";

        var targetEmpId = ResolveEmpId(empId, employeeId) ?? tenant.EmployeeId;
        if (string.IsNullOrWhiteSpace(targetEmpId))
            return """{"error": "No empId available. Provide empId or log in again."}""";

        var stats = await _api.GetLeaveStatsAsync(targetEmpId, ct);
        if (stats is null)
            return JsonSerializer.Serialize(new { error = $"No leave stats found for {targetEmpId}." }, JsonOpts);

        return JsonSerializer.Serialize(new
        {
            empId = targetEmpId,
            stats.DaysSinceLastLeave,
            stats.LeaveTakenInLast12Months
        }, JsonOpts);
    }

    [McpServerTool]
    [Description("Create a leave request for the current user. An explicit timezone override takes priority; otherwise uses the TimePro profile timezone first, then the MCP host machine timezone as the browser-equivalent fallback.")]
    public async Task<string> CreateLeave(
        [Description("Start date (yyyy-MM-dd)")] string start,
        [Description("End date (yyyy-MM-dd)")] string end,
        [Description("Leave type ID or active leave type name")] string type,
        [Description("Leave note/reason")] string note,
        [Description("Approver's email address")] string? approvedBy = null,
        [Description("Comma-separated list of emails to notify")] string? cc = null,
        [Description("Request partial-day leave; start and end must be the same day")] bool halfDay = false,
        [Description("Start time override (HH:mm, default 09:00)")] string? startTime = null,
        [Description("End time override (HH:mm, default 18:00)")] string? endTime = null,
        [Description("Timezone override (IANA or Windows ID); takes priority over the TimePro user profile timezone")] string? timeZoneId = null,
        [Description("Validate and return the proposed request without creating leave")] bool dryRun = false,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (string.IsNullOrWhiteSpace(tenant?.EmployeeId))
            return """{"error": "Not logged in. Run 'tp login --tenant <id>' first."}""";

        try
        {
            var plan = await _leaveCreateService.PrepareAsync(
                tenant.EmployeeId,
                new LeaveCreateOptions(
                    Start: start,
                    End: end,
                    Type: type,
                    Note: note,
                    ApprovedBy: approvedBy,
                    Cc: cc,
                    HalfDay: halfDay,
                    StartTime: startTime,
                    EndTime: endTime,
                    TimeZoneId: timeZoneId),
                ct);

            if (dryRun)
                return JsonSerializer.Serialize(new { dryRun = true, request = plan.Request }, JsonOpts);

            var result = await _leaveCreateService.ApplyAsync(plan, ct);
            return JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (LeaveCreateValidationException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts);
        }
    }

    [McpServerTool]
    [Description("Update an existing leave request for the current user. Unspecified API-returned fields are preserved. List leave entries first to obtain the leave ID.")]
    public async Task<string> UpdateLeave(
        [Description("Leave entry ID (GUID)")] string id,
        [Description("New start date (yyyy-MM-dd); omit to preserve")] string? start = null,
        [Description("New end date (yyyy-MM-dd); omit to preserve")] string? end = null,
        [Description("New leave type ID or active leave type name; omit to preserve")] string? type = null,
        [Description("New leave note/reason; omit to preserve")] string? note = null,
        [Description("New approver email; omit to preserve")] string? approvedBy = null,
        [Description("Remove the current approver")] bool clearApprovedBy = false,
        [Description("Comma-separated CC recipients; omit to preserve")] string? cc = null,
        [Description("Remove all current CC recipients")] bool clearCc = false,
        [Description("true for partial-day, false for full-day, omit to preserve")] bool? halfDay = null,
        [Description("Employee workday start time (HH:mm); omit to preserve it when returned, otherwise use the profile value")] string? startTime = null,
        [Description("Employee workday end time (HH:mm); omit to preserve it when returned, otherwise use the profile value")] string? endTime = null,
        [Description("Timezone for changed dates (IANA or Windows ID)")] string? timeZoneId = null,
        [Description("Validate and return the proposed update without applying it")] bool dryRun = false,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (string.IsNullOrWhiteSpace(tenant?.EmployeeId))
            return """{"error": "Not logged in. Run 'tp login --tenant <id>' first."}""";

        try
        {
            var plan = await _leaveUpdateService.PrepareAsync(
                id,
                tenant.EmployeeId,
                new LeaveUpdateOptions(
                    Start: start,
                    End: end,
                    Type: type,
                    Note: note,
                    ApprovedBy: approvedBy,
                    ClearApprovedBy: clearApprovedBy,
                    Cc: cc,
                    ClearCc: clearCc,
                    AllDay: halfDay is null ? null : !halfDay.Value,
                    StartTime: startTime,
                    EndTime: endTime,
                    TimeZoneId: timeZoneId),
                ct);

            if (dryRun)
            {
                return JsonSerializer.Serialize(new
                {
                    dryRun = true,
                    leaveId = plan.Request.Id,
                    changes = plan.Changes,
                    request = plan.Request
                }, JsonOpts);
            }

            await _leaveUpdateService.ApplyAsync(plan, ct);
            return JsonSerializer.Serialize(new
            {
                success = true,
                leaveId = plan.Request.Id,
                changes = plan.Changes
            }, JsonOpts);
        }
        catch (LeaveUpdateValidationException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts);
        }
    }

    [McpServerTool(ReadOnly = true, Destructive = false)]
    [Description("Report when TimePro's leave balances were last imported from Xero, how many employees have a stored balance, and whether the data is stale. Read-only. Check this before importing so you can tell the user whether a re-import is actually needed.")]
    public async Task<string> GetLeaveBalanceStatus(CancellationToken ct = default)
    {
        if (_config.LoadActiveTenantConfig() is null)
            return """{"error": "Not logged in. Run 'tp login --tenant <id>' first."}""";

        var status = await _api.GetLeaveBalanceStatusAsync(ct);
        if (status?.LastImportedAt is null)
            return JsonSerializer.Serialize(new { imported = false }, JsonOpts);

        return JsonSerializer.Serialize(status, JsonOpts);
    }

    private static string? ResolveEmpId(string? empId, string? employeeId)
    {
        var requestedEmpId = !string.IsNullOrWhiteSpace(empId) ? empId : employeeId;
        return string.IsNullOrWhiteSpace(requestedEmpId) ? null : requestedEmpId.Trim();
    }
}
