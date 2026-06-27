using System.Net;
using System.Text;
using FluentAssertions;
using SSW.TimePro.Cli.Features.Updates;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Updates;

public class GitHubReleaseClientTests
{
    [Fact]
    public async Task GetLatestReleaseAsync_ParsesLatestReleaseAndSetsGitHubHeaders()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """
            {
              "tag_name": "v0.2.7",
              "html_url": "https://github.com/SSWConsulting/TimePro.Tools/releases/tag/v0.2.7",
              "published_at": "2026-06-27T01:02:03Z"
            }
            """));
        var client = new GitHubReleaseClient(new HttpClient(handler));

        var release = await client.GetLatestReleaseAsync(CancellationToken.None);

        release.Version.Should().Be("0.2.7");
        release.TagName.Should().Be("v0.2.7");
        release.Url.Should().Be("https://github.com/SSWConsulting/TimePro.Tools/releases/tag/v0.2.7");
        release.PublishedAt.Should().Be(DateTimeOffset.Parse("2026-06-27T01:02:03Z"));
        handler.LastRequestUri.Should().Be(GitHubReleaseClient.LatestReleaseUrl);
        handler.LastAccept.Should().Contain("application/vnd.github+json");
        handler.LastUserAgent.Should().Contain("timepro-cli/1.0");
    }

    [Fact]
    public async Task GetLatestReleaseAsync_ThrowsForUnsuccessfulResponse()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = new GitHubReleaseClient(new HttpClient(handler));

        var act = () => client.GetLatestReleaseAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public string? LastRequestUri { get; private set; }
        public string? LastAccept { get; private set; }
        public string? LastUserAgent { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
            LastAccept = string.Join(",", request.Headers.Accept);
            LastUserAgent = request.Headers.UserAgent.ToString();

            return Task.FromResult(_respond(request));
        }
    }
}
