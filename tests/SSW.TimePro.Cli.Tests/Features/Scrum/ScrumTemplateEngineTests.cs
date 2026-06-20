using FluentAssertions;
using SSW.TimePro.Cli.Features.Scrum;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Scrum;

public class ScrumTemplateEngineTests
{
    [Fact]
    public void Render_ReplacesVariables()
    {
        var output = ScrumTemplateEngine.Render(
            "Hello {{client}} on {{date}}",
            new Dictionary<string, object?> { ["client"] = "Northwind Traders", ["date"] = "2026-03-31" });

        output.Should().Be("Hello Northwind Traders on 2026-03-31");
    }

    [Fact]
    public void Render_UnknownVariable_IsEmpty()
    {
        var output = ScrumTemplateEngine.Render(
            "Hello {{missing}}",
            new Dictionary<string, object?>());

        output.Should().Be("Hello ");
    }

    [Fact]
    public void Render_TruthySection_IncludesInnerBlock()
    {
        var output = ScrumTemplateEngine.Render(
            "{{#today}}Today: {{today}}{{/today}}",
            new Dictionary<string, object?> { ["today"] = "- Product search" });

        output.Should().Be("Today: - Product search");
    }

    [Fact]
    public void Render_FalsySection_ExcludesInnerBlock()
    {
        var output = ScrumTemplateEngine.Render(
            "{{#today}}Today: {{today}}{{/today}}",
            new Dictionary<string, object?> { ["today"] = string.Empty });

        output.Should().BeEmpty();
    }

    [Fact]
    public void Render_InvertedSection_IncludesInnerBlockWhenFalsy()
    {
        var output = ScrumTemplateEngine.Render(
            "{{^blockers}}No blockers{{/blockers}}",
            new Dictionary<string, object?> { ["blockers"] = Array.Empty<string>() });

        output.Should().Be("No blockers");
    }

    [Fact]
    public void Render_DifferentlyNamedNestedSections_RendersInnerBlock()
    {
        var output = ScrumTemplateEngine.Render(
            "{{#outer}}A{{#inner}}{{value}}{{/inner}}B{{/outer}}",
            new Dictionary<string, object?>
            {
                ["outer"] = true,
                ["inner"] = true,
                ["value"] = "Northwind"
            });

        output.Should().Be("ANorthwindB");
    }

    [Theory]
    [MemberData(nameof(TruthyValues))]
    public void Render_TruthyValues_IncludeSection(object value)
    {
        var output = ScrumTemplateEngine.Render(
            "{{#value}}yes{{/value}}",
            new Dictionary<string, object?> { ["value"] = value });

        output.Should().Be("yes");
    }

    [Theory]
    [MemberData(nameof(FalsyValues))]
    public void Render_FalsyValues_IncludeInvertedSection(object value)
    {
        var output = ScrumTemplateEngine.Render(
            "{{^value}}no{{/value}}",
            new Dictionary<string, object?> { ["value"] = value });

        output.Should().Be("no");
    }

    [Fact]
    public void Render_NullValue_IsFalsy()
    {
        var output = ScrumTemplateEngine.Render(
            "{{^value}}no{{/value}}",
            new Dictionary<string, object?> { ["value"] = null });

        output.Should().Be("no");
    }

    public static TheoryData<object> TruthyValues() => new()
    {
        "Product search",
        new[] { "Checkout API" },
        true,
        0,
        42
    };

    public static TheoryData<object> FalsyValues() => new()
    {
        string.Empty,
        Array.Empty<string>(),
        false
    };
}
