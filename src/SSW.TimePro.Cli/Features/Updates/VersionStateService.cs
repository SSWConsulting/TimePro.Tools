using SSW.TimePro.Cli.Infrastructure.Config;
using System.Text.Json;

namespace SSW.TimePro.Cli.Features.Updates;

public static class VersionStateService
{
    public static bool RecordInstalledVersion(
        IConfigService configService,
        string currentVersion,
        DateTimeOffset installedAt)
    {
        if (!SemanticVersion.TryParse(currentVersion, out _)
            || SemanticVersion.IsDevelopmentVersion(currentVersion))
        {
            return false;
        }

        try
        {
            var global = configService.LoadGlobalConfig();
            if (string.Equals(global.Version.Version, currentVersion, StringComparison.OrdinalIgnoreCase))
                return false;

            global.Version.PreviousVersion = SemanticVersion.IsDevelopmentVersion(global.Version.Version)
                ? null
                : global.Version.Version;
            global.Version.Version = currentVersion;
            global.Version.InstalledAt = installedAt;
            global.Version.LastUpdateCheckedAt = installedAt;
            global.Version.LastUpdateCheckedVersion = currentVersion;
            configService.SaveGlobalConfig(global);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public static bool RecordUpdateCheck(
        IConfigService configService,
        string latestVersion,
        DateTimeOffset checkedAt)
    {
        if (!SemanticVersion.TryParse(latestVersion, out _))
            return false;

        try
        {
            var global = configService.LoadGlobalConfig();
            global.Version.LastUpdateCheckedAt = checkedAt;
            global.Version.LastUpdateCheckedVersion = latestVersion;
            configService.SaveGlobalConfig(global);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
