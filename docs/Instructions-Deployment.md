# Instructions - Deployment

TimePro Tools is a local CLI and MCP host. There is no hosted application, database, or cloud infrastructure to deploy from this repository.

Deployment means packaging the `tp` .NET global tool and installing it where it will run.

## Release Package

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
dotnet tool uninstall -g SSW.TimePro.Cli
dotnet tool install -g --add-source artifacts/nupkg SSW.TimePro.Cli
```

## Distribution

Current supported distribution paths:

- Local or internal package installation via `dotnet tool install -g --add-source`.
- Manual NuGet publish via the **Release (NuGet)** GitHub Actions workflow (see below).

## Publish via GitHub Actions

The `.github/workflows/release.yml` workflow packs and publishes the `tp` global
tool to NuGet. It is **manually triggered** (`workflow_dispatch`) so a deploy only
happens when a maintainer asks for one.

### One-time setup

Add a repository secret named `NUGET_API_KEY` (Settings → Secrets and variables →
Actions) containing a NuGet API key scoped to push the `SSW.TimePro.Cli` package.

### Running a release

1. Go to **Actions → Release (NuGet) → Run workflow**.
2. Pick the branch/tag to build from.
3. Inputs:
   - **version** – optional. Overrides the version in the `.csproj` (e.g. `1.2.3`).
     Leave blank to use the committed `<Version>`.
   - **dry_run** – defaults to `true`. A dry run builds, tests, runs the NuGet
     vulnerability audit, packs, and uploads the `.nupkg` as a build artifact
     **without** pushing to NuGet. Set to `false` to publish.

The workflow always runs the test suite and `scripts/security/nuget-audit.sh`
before packing, and uses `--skip-duplicate` so re-running a published version is a
no-op rather than a failure.

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
