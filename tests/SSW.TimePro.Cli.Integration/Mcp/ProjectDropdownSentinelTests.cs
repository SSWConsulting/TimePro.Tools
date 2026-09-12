using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Both surfaces must drop <see cref="NorthwindApi.SentinelDisplayText"/>, which the fake API
/// serves on every project read.
/// </summary>
[Collection(CliConsoleCollection.Name)]
public class ProjectDropdownSentinelTests : TestBase
{
    [Fact]
    public async Task FakeApiStillServesTheSentinel()
    {
        NorthwindApi.StubAll(WireMock);

        var raw = await ApiClient.GetProjectsForClientAsync(
            NorthwindApi.EmpId, NorthwindApi.ClientId, TestContext.Current.CancellationToken);

        raw.Should().Contain(p =>
            p.Value == null && p.DisplayText == NorthwindApi.SentinelDisplayText);
    }

    [Fact]
    public async Task NeitherSurfaceReturnsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        NorthwindApi.StubAll(WireMock);
        var config = McpToolCatalog.Config(WireMock.Url!);

        var cli = await CliRunner.RunAsync(
            ["project", "list", "--client", NorthwindApi.ClientId, "--json"], ApiClient, config, ct);
        var mcp = await new McpToolHost(ApiClient, config)
            .Lookups.GetProjectsForClient(NorthwindApi.ClientId, ct);

        foreach (var (surface, json) in new[] { ("project list --json", cli.Stdout), ("GetProjectsForClient", mcp) })
        {
            json.Should().NotContain(NorthwindApi.SentinelDisplayText, $"{surface} must not offer the placeholder");

            var rows = JsonNode.Parse(json)!.AsArray();
            rows.Should().HaveCount(2, surface);
            rows.Should().OnlyContain(
                row => !string.IsNullOrWhiteSpace((string?)row!["value"]), surface);
        }
    }
}
