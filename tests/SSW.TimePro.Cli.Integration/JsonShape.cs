using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SSW.TimePro.Cli.Integration;

/// <summary>
/// Strict deserialization check: reports response properties that no DTO property would bind,
/// so API shape drift surfaces as a test failure instead of a silently zeroed field.
/// </summary>
public static class JsonShape
{
    /// <summary>
    /// Asserts every property in <paramref name="json"/> binds to a property on <typeparamref name="T"/>.
    /// Paths are reported index-free (<c>$.data[].unit</c>); pass known-unbound ones as
    /// <paramref name="allowUnmapped"/> to document the omission.
    /// </summary>
    public static void AssertFullyMapped<T>(string json, params string[] allowUnmapped)
    {
        using var doc = JsonDocument.Parse(json);
        var unmapped = new List<string>();
        Walk(doc.RootElement, typeof(T), "$", unmapped);

        var allowed = new HashSet<string>(allowUnmapped, StringComparer.OrdinalIgnoreCase);
        var offenders = unmapped.Distinct().Where(p => !allowed.Contains(p)).Order().ToList();

        if (offenders.Count > 0)
            throw new Xunit.Sdk.XunitException(
                $"{typeof(T).Name} does not bind: {string.Join(", ", offenders)}");
    }

    private static void Walk(JsonElement element, Type type, string path, List<string> unmapped)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (!IsBindable(type))
                    return;
                foreach (var prop in element.EnumerateObject())
                {
                    var target = FindProperty(type, prop.Name);
                    if (target is null)
                        unmapped.Add($"{path}.{prop.Name}");
                    else
                        Walk(prop.Value, target.PropertyType, $"{path}.{prop.Name}", unmapped);
                }
                break;

            case JsonValueKind.Array:
                var elementType = GetElementType(type);
                if (elementType is null)
                    return;
                foreach (var item in element.EnumerateArray())
                    Walk(item, elementType, $"{path}[]", unmapped);
                break;
        }
    }

    private static bool IsBindable(Type type) =>
        type.IsClass && type != typeof(string) && type != typeof(object);

    private static Type? GetElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();
        if (!typeof(IEnumerable).IsAssignableFrom(type))
            return null;
        return type.IsGenericType ? type.GetGenericArguments().FirstOrDefault() : null;
    }

    private static PropertyInfo? FindProperty(Type type, string jsonName) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p =>
                string.Equals(
                    p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name,
                    jsonName,
                    StringComparison.OrdinalIgnoreCase));
}
