using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.RepoMap;

[Description("Map a repository path to a client/project")]
public class SetCommand : Command<SetCommand.Settings>
{
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("Repository path or glob pattern")]
        public string Path { get; set; } = string.Empty;

        [CommandOption("--client <CLIENT>")]
        [Description("Client ID")]
        public string ClientId { get; set; } = string.Empty;

        [CommandOption("--project <PROJECT>")]
        [Description("Project ID")]
        public string ProjectId { get; set; } = string.Empty;

        [CommandOption("--project-name <NAME>")]
        [Description("Project display name")]
        public string? ProjectName { get; set; }

        [CommandOption("--category <CAT>")]
        [Description("Category ID")]
        public string? Category { get; set; }
    }

    public SetCommand(IConfigService config) => _config = config;

    public override int Execute(CommandContext context, Settings settings)
    {
        if (string.IsNullOrEmpty(settings.ClientId) || string.IsNullOrEmpty(settings.ProjectId))
        {
            OutputHelper.WriteError("--client and --project are required");
            return 1;
        }

        var mappings = _config.LoadRepoMappings();
        var normalizedPath = settings.Path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        // Update or add
        var existing = mappings.FirstOrDefault(m =>
            m.PathPattern.Equals(settings.Path, StringComparison.OrdinalIgnoreCase) ||
            m.PathPattern.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.ClientId = settings.ClientId;
            existing.ProjectId = settings.ProjectId;
            existing.ProjectName = settings.ProjectName;
            existing.CategoryId = settings.Category;
        }
        else
        {
            mappings.Add(new RepoMappingEntry
            {
                PathPattern = settings.Path,
                ClientId = settings.ClientId,
                ProjectId = settings.ProjectId,
                ProjectName = settings.ProjectName,
                CategoryId = settings.Category
            });
        }

        _config.SaveRepoMappings(mappings);
        OutputHelper.WriteSuccess($"Mapped '{settings.Path}' -> {settings.ClientId}/{settings.ProjectId}");
        return 0;
    }
}
