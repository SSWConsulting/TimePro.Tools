using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.RepoMap;

[Description("Detect client/project mapping for the current directory")]
public class DetectCommand : Command<DetectCommand.Settings>
{
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public DetectCommand(IConfigService config) => _config = config;

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var cwd = Environment.CurrentDirectory;
        var mappings = _config.LoadRepoMappings();

        var match = RepoDetector.Detect(cwd, mappings);

        if (match is null)
        {
            if (settings.Json)
                OutputHelper.WriteJson(new { detected = false, path = cwd });
            else
                OutputHelper.WriteInfo($"No mapping found for '{cwd}'");
            return 1;
        }

        OutputHelper.Render(new
        {
            detected = true,
            path = cwd,
            match.PathPattern,
            match.ClientId,
            match.ProjectId,
            match.ProjectName,
            match.CategoryId
        }, settings.Json, _ =>
        {
            var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
            table.AddRow("[bold]Path[/]", Markup.Escape(cwd));
            table.AddRow("[bold]Matched[/]", Markup.Escape(match.PathPattern));
            table.AddRow("[bold]Client[/]", Markup.Escape(match.ClientId));
            table.AddRow("[bold]Project[/]", Markup.Escape($"{match.ProjectId} ({match.ProjectName ?? ""})"));
            if (!string.IsNullOrEmpty(match.CategoryId))
                table.AddRow("[bold]Category[/]", Markup.Escape(match.CategoryId));
            AnsiConsole.Write(table);
        });

        return 0;
    }
}
