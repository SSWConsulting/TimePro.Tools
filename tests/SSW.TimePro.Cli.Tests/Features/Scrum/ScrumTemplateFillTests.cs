using FluentAssertions;
using SSW.TimePro.Cli.Features.Scrum;
using SSW.TimePro.Cli.Infrastructure.Config;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Scrum;

/// <summary>
/// Tests the model → variable binding when a template is *filled* (as opposed to
/// <see cref="ScrumTemplateRenderer.ResolveRawTemplate"/>, which only locates the
/// raw text). Covers built-in default substitution and a custom localized template
/// exercising sections / inverted sections / item substitution.
/// </summary>
public class ScrumTemplateFillTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScrumTemplateRenderer _renderer;
    private static readonly ScrumConfig Config = new() { FooterUrl = "https://example.test/footer" };

    public ScrumTemplateFillTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"timepro-scrum-fill-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _renderer = new ScrumTemplateRenderer(_tempDir);
    }

    private static ScrumItem Item(string reference, string title) =>
        new() { Kind = "PBI", Reference = reference, Title = title, Url = $"https://example.test/{reference.TrimStart('#')}" };

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(_tempDir, name), content);

    [Fact]
    public void Render_BuiltInDefault_SubstitutesItems()
    {
        var model = new ScrumModel { TodayDate = new DateOnly(2026, 6, 18), Today = [Item("#42", "Product search")] };

        var md = _renderer.Render(ScrumTemplateFormat.Markdown, tenantId: null, clientId: null, model, Config);

        md.Should().Contain("#42");
        md.Should().NotContain("{{"); // no unresolved placeholders
    }

    [Fact]
    public void Render_CustomTemplate_HonorsSectionsAndSubstitutesLists()
    {
        // A localized template: heading literal owned by the template, list injected,
        // blockers heading only when present, inverted section for the empty case.
        Write("daily-scrum.md",
            "Bonjour,\n{{#today}}{{today}}{{/today}}{{^today}}- (rien){{/today}}\n{{#blockers}}Bloqué :\n{{blockers}}{{/blockers}}");

        var model = new ScrumModel
        {
            TodayDate = new DateOnly(2026, 6, 18),
            Today = [Item("#42", "Product search")]
            // no blockers
        };

        var md = _renderer.Render(ScrumTemplateFormat.Markdown, tenantId: null, clientId: null, model, Config);

        md.Should().StartWith("Bonjour,");
        md.Should().Contain("#42");
        md.Should().NotContain("(rien)", "the today section is non-empty so the inverted block is skipped");
        md.Should().NotContain("Bloqué :", "there are no blockers so that section is excluded");
    }

    [Fact]
    public void Render_CustomTemplate_EmptyToday_UsesInvertedSection()
    {
        Write("daily-scrum.md", "{{#today}}{{today}}{{/today}}{{^today}}- (rien){{/today}}");

        var model = new ScrumModel { TodayDate = new DateOnly(2026, 6, 18) };

        var md = _renderer.Render(ScrumTemplateFormat.Markdown, null, null, model, Config);

        md.Trim().Should().Be("- (rien)");
    }

    [Fact]
    public void Render_InternalData_ExposedToTemplate()
    {
        Write("daily-scrum.md",
            "{{#internal}}J:{{daysUntilNextClientBooking}} {{joinedScrumMeeting}}{{/internal}}{{^internal}}NA{{/internal}}");

        var model = new ScrumModel
        {
            TodayDate = new DateOnly(2026, 6, 18),
            IsInternal = true,
            Internal = new InternalBlock { DaysUntilNextClientBooking = 4, JoinedScrumMeeting = true }
        };

        var md = _renderer.Render(ScrumTemplateFormat.Markdown, null, null, model, Config);

        md.Should().Be("J:4 ✅");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
