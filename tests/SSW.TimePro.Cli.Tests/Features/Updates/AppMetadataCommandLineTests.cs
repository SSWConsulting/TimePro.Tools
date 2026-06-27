using FluentAssertions;
using SSW.TimePro.Cli.Features.Updates;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Updates;

public class AppMetadataCommandLineTests
{
    [Theory]
    [InlineData("--check-update")]
    [InlineData("--check-version")]
    [InlineData("--whats-new")]
    public void IsMetadataRequest_ReturnsTrueForLegacyTopLevelFlags(string option)
    {
        AppMetadataCommandLine.IsMetadataRequest([option]).Should().BeTrue();
    }

    [Fact]
    public void IsMetadataRequest_AllowsDiscoverableWhatsNewCommandOptions()
    {
        AppMetadataCommandLine.IsMetadataRequest(["whats-new", "--url"]).Should().BeFalse();
    }
}
