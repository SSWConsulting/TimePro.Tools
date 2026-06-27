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
        output.Should().Contain("timepro-clients-50k-revenue.csv");
        output.Should().Contain("invoicedExGstInWindow >= 50000");
        output.Should().Contain("timepro-tax-mismatch.csv");
        output.Should().Contain("tp accounting tax-mismatches --query NWIND --limit 500 --json");
        output.Should().Contain("Tax rates may arrive as `0.1` or `10` for 10%");
        output.Should().Contain("Excel, CSV, Xero MCP, bank-feed MCP, or another system");
        output.Should().Contain("tp accounting invoice-diagnostics <invoiceId> --json");
        output.Should().Contain("tp accounting client-diagnostics <clientId> --rates --json");
        output.Should().Contain("MCP exposes the same report shapes");
        output.Should().Contain("With another MCP such as Xero");
        output.Should().Contain("tp feature accounting enable");
        output.Should().NotContain("## Run these first");
        output.Should().NotContain("!`");
    }

    [Fact]
    public void TenantSetup_ProducesDefaultTenantMovementSkill()
    {
        var model = SkillModelBuilder.BuildTenantSetup();
        var output = SkillRenderer.Render(model);

        SkillRenderer.RelativePath(model.Name)
            .Should().Be("skills/timepro-tenant-setup/SKILL.md");
        output.Should().Contain("name: timepro-tenant-setup");
        output.Should().Contain("allowed-tools: Bash(tp *)");
        output.Should().Contain("tp tenant list    # configured tenant profiles; no secret values");
        output.Should().Contain("tp tenant set ssw-staging");
        output.Should().Contain("tp login --tenant ssw-staging --api-url https://api.staging-sswtimepro.com");
        output.Should().Contain("tp tenant info --tenant northwind --env staging --json");
        output.Should().Contain("tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json");
        output.Should().Contain("`--tenant northwind --env staging` -> `northwind-staging`");
        output.Should().Contain("Do not use `direnv exec .` just to access tenant config");
        output.Should().NotContain("direnv exec . tp");
    }

    [Fact]
    public void DeveloperDiagnostics_ProducesDevOnlyDiagnosticSkill()
    {
        var model = SkillModelBuilder.BuildDeveloperDiagnostics();
        var output = SkillRenderer.Render(model);

        SkillRenderer.RelativePath(model.Name)
            .Should().Be("skills/timepro-dev-diagnostics/SKILL.md");
        output.Should().Contain("name: timepro-dev-diagnostics");
        output.Should().Contain("allowed-tools: Bash(tp *), Bash(az monitor app-insights query *), Bash(jq *)");
        output.Should().Contain("Local and staging can be more experimental");
        output.Should().Contain("Production defaults to read-only");
        output.Should().Contain("Ask the user before any non-read-only production action");
        output.Should().Contain("tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json");
        output.Should().Contain("tp ts suggest 2026-03-12 --tenant northwind --env staging --json");
        output.Should().Contain("tp bk list --date 2026-03-12 --tenant northwind --env staging --json");
        output.Should().Contain("az monitor app-insights query");
        output.Should().Contain("do not accept `--emp-id`");
        output.Should().Contain("timepro-dev-timesheet-diagnostics");
        output.Should().Contain("timepro-dev-finance-diagnostics");
    }

    [Fact]
    public void DeveloperTimesheetDiagnostics_ProducesDevOnlyTimesheetDiagnosticSkill()
    {
        var model = SkillModelBuilder.BuildDeveloperTimesheetDiagnostics();
        var output = SkillRenderer.Render(model);

        SkillRenderer.RelativePath(model.Name)
            .Should().Be("skills/timepro-dev-timesheet-diagnostics/SKILL.md");
        output.Should().Contain("name: timepro-dev-timesheet-diagnostics");
        output.Should().Contain("allowed-tools: Bash(tp *), Bash(az monitor app-insights query *), Bash(jq *)");
        output.Should().Contain("Suggested timesheets missing");
        output.Should().Contain("CRM bookings missing");
        output.Should().Contain("tp bk list --date 2026-03-12 --tenant northwind --env staging --json");
        output.Should().Contain("tp ts suggest 2026-03-12 --tenant northwind --env staging --json");
        output.Should().Contain("tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json");
        output.Should().Contain("Wrong tenant profile employee");
        output.Should().Contain("CRM employee mismatch");
        output.Should().Contain("Refresh succeeded but no suggested rows persisted");
    }

    [Fact]
    public void DeveloperFinanceDiagnostics_ProducesDevOnlyFinanceDiagnosticSkill()
    {
        var model = SkillModelBuilder.BuildDeveloperFinanceDiagnostics();
        var output = SkillRenderer.Render(model);

        SkillRenderer.RelativePath(model.Name)
            .Should().Be("skills/timepro-dev-finance-diagnostics/SKILL.md");
        output.Should().Contain("name: timepro-dev-finance-diagnostics");
        output.Should().Contain("allowed-tools: Bash(tp *), Bash(az monitor app-insights query *), Bash(jq *)");
        output.Should().Contain("invoices, credit notes, receipts, client rates, prepaid drawdown, tax");
        output.Should().Contain("tp invoice timesheets 142 --tenant northwind --env staging --json");
        output.Should().Contain("tp creditnote list --client NWIND --tenant northwind --env staging --json");
        output.Should().Contain("tp rate list --client NWIND --tenant northwind --env staging --emp-id ALEX --show-expired --json");
        output.Should().Contain("tp accounting tax-mismatches --query NWIND --tenant northwind --env staging --json");
        output.Should().Contain("External sync status/reference");
        output.Should().Contain("Xero MCP");
    }

    [Fact]
    public void EnvironmentCompare_ProducesDevOnlyComparisonSkill()
    {
        var model = SkillModelBuilder.BuildEnvironmentCompare();
        var output = SkillRenderer.Render(model);

        SkillRenderer.RelativePath(model.Name)
            .Should().Be("skills/timepro-env-compare/SKILL.md");
        output.Should().Contain("name: timepro-env-compare");
        output.Should().Contain("allowed-tools: Bash(tp *), Bash(jq *), Bash(diff *)");
        output.Should().Contain("Production defaults to read-only");
        output.Should().Contain("Ask the user before any non-read-only production action");
        output.Should().Contain("tp tenant info --tenant northwind --env prod --json > /tmp/tp-prod-tenant.json");
        output.Should().Contain("tp tenant info --tenant northwind --env staging --json > /tmp/tp-staging-tenant.json");
        output.Should().Contain("diff -u /tmp/tp-prod-tenant.sorted.json /tmp/tp-staging-tenant.sorted.json");
        output.Should().Contain("tp user get ALEX --tenant northwind --env prod --json");
        output.Should().Contain("# tp ts suggest 2026-03-12 --tenant northwind --env prod --json");
    }

    [Fact]
    public void Templates_RenderWithoutUnresolvedPlaceholders()
    {
        var outputs = new[]
        {
            SkillRenderer.Render(Timesheets()),
            SkillRenderer.Render(SkillModelBuilder.BuildTenantSetup()),
            SkillRenderer.Render(SkillModelBuilder.BuildAccounting(tenant: null)),
            SkillRenderer.Render(SkillModelBuilder.BuildDeveloperDiagnostics()),
            SkillRenderer.Render(SkillModelBuilder.BuildDeveloperTimesheetDiagnostics()),
            SkillRenderer.Render(SkillModelBuilder.BuildDeveloperFinanceDiagnostics()),
            SkillRenderer.Render(SkillModelBuilder.BuildEnvironmentCompare()),
        };

        outputs.Should().OnlyContain(output => !output.Contains("{{", StringComparison.Ordinal));
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
        output.Should().NotContain("/Users/jk/Developer/clients");
        output.Should().NotContain("SSWConsulting");
        output.Should().NotContain("AskUserQuestion");
    }
}
