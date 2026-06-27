---
name: timepro-release
description: Prepare and run SSW TimePro CLI releases with matching release notes
allowed-tools: Bash(git *), Bash(gh *), Bash(dotnet *), Bash(scripts/security/nuget-audit.sh), Bash(scripts/install.sh), Bash(tp *)
---

# TimePro Release

Use this skill when preparing a new `tp` CLI release.

## Release note first

1. Read `src/SSW.TimePro.Cli/SSW.TimePro.Cli.csproj` and get `<VersionPrefix>`.
2. Get the latest GitHub Release for that prefix:

```bash
gh release list --limit 100 --json tagName --jq '.[].tagName'
```

3. Pick the next patch by adding one to the highest tag matching `v<VersionPrefix>.<patch>`.
4. Create `release-notes/<version>.md` before dispatching the release workflow.
5. Keep the filename exact and simple, for example `release-notes/0.2.7.md`.

Patch-zero versions such as `0.2.0` are developer builds. Do not create a release
note for patch zero.

## Required checks

Run these before dispatching the non-dry-run release:

```bash
dotnet test SSW.TimePro.Timesheets.Cli.slnx
scripts/security/nuget-audit.sh
```

## Release

Dispatch `.github/workflows/release.yml` with `dry_run=false`. The workflow will:

- compute the same next version from GitHub Releases,
- fail if `release-notes/<version>.md` is missing,
- pack the tool with the release notes embedded,
- create the GitHub Release using that Markdown file as the release body.

After the release, install and smoke test the released package:

```bash
scripts/install.sh
tp info
tp --whats-new
```
