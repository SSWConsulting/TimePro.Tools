using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Cli;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Runs a CLI command and its MCP tool against identical, freshly reset Northwind state and
/// compares the complete parsed documents plus the HTTP traffic each produced.
/// </summary>
public class CliMcpParityTests : TestBase
{
    public static TheoryData<int> ExecutableRows()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < McpCliParityTable.Rows.Count; i++)
            if (McpCliParityTable.Rows[i].IsExecutable)
                data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(ExecutableRows))]
    public async Task CliAndMcp_DifferOnlyWherePermitted(int rowIndex)
    {
        var row = McpCliParityTable.Rows[rowIndex];
        var ct = TestContext.Current.CancellationToken;

        var cliJson = Normalize(row, await RunCliAsync(row, ct));
        var cliRequests = CapturedRequests();

        WireMock.Reset();

        var mcpJson = Normalize(row, await RunToolAsync(row, ct));
        var mcpRequests = CapturedRequests();

        Golden.Verify($"Parity/{row.ToolMethod}.{rowIndex}.cli.json", cliJson);
        Golden.Verify($"Parity/{row.ToolMethod}.{rowIndex}.mcp.json", mcpJson);

        var differences = JsonDiff.Paths(cliJson, mcpJson);

        if (row.ExpectParity)
        {
            differences.Should().BeEmpty(
                $"{row.ToolMethod} is expected at parity.\nCLI:\n{cliJson}\nMCP:\n{mcpJson}");
        }
        else
        {
            row.Note.Should().NotBeNullOrWhiteSpace("a non-parity row must say why");
            differences.Should().BeSubsetOf(row.PermittedDifferences,
                $"{row.ToolMethod} may only differ where declared.\nCLI:\n{cliJson}\nMCP:\n{mcpJson}");
        }

        AssertTraffic(row, "CLI", cliRequests);
        AssertTraffic(row, "MCP", mcpRequests);
    }

    [Fact]
    public void Table_CoversEveryRegisteredTool()
    {
        var declared = McpCliParityTable.Rows.Select(r => r.ToolMethod).ToHashSet(StringComparer.Ordinal);

        declared.Should().BeEquivalentTo(McpToolInventory.AllMethodNames);
    }

    [Fact]
    public void EveryToolMapsToARegisteredCliCommand_OrIsAllowlisted()
    {
        var unmapped = new List<string>();

        foreach (var method in McpToolInventory.AllMethods)
        {
            var rows = McpCliParityTable.Rows.Where(r => r.ToolMethod == method.Name).ToList();
            rows.Should().NotBeEmpty($"{method.Name} needs a row in the CLI/MCP table");

            var path = rows[0].CliCommandPath;
            if (path is null)
            {
                McpCliParityTable.ToolsWithoutCliMirror.Should().Contain(method.Name,
                    $"{method.Name} has no CLI command and is not on the shrink-only allowlist");
                unmapped.Add(method.Name);
                continue;
            }

            McpCliParityTable.ToolsWithoutCliMirror.Should().NotContain(method.Name,
                $"{method.Name} now maps to '{path}', so remove it from the allowlist");

            Resolve(path).Should().BeTrue($"'tp {path}' is not a registered command");
        }

        unmapped.Should().BeSubsetOf(McpCliParityTable.ToolsWithoutCliMirror,
            "the no-CLI-mirror allowlist may only shrink");
    }

    private static bool Resolve(string commandPath)
    {
        var node = CommandCatalog.Root;
        foreach (var segment in commandPath.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node.Child(segment);
            if (node is null)
                return false;
        }

        return true;
    }

    private async Task<string> RunCliAsync(ParityRow row, CancellationToken ct)
    {
        NorthwindApi.StubAll(WireMock);
        row.Arrange?.Invoke(WireMock);

        var result = await CliRunner.RunAsync(
            row.CliArgs!, ApiClient, McpToolCatalog.Config(WireMock.Url!), ct);

        result.Stdout.Should().NotBeNullOrWhiteSpace(
            $"'tp {string.Join(' ', row.CliArgs!)}' wrote nothing to stdout. stderr:\n{result.Stderr}");

        return result.Stdout;
    }

    private async Task<string> RunToolAsync(ParityRow row, CancellationToken ct)
    {
        NorthwindApi.StubAll(WireMock);
        row.Arrange?.Invoke(WireMock);

        var host = new McpToolHost(ApiClient, McpToolCatalog.Config(WireMock.Url!));
        return await row.InvokeTool!(host, ct);
    }

    private List<(string Method, string Path, string? Body)> CapturedRequests() =>
        WireMock.LogEntries
            .Select(e => (e.RequestMessage!.Method, e.RequestMessage.AbsolutePath, e.RequestMessage.Body))
            .ToList();

    private static void AssertTraffic(
        ParityRow row, string side, List<(string Method, string Path, string? Body)> requests)
    {
        foreach (var expected in row.ExpectedRequests)
        {
            var matches = requests
                .Where(r => string.Equals(r.Method, expected.Method, StringComparison.OrdinalIgnoreCase)
                            && r.Path == expected.Path)
                .ToList();

            matches.Should().HaveCount(expected.Count,
                $"{side} {row.ToolMethod}: {expected.Method} {expected.Path}");

            foreach (var fragment in expected.BodyContains)
                matches.Should().Contain(r => r.Body != null && r.Body.Contains(fragment),
                    $"{side} {row.ToolMethod}: payload must contain {fragment}");
        }

        foreach (var forbidden in row.ForbiddenRequests)
            requests.Should().NotContain(
                r => r.Path == forbidden.Path
                     && string.Equals(r.Method, forbidden.Method, StringComparison.OrdinalIgnoreCase),
                $"{side} {row.ToolMethod} must not send {forbidden.Method} {forbidden.Path}");
    }

    private static string Normalize(ParityRow row, string json) =>
        row.TokenizeCurrentWeek ? WeekTokens.Apply(json) : json;
}
