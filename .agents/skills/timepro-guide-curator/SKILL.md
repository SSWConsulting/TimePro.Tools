---
name: timepro-guide-curator
description: Add and curate indexed TimePro diagnostic guides under guides/accounting and guides/dev
allowed-tools: Bash(rg *), Bash(dotnet *), Bash(git diff *), Bash(git status *), Bash(tp *)
---

# TimePro Guide Curator

Use this skill when adding or curating guide-backed diagnostics for the TimePro
CLI.

## Start Here

Read `docs/diagnostic-guides.md` first. It defines the `guides/accounting` and
`guides/dev` index format, how `--use-case` ranking works, and when a workflow
should stay a guide versus becoming a broad installed generated skill.

## Workflow

1. Decide whether the request is accounting evidence or developer bug
   diagnostics.
2. Update the matching guide index:
   - Accounting: `guides/accounting/index.json`
   - Developer: `guides/dev/index.json`
3. Add or update the matching Markdown guide file in the same folder.
4. Add practical keywords for exact, contains-all, and contains-one matching.
5. Point to broad installed skills only when useful. Keep highly specific
   recipes as guides.
6. Add focused tests under `tests/SSW.TimePro.Cli.Tests/Features/Guides/`.
7. Update user-facing docs when the guide collection changes.

## Rules

- Keep guide matching simple and predictable: exact match, contains all words,
  contains at least one word.
- Keep accounting diagnostics read-only unless the user explicitly approves a
  non-read-only production action.
- Keep developer diagnostics CLI-first. App Insights comes after CLI evidence
  identifies the concrete employee/date/client/invoice/reference.
- Do not add MCP-only business logic. If a report needs product logic, make it a
  CLI command first. If it is just a recipe, keep it as an indexed guide.
- Use Northwind placeholders only. Do not include real client, repo, project,
  invoice, or person names in docs, tests, examples, or generated skills.

## Verify

```bash
dotnet test tests/SSW.TimePro.Cli.Tests/
dotnet test tests/SSW.TimePro.Cli.Integration/
dotnet run --project src/SSW.TimePro.Cli -- dev guide --use-case suggested --json
dotnet run --project src/SSW.TimePro.Cli -- accounting guide --use-case "invoice reconciliation" --json
git diff --check
```
