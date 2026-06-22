using System.Reflection;

namespace SSW.TimePro.Cli.Infrastructure;

/// <summary>
/// Build-time identity of the running CLI: package version and the git commit it
/// was built from. Both values are baked into the assembly at build/pack time, so
/// they resolve identically for a local build and a CI-produced package without
/// needing git installed on the machine running <c>tp</c>.
///
/// The commit hash is the reliable identifier: even if the version number is a
/// local default (e.g. <c>0.1.0</c>), the commit pins the exact source.
/// </summary>
public static class BuildInfo
{
    private static readonly Assembly SelfAssembly = typeof(BuildInfo).Assembly;

    /// <summary>Package/assembly version, e.g. <c>0.1.42</c>. Falls back to <c>0.0.0</c>.</summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>Short git commit hash the build came from, or <c>unknown</c>.</summary>
    public static string Commit { get; } = ResolveCommit();

    private static string ResolveVersion()
    {
        var informational = SelfAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Drop any "+<build-metadata>" suffix (e.g. source revision id).
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return SelfAssembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static string ResolveCommit()
    {
        var commit = SelfAssembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, "GitCommit", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return string.IsNullOrWhiteSpace(commit) ? "unknown" : commit;
    }
}
