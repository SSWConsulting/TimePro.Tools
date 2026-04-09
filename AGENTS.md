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
- `ExecuteAsync` handles the command logic
- Use `OutputHelper` for `--json` support
- Always validate config/auth before API calls

### API Client
`TimeProApiClient` is the single HTTP client for all TimePro API calls:
- Auth headers: `x-timepro-tenant-id`, `x-timepro-api-key`, `x-timepro-api-name`
- All methods are async and accept `CancellationToken`
- API URL comes from active tenant config

### Configuration
- Global config: `~/.config/timepro-cli/config.json`
- Per-tenant: `~/.config/timepro-cli/tenants/{id}.json`
- Repo mappings: `~/.config/timepro-cli/repo-mappings.json`

## Commands

Run `tp --help` for full command list. Key commands:
- `tp login --tenant ssw` - Authenticate
- `tp tenant set ssw-staging` - Switch active tenant (uses filename, not tenantId inside the file)
- `tp ts get --week` - View week's timesheets
- `tp ts create ...` - Create timesheet
- `tp leave create --start 2026-03-30 --end 2026-03-30 --type 1 --note "..." --approved-by "email" --cc "e1,e2" --yes` - Create leave
- `tp leave cancel ID --reason "..." --yes` - Cancel leave
- `tp leave list --filter UPCOMING --json` - List leave
- `tp mcp` - Start MCP server

## Tenants

The `activeTenant` in `config.json` is the **filename** (without `.json`) of the tenant config file, not the `tenantId` property inside it. This allows multiple configs for the same tenant (e.g., `ssw` for prod, `ssw-staging` for staging) where both have `"tenantId": "ssw"` but different `apiUrl` and `apiKey`.

## Leave API

The leave create endpoint (`POST /api/leave/`) requires these fields in the request body:
- **Required**: `RequestedEmpId`, `StartDate` (DateTimeOffset), `EndDate` (DateTimeOffset), `LeaveTypeId`, `UserStartTime`, `UserEndTime`, `AllDay`
- **Optional**: `Note`, `OptionalEmp` (CC emails), `ApprovedBy` (email), `TimeLessOverride`

The cancel endpoint (`PUT /api/leave/{id}/cancel`) requires `LeaveId` (Guid) and `CancellationReason` in the request body.

## Testing

```bash
# Unit tests
dotnet test tests/SSW.TimePro.Cli.Tests/

# Integration tests (WireMock, no network needed)
dotnet test tests/SSW.TimePro.Cli.Integration/

# E2E (requires staging credentials)
./scripts/e2e/run-all.sh
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

## Build & Run

```bash
dotnet build
dotnet run --project src/SSW.TimePro.Cli -- ts get --week

# Install as global tool
dotnet pack src/SSW.TimePro.Cli/
dotnet tool install -g --add-source src/SSW.TimePro.Cli/nupkg SSW.TimePro.Cli
```
