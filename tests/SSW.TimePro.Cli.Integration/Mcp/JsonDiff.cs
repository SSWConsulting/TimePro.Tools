using System.Text.Json;
using System.Text.Json.Nodes;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Reports the JSON paths at which two documents differ, including keys present on only one side.
/// Parity cases assert the difference set, never a normalised document.
/// </summary>
public static class JsonDiff
{
    public static IReadOnlyList<string> Paths(string leftJson, string rightJson)
    {
        var differences = new List<string>();
        Walk(JsonNode.Parse(leftJson), JsonNode.Parse(rightJson), "$", differences);
        return differences.Distinct().Order(StringComparer.Ordinal).ToList();
    }

    private static void Walk(JsonNode? left, JsonNode? right, string path, List<string> differences)
    {
        if (left is null && right is null)
            return;

        if (left is JsonObject leftObject && right is JsonObject rightObject)
        {
            foreach (var key in leftObject.Select(p => p.Key).Union(rightObject.Select(p => p.Key)).Order(StringComparer.Ordinal))
            {
                var hasLeft = leftObject.TryGetPropertyValue(key, out var leftValue);
                var hasRight = rightObject.TryGetPropertyValue(key, out var rightValue);

                if (hasLeft != hasRight)
                    differences.Add($"{path}.{key}");
                else
                    Walk(leftValue, rightValue, $"{path}.{key}", differences);
            }

            return;
        }

        if (left is JsonArray leftArray && right is JsonArray rightArray)
        {
            if (leftArray.Count != rightArray.Count)
            {
                differences.Add($"{path}.length");
                return;
            }

            for (var i = 0; i < leftArray.Count; i++)
                Walk(leftArray[i], rightArray[i], $"{path}[{i}]", differences);

            return;
        }

        var leftText = left?.ToJsonString() ?? "null";
        var rightText = right?.ToJsonString() ?? "null";
        if (!NumericallyEqual(left, right) && leftText != rightText)
            differences.Add(path);
    }

    /// <summary>Treats <c>7.5</c> and <c>7.50</c> as equal; JSON number formatting is not a contract.</summary>
    private static bool NumericallyEqual(JsonNode? left, JsonNode? right) =>
        left is JsonValue leftValue
        && right is JsonValue rightValue
        && leftValue.GetValueKind() == JsonValueKind.Number
        && rightValue.GetValueKind() == JsonValueKind.Number
        && leftValue.GetValue<decimal>() == rightValue.GetValue<decimal>();
}
