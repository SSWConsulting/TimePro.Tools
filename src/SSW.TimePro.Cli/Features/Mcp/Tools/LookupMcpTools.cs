using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Features.Projects;
using SSW.TimePro.Cli.Features.Rates;
using SSW.TimePro.Cli.Features.Users;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Paths;

namespace SSW.TimePro.Cli.Features.Mcp.Tools;

[McpServerToolType]
public class LookupMcpTools
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
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

        var results = await ProjectLookup.SelectableAsync(_api, tenant.EmployeeId, clientId, ct);
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

        var rate = await RateLookup.FetchAsync(
            _api, tenant.EmployeeId, clientId, RateLookup.ResolveDate(date), ct);
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
    [Description("List active staff expected to log timesheets (empId, name, email). Excludes admin, service, work experience, contractor and retired accounts. Pair with check_week per empId to find missing timesheets.")]
    public async Task<string> ListStaff(CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error": "Not logged in"}""";

        var staff = await StaffDirectory.ListAsync(_api, ct);
        return JsonSerializer.Serialize(staff, JsonOpts);
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
            var normalized = PathExpander.ExpandHomeDirectory(repoPath);
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
