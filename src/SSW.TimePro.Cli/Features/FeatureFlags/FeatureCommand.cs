using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.FeatureFlags;

[Description("Manage optional TimePro feature packs")]
public class FeatureCommand : Command<FeatureCommand.Settings>
{
    private readonly IConfigService _config;

    private static readonly HashSet<string> EnableValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "enable", "enabled", "true", "1", "on", "yes"
    };

    private static readonly HashSet<string> DisableValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "disable", "disabled", "false", "0", "off", "no"
    };

    private sealed record FeatureStatus(
        string Key,
        string DisplayName,
        bool Enabled,
        int Version,
        int LatestVersion,
        string Description,
        IReadOnlyList<string> Aliases);

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[FEATURE]")]
        [Description("Feature name: accounting, developer")]
        public string? Feature { get; set; }

        [CommandArgument(1, "[STATE]")]
        [Description("enable/disable, true/false, 1/0. Omit to show status.")]
        public string? State { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public FeatureCommand(IConfigService config)
    {
        _config = config;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var global = _config.LoadGlobalConfig();

        if (string.IsNullOrWhiteSpace(settings.Feature) ||
            settings.Feature.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            RenderList(global, settings.Json);
            return 0;
        }

        if (!FeatureCatalog.TryNormalize(settings.Feature, out var definition))
        {
            OutputHelper.WriteError($"Unknown feature '{settings.Feature}'. Known features: {string.Join(", ", FeatureCatalog.All.Select(f => f.Key))}.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.State) ||
            settings.State.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            RenderFeature(global, definition, settings.Json);
            return 0;
        }

        bool enabled;
        if (EnableValues.Contains(settings.State))
        {
            enabled = true;
        }
        else if (DisableValues.Contains(settings.State))
        {
            enabled = false;
        }
        else
        {
            OutputHelper.WriteError($"Unknown feature state '{settings.State}'. Use enable/disable, true/false, 1/0, on/off, or yes/no.");
            return 1;
        }

        global.SetFeature(definition, enabled);
        _config.SaveGlobalConfig(global);

        var status = ToStatus(global, definition);
        OutputHelper.Render(status, settings.Json, s =>
        {
            var state = s.Enabled ? "[green]enabled[/]" : "[dim]disabled[/]";
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(s.Key)}[/] {state} [dim](version {s.Version})[/]");
        });

        return 0;
    }

    private static void RenderList(GlobalConfig global, bool json)
    {
        var statuses = FeatureCatalog.All.Select(definition => ToStatus(global, definition)).ToList();

        OutputHelper.Render(statuses, json, rows =>
        {
            var table = new Table()
                .AddColumn("Feature")
                .AddColumn("Enabled")
                .AddColumn("Version")
                .AddColumn("Description");

            foreach (var row in rows)
            {
                table.AddRow(
                    Markup.Escape(row.Key),
                    row.Enabled ? "[green]yes[/]" : "[dim]no[/]",
                    row.Version.ToString(),
                    Markup.Escape(row.Description));
            }

            AnsiConsole.Write(table);
        });
    }

    private static void RenderFeature(GlobalConfig global, FeatureDefinition definition, bool json)
    {
        var status = ToStatus(global, definition);

        OutputHelper.Render(status, json, row =>
        {
            var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
            table.AddRow("[bold]Feature[/]", Markup.Escape(row.Key));
            table.AddRow("[bold]Enabled[/]", row.Enabled ? "[green]yes[/]" : "[dim]no[/]");
            table.AddRow("[bold]Version[/]", row.Version.ToString());
            table.AddRow("[bold]Description[/]", Markup.Escape(row.Description));
            AnsiConsole.Write(table);
        });
    }

    private static FeatureStatus ToStatus(GlobalConfig global, FeatureDefinition definition)
    {
        global.Features.TryGetValue(definition.Key, out var feature);

        return new FeatureStatus(
            definition.Key,
            definition.DisplayName,
            feature?.Enabled ?? false,
            feature?.Version ?? 0,
            definition.Version,
            definition.Description,
            definition.Aliases);
    }
}
