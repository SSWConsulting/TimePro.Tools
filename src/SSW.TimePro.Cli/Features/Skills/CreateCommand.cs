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
    }

    public CreateCommand(IConfigService config) => _config = config;

    public override int Execute(CommandContext context, Settings settings)
    {
        var tenant = _config.LoadActiveTenantConfig();
        var global = _config.LoadGlobalConfig();
        var mappings = _config.LoadRepoMappings();

        // Determine output path
        string outputDir;
        if (settings.Global)
        {
            outputDir = Path.Combine(ConfigPaths.Root, "skills");
        }
        else
        {
            outputDir = Path.Combine(Environment.CurrentDirectory, settings.Target, "skills");
        }

        Directory.CreateDirectory(outputDir);
        var outputFile = Path.Combine(outputDir, "timepro-timesheets.md");

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

        var content = GenerateSkillContent(tenant, global, repoMapping, ghRepoSlug);
        File.WriteAllText(outputFile, content);

        OutputHelper.WriteSuccess($"Skill file written to {outputFile}");
        return 0;
    }

    private static string GenerateSkillContent(
        TenantConfig? tenant, GlobalConfig global, RepoMappingEntry? repoMapping,
        string? ghRepoSlug)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# TimePro Timesheets - CLI Skill");
        sb.AppendLine();
        sb.AppendLine("## Setup");
        sb.AppendLine("The `tp` CLI manages SSW TimePro timesheets. Always use `--json` for");
        sb.AppendLine("machine-readable output when parsing results programmatically.");
        sb.AppendLine();

        sb.AppendLine("## Quick Reference");
        sb.AppendLine("```bash");
        sb.AppendLine("# View timesheets");
        sb.AppendLine("tp ts get --week --json          # This week");
        sb.AppendLine("tp ts get --week -1 --json       # Last week");
        sb.AppendLine("tp ts get 2026-03-12 --json      # Specific date");
        sb.AppendLine();
        sb.AppendLine("# Suggested timesheets");
        sb.AppendLine("tp ts suggest --week --json       # View suggestions");
        sb.AppendLine("tp ts accept <ID> --yes           # Accept a suggestion");
        sb.AppendLine();
        sb.AppendLine("# Create timesheet");
        sb.AppendLine("tp ts create --client <ID> --project <ID> --date 2026-03-12 \\");
        sb.AppendLine("  --start 09:00 --end 17:00 --description \"Work done\" --yes");
        sb.AppendLine();
        sb.AppendLine("# Update timesheet (partial)");
        sb.AppendLine("tp ts update <ID> --location Home --yes");
        sb.AppendLine("tp ts update <ID> --description \"Updated notes\" --yes");
        sb.AppendLine();
        sb.AppendLine("# Lookups");
        sb.AppendLine("tp cl search <QUERY> --json      # Find client ID");
        sb.AppendLine("tp proj list --client <ID> --json # Find project ID");
        sb.AppendLine("tp rate get --client <ID> --json  # Check rate/expiry");
        sb.AppendLine("tp bk list --week --json          # CRM bookings");
        sb.AppendLine();
        sb.AppendLine("# Leave");
        sb.AppendLine("tp leave list --json");
        sb.AppendLine("tp leave create --start 2026-03-20 --end 2026-03-20 --type 1 --yes");
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## Workflow: Enter Timesheets for the Week");
        sb.AppendLine("1. Check existing: `tp ts get --week --json`");
        sb.AppendLine("2. Check CRM bookings: `tp bk list --week --json`");
        sb.AppendLine("3. Check suggested timesheets: `tp ts suggest --week --json`");
        sb.AppendLine("4. Accept suggested timesheets that match work done");
        sb.AppendLine("5. Gather context for remaining days:");
        sb.AppendLine("   - `git log --since=Monday --until=Friday --oneline` for commit history");
        if (ghRepoSlug is not null)
        {
            sb.AppendLine($"   - `gh issue list --repo {ghRepoSlug} --assignee @me --state open --json number,title`");
            sb.AppendLine($"   - `gh pr list --repo {ghRepoSlug} --author @me --state merged --json number,title,mergedAt`");
        }
        else
        {
            sb.AppendLine("   - `gh issue list --assignee @me --state open --json number,title`");
            sb.AppendLine("   - `gh pr list --author @me --state merged --json number,title,mergedAt`");
        }
        sb.AppendLine("6. Create timesheets with GH issue/PR references in the description");
        sb.AppendLine("7. Verify: `tp ts get --week` to confirm all days are covered");
        sb.AppendLine();

        sb.AppendLine("## Description Format");
        sb.AppendLine("Timesheet descriptions should reference PRs and issues. Format each line as:");
        sb.AppendLine("```");
        sb.AppendLine("<Action>: <Short summary> — PR #<N> · #<IssueN>");
        sb.AppendLine("```");
        sb.AppendLine("Examples:");
        sb.AppendLine("```");
        sb.AppendLine("Fix: Token expiry not handled on refresh — PR #1545 · #1522");
        sb.AppendLine("Improved kiosk leaderboard layout and QR code display — PR #1540 · #1442");
        sb.AppendLine("Code review and standup");
        sb.AppendLine("```");
        sb.AppendLine("Multiple lines are fine (one per PR or activity). Keep each line concise.");
        sb.AppendLine();

        sb.AppendLine("## Gathering Work Context");
        sb.AppendLine("Before creating timesheets, gather what was done:");
        sb.AppendLine("```bash");
        sb.AppendLine("# Git commits for a specific day");
        sb.AppendLine("git log --oneline --after=\"2026-03-16T00:00:00\" --before=\"2026-03-16T23:59:59\"");
        sb.AppendLine();
        if (ghRepoSlug is not null)
        {
            sb.AppendLine("# Open issues assigned to me");
            sb.AppendLine($"gh issue list --repo {ghRepoSlug} --assignee @me --state open --json number,title");
            sb.AppendLine();
            sb.AppendLine("# My recently merged PRs (check dates to match to days)");
            sb.AppendLine($"gh pr list --repo {ghRepoSlug} --author @me --state merged --limit 10 --json number,title,mergedAt");
            sb.AppendLine();
            sb.AppendLine("# My open PRs (in-progress work)");
            sb.AppendLine($"gh pr list --repo {ghRepoSlug} --author @me --state open --json number,title,headRefName");
        }
        else
        {
            sb.AppendLine("# Open issues assigned to me (run from repo root)");
            sb.AppendLine("gh issue list --assignee @me --state open --json number,title");
            sb.AppendLine();
            sb.AppendLine("# My recently merged PRs");
            sb.AppendLine("gh pr list --author @me --state merged --limit 10 --json number,title,mergedAt");
        }
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## Important Notes");
        sb.AppendLine("- Suggested timesheets improve accuracy stats — prefer accepting them over creating new");
        sb.AppendLine("- Check rate expiry with `tp rate get --client <ID>` before creating");
        sb.AppendLine("- Locked timesheets (invoiced) only allow location and description changes");
        sb.AppendLine("- Use `--yes` flag to skip confirmation prompts for batch operations");
        sb.AppendLine("- Always include GH issue/PR numbers in descriptions when available");
        sb.AppendLine();

        if (repoMapping is not null || ghRepoSlug is not null)
        {
            sb.AppendLine("## Project Context");
            if (repoMapping is not null)
            {
                sb.AppendLine($"- Client: `{repoMapping.ClientId}`");
                sb.AppendLine($"- Project: `{repoMapping.ProjectId}`");
                if (!string.IsNullOrEmpty(repoMapping.ProjectName))
                    sb.AppendLine($"- Project Name: {repoMapping.ProjectName}");
            }
            if (ghRepoSlug is not null)
                sb.AppendLine($"- GitHub: `{ghRepoSlug}`");
            sb.AppendLine();
        }

        if (tenant is not null)
        {
            sb.AppendLine("## Current Configuration");
            sb.AppendLine($"- Tenant: `{tenant.TenantId}`");
            sb.AppendLine($"- Employee: `{tenant.EmployeeId}`");
            sb.AppendLine($"- API: `{tenant.ApiUrl}`");
            sb.AppendLine();
        }

        if (global.WfhDays.Count > 0)
        {
            sb.AppendLine("## Location Defaults");
            sb.AppendLine($"- WFH days: {string.Join(", ", global.WfhDays)}");
            sb.AppendLine($"- Default: {global.DefaultLocation}");
            sb.AppendLine("- Location is auto-applied when creating timesheets based on the day");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
