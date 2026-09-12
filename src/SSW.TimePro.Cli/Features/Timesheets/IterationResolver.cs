using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Timesheets;

public static class IterationResolver
{
    public static int? ResolveByNameOrId(IReadOnlyList<IterationItem> iterations, string input)
    {
        if (int.TryParse(input, out var id) && iterations.Any(i => i.IterationId == id))
            return id;

        return iterations
            .FirstOrDefault(i => string.Equals(i.IterationName, input, StringComparison.OrdinalIgnoreCase))
            ?.IterationId;
    }

    public static string Describe(IReadOnlyList<IterationItem> iterations) =>
        iterations.Count == 0
            ? "(none)"
            : string.Join(", ", iterations.Select(i => $"{i.IterationName} ({i.IterationId})"));
}
