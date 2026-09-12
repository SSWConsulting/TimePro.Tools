using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Cli;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class VerboseCommandLineInterceptorTests
{
    [Fact]
    public void ExtractCommandLineOptions_RemovesTheFlagSoSpectreNeverSeesIt()
    {
        var result = VerboseCommandLineInterceptor.ExtractCommandLineOptions(["ts", "get", "--verbose", "--week"]);

        result.Verbose.Should().BeTrue();
        result.Args.Should().Equal("ts", "get", "--week");
    }

    [Theory]
    [InlineData(true, "--verbose=true")]
    [InlineData(false, "--verbose=false")]
    public void ExtractCommandLineOptions_HonoursAnInlineValue(bool expected, string arg)
    {
        VerboseCommandLineInterceptor.ExtractCommandLineOptions(["ts", "get", arg])
            .Verbose.Should().Be(expected);
    }

    [Fact]
    public void ExtractCommandLineOptions_LeavesArgvAloneWhenTheFlagIsAbsent()
    {
        var result = VerboseCommandLineInterceptor.ExtractCommandLineOptions(["ts", "get", "--week"]);

        result.Verbose.Should().BeFalse();
        result.Args.Should().Equal("ts", "get", "--week");
    }

    [Fact]
    public void ExtractCommandLineOptions_DoesNotReachPastAPassthroughSeparator()
    {
        var result = VerboseCommandLineInterceptor.ExtractCommandLineOptions(["query", "--", "--verbose"]);

        result.Verbose.Should().BeFalse();
        result.Args.Should().Equal("query", "--", "--verbose");
    }

    [Fact]
    public void ExtractCommandLineOptions_IgnoresASimilarlyNamedOption()
    {
        VerboseCommandLineInterceptor.ExtractCommandLineOptions(["ts", "get", "--verbosity"])
            .Verbose.Should().BeFalse();
    }
}
