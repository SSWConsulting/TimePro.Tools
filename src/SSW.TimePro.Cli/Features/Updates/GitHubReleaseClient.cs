using System.Net.Http.Headers;
using System.Text.Json;

namespace SSW.TimePro.Cli.Features.Updates;

public sealed record GitHubRelease(
    string Version,
    string TagName,
    string Url,
    DateTimeOffset? PublishedAt);

public sealed class GitHubReleaseClient
{
    public const string Repository = "SSWConsulting/TimePro.Tools";
    public const string LatestReleaseUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string InstallScriptUrl = "https://raw.githubusercontent.com/" + Repository + "/main/scripts/install.sh";
    public const string InstallPowerShellUrl = "https://raw.githubusercontent.com/" + Repository + "/main/scripts/install.ps1";

    private readonly HttpClient _http;

    public GitHubReleaseClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("timepro-cli", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var url = root.TryGetProperty("html_url", out var urlElement)
            ? urlElement.GetString() ?? ReleaseUrlFor(tagName)
            : ReleaseUrlFor(tagName);

        DateTimeOffset? publishedAt = null;
        if (root.TryGetProperty("published_at", out var publishedElement)
            && DateTimeOffset.TryParse(publishedElement.GetString(), out var parsed))
        {
            publishedAt = parsed;
        }

        return new GitHubRelease(
            Version: NormalizeVersion(tagName),
            TagName: tagName,
            Url: url,
            PublishedAt: publishedAt);
    }

    public static string ReleaseUrlFor(string versionOrTag)
    {
        var tag = versionOrTag.StartsWith('v') || versionOrTag.StartsWith('V')
            ? versionOrTag
            : $"v{versionOrTag}";
        return $"https://github.com/{Repository}/releases/tag/{tag}";
    }

    private static string NormalizeVersion(string tagName)
    {
        if (tagName.StartsWith('v') || tagName.StartsWith('V'))
            return tagName[1..];

        return tagName;
    }
}
