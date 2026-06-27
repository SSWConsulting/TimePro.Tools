using FluentAssertions;
using SSW.TimePro.Cli.Features.Updates;
using SSW.TimePro.Cli.Infrastructure.Config;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Updates;

public class UpdateCheckServiceTests
{
    [Fact]
    public void UseCachedVersionOnError_ShowsCachedUpdateWhenRefreshFails()
    {
        var checkedAt = DateTimeOffset.Parse("2026-06-27T10:30:00Z");
        var result = new UpdateCheckResult(
            CurrentVersion: "0.2.1",
            LatestVersion: null,
            ReleaseUrl: null,
            CheckedAt: DateTimeOffset.Parse("2026-06-28T10:30:00Z"),
            Status: UpdateCheckStatus.Error,
            ErrorMessage: "offline");
        var versionState = new InstalledVersionConfig
        {
            LastUpdateCheckedAt = checkedAt,
            LastUpdateCheckedVersion = "0.2.3"
        };

        var fallback = UpdateCheckService.UseCachedVersionOnError(result, versionState);

        fallback.Status.Should().Be(UpdateCheckStatus.UpdateAvailable);
        fallback.LatestVersion.Should().Be("0.2.3");
        fallback.ReleaseUrl.Should().Be("https://github.com/SSWConsulting/TimePro.Tools/releases/tag/v0.2.3");
        fallback.CheckedAt.Should().Be(checkedAt);
        fallback.ErrorMessage.Should().Be("offline");
    }

    [Fact]
    public void UseCachedVersionOnError_ShowsUpToDateWhenCachedVersionMatches()
    {
        var result = new UpdateCheckResult(
            CurrentVersion: "0.2.3",
            LatestVersion: null,
            ReleaseUrl: null,
            CheckedAt: DateTimeOffset.Parse("2026-06-28T10:30:00Z"),
            Status: UpdateCheckStatus.Error,
            ErrorMessage: "offline");
        var versionState = new InstalledVersionConfig
        {
            LastUpdateCheckedAt = DateTimeOffset.Parse("2026-06-27T10:30:00Z"),
            LastUpdateCheckedVersion = "0.2.3"
        };

        var fallback = UpdateCheckService.UseCachedVersionOnError(result, versionState);

        fallback.Status.Should().Be(UpdateCheckStatus.UpToDate);
        fallback.LatestVersion.Should().Be("0.2.3");
    }

    [Fact]
    public void UseCachedVersionOnError_KeepsErrorWhenNoCachedVersionExists()
    {
        var result = new UpdateCheckResult(
            CurrentVersion: "0.2.1",
            LatestVersion: null,
            ReleaseUrl: null,
            CheckedAt: DateTimeOffset.Parse("2026-06-28T10:30:00Z"),
            Status: UpdateCheckStatus.Error,
            ErrorMessage: "offline");

        var fallback = UpdateCheckService.UseCachedVersionOnError(result, new InstalledVersionConfig());

        fallback.Should().Be(result);
    }
}
