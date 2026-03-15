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
- `tp ts get --week` - View week's timesheets
- `tp ts create ...` - Create timesheet
- `tp mcp` - Start MCP server

## Testing

```bash
# Unit tests
dotnet test tests/SSW.TimePro.Cli.Tests/

# Integration tests (WireMock, no network needed)
dotnet test tests/SSW.TimePro.Cli.Integration/

# E2E (requires staging credentials)
./scripts/e2e/run-all.sh
```

## Build & Run

```bash
dotnet build
dotnet run --project src/SSW.TimePro.Cli -- ts get --week

# Install as global tool
dotnet pack src/SSW.TimePro.Cli/
dotnet tool install -g --add-source src/SSW.TimePro.Cli/nupkg SSW.TimePro.Cli
```
