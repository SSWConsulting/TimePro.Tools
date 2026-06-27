---
name: timepro-skill-generator
description: Add, update, and verify generated TimePro agent skills produced by tp skills create. Use when changing SkillModelBuilder, SkillBodyBuilder, SkillRenderer, SkillVersionService, packaged skill templates, feature-gated generated skills, generated skill versions, or tp info stale-skill behavior.
---

# TimePro Skill Generator

Use this skill when changing the generated skills that `tp skills create`
writes to `.agents/skills/*/SKILL.md` or `.claude/skills/*/SKILL.md`.

## Start Here

Read `docs/skill-generation.md` first. It explains the generated skill catalog,
feature gates, template layout, version tracking, and verification expectations.

If the request is for a highly specific diagnostic or report recipe, use the
`timepro-guide-curator` skill instead and add a guide under
`guides/accounting/` or `guides/dev/`. Keep generated skills for broad,
important workflows users should install and version-track.

## Workflow

1. Identify whether the change is for default, accounting, developer, tenant,
   or environment-comparison generated skills.
2. Update packaged Markdown templates under
   `src/SSW.TimePro.Cli/Features/Skills/Templates/` for prose-heavy changes.
3. Update `SkillModelBuilder` for catalog membership, feature gates, names,
   descriptions, allowed tools, prefetch selection, and skill version.
4. Update `SkillBodyBuilder` only for dynamic placeholders, template rendering,
   or prefetch blocks.
5. Update `SkillRenderer` only for generated file layout or frontmatter shape.
6. Update `SkillVersionService`, `InfoCommand`, or `IgnoreVersionCommand` only
   when version tracking, stale notices, or ignore-version behavior changes.
7. Update tests under `tests/SSW.TimePro.Cli.Tests/Features/Skills/` and docs
   only when the generated skill catalog or behavior changes.

## Rules

- Do not add narrow one-off diagnostics as generated skills. Add indexed guides
  instead.
- Keep generated skills portable across `.agents` and `.claude`; both use the
  same `<root>/skills/<name>/SKILL.md` layout.
- Keep examples sanitized with Northwind placeholders only.
- Do not include real client, repo, project, invoice, employee, or incident
  details in templates, tests, docs, or generated output.
- Bump `CurrentSkillVersion` when generated skill content changes enough that
  existing installed skills should show as stale in `tp info`.
- Preserve feature gates: accounting skills require
  `tp feature accounting enable`; developer skills require
  `tp feature developer enable`.
- Treat legacy `--accounting`, `--developer`, and `--dev` as startup
  interception behavior, not normal `tp skills create` options.

## Verify

Use a temp config/home when testing generated output so local user skill state
does not affect the result.

```bash
dotnet test tests/SSW.TimePro.Cli.Tests/
dotnet test tests/SSW.TimePro.Cli.Integration/
git diff --check

TMP_HOME=$(mktemp -d)
DOTNET_CLI_HOME="$TMP_HOME" HOME="$TMP_HOME" \
  dotnet run --project src/SSW.TimePro.Cli -- feature accounting enable

DOTNET_CLI_HOME="$TMP_HOME" HOME="$TMP_HOME" \
  dotnet run --project src/SSW.TimePro.Cli -- feature developer enable

DOTNET_CLI_HOME="$TMP_HOME" HOME="$TMP_HOME" \
  dotnet run --project src/SSW.TimePro.Cli -- skills create "$TMP_HOME/agent-root"

DOTNET_CLI_HOME="$TMP_HOME" HOME="$TMP_HOME" \
  dotnet run --project src/SSW.TimePro.Cli -- info --no-update-check --json
```
