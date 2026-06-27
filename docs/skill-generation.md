# Skill Generation

`tp skills create` generates agent skill files for TimePro workflows.

## CLI surface

```bash
tp skills create <TARGET> [--global] [--accounting]
```

- `<TARGET>` is the agent root, for example `.agents` or `.claude`.
- `--global` swaps the base directory to the CLI global config root.
- `--accounting` also writes the `timepro-accounting-cli` skill.

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

The accounting skill is instruction-only and has no run-these-first block.

## Why it is unified

Both supported agent roots use the same `<name>/SKILL.md` subfolder layout and
understand the same frontmatter, including `allowed-tools`. The previous split
only existed to support Claude load-time command prefetch. That syntax was
dropped so generated skills stay simple, portable, and readable everywhere.

The old Codex path also wrote `skills/<name>.md`, which Codex does not discover
as a skill. The unified renderer fixes that by always writing the subfolder
layout.

## Content model

`SkillModelBuilder` builds a `SkillContentModel` for each skill. The long shared
markdown body lives in `SkillBodyBuilder`, and `SkillRenderer` adds the
frontmatter, optional run-these-first block, and final output path.

The timesheets skill keeps:

- `allowed-tools: Bash(tp *), Bash(sl *)`
- `tp info --json` as the first health/update check, preferred over `tp --version`
- `tp project recent --json` as the first project-selection step
- the existing timesheet, booking, leave, repo mapping, scrum, and troubleshooting guidance
- Northwind-only examples (`NWIND`, `1I776Q`, `Northwind/traders-app`)

The accounting skill keeps:

- `allowed-tools: Bash(tp *)`
- instruction-only read-only accounting workflows
- client billable-work threshold report guidance, including the `.rows` JSON envelope shape
- no prefetch/run-these-first commands
