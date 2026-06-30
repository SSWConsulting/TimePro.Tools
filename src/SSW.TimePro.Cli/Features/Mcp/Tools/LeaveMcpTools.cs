using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Features.Leave;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Mcp.Tools;

[McpServerToolType]
public class LeaveMcpTools
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public LeaveMcpTools(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    [McpServerTool]
    [Description("List EasyLeave entries. Use empId for one person; employeeId is accepted as an alias. Omit both to return all visible leave.")]
    public async Task<string> GetLeaveEntries(
        [Description("Filter: UPCOMING (default) or PAST")] string filter = "UPCOMING",
        [Description("Number of entries to return")] int limit = 10,
        [Description("empId to filter by")] string? empId = null,
        [Description("Alias for empId")] string? employeeId = null,
        CancellationToken ct = default)
    {
        if (_config.LoadActiveTenantConfig() is null)
            return """{"error":"Not logged in. Run 'tp login --tenant <id>' first."}""";

        var response = await _api.GetLeaveAsync(
            filter.ToUpperInvariant(),
            pageNumber: 1,
            pageSize: limit,
            employeeId: ResolveEmpId(empId, employeeId),
            ct);

        return JsonSerializer.Serialize(response?.Leaves?.Items ?? [], JsonOpts);
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
    [Description("Create an EasyLeave request for the current user. An explicit timezone override takes priority; otherwise uses the TimePro profile timezone first, then the MCP host machine timezone as the browser-equivalent fallback.")]
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
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in. Run 'tp login --tenant <id>' first."}""";

        if (string.IsNullOrWhiteSpace(note))
            return JsonSerializer.Serialize(new { error = "note is required: a reason/description is mandatory for leave" }, JsonOpts);

        var settings = string.IsNullOrWhiteSpace(timeZoneId)
            ? await _api.GetEmployeeSettingsAsync(ct)
            : null;
        if (!LeaveRequestParser.TryResolveRequestTimeZone(timeZoneId, settings, out var requestTimeZone, out var timeZoneError))
            return JsonSerializer.Serialize(new { error = timeZoneError ?? "Invalid leave request timezone" }, JsonOpts);

        if (!LeaveRequestParser.TryParseDateRange(start, end, requestTimeZone, out var startDate, out var endDate, out var dateError))
            return JsonSerializer.Serialize(new { error = dateError ?? "Invalid leave date range" }, JsonOpts);

        if (IsWeekend(startDate) || IsWeekend(endDate))
            return JsonSerializer.Serialize(new { error = "Leave start and end dates must be weekdays" }, JsonOpts);

        var leaveTypeId = await ResolveLeaveTypeAsync(type, ct);
        if (leaveTypeId is null)
            return JsonSerializer.Serialize(new { error = $"Unknown leave type: '{type}'." }, JsonOpts);

        var request = new CreateLeaveRequest
        {
            RequestedEmpId = tenant.EmployeeId,
            StartDate = startDate.ToString("o"),
            EndDate = endDate.ToString("o"),
            LeaveTypeId = leaveTypeId.Value,
            Note = note.Trim(),
            UserStartTime = LeaveRequestParser.NormalizeTime(startTime, LeaveRequestParser.DefaultStartTime),
            UserEndTime = LeaveRequestParser.NormalizeTime(endTime, LeaveRequestParser.DefaultEndTime),
            AllDay = !halfDay,
            OptionalEmp = LeaveRequestParser.ParseOptionalEmployees(cc),
            ApprovedBy = approvedBy,
            TimeLessOverride = null
        };

        await _api.CreateLeaveAsync(request, ct);
        return JsonSerializer.Serialize(new { success = true }, JsonOpts);
    }

    private static string? ResolveEmpId(string? empId, string? employeeId)
    {
        var requestedEmpId = !string.IsNullOrWhiteSpace(empId) ? empId : employeeId;
        return string.IsNullOrWhiteSpace(requestedEmpId) ? null : requestedEmpId.Trim();
    }

    private async Task<int?> ResolveLeaveTypeAsync(string typeInput, CancellationToken ct)
    {
        if (int.TryParse(typeInput, out var id))
            return id;

        var types = await _api.GetLeaveTypesAsync(ct);
        var match = types.FirstOrDefault(t =>
            t.Name.Equals(typeInput, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }

    private static bool IsWeekend(DateTimeOffset date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
