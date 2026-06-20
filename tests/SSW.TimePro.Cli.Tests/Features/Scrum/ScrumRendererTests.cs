using FluentAssertions;
using SSW.TimePro.Cli.Features.Scrum;
using SSW.TimePro.Cli.Infrastructure.Config;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Scrum;

/// <summary>
/// Golden-ish tests for the built-in <see cref="ScrumRenderer"/> (markdown + html),
/// locking the section structure, item formatting, empty-state text and HTML encoding.
/// </summary>
public class ScrumRendererTests
{
    private static readonly ScrumConfig Config = new() { FooterUrl = "https://example.test/footer" };

    private static ScrumItem Item(string reference, string title) =>
        new() { Kind = "PBI", Reference = reference, Title = title, Url = $"https://example.test/{reference.TrimStart('#')}" };

    private static ScrumModel Populated() => new()
    {
        TodayDate = new DateOnly(2026, 6, 18),
        YesterdayDate = new DateOnly(2026, 6, 17),
        IsInternal = true,
        Yesterday = [Item("#41", "Order history")],
        Today = [Item("#42", "Product search")],
        Blockers = [Item("#43", "Checkout API")],
        Internal = new InternalBlock { DaysUntilNextClientBooking = 3, JoinedScrumMeeting = true }
    };

    private static ScrumModel Empty() => new()
    {
        TodayDate = new DateOnly(2026, 6, 18),
        IsInternal = false
    };

    [Fact]
    public void RenderMarkdown_Populated_HasAllSectionsAndItems()
    {
        var md = new ScrumRenderer(Config).RenderMarkdown(Populated());

        md.Should().Contain("Yesterday I worked on:");
        md.Should().Contain("Today I'm working on:");
        md.Should().Contain("Blocked:");
        md.Should().Contain("#42").And.Contain("https://example.test/42");
        md.Should().Contain("days until my next client booking");
        md.Should().Contain("example.test/footer");
    }

    [Fact]
    public void RenderMarkdown_Empty_ShowsPlaceholders_AndNoBlockedSection()
    {
        var md = new ScrumRenderer(Config).RenderMarkdown(Empty());

        md.Should().Contain("(nothing recorded)");
        md.Should().Contain("(nothing planned)");
        md.Should().NotContain("Blocked:");
    }

    [Fact]
    public void RenderHtml_Populated_HasListItemsAndLinks()
    {
        var html = new ScrumRenderer(Config).RenderHtml(Populated());

        html.Should().StartWith("<div").And.EndWith("</div>");
        html.Should().Contain("<li>");
        html.Should().Contain("href=\"https://example.test/42\"");
        html.Should().Contain("Blocked:");
    }

    [Fact]
    public void RenderHtml_NoBlockers_OmitsBlockedSection()
    {
        var model = Populated();
        model.Blockers.Clear();

        var html = new ScrumRenderer(Config).RenderHtml(model);

        html.Should().NotContain("Blocked:");
    }

    [Fact]
    public void RenderHtml_EncodesItemTitle()
    {
        var model = Empty();
        model.Today.Add(Item("#99", "<script>alert(1)</script>"));

        var html = new ScrumRenderer(Config).RenderHtml(model);

        html.Should().NotContain("<script>alert(1)</script>");
        html.Should().Contain("&lt;script&gt;");
    }
}
