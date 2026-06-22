using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class BuildInfoTests
{
    [Fact]
    public void Version_IsNeverNullOrEmpty()
    {
        BuildInfo.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Commit_IsNeverNullOrEmpty()
    {
        // Resolves to a real hash when built from git, otherwise the "unknown"
        // sentinel -- but always a usable, non-empty value for a request header.
        BuildInfo.Commit.Should().NotBeNullOrWhiteSpace();
    }
}
