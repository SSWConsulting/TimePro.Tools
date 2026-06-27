using System.Net;
using System.Text;
using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Guides;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Guides;

public sealed class GuideCatalogCacheTests
{
    [Fact]
    public async Task LoadFromGitHubCacheAsync_RefreshesFromGitHubAndWritesCache()
    {
        using var temp = TempDirectory.Create();
        var handler = new StubHttpMessageHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/SSWConsulting/TimePro.Tools/main/guides/dev/index.json" => TextResponse(IndexJson("Remote Guide")),
            "/SSWConsulting/TimePro.Tools/main/guides/dev/remote-guide.md" => TextResponse("# Remote Guide\n\nFrom GitHub."),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var guides = await GuideCatalog.LoadFromGitHubCacheAsync(
            "dev",
            Options(temp.Path, TimeSpan.FromMinutes(5), forceRefresh: true, new HttpClient(handler)),
            CancellationToken.None);

        guides.Should().ContainSingle(guide => guide.Slug == "remote-guide" && guide.Title == "Remote Guide");
        guides.Single().Source.Should().Contain(Path.Combine("guides-cache", "dev", "remote-guide.md"));
        File.Exists(Path.Combine(temp.Path, "guides-cache", "dev", "index.json")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, "guides-cache", "dev", "remote-guide.md")).Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
        handler.UserAgents.Should().OnlyContain(value => value.Contains("timepro-cli/1.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadFromGitHubCacheAsync_UsesFreshCacheWithoutHttp()
    {
        using var temp = TempDirectory.Create();
        var setupHandler = new StubHttpMessageHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/SSWConsulting/TimePro.Tools/main/guides/dev/index.json" => TextResponse(IndexJson("Cached Guide")),
            "/SSWConsulting/TimePro.Tools/main/guides/dev/remote-guide.md" => TextResponse("# Cached Guide\n\nFrom GitHub."),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        await GuideCatalog.LoadFromGitHubCacheAsync(
            "dev",
            Options(temp.Path, TimeSpan.FromMinutes(5), forceRefresh: true, new HttpClient(setupHandler)),
            CancellationToken.None);

        var failingHandler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be used for a fresh cache."));

        var guides = await GuideCatalog.LoadFromGitHubCacheAsync(
            "dev",
            Options(temp.Path, TimeSpan.FromMinutes(5), forceRefresh: false, new HttpClient(failingHandler)),
            CancellationToken.None);

        guides.Should().ContainSingle(guide => guide.Title == "Cached Guide");
        failingHandler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadFromGitHubCacheAsync_ZeroCacheTtlRefreshesFromGitHub()
    {
        using var temp = TempDirectory.Create();
        var firstHandler = new StubHttpMessageHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/SSWConsulting/TimePro.Tools/main/guides/dev/index.json" => TextResponse(IndexJson("First Guide")),
            "/SSWConsulting/TimePro.Tools/main/guides/dev/remote-guide.md" => TextResponse("# First Guide\n\nFrom GitHub."),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        await GuideCatalog.LoadFromGitHubCacheAsync(
            "dev",
            Options(temp.Path, TimeSpan.FromMinutes(5), forceRefresh: true, new HttpClient(firstHandler)),
            CancellationToken.None);

        var secondHandler = new StubHttpMessageHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/SSWConsulting/TimePro.Tools/main/guides/dev/index.json" => TextResponse(IndexJson("Second Guide")),
            "/SSWConsulting/TimePro.Tools/main/guides/dev/remote-guide.md" => TextResponse("# Second Guide\n\nFrom GitHub."),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var guides = await GuideCatalog.LoadFromGitHubCacheAsync(
            "dev",
            Options(temp.Path, TimeSpan.Zero, forceRefresh: false, new HttpClient(secondHandler)),
            CancellationToken.None);

        guides.Should().ContainSingle(guide => guide.Title == "Second Guide");
        secondHandler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadFromGitHubCacheAsync_RefreshesWhenConfiguredBranchChanges()
    {
        using var temp = TempDirectory.Create();
        var mainHandler = new StubHttpMessageHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/SSWConsulting/TimePro.Tools/main/guides/dev/index.json" => TextResponse(IndexJson("Main Guide")),
            "/SSWConsulting/TimePro.Tools/main/guides/dev/remote-guide.md" => TextResponse("# Main Guide\n\nFrom GitHub."),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        await GuideCatalog.LoadFromGitHubCacheAsync(
            "dev",
            Options(temp.Path, TimeSpan.FromMinutes(5), forceRefresh: true, new HttpClient(mainHandler)),
            CancellationToken.None);

        var branchHandler = new StubHttpMessageHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/SSWConsulting/TimePro.Tools/codex/dev-skill-templates/guides/dev/index.json" => TextResponse(IndexJson("Branch Guide")),
            "/SSWConsulting/TimePro.Tools/codex/dev-skill-templates/guides/dev/remote-guide.md" => TextResponse("# Branch Guide\n\nFrom GitHub."),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var guides = await GuideCatalog.LoadFromGitHubCacheAsync(
            "dev",
            new GuideCatalogOptions(
                temp.Path,
                TimeSpan.FromMinutes(5),
                ForceRefresh: false,
                "https://github.com/SSWConsulting/TimePro.Tools",
                "codex/dev-skill-templates",
                new HttpClient(branchHandler)),
            CancellationToken.None);

        guides.Should().ContainSingle(guide => guide.Title == "Branch Guide");
        branchHandler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadFromGitHubCacheAsync_FallsBackToEmbeddedGuidesWhenGitHubFailsWithoutCache()
    {
        using var temp = TempDirectory.Create();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var guides = await GuideCatalog.LoadFromGitHubCacheAsync(
            "dev",
            Options(temp.Path, TimeSpan.FromMinutes(5), forceRefresh: true, new HttpClient(handler)),
            CancellationToken.None);

        guides.Should().Contain(guide => guide.Slug == "suggested-timesheets-missing"
            && guide.Source.StartsWith("embedded:", StringComparison.Ordinal));
    }

    private static string IndexJson(string title) =>
        $$"""
        {
          "guides": [
            {
              "slug": "remote-guide",
              "file": "remote-guide.md",
              "title": "{{title}}",
              "description": "Downloaded from GitHub.",
              "keywords": ["remote"],
              "commands": ["tp dev guide --use-case remote --json"],
              "mcpTools": [],
              "skills": ["timepro-dev-diagnostics"]
            }
          ]
        }
        """;

    private static HttpResponseMessage TextResponse(string text) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain")
        };

    private static GuideCatalogOptions Options(
        string configRoot,
        TimeSpan cacheTtl,
        bool forceRefresh,
        HttpClient httpClient) =>
        new(
            configRoot,
            cacheTtl,
            forceRefresh,
            "https://github.com/SSWConsulting/TimePro.Tools",
            "main",
            httpClient);

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<string> Requests { get; } = [];
        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri?.ToString() ?? string.Empty);
            UserAgents.Add(request.Headers.UserAgent.ToString());

            return Task.FromResult(_respond(request));
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create() =>
            new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tp-guides-{Guid.NewGuid():N}"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
