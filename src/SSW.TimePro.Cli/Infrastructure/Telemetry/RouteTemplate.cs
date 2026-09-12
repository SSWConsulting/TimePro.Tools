using System.Text.RegularExpressions;

namespace SSW.TimePro.Cli.Infrastructure.Telemetry;

/// <summary>
/// The route recorded for an API call.
/// <para>
/// Any call whose path carries an identifier states its own template
/// (<c>/api/employees/{empId}</c>); nothing is inferred from the final URL, because employee,
/// client and product ids are ordinary strings (<c>BOB</c>, <c>NWIND</c>) that no value-shape rule
/// can tell apart from a path literal.
/// </para>
/// </summary>
public static partial class RouteTemplate
{
    public const string Unknown = "unknown";

    /// <summary>
    /// The route for a call that declared no template: the literal path it was built from, minus
    /// the query string, which carries employee ids, client ids and dates.
    /// </summary>
    public static string FromLiteralPath(Uri? uri)
    {
        if (uri is null)
            return Unknown;

        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.ToString();
        var query = path.IndexOf('?');
        if (query >= 0)
            path = path[..query];

        return Normalize(path);
    }

    /// <summary>
    /// Accepts a caller-supplied template only if it is a plain route. Anything else is discarded
    /// rather than logged, so a template can never smuggle a value in.
    /// </summary>
    public static string Sanitize(string? template, Uri? uri)
    {
        if (string.IsNullOrWhiteSpace(template))
            return FromLiteralPath(uri);

        var trimmed = template.Trim();
        return SafeTemplate().IsMatch(trimmed) ? Normalize(trimmed) : Unknown;
    }

    private static string Normalize(string path)
    {
        var normalized = '/' + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length > 1 ? normalized : "/";
    }

    // Route characters only: no whitespace (a newline would break the header) and no query.
    [GeneratedRegex(@"^/[A-Za-z0-9._\-/{}]*$")]
    private static partial Regex SafeTemplate();
}
