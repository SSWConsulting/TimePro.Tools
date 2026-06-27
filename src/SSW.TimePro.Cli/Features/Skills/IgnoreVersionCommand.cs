using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Skills;

[Description("Ignore the current generated skill version warning")]
public class IgnoreVersionCommand : Command<IgnoreVersionCommand.Settings>
{
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<SKILL>")]
        [Description("Generated skill name, e.g. timepro-accounting-cli")]
        public string Skill { get; set; } = string.Empty;

        [CommandArgument(1, "[VERSION]")]
        [Description("Version to ignore. Defaults to the current bundled version.")]
        public int? Version { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public IgnoreVersionCommand(IConfigService config)
    {
        _config = config;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var global = _config.LoadGlobalConfig();
        if (!SkillVersionService.IgnoreVersion(global, settings.Skill, settings.Version, out var error))
        {
            OutputHelper.WriteError(error);
            return 1;
        }

        _config.SaveGlobalConfig(global);

        var status = SkillVersionService.GetStatuses(global)
            .First(s => s.Name.Equals(settings.Skill, StringComparison.OrdinalIgnoreCase));

        OutputHelper.Render(status, settings.Json, s =>
        {
            AnsiConsole.MarkupLine($"[green]Ignored {Markup.Escape(s.Name)} version {s.IgnoredVersion}.[/]");
        });

        return 0;
    }
}
