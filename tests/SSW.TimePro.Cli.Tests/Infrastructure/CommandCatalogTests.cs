using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Cli;
using Spectre.Console;
using Spectre.Console.Cli;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

/// <summary>
/// The catalog is a hand-written mirror of the registered command tree, so it is compared
/// against the tree the real app builds; the help output is the only public view of it.
/// </summary>
public class CommandCatalogTests
{
    [Fact]
    public void Catalog_MatchesTheRegisteredCommandTree()
    {
        Describe(CommandCatalog.Root).Should().BeEquivalentTo(
            Describe(RegisteredTree()),
            options => options.WithStrictOrdering());
    }

    private static IReadOnlyList<string> Describe(CommandNode node)
    {
        var paths = new List<string>();

        void Walk(CommandNode current, string prefix)
        {
            foreach (var child in current.Children)
            {
                var path = string.IsNullOrEmpty(prefix) ? child.Name : $"{prefix} {child.Name}";
                paths.Add(path);
                Walk(child, path);
            }
        }

        Walk(node, string.Empty);
        return paths;
    }

    private static CommandNode RegisteredTree()
    {
        CommandNode Walk(IReadOnlyList<string> path)
        {
            var children = SubcommandsOf(path)
                .Select(name => Walk([.. path, name]))
                .ToList();

            return new CommandNode(path.Count == 0 ? "tp" : path[^1], children);
        }

        return Walk([]);
    }

    private static IReadOnlyList<string> SubcommandsOf(IReadOnlyList<string> path)
    {
        var help = RenderHelp([.. path, "--help"]);
        var marker = help.IndexOf("COMMANDS:", StringComparison.Ordinal);
        if (marker < 0)
            return [];

        return help[marker..]
            .Split('\n')
            .Skip(1)
            .TakeWhile(line => line.StartsWith("    ", StringComparison.Ordinal))
            .Where(line => line.Length > 4 && line[4] != ' ')
            .Select(line => line[4..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .ToList();
    }

    private static string RenderHelp(string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        console.Profile.Width = 300;

        var app = new CommandApp();
        app.Configure(config =>
        {
            config.ConfigureConsole(console);
            CliConfiguration.Configure(config);
        });

        app.Run(args);
        return writer.ToString();
    }
}
