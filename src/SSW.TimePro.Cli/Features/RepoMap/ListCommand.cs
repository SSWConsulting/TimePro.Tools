using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.RepoMap;

[Description("List repository mappings")]
public class ListCommand : Command<ListCommand.Settings>
{
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public ListCommand(IConfigService config) => _config = config;

    public override int Execute(CommandContext context, Settings settings)
    {
        var mappings = _config.LoadRepoMappings();

        if (mappings.Count == 0)
        {
            OutputHelper.WriteInfo("No repo mappings configured. Use 'tp map set <PATH> --client <ID> --project <ID>'");
            return 0;
        }

        OutputHelper.Render(mappings, settings.Json, list =>
        {
            var table = new Table()
                .AddColumn("Path / Remote")
                .AddColumn("Client")
                .AddColumn("Project")
                .AddColumn("Name");

            foreach (var m in list)
            {
                var pattern = m.PathPattern;
                if (!string.IsNullOrEmpty(m.RemotePattern))
                    pattern += $"\n[dim]remote: {Markup.Escape(m.RemotePattern)}[/]";
                table.AddRow(
                    pattern,
                    Markup.Escape(m.ClientId),
                    Markup.Escape(m.ProjectId),
                    Markup.Escape(m.ProjectName ?? ""));
            }

            AnsiConsole.Write(table);
        });

        return 0;
    }
}
