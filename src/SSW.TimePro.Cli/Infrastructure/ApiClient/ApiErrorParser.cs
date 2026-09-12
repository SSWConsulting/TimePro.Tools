using System.Text.Json;

namespace SSW.TimePro.Cli.Infrastructure.ApiClient;

/// <summary>
/// Extracts human-readable error details from TimePro API error responses.
/// </summary>
public static class ApiErrorParser
{
    private const int MaxDetailLength = 500;

    private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
        { "type", "title", "status", "detail", "traceId", "errors", "message", "instance" };

    /// <summary>
    /// Extracts a single-line detail from a response body: RFC 7807 problem details
    /// (including the <c>errors</c> dictionary), bare JSON strings, <c>{"message": ...}</c>,
    /// or the raw body truncated to 500 characters.
    /// </summary>
    public static string? ExtractDetail(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        var fromJson = TryExtractFromJson(responseBody);
        return Normalize(fromJson ?? responseBody);
    }

    private static string? TryExtractFromJson(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.String)
                return root.GetString();

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var messages = new List<string>();

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                    messages.AddRange(FieldMessages(field));
            }

            // TimePro also returns flat objects like { "title": "...", "CategoryID": "Please specify..." }.
            foreach (var prop in root.EnumerateObject())
            {
                if (ReservedKeys.Contains(prop.Name))
                    continue;

                messages.AddRange(FieldMessages(prop));
            }

            if (messages.Count > 0)
                return string.Join("; ", messages);

            return FirstNonEmptyString(root, "detail", "message", "title");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> FieldMessages(JsonProperty field)
    {
        switch (field.Value.ValueKind)
        {
            case JsonValueKind.String:
                yield return $"{field.Name}: {field.Value.GetString()}";
                break;
            case JsonValueKind.Array:
                foreach (var item in field.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        yield return $"{field.Name}: {item.GetString()}";
                }

                break;
        }
    }

    private static string? FirstNonEmptyString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var singleLine = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (singleLine.Length <= MaxDetailLength)
            return singleLine;

        // Cutting mid-surrogate would leave an unpaired half in the detail.
        var cut = MaxDetailLength;
        if (char.IsHighSurrogate(singleLine[cut - 1]))
            cut--;

        return singleLine[..cut] + "…";
    }
}
