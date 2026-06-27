namespace SSW.TimePro.Cli.Infrastructure.Config;

public sealed record FeatureDefinition(
    string Key,
    string DisplayName,
    string Description,
    int Version,
    IReadOnlyList<string> Aliases);

public static class FeatureCatalog
{
    public const string Accounting = "accounting";
    public const string Developer = "developer";

    public static IReadOnlyList<FeatureDefinition> All { get; } =
    [
        new(
            Accounting,
            "Accounting",
            "Accounting skills and read-only accounting MCP diagnostics.",
            Version: 1,
            Aliases: ["accounts", "accountant"]),
        new(
            Developer,
            "Developer",
            "Developer diagnostics, environment comparison, reproduction, and fix-verification skills.",
            Version: 1,
            Aliases: ["dev", "developers"]),
    ];

    public static bool TryNormalize(string feature, out FeatureDefinition definition)
    {
        var requested = feature.Trim().ToLowerInvariant();

        foreach (var candidate in All)
        {
            if (candidate.Key == requested ||
                candidate.Aliases.Any(alias => alias.Equals(requested, StringComparison.OrdinalIgnoreCase)))
            {
                definition = candidate;
                return true;
            }
        }

        definition = default!;
        return false;
    }

    public static FeatureConfig GetFeature(this GlobalConfig config, string featureKey)
    {
        if (!config.Features.TryGetValue(featureKey, out var feature))
        {
            feature = new FeatureConfig();
            config.Features[featureKey] = feature;
        }

        return feature;
    }

    public static bool IsFeatureEnabled(this GlobalConfig config, string featureKey) =>
        config.Features.TryGetValue(featureKey, out var feature) && feature.Enabled;

    public static void SetFeature(this GlobalConfig config, FeatureDefinition definition, bool enabled)
    {
        var feature = config.GetFeature(definition.Key);
        feature.Enabled = enabled;
        feature.Version = definition.Version;
    }

    public static void TouchFeatureVersion(this GlobalConfig config, string featureKey)
    {
        if (!TryNormalize(featureKey, out var definition))
            return;

        var feature = config.GetFeature(definition.Key);
        feature.Version = definition.Version;
    }
}
