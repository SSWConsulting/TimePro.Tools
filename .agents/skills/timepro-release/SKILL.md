---
name: timepro-release
description: Prepare and run SSW TimePro CLI releases with matching release notes
allowed-tools: Bash(git *), Bash(gh *), Bash(dotnet *), Bash(scripts/security/nuget-audit.sh), Bash(scripts/install.sh), Bash(tp *)
---

# TimePro Release

Use this skill when preparing a new `tp` CLI release.

## Production gate

This is a production release workflow. Do not treat publishing as a follow-up
chore after a local experiment.

Before creating release notes or dispatching a release, verify all of the
following:

1. The user has explicitly asked for a release. If they say not to release, stop
   after source changes, tests, commit, and push.
2. The behavioral fix or user-visible change is already committed and pushed to
   `main`. A release-notes-only commit does not count.
3. `git status --short` is clean except for intentional release-note edits while
   preparing the release.
4. `git log --oneline <previous-release-tag>..origin/main` shows the actual
   fix/change commit(s), not just release metadata.
5. If working from a detached `HEAD`, push the exact intended commit to `main`
   and verify:

```bash
git fetch origin
test "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)"
```

If any gate fails, do not dispatch the release workflow. Fix the source control
state first and tell the user what was blocked.

## Release note first

1. Read `src/SSW.TimePro.Cli/SSW.TimePro.Cli.csproj` and get `<VersionPrefix>`.
2. Get the latest GitHub Release for that prefix:

```bash
gh release list --limit 100 --json tagName --jq '.[].tagName'
```

3. Pick the next patch by adding one to the highest tag matching `v<VersionPrefix>.<patch>`.
4. Create `release-notes/<version>.md` before dispatching the release workflow.
5. Repoint `release-notes/latest.md` to that versioned file:

```bash
ln -sfn <version>.md release-notes/latest.md
```

6. Keep the filename exact and simple, for example `release-notes/0.2.7.md`.

Patch-zero versions such as `0.2.0` are developer builds. Do not create a release
note for patch zero.

## Style — keep it simple

Release notes are read by `tp` users via `tp --whats-new`, not by maintainers. Most
won't care about internals. Match the existing notes (see `release-notes/0.2.2.md`):

- A `# <version>` heading, then a short bullet list — usually 1–4 bullets.
- One bullet per user-visible change. Say **what changed and why it matters to the
  user**, not how it was implemented (no file names, class names, or internal mechanics).
- Plain language, present tense, one line where possible. Skip pure refactors, test-only
  changes, and chores that have no user-facing effect.
- If nothing user-facing changed, the release probably doesn't need notes.

Good:

```markdown
# 0.2.3

- `tp skills create --global` now installs skills where your agent looks for them
  (e.g. `~/.claude`, `~/.codex`).
```

Avoid: "Refactored `CreateCommand` to extract `ResolveBaseDir` and updated unit tests."

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
- fail if `release-notes/latest.md` is not a symlink to `<version>.md`,
- pack the tool with the release notes embedded,
- create the GitHub Release using `release-notes/latest.md` as the release body.

After the release, install and smoke test the released package:

```bash
scripts/install.sh
tp info
tp --whats-new
```
