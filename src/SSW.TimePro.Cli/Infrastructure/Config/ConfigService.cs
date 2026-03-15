using System.Text.Json;

namespace SSW.TimePro.Cli.Infrastructure.Config;

/// <summary>
/// Reads and writes CLI configuration files.
/// </summary>
public interface IConfigService
{
    GlobalConfig LoadGlobalConfig();
    void SaveGlobalConfig(GlobalConfig config);
    TenantConfig? LoadTenantConfig(string tenantId);
    void SaveTenantConfig(TenantConfig config);
    void DeleteTenantConfig(string tenantId);
    TenantConfig? LoadActiveTenantConfig();
    List<TenantConfig> ListTenants();
}

public class ConfigService : IConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GlobalConfig LoadGlobalConfig()
    {
        var path = ConfigPaths.GlobalConfigFile;
        if (!File.Exists(path))
            return new GlobalConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GlobalConfig>(json, JsonOptions) ?? new GlobalConfig();
    }

    public void SaveGlobalConfig(GlobalConfig config)
    {
        ConfigPaths.EnsureDirectories();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPaths.GlobalConfigFile, json);
    }

    public TenantConfig? LoadTenantConfig(string tenantId)
    {
        var path = ConfigPaths.TenantConfigFile(tenantId);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TenantConfig>(json, JsonOptions);
    }

    public void SaveTenantConfig(TenantConfig config)
    {
        ConfigPaths.EnsureDirectories();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPaths.TenantConfigFile(config.TenantId), json);
    }

    public void DeleteTenantConfig(string tenantId)
    {
        var path = ConfigPaths.TenantConfigFile(tenantId);
        if (File.Exists(path))
            File.Delete(path);
    }

    public TenantConfig? LoadActiveTenantConfig()
    {
        var global = LoadGlobalConfig();
        if (string.IsNullOrEmpty(global.ActiveTenant))
            return null;

        return LoadTenantConfig(global.ActiveTenant);
    }

    public List<TenantConfig> ListTenants()
    {
        var dir = ConfigPaths.TenantsDir;
        if (!Directory.Exists(dir))
            return [];

        var tenants = new List<TenantConfig>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var tenant = JsonSerializer.Deserialize<TenantConfig>(json, JsonOptions);
                if (tenant is not null)
                    tenants.Add(tenant);
            }
            catch
            {
                // Skip malformed config files
            }
        }

        return tenants;
    }
}
