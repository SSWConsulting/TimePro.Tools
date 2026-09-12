using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Cli;
using SSW.TimePro.Cli.Infrastructure.Telemetry;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class CommandPathResolverTests
{
    [Theory]
    [InlineData("ts get", "ts", "get", "--week")]
    [InlineData("ts update", "ts", "update", "42", "--iteration", "Sprint 5")]
    [InlineData("leave balances import", "leave", "balances", "import", "./LeaveBalances.csv", "--yes")]
    [InlineData("invoice get", "invoice", "get", "999999", "--json")]
    [InlineData("info", "info")]
    [InlineData("mcp", "mcp")]
    public void Resolve_KeepsOnlyTheRegisteredCommandTokens(string expected, params string[] args)
    {
        CommandPathResolver.Resolve(args).Should().Be(expected);
    }

    [Theory]
    [InlineData("ts", "ts", "nosuchsubcommand")]
    [InlineData("ts", "ts", "--help")]
    public void Resolve_StopsAtTheFirstTokenThatIsNotACommand(string expected, params string[] args)
    {
        CommandPathResolver.Resolve(args).Should().Be(expected);
    }

    [Theory]
    [InlineData()]
    [InlineData("--version")]
    [InlineData("nosuchcommand")]
    public void Resolve_ReportsUnknown_WhenNoCommandWasNamed(params string[] args)
    {
        CommandPathResolver.Resolve(args).Should().Be(CommandPathResolver.Unknown);
    }

    [Fact]
    public void Resolve_NeverLeaksArgumentValues()
    {
        var resolved = CommandPathResolver.Resolve(
            ["ts", "create", "--client", "NWIND", "--notes", "Checkout API for Northwind"]);

        resolved.Should().Be("ts create");
    }

    [Fact]
    public void ClientContext_SurfacesTheResolvedPathAsTheCliCommand()
    {
        var invocation = ClientContext.BeginCli(CommandPathResolver.Resolve(["ts", "get", "--week"]));

        invocation.Surface.Should().Be("cli");
        invocation.Command.Should().Be("ts get");
        ClientContext.Clear();
    }
}
