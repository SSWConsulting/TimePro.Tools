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

        var content = GenerateSkillContent(tenant, global, repoMapping);
        File.WriteAllText(outputFile, content);

        OutputHelper.WriteSuccess($"Skill file written to {outputFile}");
        return 0;
    }

    private static string GenerateSkillContent(
        TenantConfig? tenant, GlobalConfig global, RepoMappingEntry? repoMapping)
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
        sb.AppendLine("5. For remaining days, create timesheets using git history for context");
        sb.AppendLine("6. Verify: `tp ts get --week` to confirm all days are covered");
        sb.AppendLine();

        sb.AppendLine("## Important Notes");
        sb.AppendLine("- Suggested timesheets improve accuracy stats — prefer accepting them over creating new");
        sb.AppendLine("- Check rate expiry with `tp rate get --client <ID>` before creating");
        sb.AppendLine("- Locked timesheets (invoiced) only allow location and description changes");
        sb.AppendLine("- Use `--yes` flag to skip confirmation prompts for batch operations");
        sb.AppendLine();

        if (repoMapping is not null)
        {
            sb.AppendLine("## Project Context (from repo mapping)");
            sb.AppendLine($"- Client: `{repoMapping.ClientId}`");
            sb.AppendLine($"- Project: `{repoMapping.ProjectId}`");
            if (!string.IsNullOrEmpty(repoMapping.ProjectName))
                sb.AppendLine($"- Project Name: {repoMapping.ProjectName}");
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
