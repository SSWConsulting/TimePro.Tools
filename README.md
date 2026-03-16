# SSW TimePro CLI + MCP

> CLI-first tool with MCP support for managing [SSW TimePro](https://www.sswtimepro.com/) timesheets. Built for AI agents (Claude Code, Codex, VS Code Copilot) and human users.
>
> This project uses the existing TimePro API with your own credentials.

SSW TimePro is a time tracking and invoicing system. This CLI makes it fast to view, create, and manage timesheets from the terminal — and exposes the same capabilities to AI agents via [MCP](https://modelcontextprotocol.io/).

## What it does

- **Week View** — Compact or detailed view of your timesheets with totals, billable hours, and missing days
- **Timesheet CRUD** — Create, update, delete timesheets with rate checking and lock detection
- **Suggested Timesheets** — View and accept suggested timesheets to keep accuracy stats high
- **CRM Bookings** — See your appointments from the CRM calendar
- **Leave Management** — Create, list, and cancel EasyLeave requests
- **Repo Mapping** — Map git repos to clients/projects; auto-detects via path or remote URL, with git worktree support
- **Location Defaults** — Set WFH days so location is auto-applied when creating timesheets
- **CSV Export** — Export timesheets for tax reports or analysis
- **Skills Generation** — Generate agent skill files with project context and `gh` commands
- **MCP Server** — Exposes 11 tools for AI agents via stdio transport

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A TimePro account with API access
- (Optional) [GitHub CLI](https://cli.github.com/) for issue/PR references in timesheets

## Quick Start

### 1. Install as global tool

```bash
git clone https://github.com/SSWConsulting/timepro-cli.git
cd timepro-cli
dotnet pack src/SSW.TimePro.Cli/ -o src/SSW.TimePro.Cli/nupkg
dotnet tool install -g --add-source src/SSW.TimePro.Cli/nupkg SSW.TimePro.Cli
```

This makes the `tp` command available system-wide.

To update after pulling new changes:

```bash
cd timepro-cli
git pull
dotnet pack src/SSW.TimePro.Cli/ -o src/SSW.TimePro.Cli/nupkg
dotnet tool uninstall -g SSW.TimePro.Cli
dotnet tool install -g --add-source src/SSW.TimePro.Cli/nupkg SSW.TimePro.Cli
```

To uninstall:

```bash
dotnet tool uninstall -g SSW.TimePro.Cli
```

<details>
<summary>Alternative: Run from project directory without global install</summary>

```bash
git clone https://github.com/SSWConsulting/timepro-cli.git
cd timepro-cli
dotnet build
```

Then prefix all commands with `dotnet run`:

```bash
dotnet run --project src/SSW.TimePro.Cli -- login --tenant ssw
dotnet run --project src/SSW.TimePro.Cli -- ts get --week
```

</details>

### 2. Authenticate

```bash
tp login --tenant ssw
```

This sets your tenant and prompts for an API token. To get your token:
1. Visit `https://{tenant}.sswtimepro.com/b/admin/api-key`
2. Copy the API key

For non-interactive login (CI, scripts):

```bash
tp login --tenant ssw --token YOUR_API_KEY --api-url https://api.sswtimepro.com
```

Your employee ID and name are auto-detected on login. Credentials are stored at `~/.config/timepro-cli/tenants/{tenant}.json`.

### 3. View your timesheets

```bash
tp ts get --week           # This week (compact view)
tp ts get --week --detailed # With full descriptions, invoices, lock status
tp ts get --week -1        # Last week
tp ts get 2026-03-12       # Specific date
```

## CLI Reference

| Command | Description |
|---------|-------------|
| `tp login --tenant ID` | Authenticate with a TimePro tenant |
| `tp logout` | Remove stored credentials |
| `tp tenant set ID` | Switch active tenant |
| `tp tenant info` | Show active tenant details |
| `tp tenant list` | List all stored tenants |
| `tp ts get [DATE]` | View timesheets (supports `--week`, `--detailed`, `--json`) |
| `tp ts create` | Create a new timesheet (see options below) |
| `tp ts update ID` | Update a timesheet (partial — only specified fields) |
| `tp ts delete ID` | Delete a timesheet |
| `tp ts suggest [DATE]` | View suggested timesheets |
| `tp ts accept ID` | Accept a suggested timesheet |
| `tp ts export` | Export timesheets to CSV |
| `tp bk list` | List CRM bookings/appointments |
| `tp leave list` | List leave entries |
| `tp leave create` | Create a leave request |
| `tp leave cancel ID` | Cancel a leave request |
| `tp cl search QUERY` | Search for clients |
| `tp proj list --client ID` | List projects for a client |
| `tp rate get --client ID` | Get billing rate (with expiry warnings) |
| `tp loc info [--date D]` | Show location defaults / check a specific date |
| `tp loc set LOC --day D` | Set WFH day defaults |
| `tp map set PATH` | Map a repo path to a client/project |
| `tp map detect` | Detect mapping for current directory |
| `tp map list` | List all repo mappings |
| `tp map remove PATH` | Remove a repo mapping |
| `tp skills create TARGET` | Generate agent skill files |
| `tp user me` | Show current user info |
| `tp mcp` | Start MCP server (stdio) |

All read commands support `--json` for machine-readable output. All write commands support `--yes` to skip confirmation prompts.

### Aliases

Every command group has a short alias:

| Full | Alias |
|------|-------|
| `tp timesheet` | `tp ts` |
| `tp booking` | `tp bk` |
| `tp leave` | `tp lv` |
| `tp client` | `tp cl` |
| `tp project` | `tp proj` |
| `tp location` | `tp loc` |

### Creating Timesheets

```bash
tp ts create \
  --client NWIND \
  --project 1I776Q \
  --date 2026-03-16 \
  --start 09:00 \
  --end 18:00 \
  --less 60 \
  --description "Added product search API endpoint — PR #42 · #108" \
  --location Home \
  --billable B \
  --yes
```

When creating timesheets:
- **Rate** is checked automatically; you'll get a warning if it's expired or expiring soon
- **Location** defaults to your WFH settings if not specified
- **Locked timesheets** (invoiced) only allow location and description changes

### Week View

Compact view shows one line per timesheet with totals:

```
 Week of Mar 10 - Mar 14, 2026
─────────────────────────────────────────────────────────────────────
 Mon 10 │ 8.0h │ Northwind  Northwind Traders  4.0h  Home    B
         │      │ SSW        Internal           4.0h  Home    W
 Tue 11 │ 8.0h │ Northwind  Northwind Traders  8.0h  Home    B
 Wed 12 │ 0.0h │ No timesheets
 Thu 13 │ 8.0h │ Northwind  Northwind Traders  8.0h  Office  B
 Fri 14 │ 8.0h │ SSW        Brainstorming      8.0h  Office  W
─────────────────────────────────────────────────────────────────────
 Total: 32.0h / 40.0h  │  Billable: 20.0h  │  Missing: Wed
```

Detailed view (`--detailed`) shows full descriptions, invoice info, and suggested timesheets.

### Repo Mapping

Map repositories to clients/projects so AI agents know what to bill:

```bash
tp map set ~/Developer/git/Northwind/traders-app \
  --remote github.com/Northwind/traders-app \
  --client NWIND --project 1I776Q --project-name "Northwind Traders"

tp map set ~/Developer/git/SSW.Rewards.Mobile \
  --client SSW --project 4BPT0L --project-name "Rewards (Mobile app)"
```

Detection supports:
- **Exact path** — `~/Developer/git/SSW.TimePRO`
- **Glob patterns** — `~/Developer/git/Northwind/*`
- **Git remote URL** — `github.com/Northwind/traders-app`
- **Git worktrees** — Codex/Claude worktrees at `~/.codex/worktrees/` or `~/.claude-worktrees/` resolve to the main repo automatically

```bash
tp map detect  # Shows matched client/project for current directory
```

### Location Defaults

Set which days you work from home:

```bash
tp location set Home --day Mon,Tue
tp location info                      # Show defaults
tp location info --date 2026-03-16    # Check a specific date
```

When creating timesheets, location is auto-applied based on the day of week.

### Skills Generation

Generate agent skill files for a project:

```bash
tp skills create .agents           # Local to current project
tp skills create .claude --global  # Global (all projects)
```

The generated file includes:
- Quick reference for all `tp` commands
- Workflow for entering a full week of timesheets
- `gh` commands pre-filled with the repo slug for issue/PR lookup
- Description format guide with PR and issue number examples
- Project context (client, project, GitHub repo) auto-detected from repo mapping

## MCP Server

The MCP server exposes TimePro data to AI agents via stdio transport.

| Tool | Description |
|------|-------------|
| `GetTimesheets` | Get timesheets for a date or range |
| `CreateTimesheet` | Create a new timesheet |
| `UpdateTimesheet` | Update an existing timesheet |
| `DeleteTimesheet` | Delete a timesheet |
| `GetSuggestedTimesheets` | Get suggested timesheets for a date |
| `AcceptSuggestedTimesheet` | Accept a suggested timesheet |
| `SearchClients` | Search clients by name |
| `GetProjectsForClient` | Get projects for a client |
| `GetClientRate` | Get billing rate with expiry info |
| `GetCrmBookings` | Get CRM appointments |
| `GetLocationAndMapping` | Get WFH defaults and repo mapping |

### Claude Code

Add to `~/.claude/settings.json`:

```json
{
  "mcpServers": {
    "timepro": {
      "command": "tp",
      "args": ["mcp"]
    }
  }
}
```

Then ask Claude things like:
- "What are my timesheets for this week?"
- "Create a timesheet for today — I worked on the Northwind Traders app"
- "Accept the suggested timesheet for Monday"
- "What's my billing rate for Northwind?"

### VS Code (Copilot / Continue)

Add to `.vscode/settings.json` or user settings:

```json
{
  "mcp": {
    "servers": {
      "timepro": {
        "command": "tp",
        "args": ["mcp"]
      }
    }
  }
}
```

### Codex (OpenAI CLI)

Add to your Codex MCP config:

```json
{
  "mcpServers": {
    "timepro": {
      "command": "tp",
      "args": ["mcp"]
    }
  }
}
```

## Project Structure

```
timepro-cli/
├── src/SSW.TimePro.Cli/
│   ├── Program.cs                    # Entry point, DI, command tree
│   ├── Infrastructure/
│   │   ├── ApiClient/                # HTTP client, auth headers
│   │   ├── Config/                   # Global + tenant config, repo mapping
│   │   ├── Output/                   # JSON + human output helpers
│   │   └── DependencyInjection/      # Spectre.Console DI bridge
│   ├── Features/
│   │   ├── Auth/                     # login, logout
│   │   ├── Tenants/                  # set, info, list
│   │   ├── Timesheets/              # get, create, update, delete, suggest, accept, export
│   │   ├── Bookings/                # list
│   │   ├── Leave/                   # list, create, cancel
│   │   ├── Clients/                 # search
│   │   ├── Projects/                # list
│   │   ├── Rates/                   # get
│   │   ├── Location/                # info, set
│   │   ├── RepoMap/                 # set, list, remove, detect
│   │   ├── Skills/                  # create
│   │   ├── Users/                   # me
│   │   └── Mcp/                     # MCP server + 11 tools
│   └── Shared/Models/               # API DTOs
├── tests/
│   ├── SSW.TimePro.Cli.Tests/       # Unit tests (xUnit + FluentAssertions)
│   └── SSW.TimePro.Cli.Integration/ # WireMock.Net integration tests
├── scripts/e2e/                     # E2E shell scripts for staging
└── docs/plans/                      # Architecture + testing strategy
```

## Configuration

Config is stored at `~/.config/timepro-cli/`:

| File | Description |
|------|-------------|
| `config.json` | Active tenant, WFH days, default location |
| `tenants/{id}.json` | Per-tenant credentials (API key, employee ID, API URL) |
| `repo-mappings.json` | Repository-to-client/project mappings |

## Running Tests

```bash
# Unit tests (pure logic, no network)
dotnet test tests/SSW.TimePro.Cli.Tests/

# Integration tests (WireMock.Net, no network)
dotnet test tests/SSW.TimePro.Cli.Integration/

# All tests
dotnet test

# E2E (requires staging credentials)
TIMEPRO_E2E_API_KEY=... ./scripts/e2e/run-all.sh
```

## License

MIT
