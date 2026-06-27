namespace SSW.TimePro.Cli.Infrastructure.Config;

public sealed record FeatureFlagParseResult(
    string[] Args,
    IReadOnlyList<FeatureDefinition> EnableFeatures);

public static class FeatureFlagCommandLineInterceptor
{
    public static FeatureFlagParseResult ExtractCommandLineOptions(string[] args)
    {
        var filtered = new List<string>(args.Length);
        var enableFeatures = new Dictionary<string, FeatureDefinition>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                filtered.AddRange(args[i..]);
                break;
            }

            if (TryResolveLegacyFlag(arg, out var definition))
            {
                enableFeatures[definition.Key] = definition;
                continue;
            }

            filtered.Add(arg);
        }

        return new FeatureFlagParseResult(filtered.ToArray(), enableFeatures.Values.ToList());
    }

    public static void EnableRequestedFeatures(IConfigService config, IReadOnlyList<FeatureDefinition> requestedFeatures)
    {
        if (requestedFeatures.Count == 0)
            return;

        var global = config.LoadGlobalConfig();
        var changed = false;

        foreach (var feature in requestedFeatures)
        {
            var existing = global.GetFeature(feature.Key);
            if (!existing.Enabled || existing.Version != feature.Version)
            {
                global.SetFeature(feature, enabled: true);
                changed = true;
            }
        }

        if (changed)
            config.SaveGlobalConfig(global);
    }

    private static bool TryResolveLegacyFlag(string arg, out FeatureDefinition definition)
    {
        switch (arg)
        {
            case "--accounting":
                return FeatureCatalog.TryNormalize(FeatureCatalog.Accounting, out definition);
            case "--developer":
            case "--dev":
                return FeatureCatalog.TryNormalize(FeatureCatalog.Developer, out definition);
            default:
                definition = default!;
                return false;
        }
    }
}
