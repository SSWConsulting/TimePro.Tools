# Skill Generation

`tp skills create` generates agent skill files for TimePro workflows.

## CLI surface

```bash
tp skills create <TARGET> [--global]
```

- `<TARGET>` is the agent root, for example `.agents` or `.claude`.
- `--global` swaps the base directory to the CLI global config root.

The default output writes:

- `timepro-timesheets`
- `timepro-tenant-setup`

Optional output:

- `timepro-accounting-cli` when `tp feature accounting enable` is set
- `timepro-dev-diagnostics` when `tp feature developer enable` is set
- `timepro-dev-timesheet-diagnostics` when `tp feature developer enable` is set
- `timepro-dev-finance-diagnostics` when `tp feature developer enable` is set
- `timepro-env-compare` when `tp feature developer enable` is set

Legacy shorthand flags `--accounting`, `--developer`, and `--dev` are consumed
by the startup interceptor before Spectre command parsing. They enable the
matching persistent feature in `~/.config/timepro-cli/config.json`, strip the
flag, and then run the command normally.

The generator deliberately has no per-agent selector. Claude and Codex/.agents
skills share the same discoverable layout and frontmatter shape, so the old
renderer split added complexity without buying useful behavior.

## Output format

Each skill is written to:

```text
<base>/skills/<name>/SKILL.md
```

The frontmatter always includes `name` and `description`. When the model has
allowed tools, it also includes a comma-separated `allowed-tools` line:

```yaml
---
name: timepro-timesheets
description: Manage SSW TimePro timesheets with the tp CLI...
allowed-tools: Bash(tp *), Bash(sl *)
---
```

If the skill has deterministic read-only setup commands, they render as a plain
section that is inert on every agent:

````markdown
## Run these first
Run these read-only commands before you start, and read their output:

```bash
tp info --json              # CLI health, active tenant, user, and update status
tp project recent --json    # ranked likely projects + repo paths (start here)
tp ts get --week --json     # current week's entries + suggestions
tp bk list --week --json    # CRM bookings for the week
tp loc info --json          # location defaults / WFH days
```
````

The accounting skill is instruction-only and has no run-these-first block. The
tenant setup and developer skills include small `tp tenant ...` preflight blocks
because the selected tenant/profile is the most important context for those
workflows.

## Why it is unified

Both supported agent roots use the same `<name>/SKILL.md` subfolder layout and
understand the same frontmatter, including `allowed-tools`. The previous split
only existed to support Claude load-time command prefetch. That syntax was
dropped so generated skills stay simple, portable, and readable everywhere.

The old Codex path also wrote `skills/<name>.md`, which Codex does not discover
as a skill. The unified renderer fixes that by always writing the subfolder
layout.

## Content model

`SkillModelBuilder` builds a `SkillContentModel` for each skill. Long-form skill
instructions live as packaged Markdown templates in
`src/SSW.TimePro.Cli/Features/Skills/Templates/*.md`; `SkillBodyBuilder` only
fills small dynamic placeholders such as detected GitHub repo, repo mapping,
current tenant, and location defaults. `SkillRenderer` adds the frontmatter,
optional run-these-first block, and final output path.

The timesheets skill keeps:

- `allowed-tools: Bash(tp *), Bash(sl *)`
- `tp info --json` as the first health/update check, preferred over `tp --version`
- `tp project recent --json` as the first project-selection step
- the existing timesheet, booking, leave, repo mapping, scrum, and troubleshooting guidance
- Northwind-only examples (`NWIND`, `1I776Q`, `Northwind/traders-app`)

The tenant setup skill keeps:

- `allowed-tools: Bash(tp *)`
- safe discovery via `tp tenant list`, `tp tenant info`, and `tp user me`
- a workflow that switches the global active tenant to `ssw-staging`
- a workflow that uses `--tenant` / `--env` without changing the active tenant
- tenant profile naming and environment-resolution rules
- no `direnv exec .` dependency for tenant config

The accounting skill keeps:

- `allowed-tools: Bash(tp *)`
- instruction-only read-only accounting workflows
- client billable-work threshold report guidance, including the `.rows` JSON envelope shape
- deeper reconciliation diagnostics for Excel, CSV, Xero MCP, bank-feed MCP, or another external source
- guidance to check `tp accounting guide` first, then use specialized markdown skills such as `timepro-accounting-tax-mismatch`, `timepro-accounting-invoice-diagnostics`, and `timepro-accounting-client-diagnostics`
- no prefetch/run-these-first commands

The developer diagnostics skill keeps:

- `allowed-tools: Bash(tp *), Bash(az monitor app-insights query *), Bash(jq *)`
- CLI-first reproduction and fix-verification workflows
- clear environment safety: local/staging can be more experimental; production defaults to read-only
- explicit instruction to ask the user before any non-read-only production action
- links to narrower developer diagnostics for timesheet and finance bug families

The developer timesheet diagnostics skill keeps:

- `allowed-tools: Bash(tp *), Bash(az monitor app-insights query *), Bash(jq *)`
- suggested-timesheet, CRM booking, saved-timesheet, accept/create/update, and duplicate/missing-row diagnostics
- reminders that `tp ts suggest` and `tp bk list` use the selected tenant-profile employee
- App Insights follow-up guidance for TimePro, CRM appointment, booking, and suggestion boundaries

The developer finance diagnostics skill keeps:

- `allowed-tools: Bash(tp *), Bash(az monitor app-insights query *), Bash(jq *)`
- bug-focused workflows for invoices, credit notes, receipts, client rates, prepaid drawdown, tax, billing status, and external sync
- guidance that accounting-like scenarios are used to find code/data/API/sync boundaries, not to produce final reconciliation conclusions
- pointers to `tp accounting ...` CLI diagnostics for deeper invoice, client, and tax mismatch evidence

The environment comparison skill keeps:

- `allowed-tools: Bash(tp *), Bash(jq *), Bash(diff *)`
- normalized JSON capture and `diff -u` comparisons between prod/staging/local profiles
- read-only production default
- a production warning for `tp ts suggest` because it refreshes suggested-timesheet state
