using FluentAssertions;
using SSW.TimePro.Cli.Features.Updates;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Updates;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("0.2.7", 0, 2, 7)]
    [InlineData("v0.2.7", 0, 2, 7)]
    [InlineData("0.2", 0, 2, 0)]
    [InlineData("0.2.7+abc123", 0, 2, 7)]
    public void TryParse_ParsesExpectedShapes(string input, int major, int minor, int patch)
    {
        SemanticVersion.TryParse(input, out var version).Should().BeTrue();
        version.Should().Be(new SemanticVersion(major, minor, patch));
    }

    [Fact]
    public void CompareTo_OrdersByMajorMinorPatch()
    {
        new SemanticVersion(0, 2, 10)
            .CompareTo(new SemanticVersion(0, 2, 9))
            .Should().BePositive();
    }

    [Theory]
    [InlineData("0.1")]
    [InlineData("0.1.0")]
    [InlineData("0.2.0")]
    public void IsDevelopmentVersion_TreatsPatchZeroAsDeveloperBuild(string version) =>
        SemanticVersion.IsDevelopmentVersion(version).Should().BeTrue();
}
