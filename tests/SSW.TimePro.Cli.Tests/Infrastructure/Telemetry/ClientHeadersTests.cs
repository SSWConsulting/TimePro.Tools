using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Telemetry;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure.Telemetry;

public class ClientHeadersTests
{
    [Theory]
    [InlineData("0.3.1", "timepro-cli/0.3.1")]
    [InlineData("0.3.1+abc1234def56", "timepro-cli/0.3.1")]
    [InlineData("1.0.0-beta.2+sha", "timepro-cli/1.0.0-beta.2")]
    public void UserAgent_IsProductSlashVersion_WithBuildMetadataDropped(string version, string expected)
    {
        ClientHeaders.UserAgent(version).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0.3.1 (local build)")]
    public void UserAgent_FallsBackToZeroes_WhenTheVersionIsNotASingleToken(string? version)
    {
        ClientHeaders.UserAgent(version!).Should().Be("timepro-cli/0.0.0");
    }

    [Theory]
    [InlineData("x-timepro-client-request-id")]
    [InlineData("request-id")]
    [InlineData("x-request-id")]
    public void ResolveRequestId_PrefersTheServerEchoedId(string header)
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation(header, "server-side-id");

        ClientHeaders.ResolveRequestId(response, "locally-generated").Should().Be("server-side-id");
    }

    [Fact]
    public void ResolveRequestId_KeepsTheLocalId_WhenTheServerEchoesNothing()
    {
        using var response = new HttpResponseMessage();

        ClientHeaders.ResolveRequestId(response, "locally-generated").Should().Be("locally-generated");
    }

    [Fact]
    public void ResolveRequestId_IgnoresAnEmptyEcho()
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("x-request-id", "  ");

        ClientHeaders.ResolveRequestId(response, "locally-generated").Should().Be("locally-generated");
    }
}
