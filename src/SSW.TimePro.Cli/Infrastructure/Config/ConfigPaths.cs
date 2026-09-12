namespace SSW.TimePro.Cli.Infrastructure.Config;

/// <summary>
/// Centralised paths for all configuration files.
/// </summary>
public static class ConfigPaths
{
    /// <summary>
    /// Overrides the config root. Exists so tests and MCP hosts can run against an isolated
    /// config directory instead of the developer's real <c>~/.config/timepro-cli</c>.
    /// </summary>
    public const string ConfigDirEnvVar = "TIMEPRO_CLI_CONFIG_DIR";

    private static readonly string ConfigHome = Resolve(
        Environment.GetEnvironmentVariable(ConfigDirEnvVar),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string Root => ConfigHome;
    public static string GlobalConfigFile => Path.Combine(ConfigHome, "config.json");
    public static string TenantsDir => Path.Combine(ConfigHome, "tenants");
    public static string RepoMappingsFile => Path.Combine(ConfigHome, "repo-mappings.json");
    public static string LogsDir => Path.Combine(ConfigHome, "logs");

    public static string TenantConfigFile(string tenantId) =>
        Path.Combine(TenantsDir, $"{tenantId.ToLowerInvariant()}.json");

    internal static string Resolve(string? configDirOverride, string userProfile)
    {
        if (string.IsNullOrWhiteSpace(configDirOverride))
            return Path.Combine(userProfile, ".config", "timepro-cli");

        var expanded = Paths.PathExpander.ExpandHomeDirectory(configDirOverride.Trim());
        return Path.GetFullPath(expanded);
    }

    /// <summary>
    /// Ensures all config directories exist.
    /// </summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigHome);
        Directory.CreateDirectory(TenantsDir);
    }
}
