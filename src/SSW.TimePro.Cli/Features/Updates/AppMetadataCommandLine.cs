using SSW.TimePro.Cli.Infrastructure;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using System.Text.Json;

namespace SSW.TimePro.Cli.Features.Updates;

public static class AppMetadataCommandLine
{
    private static readonly HashSet<string> KnownOptions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "--check-update",
            "--check-version",
            "--whats-new",
            "--url"
        };

    public static bool IsMetadataRequest(string[] args) =>
        args.Any(arg => KnownOptions.Contains(arg));

    public static async Task<int> ExecuteAsync(
        string[] args,
        IConfigService configService,
        CancellationToken cancellationToken)
    {
        var checkUpdate = args.Contains("--check-update", StringComparer.OrdinalIgnoreCase)
            || args.Contains("--check-version", StringComparer.OrdinalIgnoreCase);
        var whatsNew = args.Contains("--whats-new", StringComparer.OrdinalIgnoreCase);
        var urlOnly = args.Contains("--url", StringComparer.OrdinalIgnoreCase);

        if (checkUpdate == whatsNew)
        {
            OutputHelper.WriteError("Use exactly one of --check-update/--check-version or --whats-new.");
            return 1;
        }

        var unsupported = args
            .Where(arg => arg.StartsWith('-') && !KnownOptions.Contains(arg))
            .ToList();
        if (unsupported.Count > 0)
        {
            OutputHelper.WriteError($"Unsupported update option: {unsupported[0]}");
            return 1;
        }

        var positional = args.Where(arg => !arg.StartsWith('-')).ToList();
        if (positional.Count > 0)
        {
            OutputHelper.WriteError("Update metadata options are top-level flags. Use them without a command name.");
            return 1;
        }

        if (checkUpdate)
        {
            if (urlOnly)
            {
                OutputHelper.WriteError("--url is only supported with --whats-new.");
                return 1;
            }

            return await CheckUpdateAsync(configService, cancellationToken);
        }

        return WriteWhatsNew(configService, urlOnly);
    }

    private static async Task<int> CheckUpdateAsync(IConfigService configService, CancellationToken cancellationToken)
    {
        var result = await UpdateCheckService.CheckLatestReleaseAsync(cancellationToken);
        RecordUpdateCheck(configService, result);
        result = UpdateCheckService.UseCachedVersionOnError(result, LoadVersionState(configService));

        Console.WriteLine($"Current version: {result.CurrentVersion}");
        if (result.Status == UpdateCheckStatus.DevelopmentBuild)
        {
            Console.WriteLine("Development build detected; skipping GitHub update check and treating it as current.");
            return 0;
        }

        if (result.Status == UpdateCheckStatus.Error)
        {
            OutputHelper.WriteError($"Could not check GitHub Releases: {result.ErrorMessage}");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            OutputHelper.WriteWarning($"Could not refresh GitHub Releases: {result.ErrorMessage}");
            OutputHelper.WriteWarning("Using the last successfully checked version.");
        }

        Console.WriteLine($"Latest version: {result.LatestVersion}");
        Console.WriteLine($"Last checked: {result.CheckedAt:u}");
        Console.WriteLine($"Release notes: {result.ReleaseUrl}");

        if (result.Status == UpdateCheckStatus.UpToDate)
            Console.WriteLine("Status: up to date");
        else
            WriteUpdateAvailable();

        return 0;
    }

    public static bool RecordUpdateCheck(IConfigService configService, UpdateCheckResult result)
    {
        if (result.LatestVersion is not null && result.CheckedAt is not null)
            return VersionStateService.RecordUpdateCheck(configService, result.LatestVersion, result.CheckedAt.Value);

        return false;
    }

    public static void WriteUpdateAvailable()
    {
        Console.WriteLine("Status: update available");
        Console.WriteLine();
        Console.WriteLine("Install/update:");
        Console.WriteLine($"  macOS/Linux: curl -fsSL {GitHubReleaseClient.InstallScriptUrl} | bash");
        Console.WriteLine($"  Windows:     irm {GitHubReleaseClient.InstallPowerShellUrl} | iex");
    }

    private static int WriteWhatsNew(IConfigService configService, bool urlOnly)
    {
        var catalog = ReleaseNotesCatalog.LoadEmbedded();
        var latest = catalog.LatestKnown();
        if (latest is null)
        {
            OutputHelper.WriteError("No embedded release notes were found.");
            return 1;
        }

        if (urlOnly)
        {
            Console.WriteLine(latest.Url);
            return 0;
        }

        var versionState = LoadVersionState(configService);
        Console.WriteLine(catalog.RenderWhatsNewMarkdown(
            currentVersion: BuildInfo.Version,
            previousVersion: versionState.PreviousVersion,
            installedAt: versionState.InstalledAt));
        return 0;
    }

    private static InstalledVersionConfig LoadVersionState(IConfigService configService)
    {
        try
        {
            return configService.LoadGlobalConfig().Version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            OutputHelper.WriteWarning("Could not read installed version state; showing the latest known release notes.");
            return new InstalledVersionConfig();
        }
    }
}
