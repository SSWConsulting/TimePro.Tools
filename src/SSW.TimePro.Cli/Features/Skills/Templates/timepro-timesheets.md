<!--
Template source for a generated TimePro agent skill.
SkillRenderer adds YAML frontmatter when tp skills create writes SKILL.md.
Do not install this file directly as an agent skill.
-->

# TimePro Timesheets

## Setup
The `tp` CLI manages SSW TimePro timesheets. Always use `--json` for machine-readable output when parsing results programmatically.

For tenant switching or per-command tenant/environment overrides, use the generated `timepro-tenant-setup` skill.

## Start here: pick the project
Begin by surfacing the user's likely projects rather than guessing:

```bash
tp project recent --json    # ranked: booked > suggested > recent > leave
```

This returns a small candidate list with repo paths and flags any booked clients that have no repo mapping yet. Use it to choose the project before filling timesheets. If more than one project is plausible, ask the user which one to use before continuing.

## Quick Reference
```bash
# Pick the project to work on
tp project recent --json

# View timesheets
tp ts get --week --json
tp ts get --week -1 --json
tp ts get 2026-03-12 --json

# Suggested timesheets: accept with notes and location in one step
tp ts accept <ID> --notes "Work done" --location Home --yes

# Create timesheet
tp ts create --client <ID> --project <ID> --date 2026-03-12 \
  --start 09:00 --end 18:00 --less 60 --description "Work done" --yes

# Create timesheet with explicit category and iteration
tp ts create --client NWIND --project 1I776Q --iteration 3402 \
  --date 2026-03-12 --category WEBDEV --billable B \
  --description "Northwind checkout API" --yes

# Update timesheet
tp ts update <ID> --location Home --yes
tp ts update <ID> --description "Updated notes" --yes

# Repo mappings
tp map list
tp map detect
tp map set <PATH> --client <ID> --project <ID> --category <CAT>

# Lookups
tp cl search <QUERY> --json
tp proj list --client <ID> --json
tp iter list --project <ID>
tp rate get --client <ID> --json
tp bk list --week --json

# Leave
tp leave list --filter UPCOMING --json
tp leave create --start 2026-03-30 --end 2026-03-30 --type 1 \
  --note "Reason" --approved-by "approver@northwind.example" \
  --cc "notify1@northwind.example,notify2@northwind.example" --yes
# Leave create uses --timezone first, then the TimePro profile timezone, then the machine timezone.
tp leave cancel <ID> --reason "Plans changed" --yes
```

## Workflow: Enter Timesheets for the Week
1. Pick the project: `tp project recent --json`.
2. Verify repo mapping: `tp map detect`. If category is missing, run the repo mapping setup workflow below.
3. Check existing entries: `tp ts get --week --json`.
4. Check CRM bookings: `tp bk list --week --json`.
5. Decide per day: accept suggestions or create new entries, never both for the same time range.
6. Gather context for each day from git and GitHub.
7. Accept or create timesheets with issue/PR references in the description.
8. Verify with `tp ts get --week`.

### Accepting Suggestions
Accept with description and location in a single command:

```bash
tp ts accept <SUGGESTED_ID> \
  --notes "Fix: token expiry not handled on refresh - PR #42" \
  --location Home \
  --yes
```

Do not create a new entry if a suggestion for the same time range was already accepted. The API will reject it as a duplicate.

## Repo Mapping Setup
Repo mappings let the CLI auto-resolve client, project, and category for a repository.

```bash
# 1. Find the client ID
tp cl search "<company name>" --json

# 2. Find the project ID
tp proj list --client <CLIENT_ID> --json

# 3. Discover the correct category from recent timesheets
tp query --client <CLIENT_ID> --project <PROJECT_ID> --from <date> --to <date> --json

# 4. Check whether the project requires iterations
tp iter list --project <PROJECT_ID>

# 5. Set the repo mapping
tp map set <PATH> \
  --client <CLIENT_ID> --project <PROJECT_ID> \
  --project-name "<name>" \
  --remote "github.com/<org>/<repo>" \
  --category <CATEGORY_ID>

# 6. Verify
tp map list
tp map detect
```

`tp map set` preserves existing `--remote`, `--category`, and `--project-name` when omitted on update.

## Categories
Most projects require a `CategoryID`. The CLI auto-resolves the category in this order:
1. Explicit `--category <ID>` flag
2. `categoryId` from `~/.config/timepro-cli/repo-mappings.json`
3. Most recent timesheet for the same employee, client, and project

If auto-resolution fails, the API returns a 400 error with `"CategoryID": "Please specify a category"`.

## Description Format
Timesheet descriptions should reference PRs and issues. Format each line as:

```text
<Action>: <Short summary> - PR #<N> - #<IssueN>
```

Examples:

```text
Fix: Token expiry not handled on refresh - PR #42 - #108
Improve checkout API diagnostics - PR #74
Code review and standup
```

## Gathering Work Context
Before creating timesheets, gather what was done:

```bash
# Git commits for a specific day
git log --all --oneline --after="2026-03-16T00:00:00" --before="2026-03-16T23:59:59"

{{GITHUB_CONTEXT_COMMANDS}}
```

## Iterations
Some projects require an iteration ID when creating timesheets.

1. Run `tp iter list --project <PROJECT_ID>`.
2. If the list is non-empty, pick the matching iteration.
3. Pass `--iteration <ID>` on create.

Known sample: `1I776Q` (Northwind Traders) uses iterations for each sample milestone.

## Standard Day Format
- Default hours: `--start 09:00 --end 18:00 --less 60` (= 8h billable).
- The `--less` flag takes minutes.
- If `--end` is omitted it defaults to 17:00, which is only 8h gross with no break.

## Daily Scrum (`tp scrum`)
```bash
tp scrum
tp scrum --json
tp scrum --html
tp scrum --date 2026-04-09
tp scrum --project 1I776Q
tp scrum --internal
tp scrum --external
tp scrum -i
tp scrum --copy --format rich
tp scrum --set-trello-url URL
```

Use `tp scrum --json` as the structured baseline, then assemble any user-requested additions outside the command. Do not try to round-trip custom prose back through `tp scrum`.

## Important Notes
- Suggested timesheets improve accuracy stats; prefer accepting over creating new entries.
- `tp ts accept` supports `--notes` and `--location` directly.
- Check rate expiry with `tp rate get --client <ID>` before creating.
- Locked timesheets only allow location and description changes.
- Use `--yes` only when the intended write is clear.
- Always include issue/PR numbers in descriptions when available.
- Error messages include API validation details.

## Troubleshooting
| Symptom | Cause | Fix |
|---------|-------|-----|
| 400 "Please specify a category" | API requires CategoryID | Discover and set the category in repo mapping |
| 400 "duplicate of another entry" | Timesheet already exists for that time slot | Use `tp ts update <ID>` instead of create |
| `tp map detect` shows no category | Mapping was set without `--category` | Run `tp map set <PATH> --client <ID> --project <ID> --category <CAT>` |

{{PROJECT_CONTEXT}}
{{CURRENT_CONFIGURATION}}
{{LOCATION_DEFAULTS}}
