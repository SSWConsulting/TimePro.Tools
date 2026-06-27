using System.Reflection;

namespace SSW.TimePro.Cli.Features.Updates;

public sealed record ReleaseNote(
    SemanticVersion Version,
    string VersionText,
    string Markdown)
{
    public string Url => GitHubReleaseClient.ReleaseUrlFor(VersionText);
}

public sealed class ReleaseNotesCatalog
{
    private const string ResourcePrefix = "release-notes/";
    private readonly IReadOnlyList<ReleaseNote> _notes;

    public ReleaseNotesCatalog(IEnumerable<ReleaseNote> notes)
    {
        _notes = notes
            .OrderBy(note => note.Version)
            .ToList();
    }

    public static ReleaseNotesCatalog LoadEmbedded()
    {
        var assembly = typeof(ReleaseNotesCatalog).Assembly;
        var notes = new List<ReleaseNote>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !resourceName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var versionText = Path.GetFileNameWithoutExtension(resourceName);
            if (!SemanticVersion.TryParse(versionText, out var version))
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                continue;

            using var reader = new StreamReader(stream);
            notes.Add(new ReleaseNote(version, version.ToString(), reader.ReadToEnd().Trim()));
        }

        return new ReleaseNotesCatalog(notes);
    }

    public ReleaseNote? LatestKnown() => _notes.LastOrDefault();

    public IReadOnlyList<ReleaseNote> NotesSince(string? previousVersion, string? currentVersion)
    {
        var hasPrevious = SemanticVersion.TryParse(previousVersion, out var previous);
        var hasCurrent = SemanticVersion.TryParse(currentVersion, out var current);

        if (hasPrevious && hasCurrent)
            return _notes.Where(note => note.Version.CompareTo(previous) > 0 && note.Version.CompareTo(current) <= 0).ToList();

        if (hasPrevious)
            return _notes.Where(note => note.Version.CompareTo(previous) > 0).ToList();

        if (hasCurrent)
        {
            var currentNote = _notes.Where(note => note.Version.CompareTo(current) <= 0).LastOrDefault();
            return currentNote is null ? [] : [currentNote];
        }

        var latest = LatestKnown();
        return latest is null ? [] : [latest];
    }

    public string RenderWhatsNewMarkdown(
        string currentVersion,
        string? previousVersion,
        DateTimeOffset? installedAt)
    {
        var latestKnown = LatestKnown();
        var effectiveCurrentVersion = SemanticVersion.IsDevelopmentVersion(currentVersion)
            ? latestKnown?.VersionText ?? currentVersion
            : currentVersion;

        var notes = NotesSince(previousVersion, effectiveCurrentVersion);

        var lines = new List<string>
        {
            "# What's new in TimePro.Tools",
            string.Empty,
            $"- Current version: `{currentVersion}`",
            $"- Previous version: `{(string.IsNullOrWhiteSpace(previousVersion) ? "none recorded" : previousVersion)}`",
            $"- Installed at: `{(installedAt is null ? "not recorded" : installedAt.Value.ToString("u"))}`"
        };

        if (SemanticVersion.IsDevelopmentVersion(currentVersion) && latestKnown is not null)
            lines.Add($"- Development build: treating `{latestKnown.VersionText}` as the latest known release");

        lines.Add(string.Empty);

        if (notes.Count == 0)
        {
            lines.Add("_No local release notes were found for this version range._");
            return string.Join(Environment.NewLine, lines);
        }

        foreach (var note in notes.OrderByDescending(note => note.Version))
        {
            lines.Add(note.Markdown);
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }
}
