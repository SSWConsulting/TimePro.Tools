using FluentAssertions;
using SSW.TimePro.Cli.Features.Skills;
using SSW.TimePro.Cli.Infrastructure.Config;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Skills;

public class SkillGenerationTests
{
    private static GlobalConfig Global() => new() { DefaultLocation = "Office" };

    private static SkillContentModel Timesheets(
        RepoMappingEntry? repoMapping = null,
        string? ghRepoSlug = null) =>
        SkillModelBuilder.BuildTimesheets(
            tenant: null,
            Global(),
            repoMapping,
            ghRepoSlug);

    [Fact]
    public void OutputPath_UsesSubfolderSkillMd() =>
        SkillRenderer.RelativePath("timepro-timesheets")
            .Should().Be("skills/timepro-timesheets/SKILL.md");

    [Fact]
    public void Frontmatter_ContainsNameDescriptionAndAllowedTools()
    {
        var output = SkillRenderer.Render(Timesheets());

        output.Should().StartWith("---\n");
        output.Should().Contain("name: timepro-timesheets");
        output.Should().Contain("description:");
        output.Should().Contain("allowed-tools: Bash(tp *), Bash(sl *)");
    }

    [Fact]
    public void Prefetch_RendersAsPlainRunTheseFirstBlock()
    {
        var output = SkillRenderer.Render(Timesheets());

        output.Should().Contain("## Run these first");
        output.Should().Contain("Run these read-only commands before you start, and read their output:");
        output.Should().Contain("```bash");
        output.Should().Contain("tp info --json    # CLI health, active tenant, user, and update status");
        output.Should().Contain("tp project recent --json    # ranked likely projects + repo paths (start here)");
        output.Should().Contain("tp ts get --week --json    # current week's entries + suggestions");
        output.Should().Contain("tp bk list --week --json    # CRM bookings for the week");
        output.Should().Contain("tp loc info --json    # location defaults / WFH days");
        output.Should().NotContain("!`");
    }

    [Fact]
    public void Accounting_ProducesAccountingSkillWithoutPrefetchBlock()
    {
        var model = SkillModelBuilder.BuildAccounting(tenant: null);
        var output = SkillRenderer.Render(model);

        SkillRenderer.RelativePath(model.Name)
            .Should().Be("skills/timepro-accounting-cli/SKILL.md");
        output.Should().Contain("name: timepro-accounting-cli");
        output.Should().Contain("allowed-tools: Bash(tp *)");
        output.Should().Contain("# TimePro Accounting (CLI)");
        output.Should().Contain("Start with `tp info --json`; prefer it over `tp --version`");
        output.Should().Contain("tp client billable-work --from 2025-06-26 --to 2026-06-26 --threshold 50000 --json");
        output.Should().Contain("jq '.rows | map({clientId, clientName, firstInvoiceDate, billableTimesheetValueExGst})'");
        output.Should().Contain("billableTimesheetValueExGst");
        output.Should().Contain("--output ./Reports/client-billable-work.csv");
        output.Should().NotContain("## Run these first");
        output.Should().NotContain("!`");
    }

    [Fact]
    public void GeneratedExamplesUseNorthwindPlaceholdersOnly()
    {
        var repoMapping = new RepoMappingEntry
        {
            PathPattern = "~/Developer/git/Northwind/traders-app",
            RemotePattern = "github.com/Northwind/traders-app",
            ClientId = "NWIND",
            ProjectId = "1I776Q",
            ProjectName = "Northwind Traders",
            CategoryId = "WEBDEV",
        };

        var output = SkillRenderer.Render(Timesheets(repoMapping, "Northwind/traders-app"));

        output.Should().Contain("NWIND");
        output.Should().Contain("1I776Q");
        output.Should().Contain("Northwind Traders");
        output.Should().Contain("Northwind/traders-app");
        output.Should().NotContain("SSW.TimePRO");
        output.Should().NotContain("SSWConsulting");
        output.Should().NotContain("AskUserQuestion");
    }
}
