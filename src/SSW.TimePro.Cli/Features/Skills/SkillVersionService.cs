using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Features.Skills;

public sealed record SkillVersionStatus(
    string Name,
    int InstalledVersion,
    int LatestVersion,
    int? IgnoredVersion,
    bool IsOutOfDate,
    bool IsIgnored,
    string? Path,
    bool Global);

public static class SkillVersionService
{
    public static IReadOnlyList<SkillVersionStatus> GetStatuses(GlobalConfig config) =>
        config.Skills
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair =>
            {
                var latest = SkillModelBuilder.FindDefinition(pair.Key)?.Version ?? pair.Value.Version;
                var ignored = pair.Value.IgnoredVersion == latest;
                var outOfDate = pair.Value.Version < latest && !ignored;

                return new SkillVersionStatus(
                    pair.Key,
                    pair.Value.Version,
                    latest,
                    pair.Value.IgnoredVersion,
                    outOfDate,
                    ignored,
                    pair.Value.Path,
                    pair.Value.Global);
            })
            .ToList();

    public static void RecordInstall(
        GlobalConfig config,
        SkillContentModel model,
        string path,
        bool global,
        DateTimeOffset installedAt)
    {
        config.Skills[model.Name] = new SkillInstallConfig
        {
            Version = model.Version,
            IgnoredVersion = null,
            InstalledAt = installedAt,
            Path = path,
            Global = global
        };
    }

    public static bool IgnoreVersion(GlobalConfig config, string skillName, int? version, out string error)
    {
        var definition = SkillModelBuilder.FindDefinition(skillName);
        if (definition is null && !config.Skills.ContainsKey(skillName))
        {
            error = $"Unknown generated skill '{skillName}'. Known skills: {string.Join(", ", SkillModelBuilder.Catalog.Select(s => s.Name))}.";
            return false;
        }

        var canonicalName = definition?.Name ?? skillName;
        if (!config.Skills.TryGetValue(canonicalName, out var installed))
            installed = config.Skills[canonicalName] = new SkillInstallConfig();

        installed.IgnoredVersion = version ?? definition?.Version ?? installed.Version;
        error = string.Empty;
        return true;
    }
}
