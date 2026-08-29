using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Paths;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure.Paths;

public class PathExpanderTests
{
    [Theory]
    [InlineData("~/Downloads/LeaveBalances.csv")]
    [InlineData(@"~\Downloads\LeaveBalances.csv")]
    public void ExpandHomeDirectory_WithTildePrefix_UsesCurrentUserProfile(string path)
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "LeaveBalances.csv");

        PathExpander.ExpandHomeDirectory(path).Should().Be(expected);
    }

    [Fact]
    public void ExpandHomeDirectory_WithTildeOnly_ReturnsCurrentUserProfile()
    {
        PathExpander.ExpandHomeDirectory("~").Should().Be(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    [Theory]
    [InlineData("~northwind/LeaveBalances.csv")]
    [InlineData("exports/~/LeaveBalances.csv")]
    public void ExpandHomeDirectory_WhenTildeIsNotTheFirstSegment_LeavesPathUnchanged(string path)
    {
        PathExpander.ExpandHomeDirectory(path).Should().Be(path);
    }
}
