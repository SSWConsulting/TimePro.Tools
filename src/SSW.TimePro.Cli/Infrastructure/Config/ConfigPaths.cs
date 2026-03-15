namespace SSW.TimePro.Cli.Infrastructure.Config;

/// <summary>
/// Centralised paths for all configuration files.
/// </summary>
public static class ConfigPaths
{
    private static readonly string ConfigHome = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "timepro-cli");

    public static string Root => ConfigHome;
    public static string GlobalConfigFile => Path.Combine(ConfigHome, "config.json");
    public static string TenantsDir => Path.Combine(ConfigHome, "tenants");
    public static string RepoMappingsFile => Path.Combine(ConfigHome, "repo-mappings.json");

    public static string TenantConfigFile(string tenantId) =>
        Path.Combine(TenantsDir, $"{tenantId.ToLowerInvariant()}.json");

    /// <summary>
    /// Ensures all config directories exist.
    /// </summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigHome);
        Directory.CreateDirectory(TenantsDir);
    }
}
