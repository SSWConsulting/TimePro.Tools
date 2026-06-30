using FluentAssertions;
using SSW.TimePro.Cli.Features.Updates;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Updates;

public class ReleaseNotesCatalogTests
{
    [Fact]
    public void NotesSince_ReturnsNotesAfterPreviousThroughCurrent()
    {
        var catalog = Catalog();

        var notes = catalog.NotesSince("0.2.1", "0.2.3");

        notes.Select(note => note.VersionText).Should().Equal("0.2.2", "0.2.3");
    }

    [Fact]
    public void NotesSince_WithoutPrevious_ReturnsCurrentNote()
    {
        var catalog = Catalog();

        var notes = catalog.NotesSince(null, "0.2.2");

        notes.Should().ContainSingle();
        notes[0].VersionText.Should().Be("0.2.2");
    }

    [Fact]
    public void RenderWhatsNewMarkdown_IncludesVersionState()
    {
        var catalog = Catalog();

        var markdown = catalog.RenderWhatsNewMarkdown(
            currentVersion: "0.2.3",
            previousVersion: "0.2.1",
            installedAt: DateTimeOffset.Parse("2026-06-27T10:30:00Z"));

        markdown.Should().Contain("# What's new in TimePro.Tools");
        markdown.Should().Contain("Current version: `0.2.3`");
        markdown.Should().Contain("Previous version: `0.2.1`");
        markdown.Should().Contain("# 0.2.3");
        markdown.Should().Contain("# 0.2.2");
        markdown.Should().NotContain("# 0.2.1");
    }

    [Fact]
    public void LoadEmbedded_FindsBackfilledReleaseNotes()
    {
        var catalog = ReleaseNotesCatalog.LoadEmbedded();

        catalog.LatestKnown()!.VersionText.Should().Be("0.2.4");
    }

    private static ReleaseNotesCatalog Catalog() =>
        new(
        [
            new ReleaseNote(new SemanticVersion(0, 2, 1), "0.2.1", "# 0.2.1"),
            new ReleaseNote(new SemanticVersion(0, 2, 2), "0.2.2", "# 0.2.2"),
            new ReleaseNote(new SemanticVersion(0, 2, 3), "0.2.3", "# 0.2.3"),
        ]);
}
