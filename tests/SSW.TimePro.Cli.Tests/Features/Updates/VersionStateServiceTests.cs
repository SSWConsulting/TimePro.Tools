using FluentAssertions;
using SSW.TimePro.Cli.Features.Updates;
using SSW.TimePro.Cli.Infrastructure.Config;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Updates;

public class VersionStateServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"timepro-version-test-{Guid.NewGuid():N}");
    private readonly ConfigService _config;

    public VersionStateServiceTests()
    {
        _config = new ConfigService(_tempDir);
    }

    [Fact]
    public void RecordInstalledVersion_FirstRelease_SavesVersionAndInstallDate()
    {
        var installedAt = new DateTimeOffset(2026, 6, 27, 10, 30, 0, TimeSpan.Zero);

        var changed = VersionStateService.RecordInstalledVersion(_config, "0.2.1", installedAt);

        changed.Should().BeTrue();
        var version = _config.LoadGlobalConfig().Version;
        version.Version.Should().Be("0.2.1");
        version.PreviousVersion.Should().BeNull();
        version.InstalledAt.Should().Be(installedAt);
        version.LastUpdateCheckedAt.Should().Be(installedAt);
        version.LastUpdateCheckedVersion.Should().Be("0.2.1");
    }

    [Fact]
    public void RecordInstalledVersion_WhenVersionChanges_PreservesPreviousVersion()
    {
        VersionStateService.RecordInstalledVersion(_config, "0.2.1", DateTimeOffset.Parse("2026-06-27T10:30:00Z"));

        VersionStateService.RecordInstalledVersion(_config, "0.2.2", DateTimeOffset.Parse("2026-06-28T10:30:00Z"));

        var version = _config.LoadGlobalConfig().Version;
        version.Version.Should().Be("0.2.2");
        version.PreviousVersion.Should().Be("0.2.1");
        version.InstalledAt.Should().Be(DateTimeOffset.Parse("2026-06-28T10:30:00Z"));
        version.LastUpdateCheckedAt.Should().Be(DateTimeOffset.Parse("2026-06-28T10:30:00Z"));
        version.LastUpdateCheckedVersion.Should().Be("0.2.2");
    }

    [Fact]
    public void RecordInstalledVersion_DevelopmentBuild_DoesNotOverwriteSavedReleaseVersion()
    {
        VersionStateService.RecordInstalledVersion(_config, "0.2.1", DateTimeOffset.Parse("2026-06-27T10:30:00Z"));

        var changed = VersionStateService.RecordInstalledVersion(_config, "0.2.0", DateTimeOffset.Parse("2026-06-28T10:30:00Z"));

        changed.Should().BeFalse();
        var version = _config.LoadGlobalConfig().Version;
        version.Version.Should().Be("0.2.1");
        version.PreviousVersion.Should().BeNull();
        version.LastUpdateCheckedVersion.Should().Be("0.2.1");
    }

    [Fact]
    public void RecordUpdateCheck_SavesLatestVersionAndCheckDate()
    {
        var checkedAt = DateTimeOffset.Parse("2026-06-29T10:30:00Z");

        var changed = VersionStateService.RecordUpdateCheck(_config, "0.2.3", checkedAt);

        changed.Should().BeTrue();
        var version = _config.LoadGlobalConfig().Version;
        version.LastUpdateCheckedAt.Should().Be(checkedAt);
        version.LastUpdateCheckedVersion.Should().Be("0.2.3");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
