using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Fixed config for in-process harness runs. Deliberately never touches the filesystem, so a
/// harness case can never pick up the developer's own tenants, WFH days or feature flags.
/// </summary>
public sealed class TestConfigService : IConfigService
{
    private readonly TenantConfig _tenant;

    public TestConfigService(TenantConfig tenant) => _tenant = tenant;

    public GlobalConfig Global { get; init; } = new()
    {
        ActiveTenant = "northwind-test",
        DefaultLocation = "Office",
        WfhDays = []
    };

    public List<RepoMappingEntry> RepoMappings { get; init; } = [];

    public string ConfigDirectory => Path.Combine(Path.GetTempPath(), "tp-mcp-harness-inproc");
    public GlobalConfig LoadGlobalConfig() => Global;
    public void SaveGlobalConfig(GlobalConfig config) { }
    public TenantConfig? LoadTenantConfig(string tenantId) => _tenant;
    public void SaveTenantConfig(TenantConfig config) { }
    public void DeleteTenantConfig(string tenantId) { }
    public TenantConfig? LoadActiveTenantConfig() => _tenant;
    public void SetActiveTenantOverride(TenantConfig tenant) { }
    public void ClearActiveTenantOverride() { }
    public List<TenantConfig> ListTenants() => [_tenant];
    public List<RepoMappingEntry> LoadRepoMappings() => RepoMappings;
    public void SaveRepoMappings(List<RepoMappingEntry> mappings) { }
}
