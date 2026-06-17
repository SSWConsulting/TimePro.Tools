using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.RepoMap;

[Description("Remove a repository mapping")]
public class RemoveCommand : Command<RemoveCommand.Settings>
{
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("Repository path to remove")]
        public string Path { get; set; } = string.Empty;
    }

    public RemoveCommand(IConfigService config) => _config = config;

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var mappings = _config.LoadRepoMappings();
        var normalizedPath = settings.Path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        var removed = mappings.RemoveAll(m =>
            m.PathPattern.Equals(settings.Path, StringComparison.OrdinalIgnoreCase) ||
            m.PathPattern.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
        {
            OutputHelper.WriteError($"No mapping found for '{settings.Path}'");
            return 1;
        }

        _config.SaveRepoMappings(mappings);
        OutputHelper.WriteSuccess($"Removed mapping for '{settings.Path}'");
        return 0;
    }
}
