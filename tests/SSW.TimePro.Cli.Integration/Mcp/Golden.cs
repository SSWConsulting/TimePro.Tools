using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Checked-in JSON snapshots for the MCP harness. Files are written with sorted keys so a
/// contract change shows up as a reviewable diff rather than reordered noise; regenerate
/// deliberately with <c>UPDATE_MCP_GOLDENS=1 dotnet test</c>.
/// </summary>
public static class Golden
{
    private const string UpdateEnvVar = "UPDATE_MCP_GOLDENS";

    private static readonly Lazy<string> Directory = new(FindGoldensDirectory);

    public static bool UpdateRequested =>
        Environment.GetEnvironmentVariable(UpdateEnvVar) is "1" or "true";

    public static string PathFor(string relativePath) =>
        System.IO.Path.Combine(Directory.Value, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>
    /// Compares <paramref name="actualJson"/> with the golden at <paramref name="relativePath"/>.
    /// Both sides are canonicalised (sorted keys, two-space indent) before comparison, so
    /// property order and whitespace are not part of the contract but every value is.
    /// </summary>
    public static void Verify(string relativePath, string actualJson)
    {
        var canonical = Canonicalize(actualJson);
        var path = PathFor(relativePath);

        if (UpdateRequested || !File.Exists(path))
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, canonical + "\n", new UTF8Encoding(false));

            if (!UpdateRequested)
                throw new Xunit.Sdk.XunitException(
                    $"Golden '{relativePath}' did not exist; it has been written. Review and commit it.");

            return;
        }

        var expected = File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n');
        if (expected == canonical)
            return;

        throw new Xunit.Sdk.XunitException(
            $"""
            MCP golden mismatch: {relativePath}
            Re-run with UPDATE_MCP_GOLDENS=1 only after confirming the change is intended.

            --- expected (checked in) ---
            {expected}

            --- actual ---
            {canonical}
            """);
    }

    /// <summary>Non-JSON snapshot (plain text), same update semantics.</summary>
    public static void VerifyText(string relativePath, string actual)
    {
        var normalized = actual.Replace("\r\n", "\n").TrimEnd('\n');
        var path = PathFor(relativePath);

        if (UpdateRequested || !File.Exists(path))
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, normalized + "\n", new UTF8Encoding(false));

            if (!UpdateRequested)
                throw new Xunit.Sdk.XunitException(
                    $"Golden '{relativePath}' did not exist; it has been written. Review and commit it.");

            return;
        }

        var expected = File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n');
        if (expected != normalized)
            throw new Xunit.Sdk.XunitException(
                $"""
                MCP golden mismatch: {relativePath}

                --- expected (checked in) ---
                {expected}

                --- actual ---
                {normalized}
                """);
    }

    public static string Canonicalize(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true
            });
        }
        catch (JsonException ex)
        {
            throw new Xunit.Sdk.XunitException($"Tool returned non-JSON text: {ex.Message}\n{json}");
        }

        var sorted = Sort(node);
        return sorted is null
            ? "null"
            : sorted.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode? Sort(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (var pair in obj.ToList().OrderBy(p => p.Key, StringComparer.Ordinal))
                    sorted[pair.Key] = Sort(pair.Value?.DeepClone());
                return sorted;

            case JsonArray array:
                var result = new JsonArray();
                foreach (var item in array.ToList())
                    result.Add(Sort(item?.DeepClone()));
                return result;

            default:
                return node?.DeepClone();
        }
    }

    private static string FindGoldensDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = System.IO.Path.Combine(
                dir.FullName, "tests", "SSW.TimePro.Cli.Integration", "Goldens", "Mcp");
            if (System.IO.Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate tests/SSW.TimePro.Cli.Integration/Goldens/Mcp.");
    }
}
