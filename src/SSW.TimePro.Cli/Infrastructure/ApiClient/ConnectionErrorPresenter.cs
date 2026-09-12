using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Infrastructure.ApiClient;

/// <summary>
/// Turns a <see cref="TimeProConnectionException"/> into operator-readable text that names the
/// tenant config behind the unreachable URL and how to switch away from it.
/// </summary>
public static class ConnectionErrorPresenter
{
    public static IReadOnlyList<string> BuildContextLines(TimeProConnectionException ex, string? homeDirectory = null)
    {
        var tenantLabel = ex.TenantFile ?? ex.TenantId ?? "unknown";
        var location = ex.TenantFile is null
            ? $"apiUrl {ex.ApiUrl}"
            : $"{TenantConfigPath(ex.TenantFile, homeDirectory)}, apiUrl {ex.ApiUrl}";

        return
        [
            $"Active tenant: {tenantLabel} ({location})",
            "Switch with: tp tenant set <name>   (see: tp tenant list)"
        ];
    }

    public static string BuildDetail(TimeProConnectionException ex, string? homeDirectory = null) =>
        string.Join(" ", BuildContextLines(ex, homeDirectory));

    private static string TenantConfigPath(string tenantFile, string? homeDirectory)
    {
        var path = ConfigPaths.TenantConfigFile(tenantFile);
        var home = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return !string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.Ordinal)
            ? string.Concat("~", path.AsSpan(home.Length))
            : path;
    }
}
