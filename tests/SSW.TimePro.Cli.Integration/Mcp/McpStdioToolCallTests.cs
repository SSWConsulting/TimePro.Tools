using System.Text.Json.Nodes;
using FluentAssertions;
using WireMock.Server;
using WireMock.Settings;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// A representative slice of tools driven through real <c>tools/call</c> frames, so the envelope
/// itself — content type, <c>isError</c>, presence of <c>structuredContent</c> — is locked, and the
/// returned text is proven identical to the direct-call goldens.
/// </summary>
[Collection(McpStdioCollection.Name)]
public class McpStdioToolCallTests : IDisposable
{
    private readonly WireMockServer _wireMock = WireMockServer.Start(new WireMockServerSettings { Port = 0 });

    public McpStdioToolCallTests() => NorthwindApi.StubAll(_wireMock);

    public static TheoryData<string, string> Calls() => new()
    {
        { "get_timesheets", $$"""{"date":"{{NorthwindApi.AnyDate}}"}""" },
        { "list_iterations", $$"""{"projectId":"{{NorthwindApi.ProjectId}}"}""" },
        { "get_client_rate", $$"""{"clientId":"{{NorthwindApi.ClientId}}","date":"{{NorthwindApi.AnyDate}}"}""" },
        { "get_leave_entries", $$"""{"filter":"UPCOMING","limit":10,"empId":"{{NorthwindApi.EmpId}}"}""" },
        { "update_timesheet", $$"""{"timesheetId":{{NorthwindApi.TimesheetId}},"description":"Order history","date":"{{NorthwindApi.AnyDate}}"}""" },
        { "delete_timesheet", $$"""{"timesheetId":{{NorthwindApi.TimesheetId}},"date":"{{NorthwindApi.AnyDate}}"}""" },
    };

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task ToolCall_OverStdio_MatchesEnvelopeGolden(string toolName, string argumentsJson)
    {
        var result = await CallAsync(toolName, argumentsJson);

        Golden.Verify($"Calls/{toolName}.json", result.ToJsonString());
    }

    [Theory]
    [InlineData("get_timesheets", "GetTimesheets", """{"date":"2026-03-16"}""")]
    [InlineData("list_iterations", "ListIterations", """{"projectId":"1I776Q"}""")]
    [InlineData("get_client_rate", "GetClientRate", """{"clientId":"NWIND","date":"2026-03-16"}""")]
    [InlineData("get_leave_entries", "GetLeaveEntries", """{"filter":"UPCOMING","limit":10,"empId":"BOB"}""")]
    [InlineData("update_timesheet", "UpdateTimesheet", """{"timesheetId":4242,"description":"Order history","date":"2026-03-16"}""")]
    public async Task ToolCall_OverStdio_ReturnsTheSameTextAsTheDirectCall(
        string toolName, string methodName, string argumentsJson)
    {
        var result = await CallAsync(toolName, argumentsJson);

        var text = result["content"]!.AsArray()
            .Select(c => c!.AsObject())
            .Single(c => c["type"]!.GetValue<string>() == "text")["text"]!
            .GetValue<string>();

        var directCallGolden = File.ReadAllText(Golden.PathFor($"Tools/{methodName}.populated.json"));

        Golden.Canonicalize(text).Should().Be(Golden.Canonicalize(directCallGolden));
    }

    [Fact]
    public async Task ToolCall_WhenTheApiFails_IsReportedAsAToolError()
    {
        NorthwindApi.Status(
            _wireMock, "/api/Timesheets/GetTimesheetListViewModel", "GET", 500,
            """{"title":"Server error"}""", NorthwindApi.OverridePriority);

        var result = await CallAsync("get_timesheets", $$"""{"date":"{{NorthwindApi.AnyDate}}"}""");

        result["isError"]!.GetValue<bool>().Should().BeTrue();
        result["content"]!.AsArray().Should().NotBeEmpty();
    }

    private async Task<JsonObject> CallAsync(string toolName, string argumentsJson)
    {
        using var config = new IsolatedConfigDirectory(_wireMock.Url!, accountingEnabled: false);
        await using var client = await McpStdioClient.StartAsync(
            config.Root, ct: TestContext.Current.CancellationToken);

        return await client.CallToolAsync(
            toolName,
            (JsonObject)JsonNode.Parse(argumentsJson)!,
            TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _wireMock.Dispose();
        GC.SuppressFinalize(this);
    }
}
