using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Cli;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class UnknownCommandHelpTests
{
    [Fact]
    public void TryDescribe_UnknownSubcommand_ListsSiblingCommands()
    {
        UnknownCommandHelp.TryDescribe(["ts", "list"])!.Message
            .Should().Be("Unknown command 'list'. Commands: get, create, update, delete, suggest, accept, export, check, copy");
    }

    [Fact]
    public void TryDescribe_NearMiss_SuggestsClosestCommand()
    {
        var unknown = UnknownCommandHelp.TryDescribe(["ts", "updat"])!;

        unknown.Token.Should().Be("updat");
        unknown.Suggestion.Should().Be("update");
        unknown.Detail.Should().StartWith("Did you mean 'update'? Commands: get, create, update");
    }

    [Fact]
    public void TryDescribe_NestedBranch_UsesNestedCommands()
    {
        UnknownCommandHelp.TryDescribe(["leave", "balances", "stats"])!.Message
            .Should().Be("Unknown command 'stats'. Did you mean 'status'? Commands: status, import");
    }

    [Fact]
    public void TryDescribe_UnknownTopLevelCommand_ListsTopLevelCommands()
    {
        var unknown = UnknownCommandHelp.TryDescribe(["timesheets"])!;

        unknown.Suggestion.Should().Be("timesheet");
        unknown.Detail.Should().Contain("Commands: login, logout, tenant");
    }

    [Theory]
    [InlineData("ts", "get")]
    [InlineData("ts", "get", "2026-01-05")]
    [InlineData("leave", "balances", "import", "./balances.csv")]
    [InlineData("--version")]
    [InlineData]
    public void TryDescribe_ValidInvocation_ReturnsNull(params string[] args)
    {
        UnknownCommandHelp.TryDescribe(args).Should().BeNull();
    }

    [Fact]
    public void ClosestMatch_NoCandidateWithinEditDistance_ReturnsNull()
    {
        UnknownCommandHelp.ClosestMatch("list", ["get", "create", "update"]).Should().BeNull();
    }
}
