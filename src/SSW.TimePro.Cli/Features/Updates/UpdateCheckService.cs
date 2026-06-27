using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure;
using System.Text.Json;

namespace SSW.TimePro.Cli.Features.Updates;

public enum UpdateCheckStatus
{
    Skipped,
    DevelopmentBuild,
    UpToDate,
    UpdateAvailable,
    Error
}

public sealed record UpdateCheckResult(
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    DateTimeOffset? CheckedAt,
    UpdateCheckStatus Status,
    string? ErrorMessage)
{
    public bool UpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable;
}

public static class UpdateCheckService
{
    private static readonly HttpClient GitHubHttpClient = new();

    public static async Task<UpdateCheckResult> CheckLatestReleaseAsync(CancellationToken cancellationToken)
    {
        var currentVersion = BuildInfo.Version;
        if (SemanticVersion.IsDevelopmentVersion(currentVersion))
        {
            return new UpdateCheckResult(
                CurrentVersion: currentVersion,
                LatestVersion: currentVersion,
                ReleaseUrl: null,
                CheckedAt: null,
                Status: UpdateCheckStatus.DevelopmentBuild,
                ErrorMessage: null);
        }

        GitHubRelease latest;
        var checkedAt = DateTimeOffset.UtcNow;
        try
        {
            latest = await new GitHubReleaseClient(GitHubHttpClient).GetLatestReleaseAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new UpdateCheckResult(
                CurrentVersion: currentVersion,
                LatestVersion: null,
                ReleaseUrl: null,
                CheckedAt: checkedAt,
                Status: UpdateCheckStatus.Error,
                ErrorMessage: ex.Message);
        }

        if (!SemanticVersion.TryParse(currentVersion, out var current)
            || !SemanticVersion.TryParse(latest.Version, out var latestVersion))
        {
            return new UpdateCheckResult(
                CurrentVersion: currentVersion,
                LatestVersion: latest.Version,
                ReleaseUrl: latest.Url,
                CheckedAt: checkedAt,
                Status: UpdateCheckStatus.Error,
                ErrorMessage: $"Could not compare versions. Current: '{currentVersion}', latest: '{latest.Version}'.");
        }

        return new UpdateCheckResult(
            CurrentVersion: currentVersion,
            LatestVersion: latest.Version,
            ReleaseUrl: latest.Url,
            CheckedAt: checkedAt,
            Status: current.CompareTo(latestVersion) >= 0
                ? UpdateCheckStatus.UpToDate
                : UpdateCheckStatus.UpdateAvailable,
            ErrorMessage: null);
    }

    public static UpdateCheckResult UseCachedVersionOnError(
        UpdateCheckResult result,
        InstalledVersionConfig versionState)
    {
        if (result.Status != UpdateCheckStatus.Error
            || string.IsNullOrWhiteSpace(versionState.LastUpdateCheckedVersion)
            || !SemanticVersion.TryParse(result.CurrentVersion, out var current)
            || !SemanticVersion.TryParse(versionState.LastUpdateCheckedVersion, out var cachedLatest))
        {
            return result;
        }

        return result with
        {
            LatestVersion = versionState.LastUpdateCheckedVersion,
            ReleaseUrl = GitHubReleaseClient.ReleaseUrlFor(versionState.LastUpdateCheckedVersion),
            CheckedAt = versionState.LastUpdateCheckedAt,
            Status = current.CompareTo(cachedLatest) >= 0
                ? UpdateCheckStatus.UpToDate
                : UpdateCheckStatus.UpdateAvailable
        };
    }
}
