# Diagnostic Guides

Diagnostic guides are the lightweight routing layer behind `tp accounting guide`
and `tp dev guide`. They help agents choose the right evidence pack before
pulling broad TimePro data.

New diagnostic guides are welcome. Prefer adding a guide entry and Markdown
recipe before adding a new CLI command. Add code only when the workflow needs a
stable, reusable API surface or non-trivial behavior that must be tested as
product logic.

Broad, important workflows still belong in installed generated skills such as
`timepro-accounting-cli`, `timepro-dev-diagnostics`,
`timepro-dev-timesheet-diagnostics`, and `timepro-dev-finance-diagnostics`.
Highly specific diagnostics belong in `guides/accounting/` or `guides/dev/`.

## Runtime Updates

`tp accounting guide` and `tp dev guide` fetch guide indexes and Markdown files
from GitHub raw content and cache them under
`~/.config/timepro-cli/guides-cache/`. The default cache time is 5 minutes.

Users can change the cache time in `~/.config/timepro-cli/config.json`:

```json
{
  "guides": {
    "cacheMinutes": 5,
    "repositoryUrl": "https://github.com/SSWConsulting/TimePro.Tools",
    "branch": "main"
  }
}
```

Without `--refresh`, guide commands still try GitHub when the cache is missing
or older than `cacheMinutes`. Set `cacheMinutes` to `0` to refresh every time.
Use `--refresh` or `--force-refresh` on a guide command to bypass the cache for
one run.

For local PR testing, set `guides.branch` to the branch that contains the guide
changes, for example `codex/dev-skill-templates`.

If GitHub is unavailable, the CLI uses the last successful cache. If no cache is
available yet, it falls back to the embedded guides from the installed package.
Local custom guides can be placed under
`~/.config/timepro-cli/guides/accounting/` or
`~/.config/timepro-cli/guides/dev/`; matching slugs override the downloaded
guide.

## Ranking

When a user supplies `--use-case`, guide topics are ranked with simple text
matching:

1. Exact match against a topic title or keyword.
2. Contains all query words.
3. Contains at least one query word.

Only the highest matching tier is returned. If an exact match exists, lower
priority partial matches are ignored.

Examples:

```bash
tp dev guide --use-case suggested --json
tp dev guide --use-case suggested --refresh --json
tp dev guide --use-case "suggested timesheets missing" --json
tp accounting guide --use-case "invoice reconciliation" --json
```

Keep keywords practical. Include the words users are likely to type, not every
internal implementation term.

## Add An Accounting Guide

Use this when the workflow is read-only accounting evidence: invoices, receipts,
credit notes, tax, aged debtors, prepaid drawdown, client rates, unbilled work,
or external comparison with Excel/CSV/another MCP.

1. Add or update an entry in `guides/accounting/index.json`.
2. Add or update the matching Markdown file under `guides/accounting/`.
3. Include a clear title, one-sentence description, likely search keywords,
   read-only `tp` commands, optional MCP tool names, and broad installed skills
   that help with the workflow.
4. Use `timepro-accounting-cli` as the usual accounting skill pointer. Do not
   create a new installed generated skill for a highly specific recipe.
5. Update tests in `tests/SSW.TimePro.Cli.Tests/Features/Guides/`.
6. Update `docs/accounting.md` or `docs/skill-generation.md` when the workflow
   is user-facing.

Accounting guide topics should stay read-only by default. Production accounting
diagnostics must not mutate invoices, receipts, credit notes, sync state, or
external accounting systems without explicit user permission.

## Add A Developer Guide

Use this when the workflow is bug-focused: suggested timesheets, CRM bookings,
saved timesheets, invoice/tax bugs, credit notes, client rates, prepaid drawdown,
environment differences, or external sync failures.

1. Add or update an entry in `guides/dev/index.json`.
2. Add or update the matching Markdown file under `guides/dev/`.
3. Include keywords that match the symptom a developer will type, such as
   `suggested`, `booking`, `invoice`, `tax`, `rate`, `sync`, or `staging`.
4. Keep commands CLI-first and environment-scoped with `--tenant <name>
   --env <env>` when possible.
5. Point to broad installed skills such as `timepro-dev-timesheet-diagnostics`,
   `timepro-dev-finance-diagnostics`, `timepro-env-compare`, or
   `timepro-accounting-cli` when useful.
6. Add tests for exact, contains-all, and contains-one matching when relevant.
7. Update `docs/skill-generation.md` only if the generated skill catalog itself
   changes.

Local and staging developer diagnostics can be more experimental when scoped
and reversible. Production defaults to read-only; any non-read-only production
diagnostic needs explicit user permission first.

## Data And Examples

Never use real client, project, repo, invoice, or person names in guide topics,
Markdown skills, docs, tests, or examples. Use Northwind placeholders:

- Client: `NWIND` / `Northwind Traders`
- Repo: `Northwind/traders-app`
- Employee: `ALEX`
- Project: `1I776Q`
- IDs: small sample numbers such as `42`, `108`, or `142`

Run these checks before committing:

```bash
dotnet test tests/SSW.TimePro.Cli.Tests/
dotnet test tests/SSW.TimePro.Cli.Integration/
git diff --check
```

## Index Format

Each folder has an `index.json` with a `guides` array. `file` is relative to the
same folder.

```json
{
  "guides": [
    {
      "slug": "suggested-timesheets-missing",
      "file": "suggested-timesheets-missing.md",
      "title": "Suggested timesheets missing",
      "description": "Diagnose missing or stale suggested timesheets.",
      "keywords": ["suggested", "suggestions", "ts suggest"],
      "commands": ["tp ts suggest <date> --tenant <name> --env <env> --json"],
      "mcpTools": [],
      "skills": ["timepro-dev-timesheet-diagnostics"]
    }
  ]
}
```
