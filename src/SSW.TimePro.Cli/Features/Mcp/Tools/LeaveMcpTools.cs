using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;

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

    private static string? ResolveEmpId(string? empId, string? employeeId)
    {
        var requestedEmpId = !string.IsNullOrWhiteSpace(empId) ? empId : employeeId;
        return string.IsNullOrWhiteSpace(requestedEmpId) ? null : requestedEmpId.Trim();
    }
}
