using System.Text.RegularExpressions;

namespace SSW.TimePro.Cli.Infrastructure.Telemetry;

/// <summary>
/// Reduces a request URL to a loggable route: path only, with identifying segments replaced.
/// The query string is dropped wholesale because it carries employee ids, client ids and dates.
/// </summary>
public static partial class RouteTemplate
{
    public static string From(Uri? uri)
    {
        if (uri is null)
            return "unknown";

        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.ToString();
        var query = path.IndexOf('?');
        if (query >= 0)
            path = path[..query];

        var segments = path.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            if (IsIdentifier(segments[i]))
                segments[i] = "{id}";
        }

        return string.Join('/', segments);
    }

    private static bool IsIdentifier(string segment) =>
        segment.Length > 0 && (Numeric().IsMatch(segment) || Guid.TryParse(segment, out _));

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex Numeric();
}
