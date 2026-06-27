using System.Reflection;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Infrastructure.Guides;

public sealed record GuideDocument(
    string Domain,
    string Slug,
    string Title,
    string Description,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> McpTools,
    IReadOnlyList<string> Skills,
    string Source,
    string Body);

public static class GuideCatalog
{
    private static readonly HttpClient DefaultHttpClient = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static IReadOnlyList<GuideDocument> Load(string domain)
    {
        var guides = LoadEmbedded(domain)
            .Concat(LoadLocal(domain))
            .GroupBy(guide => guide.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(guide => guide.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return guides;
    }

    public static async Task<IReadOnlyList<GuideDocument>> LoadFromGitHubCacheAsync(
        string domain,
        GuideCatalogOptions options,
        CancellationToken cancellationToken)
    {
        options = options.Normalize();
        await RefreshCacheIfNeededAsync(domain, options, cancellationToken);

        var cachedGuides = LoadDirectory(domain, CacheDirectory(options.ConfigRoot, domain));
        var sourceGuides = cachedGuides.Count > 0
            ? cachedGuides
            : LoadEmbedded(domain).ToList();

        return sourceGuides
            .Concat(LoadDirectory(domain, UserGuideDirectory(options.ConfigRoot, domain)))
            .GroupBy(guide => guide.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(guide => guide.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static Task<IReadOnlyList<GuideDocument>> LoadFromGitHubCacheAsync(
        string domain,
        string configRoot,
        TimeSpan cacheTtl,
        bool forceRefresh,
        CancellationToken cancellationToken) =>
        LoadFromGitHubCacheAsync(
            domain,
            new GuideCatalogOptions(
                configRoot,
                cacheTtl,
                forceRefresh,
                GuideConfig.DefaultRepositoryUrl,
                GuideConfig.DefaultBranch,
                null),
            cancellationToken);

    private static IEnumerable<GuideDocument> LoadEmbedded(string domain)
    {
        const string indexName = "index.json";
        var indexResource = $"guides/{domain}/{indexName}";
        var indexJson = ReadEmbeddedText(indexResource);
        if (indexJson is null)
            yield break;

        foreach (var entry in ParseIndex(indexJson))
            yield return BuildEmbeddedGuide(domain, indexResource, entry);
    }

    private static IEnumerable<GuideDocument> LoadLocal(string domain)
    {
        var directories = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "guides", domain),
            Path.Combine(ConfigPaths.Root, "guides", domain)
        };

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
                continue;

            var indexFile = Path.Combine(directory, "index.json");
            if (!File.Exists(indexFile))
                continue;

            var indexJson = File.ReadAllText(indexFile);
            foreach (var entry in ParseIndex(indexJson))
            {
                yield return BuildLocalGuide(domain, directory, indexFile, entry);
            }
        }
    }

    private static IReadOnlyList<GuideDocument> LoadDirectory(string domain, string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        var indexFile = Path.Combine(directory, "index.json");
        if (!File.Exists(indexFile))
            return [];

        var indexJson = File.ReadAllText(indexFile);
        return ParseIndex(indexJson)
            .Select(entry => BuildLocalGuide(domain, directory, indexFile, entry))
            .ToList();
    }

    private static async Task RefreshCacheIfNeededAsync(
        string domain,
        GuideCatalogOptions options,
        CancellationToken cancellationToken)
    {
        var cacheDirectory = CacheDirectory(options.ConfigRoot, domain);
        if (!options.ForceRefresh && !IsCacheStale(cacheDirectory, options))
            return;

        try
        {
            await RefreshCacheAsync(domain, options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && Directory.Exists(cacheDirectory))
        {
            // Keep using the last successful cache when GitHub is temporarily unavailable.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No cache exists yet; the caller will fall back to embedded guides.
        }
    }

    private static bool IsCacheStale(string cacheDirectory, GuideCatalogOptions options)
    {
        if (options.CacheTtl <= TimeSpan.Zero)
            return true;

        var metadataFile = MetadataFile(cacheDirectory);
        if (!File.Exists(metadataFile))
            return true;

        try
        {
            var metadata = JsonSerializer.Deserialize<GuideCacheMetadata>(File.ReadAllText(metadataFile), JsonOptions);
            return metadata is null
                || !string.Equals(metadata.RepositoryUrl, options.RepositoryUrl, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(metadata.Branch, options.Branch, StringComparison.Ordinal)
                || DateTimeOffset.UtcNow - metadata.RefreshedAt >= options.CacheTtl;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static async Task RefreshCacheAsync(
        string domain,
        GuideCatalogOptions options,
        CancellationToken cancellationToken)
    {
        var cacheDirectory = CacheDirectory(options.ConfigRoot, domain);
        var parentDirectory = Directory.GetParent(cacheDirectory)?.FullName
            ?? throw new InvalidOperationException($"Could not resolve parent directory for guide cache '{cacheDirectory}'.");
        Directory.CreateDirectory(parentDirectory);

        var tempDirectory = Path.Combine(parentDirectory, $".{domain}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var http = options.HttpClient ?? DefaultHttpClient;
            var indexJson = await DownloadGuideTextAsync(http, options, domain, "index.json", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(tempDirectory, "index.json"), indexJson, cancellationToken);

            foreach (var entry in ParseIndex(indexJson))
            {
                var slug = ResolveSlug(entry);
                var file = ResolveFile(entry, slug);
                var body = await DownloadGuideTextAsync(http, options, domain, file, cancellationToken);
                var guideFile = Path.Combine(tempDirectory, file);
                Directory.CreateDirectory(Path.GetDirectoryName(guideFile)!);
                await File.WriteAllTextAsync(guideFile, body, cancellationToken);
            }

            var metadata = JsonSerializer.Serialize(
                new GuideCacheMetadata(DateTimeOffset.UtcNow, options.RepositoryUrl, options.Branch),
                JsonOptions);
            await File.WriteAllTextAsync(MetadataFile(tempDirectory), metadata, cancellationToken);

            if (Directory.Exists(cacheDirectory))
                Directory.Delete(cacheDirectory, recursive: true);
            Directory.Move(tempDirectory, cacheDirectory);
        }
        catch
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
            throw;
        }
    }

    private static async Task<string> DownloadGuideTextAsync(
        HttpClient http,
        GuideCatalogOptions options,
        string domain,
        string file,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, RawGuideUrl(options, domain, file));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("timepro-cli", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static GuideDocument BuildEmbeddedGuide(string domain, string indexResource, GuideIndexEntry entry)
    {
        var slug = ResolveSlug(entry);
        var file = ResolveFile(entry, slug);
        var guideResource = $"guides/{domain}/{file}";
        var body = ReadEmbeddedText(guideResource)
            ?? throw new InvalidOperationException($"Guide '{slug}' in '{indexResource}' points to missing embedded file '{guideResource}'.");

        return new GuideDocument(
            domain,
            slug,
            ResolveTitle(entry, slug),
            entry.Description ?? string.Empty,
            entry.Keywords,
            entry.Commands,
            entry.McpTools,
            entry.Skills,
            $"embedded:{guideResource}",
            body.Trim());
    }

    private static GuideDocument BuildLocalGuide(string domain, string directory, string indexFile, GuideIndexEntry entry)
    {
        var slug = ResolveSlug(entry);
        var file = ResolveFile(entry, slug);
        var guideFile = Path.Combine(directory, file);
        if (!File.Exists(guideFile))
            throw new InvalidOperationException($"Guide '{slug}' in '{indexFile}' points to missing file '{guideFile}'.");

        var body = File.ReadAllText(guideFile);

        return new GuideDocument(
            domain,
            slug,
            ResolveTitle(entry, slug),
            entry.Description ?? string.Empty,
            entry.Keywords,
            entry.Commands,
            entry.McpTools,
            entry.Skills,
            guideFile,
            body.Trim());
    }

    private static IReadOnlyList<GuideIndexEntry> ParseIndex(string indexJson)
    {
        using var doc = JsonDocument.Parse(indexJson, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var json = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.GetRawText()
            : doc.RootElement.TryGetProperty("guides", out var guides)
                ? guides.GetRawText()
                : "[]";

        return JsonSerializer.Deserialize<List<GuideIndexEntry>>(json, JsonOptions) ?? [];
    }

    private static string ResolveSlug(GuideIndexEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Slug)
            ? entry.Slug
            : Path.GetFileNameWithoutExtension(entry.File ?? "guide");

    private static string ResolveFile(GuideIndexEntry entry, string slug) =>
        string.IsNullOrWhiteSpace(entry.File)
            ? $"{slug}.md"
            : entry.File;

    private static string ResolveTitle(GuideIndexEntry entry, string slug) =>
        string.IsNullOrWhiteSpace(entry.Title)
            ? GuideText.Titleize(slug)
            : entry.Title;

    private static string? ReadEmbeddedText(string logicalName)
    {
        var assembly = typeof(GuideCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.Equals(logicalName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
            return null;

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Guide resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static string RawGuideUrl(GuideCatalogOptions options, string domain, string file)
    {
        var repositoryPath = NormalizeGitHubRepositoryPath(options.RepositoryUrl);
        var branch = EscapePath(options.Branch);
        var escapedFile = EscapePath(file);

        return $"https://raw.githubusercontent.com/{repositoryPath}/{branch}/guides/{domain}/{escapedFile}";
    }

    private static string NormalizeGitHubRepositoryPath(string repositoryUrl)
    {
        var value = repositoryUrl.Trim().TrimEnd('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            value = value[..^4];

        if (value.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            value = value["git@github.com:".Length..];
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Guide repositoryUrl must be a github.com repository URL. Current value: '{repositoryUrl}'.");

            value = uri.AbsolutePath.Trim('/');
        }

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            throw new InvalidOperationException($"Guide repositoryUrl must identify a GitHub owner and repository. Current value: '{repositoryUrl}'.");

        return $"{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}";
    }

    private static string EscapePath(string value) =>
        string.Join(
            '/',
            value.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

    private static string CacheDirectory(string configRoot, string domain) =>
        Path.Combine(configRoot, "guides-cache", domain);

    private static string UserGuideDirectory(string configRoot, string domain) =>
        Path.Combine(configRoot, "guides", domain);

    private static string MetadataFile(string cacheDirectory) =>
        Path.Combine(cacheDirectory, ".metadata.json");
}

public sealed record GuideCatalogOptions(
    string ConfigRoot,
    TimeSpan CacheTtl,
    bool ForceRefresh,
    string RepositoryUrl,
    string Branch,
    HttpClient? HttpClient)
{
    public GuideCatalogOptions Normalize() =>
        new(
            string.IsNullOrWhiteSpace(ConfigRoot) ? ConfigPaths.Root : ConfigRoot,
            CacheTtl,
            ForceRefresh,
            string.IsNullOrWhiteSpace(RepositoryUrl) ? GuideConfig.DefaultRepositoryUrl : RepositoryUrl.Trim().TrimEnd('/'),
            string.IsNullOrWhiteSpace(Branch) ? GuideConfig.DefaultBranch : Branch.Trim(),
            HttpClient);

    public static GuideCatalogOptions Default() =>
        new(
            ConfigPaths.Root,
            TimeSpan.FromMinutes(5),
            ForceRefresh: false,
            GuideConfig.DefaultRepositoryUrl,
            GuideConfig.DefaultBranch,
            HttpClient: null);
}

public sealed record GuideCacheMetadata(
    DateTimeOffset RefreshedAt,
    string RepositoryUrl,
    string Branch);

public static partial class GuideText
{
    public static string NormalizePhrase(string value) =>
        string.Join(' ', Words(value));

    public static IEnumerable<string> Words(string value) =>
        WordRegex()
            .Matches(value.ToLowerInvariant())
            .Select(match => match.Value);

    public static string Titleize(string slug) =>
        string.Join(' ', slug.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex WordRegex();
}

public sealed class GuideIndexEntry
{
    public string? Slug { get; set; }
    public string? File { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public List<string> Keywords { get; set; } = [];
    public List<string> Commands { get; set; } = [];
    public List<string> McpTools { get; set; } = [];
    public List<string> Skills { get; set; } = [];
}
