using System.Text.Json;
using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Locks the raw JSON text every MCP tool returns. These goldens are the extraction tripwire for
/// the CLI/MCP unification slices: an internal refactor that changes a tool's observable payload
/// fails here rather than in a client.
/// </summary>
public class McpToolResultGoldenTests : TestBase
{
    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var toolCase in McpToolCatalog.All)
            data.Add(toolCase.DisplayName);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ToolResult_MatchesGolden(string caseName)
    {
        var toolCase = McpToolCatalog.All.Single(c => c.DisplayName == caseName);

        NorthwindApi.StubAll(WireMock);
        toolCase.Arrange?.Invoke(WireMock);

        var host = new McpToolHost(ApiClient, McpToolCatalog.Config(WireMock.Url!));

        string actual;
        try
        {
            actual = await toolCase.Invoke(host, TestContext.Current.CancellationToken);
        }
        catch (ApiException ex)
        {
            // Today an API failure escapes the tool as a protocol-level error rather than an
            // isError payload. Snapshot it so a change of policy is a reviewed change.
            actual = JsonSerializer.Serialize(new
            {
                threw = nameof(ApiException),
                statusCode = ex.StatusCode,
                message = ex.Message,
                responseBody = ex.ResponseBody
            });
        }

        if (toolCase.TokenizeCurrentWeek)
            actual = WeekTokens.Apply(actual);

        Golden.Verify(toolCase.GoldenPath, actual);
    }

    [Fact]
    public void Catalog_CoversEveryRegisteredTool()
    {
        var declared = McpToolCatalog.ToolNames.ToHashSet(StringComparer.Ordinal);

        McpToolInventory.AllMethodNames.Should().BeSubsetOf(declared);
        declared.Should().BeSubsetOf(McpToolInventory.AllMethodNames);
    }
}
