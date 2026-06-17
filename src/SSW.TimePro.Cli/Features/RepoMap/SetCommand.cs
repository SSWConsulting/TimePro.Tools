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

        [CommandOption("--remote <PATTERN>")]
        [Description("Git remote URL pattern (e.g., github.com/org/repo or github.com/org/*)")]
        public string? Remote { get; set; }

        [CommandOption("--category <CAT>")]
        [Description("Category ID")]
        public string? Category { get; set; }

        [CommandOption("--issues-repo <REPO>")]
        [Description("GitHub repo where issues/PRs live (if different from the code repo). E.g. Northwind/traders-app")]
        public string? IssuesRepo { get; set; }
    }

    public SetCommand(IConfigService config) => _config = config;

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.ClientId) || string.IsNullOrEmpty(settings.ProjectId))
        {
            OutputHelper.WriteError("--client and --project are required");
            return 1;
        }

        var mappings = _config.LoadRepoMappings();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string Expand(string p) => p.StartsWith("~")
            ? home + p.AsSpan(1).ToString()
            : p;
        var normalizedInput = Expand(settings.Path);

        // Match by expanded path so "~/foo" and "/Users/me/foo" dedupe.
        var existing = mappings.FirstOrDefault(m =>
            Expand(m.PathPattern).Equals(normalizedInput, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.ClientId = settings.ClientId;
            existing.ProjectId = settings.ProjectId;
            // Preserve optional fields when not explicitly specified on update
            if (settings.ProjectName is not null)
                existing.ProjectName = settings.ProjectName;
            if (settings.Category is not null)
                existing.CategoryId = settings.Category;
            if (settings.Remote is not null)
                existing.RemotePattern = settings.Remote;
            if (settings.IssuesRepo is not null)
                existing.IssuesRepo = settings.IssuesRepo;
        }
        else
        {
            mappings.Add(new RepoMappingEntry
            {
                PathPattern = settings.Path,
                RemotePattern = settings.Remote,
                ClientId = settings.ClientId,
                ProjectId = settings.ProjectId,
                ProjectName = settings.ProjectName,
                CategoryId = settings.Category,
                IssuesRepo = settings.IssuesRepo
            });
        }

        _config.SaveRepoMappings(mappings);
        OutputHelper.WriteSuccess($"Mapped '{settings.Path}' -> {settings.ClientId}/{settings.ProjectId}");
        return 0;
    }
}
