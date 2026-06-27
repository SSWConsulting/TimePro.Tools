namespace SSW.TimePro.Cli.Infrastructure.Guides;

public sealed record RankedGuide(
    string Domain,
    string Slug,
    string Title,
    string Description,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> McpTools,
    IReadOnlyList<string> Skills,
    string Source,
    string Body,
    string MatchType,
    int MatchRank);

public static class GuideRanking
{
    public static IReadOnlyList<RankedGuide> Rank(
        string? query,
        IReadOnlyList<GuideDocument> guides)
    {
        if (string.IsNullOrWhiteSpace(query))
            return guides.Select(guide => ToRanked(guide, "default", 0)).ToList();

        var normalizedQuery = GuideText.NormalizePhrase(query);
        var queryWords = GuideText.Words(query).ToArray();
        if (queryWords.Length == 0)
            return guides.Select(guide => ToRanked(guide, "default", 0)).ToList();

        var matches = guides
            .Select((guide, index) => new { Guide = guide, Index = index, Match = Match(guide, normalizedQuery, queryWords) })
            .Where(candidate => candidate.Match.Rank > 0)
            .ToList();

        var highestRank = matches.Count == 0
            ? 0
            : matches.Max(candidate => candidate.Match.Rank);

        return matches
            .Where(candidate => candidate.Match.Rank == highestRank)
            .OrderByDescending(candidate => candidate.Match.Rank)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => ToRanked(candidate.Guide, candidate.Match.Type, candidate.Match.Rank))
            .ToList();
    }

    private static (int Rank, string Type) Match(GuideDocument guide, string normalizedQuery, IReadOnlyList<string> queryWords)
    {
        var exactCandidates = new[] { guide.Title, guide.Slug }
            .Concat(guide.Keywords)
            .Select(GuideText.NormalizePhrase);

        if (exactCandidates.Any(candidate => candidate == normalizedQuery))
            return (3, "exact");

        var haystack = GuideText.NormalizePhrase(string.Join(
            ' ',
            [guide.Title, guide.Description, guide.Slug, guide.Body, .. guide.Keywords, .. guide.Commands, .. guide.McpTools]));

        if (queryWords.Count > 1 && queryWords.All(word => haystack.Contains(word, StringComparison.Ordinal)))
            return (2, "contains-all");

        if (queryWords.Any(word => haystack.Contains(word, StringComparison.Ordinal)))
            return (1, "contains-one");

        return (0, "none");
    }

    private static RankedGuide ToRanked(GuideDocument guide, string matchType, int matchRank) =>
        new(
            guide.Domain,
            guide.Slug,
            guide.Title,
            guide.Description,
            guide.Keywords,
            guide.Commands,
            guide.McpTools,
            guide.Skills,
            guide.Source,
            guide.Body,
            matchType,
            matchRank);
}
