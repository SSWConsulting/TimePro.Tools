using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Skills;

[Description("Generate agent skill files for TimePro")]
public class CreateCommand : Command<CreateCommand.Settings>
{
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<TARGET>")]
        [Description("Target directory (e.g., .agents, .claude)")]
        public string Target { get; set; } = string.Empty;

        [CommandOption("--global")]
        [Description("Write to global config instead of local project")]
        public bool Global { get; set; }

        [CommandOption("--force")]
        [Description("Allow writing skills straight into the home directory (~/skills)")]
        public bool Force { get; set; }
    }

    public CreateCommand(IConfigService config) => _config = config;

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tenant = _config.LoadActiveTenantConfig();
        var global = _config.LoadGlobalConfig();
        var mappings = _config.LoadRepoMappings();

        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // --global only swaps the base dir; the skill layout is always skills/<name>/SKILL.md.
        var baseDir = ResolveBaseDir(
            settings.Global,
            settings.Target,
            Environment.CurrentDirectory,
            homeDirectory);

        var rejection = RejectHomeRoot(baseDir, homeDirectory, settings.Force);
        if (rejection is not null)
        {
            OutputHelper.WriteError(rejection);
            return 1;
        }

        // Detect repo mapping for current directory (with worktree support)
        var repoMapping = RepoDetector.Detect(Environment.CurrentDirectory, mappings);

        // Detect git remote for GH integration
        var remoteUrl = RepoDetector.GetRemoteUrl(Environment.CurrentDirectory);
        string? ghRepoSlug = null;
        if (remoteUrl is not null && remoteUrl.Contains("github.com/"))
        {
            // Extract org/repo from github.com/org/repo
            var ghPath = remoteUrl[(remoteUrl.IndexOf("github.com/") + "github.com/".Length)..];
            ghRepoSlug = ghPath.TrimEnd('/');
        }

        var installedAt = DateTimeOffset.UtcNow;

        void WriteAndTrack(SkillContentModel model)
        {
            var outputFile = WriteSkill(baseDir, model);
            SkillVersionService.RecordInstall(global, model, outputFile, settings.Global, installedAt);
        }

        var timesheets = SkillModelBuilder.BuildTimesheets(
            tenant, global, repoMapping, ghRepoSlug);
        WriteAndTrack(timesheets);
        WriteAndTrack(SkillModelBuilder.BuildTenantSetup());

        var accountingEnabled = global.IsFeatureEnabled(FeatureCatalog.Accounting);
        var developerEnabled = global.IsFeatureEnabled(FeatureCatalog.Developer);

        if (accountingEnabled)
        {
            WriteAndTrack(SkillModelBuilder.BuildAccounting(tenant));
            global.TouchFeatureVersion(FeatureCatalog.Accounting);
        }

        if (developerEnabled)
        {
            WriteAndTrack(SkillModelBuilder.BuildDeveloperDiagnostics());
            WriteAndTrack(SkillModelBuilder.BuildDeveloperTimesheetDiagnostics());
            WriteAndTrack(SkillModelBuilder.BuildDeveloperFinanceDiagnostics());
            WriteAndTrack(SkillModelBuilder.BuildEnvironmentCompare());
            global.TouchFeatureVersion(FeatureCatalog.Developer);
        }

        _config.SaveGlobalConfig(global);

        return 0;
    }

    /// <summary>
    /// Resolves where skills are written. With <paramref name="global"/>, skills go to the
    /// home-level agent directory for the target (e.g. <c>~/.claude</c>, <c>~/.codex</c>) so the
    /// agent actually discovers them; otherwise they go under the current directory.
    /// </summary>
    internal static string ResolveBaseDir(bool global, string target, string currentDirectory, string homeDirectory) =>
        global
            ? Path.Combine(homeDirectory, target)
            : Path.Combine(currentDirectory, target);

    /// <summary>
    /// No agent reads <c>~/skills</c>, so a bare home target is almost always a mistyped
    /// <c>--global</c> target rather than a deliberate choice.
    /// </summary>
    internal static string? RejectHomeRoot(string baseDir, string homeDirectory, bool force)
    {
        if (force || !IsSameDirectory(baseDir, homeDirectory))
            return null;

        return "Refusing to write skills to the home directory (~/skills), where no agent finds them. "
            + "Pass the agent directory instead, e.g. 'tp skills create .claude --global', or re-run with --force.";
    }

    private static bool IsSameDirectory(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static string WriteSkill(string baseDir, SkillContentModel model)
    {
        var relativePath = SkillRenderer.RelativePath(model.Name);
        var outputFile = Path.Combine(baseDir, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        File.WriteAllText(outputFile, SkillRenderer.Render(model));
        OutputHelper.WriteSuccess($"Skill file written to {outputFile}");
        return outputFile;
    }
}
