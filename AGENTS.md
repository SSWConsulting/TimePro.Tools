# AI Agent Guidelines for SSW.TimePro.Timesheets.Cli

## Project Overview

CLI-first tool with MCP support for managing SSW TimePro timesheets. Built with .NET 10, Spectre.Console.Cli, and Vertical Slice Architecture (VSA).

## Architecture

- **VSA**: Each CLI command is a self-contained vertical slice in `Features/{Domain}/`
- **Shared infrastructure**: `Infrastructure/` contains API client, config, output helpers
- **Models**: API DTOs in `Shared/Models/`
- **Tests**: Unit tests (pure logic), Integration tests (WireMock.Net), E2E scripts (staging)

## Key Patterns

### CLI Commands
Each command is a class inheriting `AsyncCommand<TSettings>` from Spectre.Console.Cli:
- Settings class defines options/arguments
- `protected override ExecuteAsync(CommandContext context, TSettings settings, CancellationToken cancellationToken)` handles async command logic
- `protected override Execute(CommandContext context, TSettings settings, CancellationToken cancellationToken)` handles sync command logic
- Use `OutputHelper` for `--json` support
- Always validate config/auth before API calls
- Thread `cancellationToken` through new API calls where practical

### API Client
`TimeProApiClient` is the single HTTP client for all TimePro API calls:
- Auth headers: `x-timepro-tenant-id`, `x-timepro-api-key`, `x-timepro-api-name`
- All methods are async and accept `CancellationToken`
- API URL comes from active tenant config

### Configuration
- Global config: `~/.config/timepro-cli/config.json`
- Per-tenant: `~/.config/timepro-cli/tenants/{id}.json`
- Repo mappings: `~/.config/timepro-cli/repo-mappings.json`
- Feature packs: `config.json` stores `features.<name>.enabled` and `features.<name>.version` so skills and MCP can share one persistent setting.

### Project Files
- `Directory.Build.props` centralizes shared .NET defaults (`net10.0`, implicit usings, nullable)
- `Directory.Packages.props` uses central package management for all NuGet versions
- Project files should reference packages without inline `Version` attributes unless there is a deliberate local override

## Commands

Run `tp --help` for full command list. Key commands:
- `tp login --tenant ssw` - Authenticate
- `tp tenant set ssw-staging` - Switch active tenant (uses filename, not tenantId inside the file)
- `tp ts get --week` - View week's timesheets
- `tp ts create ...` - Create timesheet
- `tp ts check --week --json` - Leave-aware weekly coverage check (see below)
- `tp project recent` - Surface projects recently logged against (likely picks for new entries)
- `tp leave create --start 2026-03-30 --end 2026-03-30 --type 1 --note "..." --approved-by "email" --cc "e1,e2" --yes` - Create leave
- `tp leave cancel ID --reason "..." --yes` - Cancel leave
- `tp leave list --filter UPCOMING --json` - List leave
- `tp leave balance --emp-id JEK` - Leave-usage signal (days since last leave + hours taken in last 12 months)
- `tp feature accounting enable` - Enable accounting skills and accounting MCP tools
- `tp feature developer enable` - Enable developer diagnostics/environment comparison skills and timesheet/finance bug diagnostic skills
- `tp mcp [--tenant NAME]` - Start MCP server (optional per-session tenant binding)

### Leave-aware `tp ts check`

`ts check` merges approved leave + public holidays into weekly coverage. A full-day leave day is `covered` (never an error); a partial-day leave only expects the remaining hours. The `--json` output gives per-day `covered` / `coverReason` (`logged`, `leave-full`, `leave-partial`, `holiday`, `missing`), `leaveHours` / `leaveType`, and a top-level `allCovered`. Fetch/merge logic lives in the shared `WeekCoverageService`; the rules are in the pure, unit-tested `CheckEvaluator`. Exit code 1 when errors are found (CI-friendly).

### `--json` error envelope

On the `--json` path, failures emit a structured envelope to **stdout** so stdout stays valid JSON: `{"error":{"code":<int|null>,"message":"...","detail":<string|null>}}` (all keys always present), with a non-zero exit code. Human-readable error/warning text goes to **stderr**.

### MCP tools + tenant resolution

Default MCP tools cover timesheets, lookups, and leave. Accounting MCP tools are feature-gated behind `tp feature accounting enable`. Before enabling or changing MCP features, ask the user what the MCP use-case is (timesheets, accounting reconciliation, Excel/CSV comparison, Xero/other MCP composition, diagnostics, etc.) so the tool surface can be adjusted deliberately.

MCP tools must not be the only implementation of TimePro behavior. Any business logic or diagnostic report exposed through MCP must be implemented as a CLI command first, with MCP delegating to the same service/report shape. Developer diagnostics are expected to be CLI/skill workflows, not dedicated MCP guide tools.

The MCP host resolves the tenant in this order: `--tenant NAME` → global active tenant → the sole tenant config if exactly one exists (single-tenant installs work without `tp tenant set`). `--tenant` does NOT change the global active tenant.

## Tenants

The `activeTenant` in `config.json` is the **filename** (without `.json`) of the tenant config file, not the `tenantId` property inside it. This allows multiple configs for the same tenant (e.g., `ssw` for prod, `ssw-staging` for staging) where both have `"tenantId": "ssw"` but different `apiUrl` and `apiKey`.

## Leave API

The leave create endpoint (`POST /api/leave/`) requires these fields in the request body:
- **Required**: `RequestedEmpId`, `StartDate` (DateTimeOffset), `EndDate` (DateTimeOffset), `LeaveTypeId`, `UserStartTime`, `UserEndTime`, `AllDay`
- **Optional**: `Note`, `OptionalEmp` (CC emails), `ApprovedBy` (email), `TimeLessOverride`

The cancel endpoint (`PUT /api/leave/{id}/cancel`) requires `LeaveId` (Guid) and `CancellationReason` in the request body.

The list endpoint (`GET /api/leave/`) returns per-entry `daysAway`, `updatedAt`, `timeLessOverride`, `cancellationReason` (all bound on `LeaveEntry`) plus a top-level `cancelledCount` on the list envelope. These surface in `tp leave list --json`.

## Testing

```bash
# Unit tests
dotnet test tests/SSW.TimePro.Cli.Tests/

# Integration tests (WireMock, no network needed)
dotnet test tests/SSW.TimePro.Cli.Integration/

# E2E (requires staging credentials)
./scripts/e2e/run-all.sh

# NuGet package safety audit (also used by the optional Git pre-push hook)
scripts/security/nuget-audit.sh
```

## Optional Git Hook

This repo supports Git 2.54 config-based hooks for a local NuGet safety check before push.
It avoids checked-in `.githooks` and does not block developers who do not have `dotnet`
installed; missing `dotnet` is skipped, while reported NuGet vulnerabilities fail the push.

```bash
chmod +x scripts/security/nuget-audit.sh
git config --local hook.nuget-audit.event pre-push
git config --local hook.nuget-audit.command scripts/security/nuget-audit.sh
git hook list pre-push
```

## Documentation & Examples

**⚠️ Never use real client, project, or repo names in documentation, READMEs, example commands, skill files, tests, or commit messages.** The only fictional placeholder allowed in this repo is **Northwind** (the canonical sample dataset).

Examples of what **not** to write:
- Real client IDs or names (e.g. any actual SSW customer)
- Real GitHub owner/repo slugs for customer work
- Real PR or issue titles scraped from live projects
- Real project codes from the TimePro system

Use these placeholders instead:
- Client: `NWIND` / `"Northwind Traders"`
- Project ID: `1I776Q` (arbitrary placeholder, not a real TimePro ID)
- GitHub repo: `Northwind/traders-app`, `Northwind/traders-mobile`
- PR/issue numbers: `#42`, `#108`, `#142` — any small integers are fine
- Example descriptions: "Product search", "Checkout API", "Order history", etc.

This rule applies to anything under version control, anything emitted by `tp skills create`, and anything in example output blocks. When updating docs, grep the working tree for anything that looks like a real customer name, a real-world repo slug, or internal-only project codes before committing. If in doubt, replace with a Northwind variant.

## Diagnostic Guides

Adding new guide-backed diagnostics is welcomed. See
[`docs/diagnostic-guides.md`](docs/diagnostic-guides.md) for how to add
accounting and developer guide indexes, Markdown recipes, ranking
keywords, and tests. Use the repo skill `timepro-guide-curator` for guide
curation work.

## Build & Run

```bash
dotnet build
dotnet run --project src/SSW.TimePro.Cli -- ts get --week

# Install as global tool
dotnet pack src/SSW.TimePro.Cli/ -c Release -o artifacts/nupkg
dotnet tool install -g --add-source artifacts/nupkg SSW.TimePro.Cli
```
