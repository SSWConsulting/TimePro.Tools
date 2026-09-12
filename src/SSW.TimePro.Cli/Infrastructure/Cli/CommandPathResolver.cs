namespace SSW.TimePro.Cli.Infrastructure.Cli;

/// <summary>
/// Derives the canonical command path (<c>ts update</c>) from argv by walking
/// <see cref="CommandCatalog"/>. Reading argv against the catalog, rather than asking Spectre,
/// is what keeps arguments out of the value: only tokens that name a registered command are taken.
/// </summary>
public static class CommandPathResolver
{
    public const string Unknown = "unknown";

    public static string Resolve(IReadOnlyList<string> args)
    {
        var path = new List<string>();
        var node = CommandCatalog.Root;

        foreach (var arg in args)
        {
            if (arg == "--" || arg.StartsWith('-'))
                break;

            var child = node.Child(arg);
            if (child is null)
                break;

            path.Add(child.Name.ToLowerInvariant());
            node = child;
        }

        return path.Count == 0 ? Unknown : string.Join(' ', path);
    }
}
