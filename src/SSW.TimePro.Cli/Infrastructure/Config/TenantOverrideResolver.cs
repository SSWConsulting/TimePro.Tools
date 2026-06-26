namespace SSW.TimePro.Cli.Infrastructure.Config;

public sealed record TenantOverrideOptions(string? TenantName, string? EnvironmentName)
{
    public bool HasOverride =>
        !string.IsNullOrWhiteSpace(TenantName)
        || !string.IsNullOrWhiteSpace(EnvironmentName);
}

public sealed record TenantOverrideParseResult(
    string[] Args,
    TenantOverrideOptions Options,
    string? Error);

public static class TenantOverrideResolver
{
    private static readonly HashSet<string> CommandsWithOwnTenantOption =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "login",
            "logout",
            "mcp"
        };

    private static readonly string[] KnownEnvironmentSuffixes =
    [
        "-production",
        "-prod",
        "-staging",
        "-stage",
        "-development",
        "-dev",
        "-local",
        "-test"
    ];

    public static TenantOverrideParseResult ExtractCommandLineOptions(string[] args)
    {
        if (CommandOwnsTenantOption(args))
            return new TenantOverrideParseResult(args, new TenantOverrideOptions(null, null), null);

        var filtered = new List<string>(args.Length);
        string? tenant = null;
        string? environment = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                filtered.AddRange(args[i..]);
                break;
            }

            if (TryReadInlineValue(arg, "--tenant=", out var inlineTenant))
            {
                tenant = inlineTenant;
                continue;
            }

            if (TryReadInlineValue(arg, "--env=", out var inlineEnv)
                || TryReadInlineValue(arg, "--environment=", out inlineEnv))
            {
                environment = inlineEnv;
                continue;
            }

            if (arg is "--tenant" or "--env" or "--environment")
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith('-'))
                {
                    return new TenantOverrideParseResult(
                        filtered.ToArray(),
                        new TenantOverrideOptions(tenant, environment),
                        $"{arg} requires a value.");
                }

                if (arg == "--tenant")
                    tenant = args[++i];
                else
                    environment = args[++i];

                continue;
            }

            filtered.Add(arg);
        }

        return new TenantOverrideParseResult(
            filtered.ToArray(),
            new TenantOverrideOptions(Normalize(tenant), Normalize(environment)),
            null);
    }

    public static TenantConfig? ResolveTenantOverride(
        IConfigService config,
        TenantOverrideOptions options,
        out string? error)
    {
        error = null;
        if (!options.HasOverride)
            return null;

        var tenantName = Normalize(options.TenantName);
        var environmentName = Normalize(options.EnvironmentName);

        if (environmentName is null)
            return LoadNamedTenant(config, tenantName!, out error);

        var baseTenantName = tenantName ?? Normalize(config.LoadGlobalConfig().ActiveTenant);
        if (baseTenantName is null)
        {
            error = "No active tenant configured to apply --env. Use --tenant <name> --env <env>, or run 'tp tenant set <name>' first.";
            return null;
        }

        var resolvedTenantName = ResolveEnvironmentTenantName(baseTenantName, environmentName);
        var tenant = config.LoadTenantConfig(resolvedTenantName);
        if (tenant is not null)
            return tenant;

        error = $"Tenant config '{resolvedTenantName}' not found for --env '{environmentName}'. Add it with 'tp login --tenant {resolvedTenantName}' or pick one from 'tp tenant list'.";
        return null;
    }

    public static string ResolveEnvironmentTenantName(string tenantName, string environmentName)
    {
        var baseTenantName = StripKnownEnvironmentSuffixes(Normalize(tenantName) ?? tenantName).ToLowerInvariant();
        var env = (Normalize(environmentName) ?? environmentName).ToLowerInvariant();

        return env switch
        {
            "production" or "prod" => baseTenantName,
            "stage" => $"{baseTenantName}-staging",
            "development" => $"{baseTenantName}-dev",
            _ => $"{baseTenantName}-{env}"
        };
    }

    private static TenantConfig? LoadNamedTenant(IConfigService config, string tenantName, out string? error)
    {
        var tenant = config.LoadTenantConfig(tenantName);
        if (tenant is not null)
        {
            error = null;
            return tenant;
        }

        error = $"Tenant '{tenantName}' not found. Add it with 'tp login --tenant {tenantName}' or pick one from 'tp tenant list'.";
        return null;
    }

    private static bool TryReadInlineValue(string arg, string prefix, out string? value)
    {
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = Normalize(arg[prefix.Length..]);
            return true;
        }

        value = null;
        return false;
    }

    private static bool CommandOwnsTenantOption(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg == "--")
                return false;

            if (arg.StartsWith('-'))
                continue;

            return CommandsWithOwnTenantOption.Contains(arg);
        }

        return false;
    }

    private static string StripKnownEnvironmentSuffixes(string tenantName)
    {
        foreach (var suffix in KnownEnvironmentSuffixes)
        {
            if (tenantName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return tenantName[..^suffix.Length];
        }

        return tenantName;
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
