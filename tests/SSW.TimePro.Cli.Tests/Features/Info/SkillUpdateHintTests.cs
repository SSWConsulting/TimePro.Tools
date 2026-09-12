using FluentAssertions;
using SSW.TimePro.Cli.Features.Info;
using SSW.TimePro.Cli.Features.Skills;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Info;

public class SkillUpdateHintTests
{
    private const string Home = "/home/bob";

    private static SkillVersionStatus Skill(string? path, bool global) =>
        new("timepro-timesheets", 1, 2, null, true, false, path, global);

    [Theory]
    [InlineData(".claude")]
    [InlineData(".codex")]
    [InlineData(".agents")]
    public void UpdateCommandFor_GlobalSkill_UsesInstalledAgentDirectory(string agentDirectory)
    {
        var path = Path.Combine(Home, agentDirectory, "skills", "timepro-timesheets", "SKILL.md");

        InfoCommand.UpdateCommandFor(Skill(path, global: true), Home)
            .Should().Be($"tp skills create {agentDirectory} --global");
    }

    [Fact]
    public void UpdateCommandFor_GlobalSkillInBareHomeSkillsFolder_DoesNotGuessAnAgentDirectory()
    {
        var path = Path.Combine(Home, "skills", "timepro-timesheets", "SKILL.md");

        InfoCommand.UpdateCommandFor(Skill(path, global: true), Home)
            .Should().Be("tp skills create <agent-dir> --global");
    }

    [Fact]
    public void UpdateCommandFor_TargetWithSpaces_IsQuotedSoTheHintIsRunnable()
    {
        var path = Path.Combine(Home, "my agents", "skills", "timepro-timesheets", "SKILL.md");

        InfoCommand.UpdateCommandFor(Skill(path, global: true), Home)
            .Should().Be("tp skills create 'my agents' --global");
    }

    [Fact]
    public void UpdateCommandFor_LocalSkill_UsesInstallDirectory()
    {
        var path = Path.Combine("/work", "repo", ".agents", "skills", "timepro-timesheets", "SKILL.md");

        InfoCommand.UpdateCommandFor(Skill(path, global: false), Home)
            .Should().Be($"tp skills create {Path.Combine("/work", "repo", ".agents")}");
    }

    [Fact]
    public void UpdateCommandFor_UnknownPath_FallsBackToDefaultTarget()
    {
        InfoCommand.UpdateCommandFor(Skill(null, global: false), Home)
            .Should().Be("tp skills create .agents");
    }

    [Fact]
    public void RejectHomeRoot_HomeDirectoryTarget_IsRejectedWithoutForce()
    {
        var rejection = CreateCommand.RejectHomeRoot(Home, Home, force: false);

        rejection.Should().NotBeNull();
        rejection.Should().Contain("--force");
    }

    [Fact]
    public void RejectHomeRoot_HomeDirectoryTarget_IsAllowedWithForce()
    {
        CreateCommand.RejectHomeRoot(Home, Home, force: true).Should().BeNull();
    }

    [Fact]
    public void RejectHomeRoot_AgentDirectoryTarget_IsAllowed()
    {
        CreateCommand.RejectHomeRoot(Path.Combine(Home, ".claude"), Home, force: false).Should().BeNull();
    }
}
