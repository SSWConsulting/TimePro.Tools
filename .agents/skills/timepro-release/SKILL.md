---
name: timepro-release
description: Use when asked to cut, publish or ship a new `tp` CLI version; syncs main, smoke tests the unreleased changes on staging, gets the user's approval of the version and notes, then releases.
allowed-tools: Bash(git *), Bash(gh *), Bash(dotnet *), Bash(scripts/security/nuget-audit.sh), Bash(scripts/e2e/*), Bash(scripts/install.sh), Bash(tp *)
---

# TimePro Release

One pass from `origin/main` to a published GitHub Release. Run it only when the user asked for a
release. `release-notes/AGENTS.md` owns what goes in the notes; this skill owns the procedure.

## 1. Sync and scope

```bash
git fetch origin --tags --prune
git checkout --detach origin/main   # main is often checked out in another worktree
gh release list --limit 100 --json tagName --jq '.[].tagName'
git log --oneline <previous-release-tag>..origin/main
```

Stop if the range holds only release metadata. Read each PR in the range (`gh pr view N`) for what
changed and how it was verified.

## 2. Smoke test on staging

Build Release and exercise every user-visible change in the range with `--tenant ssw-staging`.
Never production, even when a PR was verified there.

```bash
dotnet build src/SSW.TimePro.Cli -c Release
tp_() { dotnet src/SSW.TimePro.Cli/bin/Release/net10.0/SSW.TimePro.Cli.dll "$@"; }
dotnet test SSW.TimePro.Timesheets.Cli.slnx
TIMEPRO_MCP_SMOKE_PROJECT=<NWIND project on staging that uses iterations> \
TIMEPRO_MCP_SMOKE_TP="dotnet src/SSW.TimePro.Cli/bin/Release/net10.0/SSW.TimePro.Cli.dll" \
  scripts/e2e/test-mcp-smoke.sh
```

- Prefer reads and validation failures. Delete anything you create and confirm it is gone.
- New or changed MCP tools: call them over `tp mcp --tenant ssw-staging` stdio and compare with the
  CLI `--json` output.
- The `1I776Q` placeholder is not a staging project; ask the user for one.
- A defect found here goes to the user, not into a silent fix inside the release.

## 3. Draft, then ask

The next version is the highest `v<VersionPrefix>.<patch>` release plus one patch, which is what the
workflow computes. If the range changes a JSON or MCP contract, propose a minor bump (edit
`<VersionPrefix>`) instead. Patch zero is a developer build and never gets notes.

Draft `release-notes/<version>.md` in the shape of the latest notes: `# <version>`, grouped by area,
one short bullet per user-visible change.

**Gate:** show the user the version, the full note text, the commit range and the smoke results,
including anything that failed. Ask (AskUserQuestion where available) whether to release as-is,
change the version, or edit the notes. Push nothing, open no PR and dispatch nothing until they
approve; re-ask after any edit.

## 4. Land the notes

On a `release/<version>` branch:

- add `release-notes/<version>.md` and run `ln -sfn <version>.md release-notes/latest.md`
- update `LatestKnown()` in `tests/SSW.TimePro.Cli.Tests/Features/Updates/ReleaseNotesCatalogTests.cs`
- commit `docs(release): prepare <version>`, open a PR, `gh pr checks --watch`, squash-merge

## 5. Publish

```bash
git fetch origin --tags --prune && git checkout --detach origin/main
test "$(readlink release-notes/latest.md)" = "<version>.md"
dotnet test SSW.TimePro.Timesheets.Cli.slnx
scripts/security/nuget-audit.sh
gh workflow run release.yml --ref main -f dry_run=false
gh run watch <run-id> --exit-status
```

Then check that `v<version>` points at the merged `origin/main` commit
(`git rev-parse v<version>^{commit}`) and carries the `.nupkg`. Install that asset into a scratch
`--tool-path` and confirm `tp --version` and `tp --whats-new`.

## 6. Upgrade locally

Before upgrading, run `tp --check-update` on the old install and show the user the nudge their users
will see. Then `scripts/install.sh` and `tp info`.
