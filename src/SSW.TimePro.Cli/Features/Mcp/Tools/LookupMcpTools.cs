using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Features.Mcp.Tools;

[McpServerToolType]
public class LookupMcpTools
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public LookupMcpTools(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    [McpServerTool]
    [Description("Search for clients by name. Returns client IDs and names.")]
    public async Task<string> SearchClients(
        [Description("Search text")] string query,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in"}""";

        var results = await _api.SearchClientsAsync(tenant.EmployeeId, query, ct);
        return JsonSerializer.Serialize(results, JsonOpts);
    }

    [McpServerTool]
    [Description("Get projects for a client. Returns project IDs and names.")]
    public async Task<string> GetProjectsForClient(
        [Description("Client ID")] string clientId,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in"}""";

        var results = await _api.GetProjectsForClientAsync(tenant.EmployeeId, clientId, ct);
        return JsonSerializer.Serialize(results, JsonOpts);
    }

    [McpServerTool]
    [Description("Get the current employee's billing rate for a client. Includes rate expiry info.")]
    public async Task<string> GetClientRate(
        [Description("Client ID")] string clientId,
        [Description("Date for rate lookup (yyyy-MM-dd). Defaults to today.")] string? date = null,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in"}""";

        var dateOnly = date is not null
            ? DateOnly.ParseExact(date, "yyyy-MM-dd")
            : DateOnly.FromDateTime(DateTime.Today);

        var rate = await _api.GetClientRateAsync(tenant.EmployeeId, clientId, dateOnly, ct);
        return JsonSerializer.Serialize(rate, JsonOpts);
    }

    [McpServerTool]
    [Description("Get CRM bookings/appointments for a date range.")]
    public async Task<string> GetCrmBookings(
        [Description("Start date (yyyy-MM-dd)")] string startDate,
        [Description("End date (yyyy-MM-dd)")] string endDate,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in"}""";

        var start = DateOnly.ParseExact(startDate, "yyyy-MM-dd");
        var end = DateOnly.ParseExact(endDate, "yyyy-MM-dd");

        var results = await _api.GetAppointmentsAsync(tenant.EmployeeId, start, end.AddDays(1), ct);
        return JsonSerializer.Serialize(results, JsonOpts);
    }

    [McpServerTool]
    [Description("Get the WFH/location defaults and repo mapping for a given path.")]
    public string GetLocationAndMapping(
        [Description("Repository path to check for mapping")] string? repoPath = null)
    {
        var global = _config.LoadGlobalConfig();
        var mappings = _config.LoadRepoMappings();

        RepoMappingEntry? match = null;
        if (repoPath is not null)
        {
            var normalized = repoPath.Replace("~",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            match = RepoDetector.Detect(normalized, mappings);
        }

        var result = new
        {
            defaultLocation = global.DefaultLocation,
            wfhDays = global.WfhDays,
            repoMapping = match is not null ? new
            {
                match.ClientId,
                match.ProjectId,
                match.ProjectName,
                match.CategoryId
            } : null
        };

        return JsonSerializer.Serialize(result, JsonOpts);
    }
}
