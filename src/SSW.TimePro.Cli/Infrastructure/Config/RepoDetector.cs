using System.Diagnostics;

namespace SSW.TimePro.Cli.Infrastructure.Config;

/// <summary>
/// Detects repository mapping for a given directory, with git worktree and remote URL support.
/// Resolution order (most specific wins):
///   1. Exact path match on cwd or main worktree path
///   2. Remote URL exact match (e.g., github.com/asfaudits/chat-bot)
///   3. Glob path match (e.g., ~/Developer/git/ASF/*)
///   4. Glob remote match (e.g., github.com/asfaudits/*)
/// </summary>
public static class RepoDetector
{
    /// <summary>
    /// Find the best matching repo mapping for a given directory.
    /// </summary>
    public static RepoMappingEntry? Detect(string directory, List<RepoMappingEntry> mappings)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Collect candidate paths: cwd + main worktree
        var candidatePaths = new List<string> { directory };
        var mainRepoPath = ResolveMainRepoPath(directory);
        if (mainRepoPath is not null && !mainRepoPath.Equals(directory, StringComparison.OrdinalIgnoreCase))
            candidatePaths.Add(mainRepoPath);

        // Get remote URL (normalized)
        var remoteUrl = GetRemoteUrl(directory);

        // Score each mapping: higher = more specific
        RepoMappingEntry? bestMatch = null;
        int bestScore = -1;

        foreach (var m in mappings)
        {
            var score = ScoreMapping(m, candidatePaths, remoteUrl, home);
            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = m;
            }
        }

        return bestMatch;
    }

    private static int ScoreMapping(
        RepoMappingEntry mapping, List<string> paths, string? remoteUrl, string home)
    {
        int bestScore = -1;

        // Check path-based matching
        if (!string.IsNullOrEmpty(mapping.PathPattern))
        {
            var pattern = mapping.PathPattern.Replace("~", home);

            foreach (var path in paths)
            {
                if (pattern.EndsWith("/*"))
                {
                    var prefix = pattern[..^2];
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        bestScore = Math.Max(bestScore, 10 + prefix.Length); // glob = base 10 + specificity
                }
                else
                {
                    if (path.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                        bestScore = Math.Max(bestScore, 1000 + pattern.Length); // exact = 1000+
                    else if (path.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                        bestScore = Math.Max(bestScore, 100 + pattern.Length); // prefix = 100+
                }
            }
        }

        // Check remote URL matching
        if (!string.IsNullOrEmpty(mapping.RemotePattern) && !string.IsNullOrEmpty(remoteUrl))
        {
            var rp = mapping.RemotePattern;

            if (rp.EndsWith("/*"))
            {
                var prefix = rp[..^2];
                if (remoteUrl.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                    bestScore = Math.Max(bestScore, 50 + prefix.Length); // remote glob = 50+
            }
            else
            {
                if (remoteUrl.Contains(rp, StringComparison.OrdinalIgnoreCase))
                    bestScore = Math.Max(bestScore, 500 + rp.Length); // remote exact = 500+
            }
        }

        return bestScore;
    }

    /// <summary>
    /// Resolves the main worktree path for a git repo.
    /// For a regular repo, returns the repo root.
    /// For a worktree, returns the parent repo root.
    /// Returns null if not a git repo.
    /// </summary>
    public static string? ResolveMainRepoPath(string directory)
    {
        return RunGit(directory, "rev-parse --path-format=absolute --git-common-dir", output =>
        {
            if (output.EndsWith("/.git"))
                return output[..^5];
            if (output.EndsWith(".git"))
                return Path.GetDirectoryName(output);
            return output;
        });
    }

    /// <summary>
    /// Gets the normalized remote origin URL for a git repo.
    /// </summary>
    public static string? GetRemoteUrl(string directory)
    {
        return RunGit(directory, "remote get-url origin", url =>
        {
            // Normalize: strip .git suffix, protocol prefix, and trailing slashes
            url = url.TrimEnd('/');
            if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                url = url[..^4];

            // Strip protocol: https://github.com/org/repo -> github.com/org/repo
            if (url.Contains("://"))
                url = url[(url.IndexOf("://") + 3)..];

            // Strip auth: user@github.com:org/repo -> github.com/org/repo
            if (url.Contains('@'))
            {
                url = url[(url.IndexOf('@') + 1)..];
                url = url.Replace(':', '/'); // SSH: git@github.com:org/repo
            }

            return url;
        });
    }

    private static string? RunGit(string directory, string arguments, Func<string, string?> transform)
    {
        try
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            if (process.ExitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            return transform(output);
        }
        catch
        {
            return null;
        }
    }
}
