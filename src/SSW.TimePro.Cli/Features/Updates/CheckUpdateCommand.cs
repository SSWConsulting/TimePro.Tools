using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Updates;

[Description("Check the latest GitHub Release and print update instructions")]
public sealed class CheckUpdateCommand : AsyncCommand<CheckUpdateCommand.Settings>
{
    private readonly IConfigService _config;

    public CheckUpdateCommand(IConfigService config)
    {
        _config = config;
    }

    public sealed class Settings : CommandSettings
    {
    }

    protected override Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken) =>
        AppMetadataCommandLine.ExecuteAsync(["--check-update"], _config, cancellationToken);
}
