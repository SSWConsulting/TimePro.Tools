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

    private static string? ResolveEmpId(string? empId, string? employeeId)
    {
        var requestedEmpId = !string.IsNullOrWhiteSpace(empId) ? empId : employeeId;
        return string.IsNullOrWhiteSpace(requestedEmpId) ? null : requestedEmpId.Trim();
    }
}
