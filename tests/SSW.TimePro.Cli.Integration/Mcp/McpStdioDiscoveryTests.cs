using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Discovery goldens taken from the real stdio server, not from reflection: names, descriptions,
/// input/output schemas and annotations as an MCP client actually sees them, with the accounting
/// pack off and on.
/// </summary>
[Collection(McpStdioCollection.Name)]
public class McpStdioDiscoveryTests
{
    public const int DefaultToolCount = 19;
    public const int AccountingEnabledToolCount = 48;

    [Fact]
    public async Task ToolsList_WithAccountingDisabled_MatchesGolden()
    {
        var tools = await ListToolsAsync(accountingEnabled: false);

        tools.Count.Should().Be(DefaultToolCount);
        Golden.Verify("Discovery/tools-list.default.json", Describe(tools));
    }

    [Fact]
    public async Task ToolsList_WithAccountingEnabled_MatchesGolden()
    {
        var tools = await ListToolsAsync(accountingEnabled: true);

        tools.Count.Should().Be(AccountingEnabledToolCount);
        Golden.Verify("Discovery/tools-list.accounting.json", Describe(tools));
    }

    [Fact]
    public async Task ToolsList_DifferenceBetweenGates_IsExactlyTheAccountingTools()
    {
        var withoutAccounting = Names(await ListToolsAsync(accountingEnabled: false));
        var withAccounting = Names(await ListToolsAsync(accountingEnabled: true));

        withAccounting.Except(withoutAccounting).Should()
            .BeEquivalentTo(McpToolInventory.AccountingToolNames);
        withoutAccounting.Except(withAccounting).Should().BeEmpty();
        withoutAccounting.Should().BeEquivalentTo(McpToolInventory.DefaultToolNames);
    }

    [Fact]
    public async Task ToolsList_HasNoDuplicateNames()
    {
        var names = Names(await ListToolsAsync(accountingEnabled: true));

        names.Should().OnlyHaveUniqueItems();
    }

    private static List<string> Names(List<JsonObject> tools) =>
        tools.Select(t => t["name"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Projects the tool list into the golden shape: everything a client can see, with keys the
    /// server omits left out so that adding or dropping <c>annotations</c> is a visible diff.
    /// </summary>
    private static string Describe(List<JsonObject> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools.OrderBy(t => t["name"]!.GetValue<string>(), StringComparer.Ordinal))
            array.Add(tool.DeepClone());

        return new JsonObject
        {
            ["count"] = tools.Count,
            ["tools"] = array
        }.ToJsonString();
    }

    private static async Task<List<JsonObject>> ListToolsAsync(bool accountingEnabled)
    {
        using var config = new IsolatedConfigDirectory("https://timepro.invalid", accountingEnabled);
        await using var client = await McpStdioClient.StartAsync(
            config.Root, ct: TestContext.Current.CancellationToken);

        return await client.ListToolsAsync(TestContext.Current.CancellationToken);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public class McpStdioCollection
{
    public const string Name = "mcp-stdio";
}
