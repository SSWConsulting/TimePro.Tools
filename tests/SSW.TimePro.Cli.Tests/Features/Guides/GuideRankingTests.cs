using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Guides;
using Xunit;

using AccountingGuide = SSW.TimePro.Cli.Features.Accounting.AccountingGuide;
using DeveloperGuide = SSW.TimePro.Cli.Features.Developer.DeveloperGuide;

namespace SSW.TimePro.Cli.Tests.Features.Guides;

public class GuideRankingTests
{
    [Fact]
    public void DeveloperGuide_FiltersSuggestedUseCaseAsExactKeywordMatch()
    {
        var guide = DeveloperGuide.For("suggested");

        guide.MatchingGuides.Should().NotBeEmpty();
        guide.MatchingGuides[0].Title.Should().Be("Suggested timesheets missing");
        guide.MatchingGuides[0].MatchType.Should().Be("exact");
        guide.RecommendedCommands.Should().Contain(command => command.Contains("tp ts suggest", StringComparison.Ordinal));
        guide.RecommendedSkills.Should().Contain("timepro-dev-timesheet-diagnostics");
        guide.RecommendedSkills.Should().NotContain("timepro-dev-finance-diagnostics");
    }

    [Fact]
    public void DeveloperGuide_RanksContainsAllAboveContainsOne()
    {
        var guide = DeveloperGuide.For("refresh employee");

        guide.MatchingGuides.Should().NotBeEmpty();
        guide.MatchingGuides[0].Title.Should().Be("Suggested timesheets missing");
        guide.MatchingGuides[0].MatchType.Should().Be("contains-all");
    }

    [Fact]
    public void DeveloperGuide_ReturnsAtLeastOneWordMatches()
    {
        var guide = DeveloperGuide.For("suggested invoice");

        guide.MatchingGuides.Should().Contain(match => match.Title == "Suggested timesheets missing");
        guide.MatchingGuides.Should().Contain(match => match.Title == "Invoice tax bug");
        guide.MatchingGuides.Should().OnlyContain(match => match.MatchRank == 1);
    }

    [Theory]
    [InlineData("empid", "Employee scoped diagnostics")]
    [InlineData("timezone", "Leave profile field issue")]
    [InlineData("remaining credit", "Prepaid invoice state bug")]
    [InlineData("appinsights", "AppInsights exception correlation")]
    public void DeveloperGuide_FindsRecentDiagnosticPatterns(string useCase, string expectedTitle)
    {
        var guide = DeveloperGuide.For(useCase);

        guide.MatchingGuides.Should().ContainSingle(match => match.Title == expectedTitle);
        guide.MatchingGuides.Should().OnlyContain(match => match.MatchRank == 3);
        guide.RecommendedCommands.Should().NotBeEmpty();
        guide.RecommendedCommands.Should().OnlyContain(command =>
            command.StartsWith("tp ", StringComparison.Ordinal)
            || command.StartsWith("az monitor app-insights query", StringComparison.Ordinal));
    }

    [Fact]
    public void AccountingGuide_FiltersInvoiceReconciliationAsExactKeywordMatch()
    {
        var guide = AccountingGuide.For("invoice reconciliation");

        guide.MatchingGuides.Should().NotBeEmpty();
        guide.MatchingGuides[0].Title.Should().Be("Invoice evidence pack");
        guide.MatchingGuides[0].MatchType.Should().Be("exact");
        guide.RecommendedSkills.Should().Equal("timepro-accounting-cli");
    }

    [Fact]
    public void AccountingGuide_FiltersClientDebtAsContainsAll()
    {
        var guide = AccountingGuide.For("aged debtor client");

        guide.MatchingGuides.Should().NotBeEmpty();
        guide.MatchingGuides[0].Title.Should().Be("Client accounting position");
        guide.MatchingGuides[0].MatchType.Should().Be("contains-all");
    }

    [Theory]
    [InlineData("50k revenue", "Clients with 50k invoiced revenue")]
    [InlineData("monthly receipts", "Monthly sales and receipts")]
    public void AccountingGuide_FindsCommonReportGuides(string useCase, string expectedTitle)
    {
        var guide = AccountingGuide.For(useCase);

        guide.MatchingGuides.Should().ContainSingle(match => match.Title == expectedTitle);
        guide.MatchingGuides.Should().OnlyContain(match => match.MatchRank == 3);
        guide.RecommendedCommands.Should().NotBeEmpty();
        guide.RecommendedSkills.Should().Equal("timepro-accounting-cli");
    }

    [Theory]
    [InlineData("locked invoice", "Locked invoice or timesheet")]
    [InlineData("reconciliation delta", "Prepaid reconciliation delta")]
    [InlineData("credit note prepaid", "Credit note not reducing prepaid balance")]
    [InlineData("receipt allocation", "Receipt allocated to the wrong invoice")]
    [InlineData("recurring invoice", "Recurring invoice not generating")]
    public void AccountingGuide_FindsNewDiagnosticGuides(string useCase, string expectedTitle)
    {
        var guide = AccountingGuide.For(useCase);

        guide.MatchingGuides.Should().ContainSingle(match => match.Title == expectedTitle);
        guide.MatchingGuides[0].MatchType.Should().Be("exact");
        guide.RecommendedSkills.Should().Equal("timepro-accounting-cli");
    }

    [Fact]
    public void GuideRanking_ReturnsAllTopicsWithoutQuery()
    {
        var topics = new[]
        {
            new GuideDocument("dev", "one", "One", "First", ["first"], ["cmd one"], [], ["skill-one"], "test", "body"),
            new GuideDocument("dev", "two", "Two", "Second", ["second"], ["cmd two"], [], ["skill-two"], "test", "body")
        };

        var ranked = GuideRanking.Rank(null, topics);

        ranked.Select(match => match.Title).Should().Equal("One", "Two");
        ranked.Should().OnlyContain(match => match.MatchType == "default" && match.MatchRank == 0);
    }

    [Fact]
    public void GuideCatalog_LoadsEmbeddedIndex()
    {
        var guides = GuideCatalog.Load("dev");

        guides.Should().Contain(guide => guide.Slug == "suggested-timesheets-missing"
            && guide.Title == "Suggested timesheets missing"
            && guide.Commands.Any(command => command.Contains("tp ts suggest", StringComparison.Ordinal))
            && guide.Skills.Contains("timepro-dev-timesheet-diagnostics"));
    }

    [Fact]
    public void DeveloperGuides_DoNotContainRecentPrivateIncidentBreadcrumbs()
    {
        var forbidden = new[]
        {
            "Azure DevOps",
            "SSW.TimeProDotNet",
            "ssw2",
            "60456",
            "60470",
            "Jeoffrey",
            "Jernej",
            "Penny"
        };

        var guides = GuideCatalog.Load("dev");
        var searchableText = string.Join(
            "\n",
            guides.Select(guide => string.Join(
                "\n",
                [
                    guide.Title,
                    guide.Description,
                    guide.Body,
                    .. guide.Keywords,
                    .. guide.Commands
                ])));

        foreach (var term in forbidden)
            searchableText.Should().NotContain(term, because: "developer guides must stay public and sanitized");
    }

    [Fact]
    public void AccountingGuides_DoNotContainRecentPrivateIncidentBreadcrumbs()
    {
        var forbidden = new[]
        {
            "Azure DevOps",
            "SSW.TimeProDotNet",
            "ssw2",
            "60456",
            "60470",
            "Jeoffrey",
            "Jernej",
            "Penny"
        };

        var guides = GuideCatalog.Load("accounting");
        var searchableText = string.Join(
            "\n",
            guides.Select(guide => string.Join(
                "\n",
                [
                    guide.Title,
                    guide.Description,
                    guide.Body,
                    .. guide.Keywords,
                    .. guide.Commands,
                    .. guide.McpTools
                ])));

        foreach (var term in forbidden)
            searchableText.Should().NotContain(term, because: "accounting guides must stay public and sanitized");
    }
}
