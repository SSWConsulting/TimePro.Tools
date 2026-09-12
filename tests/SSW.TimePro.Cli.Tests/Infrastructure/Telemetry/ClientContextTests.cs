using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Telemetry;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure.Telemetry;

public class ClientContextTests
{
    [Fact]
    public void BeginMcpTool_PrefixesTheToolName()
    {
        var invocation = ClientContext.BeginMcpTool("update_timesheet");

        invocation.Surface.Should().Be("mcp");
        invocation.Command.Should().Be("mcp:update_timesheet");
        ClientContext.Clear();
    }

    [Theory]
    [InlineData("update_timesheet", "mcp:update_timesheet")]
    [InlineData("UpdateTimesheet", "mcp:updatetimesheet")]
    [InlineData("get-leave", "mcp:get-leave")]
    public void BeginMcpTool_AcceptsAPlainToolName(string tool, string expected)
    {
        ClientContext.BeginMcpTool(tool).Command.Should().Be(expected);
        ClientContext.Clear();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("update\r\nx-injected: 1")]
    [InlineData("update timesheet")]
    [InlineData("update/timesheet")]
    [InlineData("tööl")]
    public void BeginMcpTool_RejectsANameThatCouldNotBeAHeaderValue(string? tool)
    {
        // The name arrives from the MCP client; a newline in it would break every later request.
        ClientContext.BeginMcpTool(tool).Command.Should().Be($"mcp:{ClientContext.UnknownTool}");
        ClientContext.Clear();
    }

    [Fact]
    public void BeginMcpTool_RejectsAnOverlongName()
    {
        ClientContext.BeginMcpTool(new string('a', 65)).Command
            .Should().Be($"mcp:{ClientContext.UnknownTool}");
        ClientContext.Clear();
    }

    [Fact]
    public async Task OverlappingMcpToolCalls_DoNotSeeEachOthersContext()
    {
        var firstEntered = new TaskCompletionSource();
        var secondEntered = new TaskCompletionSource();

        async Task<(string Command, IReadOnlyList<RequestRecord> Requests)> CallAsync(
            string tool, TaskCompletionSource entered, Task otherEntered)
        {
            var invocation = ClientContext.BeginMcpTool(tool);
            entered.SetResult();

            // Interleave: neither call proceeds until both have set their own context.
            await otherEntered;

            ClientContext.Current!.Record(new RequestRecord($"{tool}-request", "GET", "/api/Timesheets", 200));
            await Task.Yield();

            return (ClientContext.Current!.Command, invocation.Requests);
        }

        var first = Task.Run(() => CallAsync("get_timesheets", firstEntered, secondEntered.Task));
        var second = Task.Run(() => CallAsync("update_timesheet", secondEntered, firstEntered.Task));

        var results = await Task.WhenAll(first, second);

        results[0].Command.Should().Be("mcp:get_timesheets");
        results[1].Command.Should().Be("mcp:update_timesheet");
        results[0].Requests.Should().ContainSingle().Which.RequestId.Should().Be("get_timesheets-request");
        results[1].Requests.Should().ContainSingle().Which.RequestId.Should().Be("update_timesheet-request");
    }

    [Fact]
    public void Record_IsSafeFromConcurrentRequestsInOneInvocation()
    {
        var invocation = new ClientInvocation("cli", "ts check");

        Parallel.For(0, 200, i =>
            invocation.Record(new RequestRecord($"id-{i}", "GET", "/api/Timesheets", 200)));

        invocation.Requests.Should().HaveCount(200);
    }
}
