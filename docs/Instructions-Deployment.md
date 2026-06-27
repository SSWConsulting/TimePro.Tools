# Instructions - Deployment

TimePro Tools is a local CLI and MCP host. There is no hosted application, database, or cloud infrastructure to deploy from this repository.

Deployment means packaging the `tp` .NET global tool and installing it where it will run.

## Installing (end users)

The supported way to install or upgrade `tp` is the install script. It verifies
the .NET 10 SDK is present, downloads the latest GitHub Release's `.nupkg`, and
installs it as a global tool (updating in place if already installed). Re-run it
any time to upgrade.

**macOS / Linux:**

```bash
curl -fsSL https://raw.githubusercontent.com/SSWConsulting/TimePro.Tools/main/scripts/install.sh | bash
```

**Windows (PowerShell):**

```powershell
irm https://raw.githubusercontent.com/SSWConsulting/TimePro.Tools/main/scripts/install.ps1 | iex
```

Both scripts are self-contained — they only need the .NET 10 SDK and a network
connection. To remove the tool: `dotnet tool uninstall -g SSW.TimePro.Cli`.

> The script installs from a **published GitHub Release**. If no non-dry-run
> release exists yet, cut one first (see [Release via GitHub Actions](#release-via-github-actions)).

## Building a release package (from source)

This is the manual flow contributors use to produce and test a package locally;
end users should prefer the install script above.

1. Update the package version in `src/SSW.TimePro.Cli/SSW.TimePro.Cli.csproj`.
2. Run the test suite:

```bash
dotnet test tests/SSW.TimePro.Cli.Tests/
dotnet test tests/SSW.TimePro.Cli.Integration/
```

3. Create the release package:

```bash
dotnet pack src/SSW.TimePro.Cli/ -c Release -o artifacts/nupkg
```

4. Install or update from the generated package source:

```bash
dotnet tool update -g --add-source artifacts/nupkg SSW.TimePro.Cli
```

## Distribution

Current supported distribution paths:

- **Install script** (`scripts/install.sh` / `scripts/install.ps1`) — pulls the
  latest GitHub Release and installs/updates the global tool. This is the path
  end users should use.
- GitHub Releases (with the `.nupkg` attached) via the **Release (NuGet)** GitHub
  Actions workflow (see below). The install script consumes these releases.
- Local or internal package installation via `dotnet tool update -g --add-source`
  (the from-source flow above).

> Publishing to nuget.org is currently **disabled**. The push step in the workflow
> is commented out; the workflow builds, packs, and cuts a GitHub Release instead.

## Versioning

Versions are `major.minor.patch`:

- **major.minor** is the single source of truth in
  `src/SSW.TimePro.Cli/SSW.TimePro.Cli.csproj` as `<VersionPrefix>` (e.g. `0.2`).
  Bump it by editing that one value.
- **patch** is supplied automatically by the release workflow as one higher than
  the latest GitHub Release for that major/minor line, e.g. `0.2.7`.

Local builds default to `<VersionPrefix>.0` (e.g. `0.2.0`); patch-zero builds are
treated as developer builds by update checks and installed-version tracking.
Installed release builds record their current version, previous version,
installation time, last update-check time, and the latest release version seen in
`~/.config/timepro-cli/config.json`. Running `tp info` refreshes that update
state unless `--no-update-check` is passed.

Every release must have a matching Markdown file:

```text
release-notes/<version>.md
release-notes/latest.md -> <version>.md
```

For example, release `0.2.7` must have `release-notes/0.2.7.md`. The release
workflow requires `release-notes/latest.md` to be a symlink to `0.2.7.md`, uses
that symlink as the GitHub Release body, and embeds the versioned file for
`tp --whats-new`.

## Release via GitHub Actions

The `.github/workflows/release.yml` workflow builds, tests, packs, and creates a
GitHub Release for the `tp` global tool. It is **manually triggered**
(`workflow_dispatch`) so a release only happens when a maintainer asks for one.

### Running a release

1. Go to **Actions → Release (NuGet) → Run workflow**.
2. Pick the branch/tag to build from.
3. Input:
   - **dry_run** – defaults to `true`. A dry run builds, tests, runs the NuGet
     vulnerability audit, packs, and uploads the `.nupkg` as a build artifact
     **without** creating a GitHub Release. Set to `false` to also cut the release
     (tag `v<version>` plus the attached `.nupkg`).

Before running a non-dry-run release, create the next release note file and
repoint `release-notes/latest.md` to it. The next version is computed from the
current `<VersionPrefix>` and the latest GitHub Release for that prefix. For the
first `0.2` release, that means:

```bash
ln -sfn 0.2.1.md release-notes/latest.md
```

The workflow always runs the test suite and `scripts/security/nuget-audit.sh`
before packing.

### Enabling nuget.org publishing later

1. Add a repository secret `NUGET_API_KEY` (Settings → Secrets and variables →
   Actions) scoped to push the `SSW.TimePro.Cli` package.
2. Uncomment the **Push to NuGet** step at the bottom of `release.yml`.

Future distribution options:

- Publish the package to an internal NuGet feed.
- Package signing and tag-driven (release-triggered) automation.

## Configuration After Install

Each user or automation host configures its own tenant:

```bash
tp login --tenant ssw
```

Headless automation should use environment-provided secrets and non-interactive setup. Do not commit tenant config files or API keys.

## Verification

After installation:

```bash
tp --help
tp tenant info --json
tp ts get --week
```

Run staging E2E scripts only where staging credentials are available:

```bash
./scripts/e2e/run-all.sh
```
