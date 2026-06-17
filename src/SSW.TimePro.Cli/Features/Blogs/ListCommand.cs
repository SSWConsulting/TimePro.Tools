using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Blogs;

[Description("List latest blog posts from SSW employees")]
public class ListCommand : AsyncCommand<ListCommand.Settings>
{
    private readonly ITimeProApiClient _api;

    public class Settings : CommandSettings
    {
        [CommandOption("--limit <N>")]
        [Description("Number of posts to show (default: 10)")]
        public int Limit { get; set; } = 10;

        [CommandOption("--all")]
        [Description("Include blog posts from former employees")]
        public bool All { get; set; }

        [CommandOption("--mine")]
        [Description("Show only your own blog posts")]
        public bool Mine { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public ListCommand(ITimeProApiClient api) => _api = api;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var blogs = await _api.GetBlogsAsync(settings.All, CancellationToken.None);

            if (settings.Mine)
                blogs = blogs.Where(b => b.IsMe).ToList();

            var limited = blogs.Take(settings.Limit).ToList();

            OutputHelper.Render(limited, settings.Json, list =>
            {
                if (list.Count == 0)
                {
                    OutputHelper.WriteInfo("No blog posts found");
                    return;
                }

                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule("[bold]Latest Blog Posts[/]").LeftJustified().RuleStyle("dim"));
                AnsiConsole.WriteLine();

                foreach (var b in list)
                {
                    var date = b.BlogData?.Published?.Split('T')[0] ?? "?";
                    var author = b.IsMe
                        ? $"[bold green]{Markup.Escape(b.Author ?? "?")}[/] [dim](you)[/]"
                        : $"[bold]{Markup.Escape(b.Author ?? "?")}[/]";
                    var title = Markup.Escape(b.BlogData?.Title ?? "?");
                    var url = b.BlogData?.Url ?? "";
                    var shortUrl = ShortDomain(url);

                    AnsiConsole.MarkupLine($"  [dim]{date}[/]  {author}");
                    AnsiConsole.MarkupLine($"  {title}");
                    AnsiConsole.MarkupLine($"  [link={Markup.Escape(url)}][blue]{Markup.Escape(shortUrl)}[/][/]");
                    AnsiConsole.WriteLine();
                }

                if (blogs.Count > settings.Limit)
                    AnsiConsole.MarkupLine($"[dim]Showing {settings.Limit} of {blogs.Count}. Use --limit {blogs.Count} to see all.[/]");
            });

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }

    private static string ShortDomain(string url)
    {
        var display = url.Replace("https://", "").Replace("http://", "").TrimEnd('/');
        return display;
    }
}
