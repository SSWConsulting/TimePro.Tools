using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Updates;

[Description("Check the latest GitHub Release and print update instructions")]
public sealed class CheckUpdateCommand : AsyncCommand<CommandSettings>
{
    private readonly IConfigService _config;

    public CheckUpdateCommand(IConfigService config)
    {
        _config = config;
    }

    protected override Task<int> ExecuteAsync(
        CommandContext context,
        CommandSettings settings,
        CancellationToken cancellationToken) =>
        AppMetadataCommandLine.ExecuteAsync(["--check-update"], _config, cancellationToken);
}
