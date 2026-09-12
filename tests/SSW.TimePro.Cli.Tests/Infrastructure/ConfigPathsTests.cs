using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Config;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class ConfigPathsTests
{
    [Fact]
    public void Resolve_WithoutAnOverride_UsesTheUserProfile()
    {
        ConfigPaths.Resolve(null, "/home/bob")
            .Should().Be(Path.Combine("/home/bob", ".config", "timepro-cli"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithABlankOverride_UsesTheUserProfile(string configDir)
    {
        ConfigPaths.Resolve(configDir, "/home/bob")
            .Should().Be(Path.Combine("/home/bob", ".config", "timepro-cli"));
    }

    [Fact]
    public void Resolve_WithAnOverride_UsesIt()
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tp-config"));

        ConfigPaths.Resolve(Path.Combine(Path.GetTempPath(), "tp-config"), "/home/bob")
            .Should().Be(expected);
    }

    [Fact]
    public void Resolve_WithARelativeOverride_ReturnsAnAbsolutePath()
    {
        var resolved = ConfigPaths.Resolve("./tp-config", "/home/bob");

        Path.IsPathRooted(resolved).Should().BeTrue();
        resolved.Should().EndWith("tp-config");
    }

    [Fact]
    public void Resolve_WithATildeOverride_ExpandsTheHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        ConfigPaths.Resolve("~/tp-config", "/home/bob")
            .Should().Be(Path.Combine(home, "tp-config"));
    }
}
