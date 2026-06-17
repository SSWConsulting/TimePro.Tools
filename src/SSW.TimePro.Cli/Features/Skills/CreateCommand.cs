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

        [CommandOption("--accounting")]
        [Description("Also write the accountant CLI skill (timepro-accounting-cli.md) alongside the timesheets skill. Opt-in; does not touch any existing HTTP-based timepro-accounting skill.")]
        public bool Accounting { get; set; }
    }

    public CreateCommand(IConfigService config) => _config = config;

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
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

        if (settings.Accounting)
        {
            var accountingFile = Path.Combine(outputDir, "timepro-accounting-cli.md");
            File.WriteAllText(accountingFile, GenerateAccountingSkillContent(tenant));
            OutputHelper.WriteSuccess($"Skill file written to {accountingFile}");
        }

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
        sb.AppendLine("tp ts create --client NWIND --project 1I776Q --iteration 3402 \\");
        sb.AppendLine("  --date 2026-03-12 --category WEBDEV --billable B \\");
        sb.AppendLine("  --description \"Northwind checkout API\" --yes");
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
        sb.AppendLine("tp leave list --filter UPCOMING --json");
        sb.AppendLine("tp leave create --start 2026-03-30 --end 2026-03-30 --type 1 \\");
        sb.AppendLine("  --note \"Reason\" --approved-by \"approver@northwind.example\" \\");
        sb.AppendLine("  --cc \"notify1@northwind.example,notify2@northwind.example\" --yes");
        sb.AppendLine("tp leave cancel <ID> --reason \"Plans changed\" --yes");
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
        sb.AppendLine("- `1I776Q` (Northwind Traders) — iterations for each sample milestone");
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

        // ───────── Daily Scrum ─────────
        sb.AppendLine("## Daily Scrum (`tp scrum`)");
        sb.AppendLine("Generates an SSW-format daily scrum email from timesheets, CRM bookings,");
        sb.AppendLine("repo mappings and GitHub activity (via local `gh` CLI).");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("tp scrum                            # Print styled scrum for today");
        sb.AppendLine("tp scrum --json                     # Structured output for agents");
        sb.AppendLine("tp scrum --html                     # HTML body (for emailing / piping)");
        sb.AppendLine("tp scrum --date 2026-04-09          # Generate for a specific date");
        sb.AppendLine("tp scrum --project 1I776Q           # Filter to one project");
        sb.AppendLine("tp scrum --internal                 # Force internal daily scrum format");
        sb.AppendLine("tp scrum --external                 # Force client-facing format");
        sb.AppendLine("tp scrum -i                         # Interactive: r/m/p to copy rich/markdown/plain");
        sb.AppendLine("tp scrum --copy --format rich       # Render & copy (rich | markdown | plain)");
        sb.AppendLine("tp scrum --set-trello-url URL       # Save Trello URL for internal block");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("**How it works:**");
        sb.AppendLine("- **Today** = open PRs by me in the project's issues repo.");
        sb.AppendLine("- **Yesterday** = the last *working day where I logged the same project*, not literal");
        sb.AppendLine("  yesterday. Bleeds back up to 7 days to surface PRs merged between visits.");
        sb.AppendLine("- **Internal vs external** = classified from today's CRM bookings. Any non-SSW client");
        sb.AppendLine("  booking or timesheet → external format. All-SSW → internal with the extra block.");
        sb.AppendLine("- **Issues repo**: if a project's issues live in a different repo than the code, set");
        sb.AppendLine("  it via `tp map set <path> --client <ID> --project <ID> --issues-repo org/repo`.");
        sb.AppendLine("- Raw timesheet notes are exposed in `--json` as `yesterdayNotes` / `todayNotes` for");
        sb.AppendLine("  agents to use as enrichment context — they are intentionally NOT rendered as bullets.");
        sb.AppendLine();
        sb.AppendLine("**Augmenting with extra context:**");
        sb.AppendLine("When an agent has more information than `tp scrum` can gather on its own (e.g., a");
        sb.AppendLine("longer description of what was done, blockers the user mentioned in chat, or extra");
        sb.AppendLine("PBIs to add):");
        sb.AppendLine("1. Run `tp scrum --json` to get the structured baseline.");
        sb.AppendLine("2. Show the user the generated scrum and ask what to add/edit.");
        sb.AppendLine("3. Assemble the final email text yourself (plain markdown is fine) and copy it to");
        sb.AppendLine("   the clipboard via `pbcopy` / `wl-copy` — do not try to round-trip through `tp scrum`.");
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

    private static string GenerateAccountingSkillContent(TenantConfig? tenant)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("---");
        sb.AppendLine("name: timepro-accounting-cli");
        sb.AppendLine("description: Explore SSW TimePro financial data via the `tp` CLI (read-only) — invoices with line items, timesheets billed on invoices, credit notes, receipts, sale products, client rates, aged debtors, unbilled time, recurring invoices, prepaid drawdown summaries and PDFs. Use when the user asks accountant-style questions. For raw HTTP/curl access (when `tp` isn't installed), use the `timepro-accounting` skill instead.");
        sb.AppendLine("user_invocable: true");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("# TimePro Accounting (CLI)");
        sb.AppendLine();
        sb.AppendLine("Accountant-facing read-only access to SSW TimePro via the `tp` CLI.");
        sb.AppendLine("Optimised for exploration and reconciliation — pipe `--json` output into `jq`");
        sb.AppendLine("or Python to calculate totals, compare against Xero, or audit historical data.");
        sb.AppendLine();
        sb.AppendLine("## Setup");
        sb.AppendLine();
        sb.AppendLine("This skill reuses the tenant config already configured for `tp`. If `tp login`");
        sb.AppendLine("has been run (check `~/.config/timepro-cli/tenants/`), nothing else is needed.");
        sb.AppendLine("Otherwise run `tp login --tenant <id>` first.");
        sb.AppendLine();
        sb.AppendLine("> **Note**: The sibling skill `timepro-accounting` (without the `-cli` suffix)");
        sb.AppendLine("> hits the same API via raw `curl`. Keep it for environments where `tp` isn't");
        sb.AppendLine("> installed, or when demonstrating the raw HTTP shape. This CLI skill is the");
        sb.AppendLine("> preferred day-to-day option.");
        sb.AppendLine();
        sb.AppendLine("## Quick reference");
        sb.AppendLine();
        sb.AppendLine("All commands accept `--json` for machine output.");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("# Invoices");
        sb.AppendLine("tp invoice list --limit 50 --json");
        sb.AppendLine("tp invoice list --query Northwind --field DateCreated --dir desc --json");
        sb.AppendLine("tp invoice get <INV>");
        sb.AppendLine("tp invoice lines <INV>         # line items (products billed)");
        sb.AppendLine("tp invoice timesheets <INV>    # timesheets allocated to the invoice");
        sb.AppendLine("tp invoice timesheets <INV> --writeoff   # written-off timesheets");
        sb.AppendLine("tp invoice receipts <INV>      # payments against the invoice");
        sb.AppendLine();
        sb.AppendLine("# Receipts (money in)");
        sb.AppendLine("tp receipt list --limit 500 --field PaymentDate --dir desc --json");
        sb.AppendLine("tp receipt get <RCPT>");
        sb.AppendLine("tp receipt outstanding <CLIENT_ID>   # aged debtors");
        sb.AppendLine();
        sb.AppendLine("# Credit notes, products, discounts");
        sb.AppendLine("tp creditnote list --client <CLIENT_ID> --json");
        sb.AppendLine("tp product list --json                  # products");
        sb.AppendLine("tp product list --prepaid --json        # prepaid SKUs only");
        sb.AppendLine("tp product get <PROD_ID>");
        sb.AppendLine("tp product discounts --client <CLIENT_ID>");
        sb.AppendLine();
        sb.AppendLine("# Rates, unbilled, outstanding");
        sb.AppendLine("tp rate list --client <CLIENT_ID> --show-expired --json");
        sb.AppendLine("tp client outstanding --json            # clients with unbilled time");
        sb.AppendLine("tp unbilled list --client <CLIENT_ID> --json");
        sb.AppendLine();
        sb.AppendLine("# Recurring invoice templates");
        sb.AppendLine("tp recurring list --client <CLIENT_ID> --json");
        sb.AppendLine("tp recurring get <ID>");
        sb.AppendLine();
        sb.AppendLine("# Prepaid drawdown");
        sb.AppendLine("tp prepaid summary <INVOICE_ID> --json   # structured original/drawn-down/credited/remaining totals");
        sb.AppendLine("tp prepaid status <INVOICE_ID> --output /tmp/prepaid.pdf");
        sb.AppendLine();
        sb.AppendLine("# Cross-employee/client/project timesheet query (powerful for audits)");
        sb.AppendLine("tp query --from 2026-03-01 --to 2026-03-31 --json");
        sb.AppendLine("tp query --from 2026-03-01 --to 2026-03-31 --client <CID> --json");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Common workflows");
        sb.AppendLine();
        sb.AppendLine("### 1. Drill into an invoice (header + lines + timesheets + receipts)");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("INV=142");
        sb.AppendLine("tp invoice get $INV --json          > /tmp/inv_header.json");
        sb.AppendLine("tp invoice lines $INV --json        > /tmp/inv_lines.json");
        sb.AppendLine("tp invoice timesheets $INV --json   > /tmp/inv_ts.json");
        sb.AppendLine("tp invoice receipts $INV --json     > /tmp/inv_receipts.json");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Reconcile: sum of line `sellTotal` = invoice header `subTotal` (ex-GST);");
        sb.AppendLine("`subTotal + salesTaxAmt` = header `sellTotal` (inc-GST); sum of");
        sb.AppendLine("`abs(paidTotal)` on receipts = header `paidAmt`; header `osAmt` = total - paid.");
        sb.AppendLine();
        sb.AppendLine("### 2. Monthly invoiced sales");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("tp invoice list --limit 500 --field DateCreated --dir desc --json \\");
        sb.AppendLine("  | jq '[.data[] | select(.dateCreated | startswith(\"2026-03\"))] | {count: length, total: map(.sellTotal) | add}'");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### 3. Monthly receipts (money in)");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("tp receipt list --limit 500 --field PaymentDate --dir desc --json \\");
        sb.AppendLine("  | jq '[.data[] | select(.paymentDate | startswith(\"2026-03\"))]");
        sb.AppendLine("        | {count: length, total: (map(.paidTotal // .paid) | add | fabs)}'");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### 4. Aged debtors for one client");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("tp receipt outstanding NWIND         # human table");
        sb.AppendLine("tp receipt outstanding NWIND --json  # for further processing");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### 5. Unbilled revenue in the pipeline");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("tp client outstanding --json          # list all clients with unbilled time");
        sb.AppendLine("tp unbilled list --client NWIND --json");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### 6. Credit-note audit");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("tp creditnote list --client NWIND --json \\");
        sb.AppendLine("  | jq 'sort_by(.creditNoteDate) | map({id, date: .creditNoteDate, amount, note})'");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Xero cross-check (via MCP composition)");
        sb.AppendLine();
        sb.AppendLine("With both `tp mcp` and a Xero MCP server connected in Claude Code, an agent can");
        sb.AppendLine("call tools from both servers in a single session. No bespoke feature is needed —");
        sb.AppendLine("this falls out of MCP composition.");
        sb.AppendLine();
        sb.AppendLine("Example prompts:");
        sb.AppendLine();
        sb.AppendLine("- *\"Reconcile TimePro paid receipts for March 2026 against Xero bank receipts.");
        sb.AppendLine("  Call `ListPaidReceipts` in TimePro for March; fetch the equivalent Xero");
        sb.AppendLine("  bank rec period; flag any receipts in one system but not the other, or amount");
        sb.AppendLine("  mismatches (>$0.01) by invoice reference.\"*");
        sb.AppendLine();
        sb.AppendLine("- *\"For prepaid invoice 142, compare `tp prepaid summary 142 --json`");
        sb.AppendLine("  `remaining.exGst` against Xero manual journals tagged with that invoice.");
        sb.AppendLine("  Sanity-check that drawdown entries in TimePro net to the Xero journal totals.\"*");
        sb.AppendLine();
        sb.AppendLine("- *\"List TimePro invoices where `externalSyncStatus != 1`; for each, check whether");
        sb.AppendLine("  a matching invoice exists in Xero. Report what's missing or mismatched.\"*");
        sb.AppendLine();
        sb.AppendLine("Start `tp mcp` from Claude Code's MCP config (stdio transport) the same way the");
        sb.AppendLine("timesheets tools are already wired up — the accounting tools live in the same");
        sb.AppendLine("server and will appear in the tool list automatically.");
        sb.AppendLine();
        sb.AppendLine("## Data gotchas");
        sb.AppendLine();
        sb.AppendLine("- **Receipt sign convention**: `paidTotal` is **negative** for incoming payments");
        sb.AppendLine("  (the receipt type's `typeSign` encodes direction). Report positive sales with");
        sb.AppendLine("  `abs()`. `tp receipt list` and `tp invoice receipts` already show absolute");
        sb.AppendLine("  values in the default table view; JSON output preserves the raw sign.");
        sb.AppendLine("- **Date field choice matters**:");
        sb.AppendLine("  - Receipts: `paymentDate` (money in) vs `dateCreated` (entered).");
        sb.AppendLine("  - Invoices: `dateCreated` (raised) vs `dateStart`/`dateEnd` (period covered).");
        sb.AppendLine("  - SQL reports sometimes join receipts to invoices and filter on the invoice");
        sb.AppendLine("    date, which excludes March payments against pre-March invoices. If a SQL");
        sb.AppendLine("    total disagrees, check which convention it uses.");
        sb.AppendLine("- **Paged list endpoints ignore `dateFrom` / `dateTo`** (historical quirk). Always");
        sb.AppendLine("  fetch by sort + page and filter client-side with `jq`.");
        sb.AppendLine("- **Paging**: `tp invoice list` and `tp receipt list` default to limit 50/100 —");
        sb.AppendLine("  raise to 500 or walk `--skip` when covering full months.");
        sb.AppendLine("- **GST**: Invoice header `subTotal` is GST-**exclusive**, `salesTaxAmt` is");
        sb.AppendLine("  the GST component, and `sellTotal` is GST-**inclusive**. Invoice line");
        sb.AppendLine("  `sellAmt` and `sellTotal` are GST-exclusive. Timesheet `sellTotal`,");
        sb.AppendLine("  `billableAmount`, and `amount` are treated as GST-exclusive for");
        sb.AppendLine("  reconciliation; use `salesTaxAmt` / `salesTaxPct` when present, or the");
        sb.AppendLine("  invoice header rate.");
        sb.AppendLine("- **Credit notes** appear as negative-signed adjustments. Decide whether to net");
        sb.AppendLine("  them off sales or report separately before presenting a number.");
        sb.AppendLine("- **Write-offs**: Timesheets allocated to an invoice may be written off. Pass");
        sb.AppendLine("  `--writeoff` to `tp invoice timesheets` to see them; include both allocated");
        sb.AppendLine("  and writeoff when auditing total hours worked on a job.");
        sb.AppendLine();
        sb.AppendLine("## Output etiquette");
        sb.AppendLine();
        sb.AppendLine("When presenting numbers to the user:");
        sb.AppendLine("- State the **date field** used (`paymentDate` / `dateCreated`).");
        sb.AppendLine("- State whether **GST** is included or excluded.");
        sb.AppendLine("- State whether **credit notes** are netted off or shown separately.");
        sb.AppendLine("- Always show **record count** alongside totals — single-number answers hide");
        sb.AppendLine("  filter mistakes.");
        sb.AppendLine();

        if (tenant is not null)
        {
            sb.AppendLine("## Current configuration");
            sb.AppendLine($"- Tenant: `{tenant.TenantId}`");
            sb.AppendLine($"- API: `{tenant.ApiUrl}`");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
