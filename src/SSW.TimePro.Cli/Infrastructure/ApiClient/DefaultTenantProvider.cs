using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Infrastructure.ApiClient;

/// <summary>
/// Provides the current tenant from config, with support for temporary overrides
/// (e.g. during login before the tenant is saved to disk).
/// </summary>
public class DefaultTenantProvider : ITenantProvider
{
    private readonly IConfigService _config;
    private TenantConfig? _override;

    public DefaultTenantProvider(IConfigService config)
    {
        _config = config;
    }

    public TenantConfig? GetCurrentTenant()
    {
        return _override ?? _config.LoadActiveTenantConfig();
    }

    /// <summary>
    /// Temporarily override the current tenant (e.g. during login verification).
    /// Call <see cref="ClearOverride"/> when done.
    /// </summary>
    public void SetOverride(TenantConfig tenant) => _override = tenant;

    /// <summary>
    /// Clear the temporary override.
    /// </summary>
    public void ClearOverride() => _override = null;
}
