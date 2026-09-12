using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// The create endpoint answers with an empty body, so both surfaces submit once and then read the
/// entry back. A second submission would duplicate the leave request.
/// </summary>
[Collection(CliConsoleCollection.Name)]
public class LeaveCreateReadbackTests : TestBase
{
    private static readonly string[] CliArgs =
    [
        "leave", "create", "--start", "2026-04-01", "--end", "2026-04-01",
        "--type", "Annual Leave", "--note", "Family day",
        "--timezone", McpToolCatalog.TimeZone, "--yes", "--json"
    ];

    [Fact]
    public async Task Cli_SubmitsOnceThenReadsTheEntryBack()
    {
        var ct = TestContext.Current.CancellationToken;
        NorthwindApi.StubAll(WireMock);

        var result = await CliRunner.RunAsync(CliArgs, ApiClient, McpToolCatalog.Config(WireMock.Url!), ct);

        result.ExitCode.Should().Be(0);
        AssertCreatedEntry(result.Stdout);
        AssertOneCreateThenAReadBack();
    }

    [Fact]
    public async Task Mcp_SubmitsOnceThenReadsTheEntryBack()
    {
        var ct = TestContext.Current.CancellationToken;
        NorthwindApi.StubAll(WireMock);

        var json = await new McpToolHost(ApiClient, McpToolCatalog.Config(WireMock.Url!))
            .Leave.CreateLeave(
                start: "2026-04-01", end: "2026-04-01", type: "Annual Leave", note: "Family day",
                timeZoneId: McpToolCatalog.TimeZone, ct: ct);

        AssertCreatedEntry(json);
        AssertOneCreateThenAReadBack();
    }

    [Fact]
    public async Task WhenNoEntryMatches_NeitherSurfaceSubmitsAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        NorthwindApi.StubAll(WireMock);
        NorthwindApi.RawJson(
            WireMock, "/api/leave/", "GET",
            """{"leaves":{"pageNumber":1,"pageSize":100,"totalItems":0,"totalPages":0,"items":[]},"cancelledCount":0}""",
            NorthwindApi.OverridePriority);

        var cli = await CliRunner.RunAsync(CliArgs, ApiClient, McpToolCatalog.Config(WireMock.Url!), ct);
        cli.ExitCode.Should().Be(0);
        Creates().Should().Be(1);

        WireMock.Reset();
        NorthwindApi.StubAll(WireMock);
        NorthwindApi.RawJson(
            WireMock, "/api/leave/", "GET",
            """{"leaves":{"pageNumber":1,"pageSize":100,"totalItems":0,"totalPages":0,"items":[]},"cancelledCount":0}""",
            NorthwindApi.OverridePriority);

        var mcp = await new McpToolHost(ApiClient, McpToolCatalog.Config(WireMock.Url!))
            .Leave.CreateLeave(
                start: "2026-04-01", end: "2026-04-01", type: "Annual Leave", note: "Family day",
                timeZoneId: McpToolCatalog.TimeZone, ct: ct);
        Creates().Should().Be(1);

        foreach (var (surface, json) in new[] { ("leave create --json", cli.Stdout), ("CreateLeave", mcp) })
        {
            var root = JsonNode.Parse(json)!.AsObject();
            ((bool?)root["success"]).Should().BeTrue(surface);
            root["leave"].Should().BeNull(surface);
            ((string?)root["warning"]).Should().Contain("could not be identified", surface)
                .And.Contain("Do not create it again");
        }
    }

    private static void AssertCreatedEntry(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();

        ((bool?)root["success"]).Should().BeTrue();
        ((string?)root["leaveId"]).Should().Be(NorthwindApi.LeaveId);
        ((string?)root["leave"]!["id"]).Should().Be(NorthwindApi.LeaveId);
        ((string?)root["leave"]!["note"]).Should().Be("Family day");
    }

    private void AssertOneCreateThenAReadBack()
    {
        var leaveCalls = WireMock.LogEntries
            .Select(e => $"{e.RequestMessage!.Method.ToUpperInvariant()} {e.RequestMessage.AbsolutePath}")
            .Where(call => call.EndsWith("/api/leave/", StringComparison.Ordinal))
            .ToList();

        leaveCalls.Should().StartWith(["POST /api/leave/", "GET /api/leave/"]);
        leaveCalls.Should().NotContain("PUT /api/leave/");
        leaveCalls.Count(call => call == "POST /api/leave/").Should().Be(1);
    }

    private int Creates() =>
        WireMock.LogEntries.Count(e =>
            e.RequestMessage!.AbsolutePath == "/api/leave/"
            && string.Equals(e.RequestMessage.Method, "POST", StringComparison.OrdinalIgnoreCase));
}
