using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SSW.TimePro.Cli.Features.Scrum;

/// <summary>
/// Abstraction over the GitHub activity the scrum gatherer needs, so it can be
/// faked in tests (the real implementation shells out to <c>gh</c>).
/// </summary>
public interface IGhCli
{
    List<GhCli.PullRequest> ListMyPullRequests(string ownerRepo, int limit = 20);
    List<GhCli.Issue> ListMyAssignedIssues(string ownerRepo, int limit = 10);
}

/// <summary>
/// Thin wrapper over the local <c>gh</c> CLI. We shell out so we inherit the
/// user's existing GitHub auth — no token plumbing needed.
/// </summary>
public class GhCli : IGhCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public record PullRequest(
        int Number,
        string Title,
        string State,
        [property: JsonPropertyName("mergedAt")] DateTimeOffset? MergedAt,
        [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
        string Url
    );

    public record Issue(
        int Number,
        string Title,
        string Url
    );

    /// <summary>
    /// List PRs authored by the current user in a repo. Returns an empty
    /// list if <c>gh</c> is missing or the call fails — daily scrum should
    /// degrade gracefully, not error out.
    /// </summary>
    public List<PullRequest> ListMyPullRequests(string ownerRepo, int limit = 20)
    {
        var json = Run($"pr list --repo {ownerRepo} --author @me --state all --limit {limit} --json number,title,state,mergedAt,updatedAt,url");
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<PullRequest>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// List open issues assigned to the current user in a repo.
    /// </summary>
    public List<Issue> ListMyAssignedIssues(string ownerRepo, int limit = 10)
    {
        var json = Run($"issue list --repo {ownerRepo} --assignee @me --state open --limit {limit} --json number,title,url");
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<Issue>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("gh", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return string.Empty;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? stdout : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
