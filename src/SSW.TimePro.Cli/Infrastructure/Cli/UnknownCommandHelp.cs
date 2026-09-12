namespace SSW.TimePro.Cli.Infrastructure.Cli;

public sealed record UnknownCommand(string Token, string? Suggestion, IReadOnlyList<string> Commands)
{
    public string Detail => Suggestion is null
        ? $"Commands: {string.Join(", ", Commands)}"
        : $"Did you mean '{Suggestion}'? Commands: {string.Join(", ", Commands)}";

    public string Message => $"Unknown command '{Token}'. {Detail}";
}

/// <summary>
/// Turns a mistyped or invented command into the valid siblings and the closest match.
/// </summary>
public static class UnknownCommandHelp
{
    public static UnknownCommand? TryDescribe(IReadOnlyList<string> args) =>
        TryDescribe(args, CommandCatalog.Root);

    public static UnknownCommand? TryDescribe(IReadOnlyList<string> args, CommandNode root)
    {
        var current = root;

        foreach (var token in args)
        {
            if (token.StartsWith('-'))
                break;

            if (!current.IsBranch)
                break;

            var child = current.Child(token);
            if (child is null)
            {
                var names = current.Children.Select(c => c.Name).ToList();
                return new UnknownCommand(token, ClosestMatch(token, names), names);
            }

            current = child;
        }

        return null;
    }

    internal static string? ClosestMatch(string token, IReadOnlyList<string> candidates)
    {
        var threshold = token.Length <= 4 ? 2 : 3;
        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var distance = Distance(token, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return bestDistance <= threshold ? best : null;
    }

    private static int Distance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var substitution = char.ToLowerInvariant(left[i - 1]) == char.ToLowerInvariant(right[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
