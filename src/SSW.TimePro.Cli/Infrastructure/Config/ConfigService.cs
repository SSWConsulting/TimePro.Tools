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
    List<RepoMappingEntry> LoadRepoMappings();
    void SaveRepoMappings(List<RepoMappingEntry> mappings);
}

public class RepoMappingEntry
{
    /// <summary>
    /// File system path pattern (supports ~ and trailing /*).
    /// </summary>
    public string PathPattern { get; set; } = string.Empty;

    /// <summary>
    /// Git remote URL pattern (e.g., "github.com/asfaudits/chat-bot" or "github.com/asfaudits/*").
    /// Matched against the origin remote URL. Supports trailing /* for org-wide matching.
    /// </summary>
    public string? RemotePattern { get; set; }

    public string ClientId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? CategoryId { get; set; }
}

public class ConfigService : IConfigService
{
    private readonly string _basePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a ConfigService using the default config path (~/.config/timepro-cli/).
    /// </summary>
    public ConfigService() : this(ConfigPaths.Root) { }

    /// <summary>
    /// Creates a ConfigService using a custom base path (for testing).
    /// </summary>
    public ConfigService(string basePath)
    {
        _basePath = basePath;
    }

    private string GlobalConfigFile => Path.Combine(_basePath, "config.json");
    private string TenantsDir => Path.Combine(_basePath, "tenants");
    private string TenantConfigFile(string tenantId) =>
        Path.Combine(TenantsDir, $"{tenantId.ToLowerInvariant()}.json");

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(_basePath);
        Directory.CreateDirectory(TenantsDir);
    }

    public GlobalConfig LoadGlobalConfig()
    {
        if (!File.Exists(GlobalConfigFile))
            return new GlobalConfig();

        var json = File.ReadAllText(GlobalConfigFile);
        return JsonSerializer.Deserialize<GlobalConfig>(json, JsonOptions) ?? new GlobalConfig();
    }

    public void SaveGlobalConfig(GlobalConfig config)
    {
        EnsureDirectories();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(GlobalConfigFile, json);
    }

    public TenantConfig? LoadTenantConfig(string tenantId)
    {
        var path = TenantConfigFile(tenantId);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TenantConfig>(json, JsonOptions);
    }

    public void SaveTenantConfig(TenantConfig config)
    {
        EnsureDirectories();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(TenantConfigFile(config.TenantId), json);
    }

    public void DeleteTenantConfig(string tenantId)
    {
        var path = TenantConfigFile(tenantId);
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
        if (!Directory.Exists(TenantsDir))
            return [];

        var tenants = new List<TenantConfig>();
        foreach (var file in Directory.GetFiles(TenantsDir, "*.json"))
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

    private string RepoMappingsFile => Path.Combine(_basePath, "repo-mappings.json");

    public List<RepoMappingEntry> LoadRepoMappings()
    {
        if (!File.Exists(RepoMappingsFile))
            return [];

        var json = File.ReadAllText(RepoMappingsFile);
        return JsonSerializer.Deserialize<List<RepoMappingEntry>>(json, JsonOptions) ?? [];
    }

    public void SaveRepoMappings(List<RepoMappingEntry> mappings)
    {
        EnsureDirectories();
        var json = JsonSerializer.Serialize(mappings, JsonOptions);
        File.WriteAllText(RepoMappingsFile, json);
    }
}
