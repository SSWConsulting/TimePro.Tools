using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SSW.TimePro.Cli.Features.Scrum;

internal static partial class ScrumTemplateEngine
{
    public static string Render(string template, IReadOnlyDictionary<string, object?> values)
    {
        var withSections = RenderSections(template, values);
        return VariableRegex().Replace(withSections, match =>
        {
            var name = match.Groups["name"].Value;
            return values.TryGetValue(name, out var value) ? ToText(value) : string.Empty;
        });
    }

    private static string RenderSections(string template, IReadOnlyDictionary<string, object?> values)
    {
        var sb = new StringBuilder();
        var position = 0;

        while (true)
        {
            var open = SectionOpenRegex().Match(template, position);
            if (!open.Success)
            {
                sb.Append(template, position, template.Length - position);
                break;
            }

            sb.Append(template, position, open.Index - position);

            var name = open.Groups["name"].Value;
            var close = FindMatchingClose(template, name, open.Index + open.Length);
            if (close is null)
            {
                sb.Append(open.Value);
                position = open.Index + open.Length;
                continue;
            }

            var innerStart = open.Index + open.Length;
            var inner = template[innerStart..close.Index];
            var isTruthy = values.TryGetValue(name, out var value) && IsTruthy(value);
            var isInverted = open.Groups["type"].Value == "^";

            if (isInverted ? !isTruthy : isTruthy)
                sb.Append(RenderSections(inner, values));

            position = close.Index + close.Length;
        }

        return sb.ToString();
    }

    private static Match? FindMatchingClose(string template, string name, int startAt)
    {
        var boundary = new Regex(
            @"{{\s*(?<type>[#\^/])\s*" + Regex.Escape(name) + @"\s*}}",
            RegexOptions.CultureInvariant);
        var depth = 1;

        foreach (Match match in boundary.Matches(template, startAt))
        {
            var type = match.Groups["type"].Value;
            if (type is "#" or "^")
            {
                depth++;
                continue;
            }

            depth--;
            if (depth == 0)
                return match;
        }

        return null;
    }

    private static bool IsTruthy(object? value)
    {
        if (value is null) return false;
        if (value is bool boolean) return boolean;
        if (value is string text) return text.Length > 0;
        if (value is char) return true;
        if (IsNumber(value)) return true;
        if (value is IEnumerable enumerable)
        {
            var enumerator = enumerable.GetEnumerator();
            try
            {
                return enumerator.MoveNext();
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }

        return true;
    }

    private static bool IsNumber(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal;

    private static string ToText(object? value)
    {
        if (value is null) return string.Empty;
        if (value is string text) return text;
        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        return value.ToString() ?? string.Empty;
    }

    [GeneratedRegex(@"{{\s*(?<type>[#\^])\s*(?<name>[A-Za-z0-9_.-]+)\s*}}", RegexOptions.CultureInvariant)]
    private static partial Regex SectionOpenRegex();

    [GeneratedRegex(@"{{\s*(?<name>[A-Za-z0-9_.-]+)\s*}}", RegexOptions.CultureInvariant)]
    private static partial Regex VariableRegex();
}
