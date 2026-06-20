using FluentAssertions;
using SSW.TimePro.Cli.Features.Scrum;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Scrum;

public class ScrumTemplateRendererTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScrumTemplateRenderer _renderer;

    public ScrumTemplateRendererTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"timepro-scrum-template-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _renderer = new ScrumTemplateRenderer(_tempDir);
    }

    [Fact]
    public void ResolveRawTemplate_UsesTenantAndClientBeforeTenant()
    {
        Write("daily-scrum.tenant.ssw.client.NWIND.md", "tenant-client");
        Write("daily-scrum.tenant.ssw.md", "tenant");
        Write("daily-scrum.NWIND.md", "client");
        Write("daily-scrum.md", "global");

        var template = _renderer.ResolveRawTemplate(ScrumTemplateFormat.Markdown, "ssw", "NWIND");

        template.Should().Be("tenant-client");
    }

    [Fact]
    public void ResolveRawTemplate_UsesTenantBeforeClient()
    {
        Write("daily-scrum.tenant.ssw.md", "tenant");
        Write("daily-scrum.NWIND.md", "client");
        Write("daily-scrum.md", "global");

        var template = _renderer.ResolveRawTemplate(ScrumTemplateFormat.Markdown, "ssw", "NWIND");

        template.Should().Be("tenant");
    }

    [Fact]
    public void ResolveRawTemplate_UsesClientBeforeGlobal()
    {
        Write("daily-scrum.NWIND.md", "client");
        Write("daily-scrum.md", "global");

        var template = _renderer.ResolveRawTemplate(ScrumTemplateFormat.Markdown, "ssw", "NWIND");

        template.Should().Be("client");
    }

    [Fact]
    public void ResolveRawTemplate_UsesGlobalBeforeBuiltIn()
    {
        Write("daily-scrum.md", "global");

        var template = _renderer.ResolveRawTemplate(ScrumTemplateFormat.Markdown, "ssw", "NWIND");

        template.Should().Be("global");
    }

    [Fact]
    public void ResolveRawTemplate_FallsBackToBuiltIn()
    {
        var template = _renderer.ResolveRawTemplate(ScrumTemplateFormat.Markdown, "ssw", "NWIND");

        template.Should().Be(ScrumTemplateRenderer.DefaultMarkdownTemplate);
    }

    [Fact]
    public void ResolveRawTemplate_SkipsClientScopedFilesWhenClientIsMissing()
    {
        Write("daily-scrum.NWIND.md", "client");
        Write("daily-scrum.md", "global");

        var template = _renderer.ResolveRawTemplate(ScrumTemplateFormat.Markdown, "ssw", null);

        template.Should().Be("global");
    }

    [Fact]
    public void ResolveRawTemplate_UsesHtmlExtensionForHtml()
    {
        Write("daily-scrum.tenant.ssweu.client.NWIND.html", "html tenant-client");
        Write("daily-scrum.tenant.ssweu.client.NWIND.md", "markdown tenant-client");

        var template = _renderer.ResolveRawTemplate(ScrumTemplateFormat.Html, "ssweu", "NWIND");

        template.Should().Be("html tenant-client");
    }

    private void Write(string fileName, string content) =>
        File.WriteAllText(Path.Combine(_tempDir, fileName), content);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
