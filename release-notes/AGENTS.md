# Release Notes Instructions

Release notes are user-facing and are embedded in the published `tp` package for
`tp --whats-new`. Treat them as part of the release artifact, not as a
post-release summary.

## Required Source Of Truth

- Build the release note from the complete Git range since the previous
  published release tag for the current `VersionPrefix`.
- Do not write notes from memory, from the latest PR only, or from local
  unpushed commits.
- Tags help only when they are used as the release boundary. The previous
  release tag defines the start of the range; `origin/main` defines the intended
  end before the release workflow creates the next tag.

Use this checklist before editing or approving a versioned note:

```bash
git fetch origin --tags --prune
gh release list --limit 100 --json tagName --jq '.[].tagName'
git log --oneline <previous-release-tag>..origin/main
git diff --stat <previous-release-tag>..origin/main
```

The `git log` range must show the real user-visible fix or feature commits. If
it only shows release metadata, stop: the release would not contain the change.

## Version File Rules

- Read `src/SSW.TimePro.Cli/SSW.TimePro.Cli.csproj` and use
  `<VersionPrefix>` to choose the next patch after the highest matching
  published GitHub Release tag, for example `v0.2.4` -> `0.2.5`.
- Create exactly `release-notes/<version>.md`.
- Repoint `release-notes/latest.md` to that file with:

```bash
ln -sfn <version>.md release-notes/latest.md
```

- Patch-zero versions such as `0.2.0` are developer builds. Do not create
  release notes for patch zero.

## Content Rules

- Start with `# <version>`.
- Include every user-visible change in `<previous-release-tag>..origin/main`,
  not only the change that triggered the release.
- Skip release-note-only commits, process docs, tests, refactors, and chores
  unless they change behavior visible to `tp` users.
- Explain what changed and why it matters. Avoid file names, class names, and
  internal implementation details.
- Keep bullets short. `tp --whats-new` users should be able to scan them.

## Pre-Release Gate

Before dispatching the release workflow:

```bash
git fetch origin --tags --prune
test "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)"
test "$(readlink release-notes/latest.md)" = "<version>.md"
dotnet test SSW.TimePro.Timesheets.Cli.slnx
scripts/security/nuget-audit.sh
```

After the release workflow succeeds, verify the new GitHub Release tag exists
and points at the intended `origin/main` commit. If the tag points elsewhere,
the release notes are not proof that the package contains the change.
