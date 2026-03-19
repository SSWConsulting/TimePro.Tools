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

        // ───────── Quick Reference ─────────
        sb.AppendLine("## Quick Reference");
        sb.AppendLine("```bash");
        sb.AppendLine("# View timesheets");
        sb.AppendLine("tp ts get --week --json          # This week");
        sb.AppendLine("tp ts get --week -1 --json       # Last week");
        sb.AppendLine("tp ts get 2026-03-12 --json      # Specific date");
        sb.AppendLine();
        sb.AppendLine("# Suggested timesheets — accept with notes and location in one step");
        sb.AppendLine("tp ts accept <ID> --notes \"Work done\" --location Home --yes");
        sb.AppendLine();
        sb.AppendLine("# Create timesheet (category auto-resolved from repo-mappings or recent timesheets)");
        sb.AppendLine("tp ts create --client <ID> --project <ID> --date 2026-03-12 \\");
        sb.AppendLine("  --start 09:00 --end 18:00 --less 60 --description \"Work done\" --yes");
        sb.AppendLine();
        sb.AppendLine("# Create timesheet with explicit category and iteration");
        sb.AppendLine("tp ts create --client SSW --project SSWTRN --iteration 3402 \\");
        sb.AppendLine("  --date 2026-03-12 --category TRAIN --billable W \\");
        sb.AppendLine("  --description \"MVP Summit\" --yes");
        sb.AppendLine();
        sb.AppendLine("# Update timesheet (partial — preserves all other fields)");
        sb.AppendLine("tp ts update <ID> --location Home --yes");
        sb.AppendLine("tp ts update <ID> --description \"Updated notes\" --yes");
        sb.AppendLine();
        sb.AppendLine("# Repo mappings");
        sb.AppendLine("tp map list                      # Show all mappings (includes Category column)");
        sb.AppendLine("tp map detect                    # Detect mapping for current directory");
        sb.AppendLine("tp map set <PATH> --client <ID> --project <ID> --category <CAT>");
        sb.AppendLine();
        sb.AppendLine("# Lookups");
        sb.AppendLine("tp cl search <QUERY> --json      # Find client ID");
        sb.AppendLine("tp proj list --client <ID> --json # Find project ID");
        sb.AppendLine("tp iter list --project <ID>       # List iterations (if any, one is required)");
        sb.AppendLine("tp rate get --client <ID> --json  # Check rate/expiry");
        sb.AppendLine("tp bk list --week --json          # CRM bookings");
        sb.AppendLine();
        sb.AppendLine("# Leave");
        sb.AppendLine("tp leave list --json");
        sb.AppendLine("tp leave create --start 2026-03-20 --end 2026-03-20 --type 1 --yes");
        sb.AppendLine("```");
        sb.AppendLine();

        // ───────── Workflow ─────────
        sb.AppendLine("## Workflow: Enter Timesheets for the Week");
        sb.AppendLine("1. **Pre-flight**: verify repo mapping is configured: `tp map detect`");
        sb.AppendLine("   If category is missing, run the \"Repo Mapping Setup\" workflow below first.");
        sb.AppendLine("2. Check existing: `tp ts get --week --json`");
        sb.AppendLine("3. Check CRM bookings: `tp bk list --week --json`");
        sb.AppendLine("4. **Decide per day: accept suggestions OR create new entries (never both)**");
        sb.AppendLine("   - Suggestions appear in `tp ts get --week --json` with `isSuggested: true`");
        sb.AppendLine("   - If a suggestion matches → **accept it with notes and location in one call**");
        sb.AppendLine("   - If no suggestion fits → create a new entry");
        sb.AppendLine("5. Gather context for each day:");
        sb.AppendLine("   - `git log --all --oneline --after=\"<date>T00:00:00\" --before=\"<date>T23:59:59\"` per day");
        if (ghRepoSlug is not null)
        {
            sb.AppendLine($"   - `gh pr list --repo {ghRepoSlug} --author @me --state merged --limit 20 --json number,title,mergedAt`");
            sb.AppendLine($"   - `gh pr list --repo {ghRepoSlug} --author @me --state open --json number,title,headRefName`");
        }
        else
        {
            sb.AppendLine("   - `gh pr list --author @me --state merged --limit 20 --json number,title,mergedAt`");
            sb.AppendLine("   - `gh pr list --author @me --state open --json number,title,headRefName`");
        }
        sb.AppendLine("6. Accept or create timesheets with GH issue/PR references in the description");
        sb.AppendLine("7. Verify: `tp ts get --week` to confirm all days are covered");
        sb.AppendLine();

        // ───────── Accepting Suggestions ─────────
        sb.AppendLine("### Accepting Suggestions (Preferred)");
        sb.AppendLine("Accept with description and location in a **single command**:");
        sb.AppendLine("```bash");
        sb.AppendLine("tp ts accept <SUGGESTED_ID> \\");
        sb.AppendLine("  --notes \"Fix: SSE streaming truncation — PR #74 · #4595\" \\");
        sb.AppendLine("  --location Home \\");
        sb.AppendLine("  --yes");
        sb.AppendLine("```");
        sb.AppendLine("The `--notes` and `--location` flags are applied directly during acceptance —");
        sb.AppendLine("no need to accept-then-update separately.");
        sb.AppendLine();
        sb.AppendLine("**Do NOT create a new entry if a suggestion for the same time range was already");
        sb.AppendLine("accepted.** The API will reject it as a duplicate.");
        sb.AppendLine();

        // ───────── Repo Mapping Setup ─────────
        sb.AppendLine("## Repo Mapping Setup");
        sb.AppendLine("Repo mappings let the CLI auto-resolve client, project, and category for a");
        sb.AppendLine("repository. **Run this once per project** so that `tp ts create` works without");
        sb.AppendLine("explicit `--category`.");
        sb.AppendLine();
        sb.AppendLine("### AI Discovery Workflow");
        sb.AppendLine("When a repo mapping is missing or has no category, discover the correct values:");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("# 1. Find the client ID (if unknown)");
        sb.AppendLine("tp cl search \"<company name>\" --json");
        sb.AppendLine();
        sb.AppendLine("# 2. Find the project ID");
        sb.AppendLine("tp proj list --client <CLIENT_ID> --json");
        sb.AppendLine();
        sb.AppendLine("# 3. Discover the correct category from recent timesheets");
        sb.AppendLine("tp query --client <CLIENT_ID> --project <PROJECT_ID> --from <date> --to <date> --json");
        sb.AppendLine("# → Look for categoryId in the results (e.g., \"WEBDEV\")");
        sb.AppendLine();
        sb.AppendLine("# 4. Check if the project requires iterations");
        sb.AppendLine("tp iter list --project <PROJECT_ID>");
        sb.AppendLine("# → Empty list = no iteration needed");
        sb.AppendLine("# → Non-empty = pick the matching iteration name/ID");
        sb.AppendLine();
        sb.AppendLine("# 5. Set the repo mapping with all discovered values");
        sb.AppendLine("tp map set <PATH> \\");
        sb.AppendLine("  --client <CLIENT_ID> --project <PROJECT_ID> \\");
        sb.AppendLine("  --project-name \"<name>\" \\");
        sb.AppendLine("  --remote \"github.com/<org>/<repo>\" \\");
        sb.AppendLine("  --category <CATEGORY_ID>");
        sb.AppendLine();
        sb.AppendLine("# 6. Verify");
        sb.AppendLine("tp map list");
        sb.AppendLine("tp map detect   # (from within the repo directory)");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("**Note**: `tp map set` preserves existing `--remote`, `--category`, and");
        sb.AppendLine("`--project-name` when omitted on update — so you can safely update just one");
        sb.AppendLine("field without clearing the others.");
        sb.AppendLine();

        // ───────── Categories ─────────
        sb.AppendLine("## Categories");
        sb.AppendLine("Most projects require a `CategoryID` in the API request. The CLI auto-resolves");
        sb.AppendLine("the category in this order:");
        sb.AppendLine("1. Explicit `--category <ID>` flag");
        sb.AppendLine("2. `categoryId` from `~/.config/timepro-cli/repo-mappings.json`");
        sb.AppendLine("3. Most recent timesheet for the same employee + client + project (past 14 days)");
        sb.AppendLine();
        sb.AppendLine("If auto-resolution fails, the API returns a 400 error with");
        sb.AppendLine("`\"CategoryID\": \"Please specify a category\"`.");
        sb.AppendLine();
        sb.AppendLine("Discover the correct category for any project:");
        sb.AppendLine("```bash");
        sb.AppendLine("tp query --client <ID> --from <date> --to <date> --json");
        sb.AppendLine("```");
        sb.AppendLine();

        // ───────── Description Format ─────────
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

        // ───────── Gathering Work Context ─────────
        sb.AppendLine("## Gathering Work Context");
        sb.AppendLine("Before creating timesheets, gather what was done:");
        sb.AppendLine("```bash");
        sb.AppendLine("# Git commits for a specific day (use --all for branch commits too)");
        sb.AppendLine("git log --all --oneline --after=\"2026-03-16T00:00:00\" --before=\"2026-03-16T23:59:59\"");
        sb.AppendLine();
        if (ghRepoSlug is not null)
        {
            sb.AppendLine("# Open issues assigned to me");
            sb.AppendLine($"gh issue list --repo {ghRepoSlug} --assignee @me --state open --json number,title");
            sb.AppendLine();
            sb.AppendLine("# My recently merged PRs (check dates to match to days)");
            sb.AppendLine($"gh pr list --repo {ghRepoSlug} --author @me --state merged --limit 20 --json number,title,mergedAt");
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
            sb.AppendLine("gh pr list --author @me --state merged --limit 20 --json number,title,mergedAt");
            sb.AppendLine();
            sb.AppendLine("# My open PRs (in-progress work)");
            sb.AppendLine("gh pr list --author @me --state open --json number,title,headRefName");
        }
        sb.AppendLine("```");
        sb.AppendLine();

        // ───────── Iterations ─────────
        sb.AppendLine("## Iterations (Sprints)");
        sb.AppendLine("Some projects require an iteration ID when creating timesheets.");
        sb.AppendLine();
        sb.AppendLine("**Detection workflow:**");
        sb.AppendLine("1. Run `tp iter list --project <PROJECT_ID>` to check");
        sb.AppendLine("2. If the list is non-empty, the project requires an iteration");
        sb.AppendLine("3. Pick the matching iteration and pass `--iteration <ID>` on create");
        sb.AppendLine();
        sb.AppendLine("**Known projects requiring iterations:**");
        sb.AppendLine("- `SSWTRN` (Training & Conferences) — iterations for each event/conference");
        sb.AppendLine();
        sb.AppendLine("The copy command (`tp ts copy`) automatically resolves iteration IDs from source timesheets.");
        sb.AppendLine();

        // ───────── Standard Day Format ─────────
        sb.AppendLine("## Standard Day Format");
        sb.AppendLine("- Default hours: `--start 09:00 --end 18:00 --less 60` (= 8h billable)");
        sb.AppendLine("- The `--less` flag takes **minutes** (60 = 1 hour break)");
        sb.AppendLine("- If `--end` is omitted it defaults to 17:00 (only 8h gross, no break)");
        sb.AppendLine("- Always use `--end 18:00 --less 60` for a standard 8h day with lunch");
        sb.AppendLine();

        // ───────── Important Notes ─────────
        sb.AppendLine("## Important Notes");
        sb.AppendLine("- Suggested timesheets improve accuracy stats — prefer accepting over creating new");
        sb.AppendLine("- `tp ts accept` supports `--notes` and `--location` directly — no need to accept then update");
        sb.AppendLine("- Check rate expiry with `tp rate get --client <ID>` before creating");
        sb.AppendLine("- Locked timesheets (invoiced) only allow location and description changes");
        sb.AppendLine("- Use `--yes` flag to skip confirmation prompts for batch operations");
        sb.AppendLine("- Always include GH issue/PR numbers in descriptions when available");
        sb.AppendLine("- The create command auto-resolves `--category` from repo-mappings or recent timesheets");
        sb.AppendLine("- Error messages include API validation details (e.g., missing category, duplicate entry)");
        sb.AppendLine();

        // ───────── Troubleshooting ─────────
        sb.AppendLine("## Troubleshooting");
        sb.AppendLine();
        sb.AppendLine("| Symptom | Cause | Fix |");
        sb.AppendLine("|---------|-------|-----|");
        sb.AppendLine("| 400 \"Please specify a category\" | API requires CategoryID | Run the \"Repo Mapping Setup\" workflow above to discover and set the category |");
        sb.AppendLine("| 400 \"duplicate of another entry\" | Timesheet already exists for that time slot | Use `tp ts update <ID>` instead of create |");
        sb.AppendLine("| `tp map detect` shows no category | Mapping was set without `--category` | Run `tp map set <PATH> --client <ID> --project <ID> --category <CAT>` |");
        sb.AppendLine();

        // ───────── Project Context ─────────
        if (repoMapping is not null || ghRepoSlug is not null)
        {
            sb.AppendLine("## Project Context");
            if (repoMapping is not null)
            {
                sb.AppendLine($"- Client: `{repoMapping.ClientId}`");
                sb.AppendLine($"- Project: `{repoMapping.ProjectId}`");
                if (!string.IsNullOrEmpty(repoMapping.ProjectName))
                    sb.AppendLine($"- Project Name: {repoMapping.ProjectName}");
                if (!string.IsNullOrEmpty(repoMapping.CategoryId))
                    sb.AppendLine($"- Category: `{repoMapping.CategoryId}`");
            }
            if (ghRepoSlug is not null)
                sb.AppendLine($"- GitHub: `{ghRepoSlug}`");
            sb.AppendLine();
        }

        // ───────── Configuration ─────────
        if (tenant is not null)
        {
            sb.AppendLine("## Current Configuration");
            sb.AppendLine($"- Tenant: `{tenant.TenantId}`");
            sb.AppendLine($"- Employee: `{tenant.EmployeeId}`");
            sb.AppendLine($"- API: `{tenant.ApiUrl}`");
            sb.AppendLine("- Repo mappings: `~/.config/timepro-cli/repo-mappings.json`");
            sb.AppendLine();
        }

        // ───────── Location Defaults ─────────
        sb.AppendLine("## Location Defaults");
        sb.AppendLine("Valid location IDs: `SSW` (At My Company), `Home` (At Home), `Client` (At Client), `Travel`, `Other`");
        sb.AppendLine();
        sb.AppendLine("Common aliases are resolved automatically: Office→SSW, WFH→Home, Onsite→Client");
        sb.AppendLine();
        if (global.WfhDays.Count > 0)
        {
            sb.AppendLine($"- WFH days: {string.Join(", ", global.WfhDays)}");
            sb.AppendLine($"- Default: {global.DefaultLocation}");
            sb.AppendLine("- Location is auto-applied when creating timesheets based on the day");
        }
        else
        {
            sb.AppendLine($"- Default: {global.DefaultLocation}");
            sb.AppendLine("- No WFH days configured — use `tp loc set Home --day Mon,Tue` to set");
        }
        sb.AppendLine();

        return sb.ToString();
    }
}
