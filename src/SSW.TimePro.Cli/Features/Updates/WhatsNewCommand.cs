using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Updates;

[Description("Show release notes since the previous installed version")]
public sealed class WhatsNewCommand : Command<WhatsNewCommand.Settings>
{
    private readonly IConfigService _config;

    public WhatsNewCommand(IConfigService config)
    {
        _config = config;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--url")]
        [Description("Print the latest release notes URL instead of Markdown")]
        public bool Url { get; set; }
    }

    protected override int Execute(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var args = settings.Url
            ? new[] { "--whats-new", "--url" }
            : new[] { "--whats-new" };

        return AppMetadataCommandLine.ExecuteAsync(args, _config, cancellationToken)
            .GetAwaiter()
            .GetResult();
    }
}
