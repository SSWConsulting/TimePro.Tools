using System.ComponentModel;
using FluentAssertions;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Mcp;

public class ToolIlScannerTests
{
    [Fact]
    public void FindsTheToolBehindADirectCall()
    {
        var direct = ToolIlScanner.MethodsCalling(typeof(ToolsFixture), typeof(ITimeProApiClient));

        direct.Should().BeEquivalentTo([nameof(ToolsFixture.CallsTheApiItself)]);
    }

    [Fact]
    public void RefusesToAttributeACallMadeFromAPrivateHelper()
    {
        var scan = () => ToolIlScanner.MethodsCalling(typeof(HelperFixture), typeof(ITimeProApiClient));

        scan.Should().Throw<InvalidOperationException>().WithMessage("*Helper*ITimeProApiClient*");
    }

    [McpServerToolType]
    private class ToolsFixture
    {
        private readonly ITimeProApiClient _api = null!;

        [McpServerTool]
        [Description("Fixture: reads through the API client.")]
        public async Task<string> CallsTheApiItself(CancellationToken ct) =>
            (await _api.GetIterationsAsync("1I776Q", ct)).Count.ToString();

        [McpServerTool]
        [Description("Fixture: touches nothing.")]
        public string CallsNothing() => "ok";
    }

    [McpServerToolType]
    private class HelperFixture
    {
        private readonly ITimeProApiClient _api = null!;

        [McpServerTool]
        [Description("Fixture: delegates to a private helper that reads.")]
        public Task<string> DelegatesToAHelper(CancellationToken ct) => Helper(ct);

        private async Task<string> Helper(CancellationToken ct) =>
            (await _api.GetIterationsAsync("1I776Q", ct)).Count.ToString();
    }
}
