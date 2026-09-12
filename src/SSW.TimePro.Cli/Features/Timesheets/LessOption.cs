using System.Globalization;

namespace SSW.TimePro.Cli.Features.Timesheets;

/// <summary>
/// Parses the <c>--less</c> break time. Bound as a string so a bad value produces a
/// unit-aware message instead of Spectre's generic conversion failure.
/// </summary>
public static class LessOption
{
    public static bool TryParse(string? raw, out int? minutes, out string? error)
    {
        minutes = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var trimmed = raw.Trim();
        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            error = $"--less expects whole minutes, e.g. --less 90 (got '{trimmed}')";
            return false;
        }

        if (parsed < 0)
        {
            error = "--less must be zero or greater";
            return false;
        }

        minutes = parsed;
        return true;
    }
}
