# SSW.TimePro.Timesheets.Cli — Architecture Plan

## Overview

A CLI-first tool with MCP support for managing SSW TimePro timesheets. Designed for AI agents (Claude Code, Codex) and human users. Replaces the MCP-only `SSW.TimePro.Mcp` with proper CLI commands, multi-tenant support, and richer output.

## Tech Stack

| Component | Technology | Rationale |
|-----------|-----------|-----------|
| **Runtime** | .NET 10 (`net10.0`) | Consistent with existing SSW.TimePro.Mcp |
| **CLI Framework** | Spectre.Console.Cli | Rich terminal output + CLI command framework in one package |
| **MCP SDK** | ModelContextProtocol 0.6.0+ | Same SDK as existing MCP project |
| **HTTP** | Microsoft.Extensions.Http + HttpClient | Typed clients with DI |
| **DI** | Microsoft.Extensions.DependencyInjection | Standard .NET DI |
| **JSON** | System.Text.Json | Built-in, fast |
| **Testing** | xUnit + NSubstitute + FluentAssertions + WireMock.Net | See testing-strategy.md |
| **Package Manager** | dotnet tool (global install) | `dotnet tool install -g SSW.TimePro.Cli` |

## Architecture: Vertical Slice Architecture (VSA)

Each CLI command is a self-contained vertical slice containing:
- Command definition (args, options, settings class)
- Validation logic
- API call(s) via shared `TimeProApiClient`
- Output formatting (normal + `--json`)

Shared infrastructure (API client, config, output helpers) lives in `Infrastructure/`.

## Project Structure

```
SSW.TimePro.Timesheets.Cli/
├── SSW.TimePro.Timesheets.Cli.slnx
├── AGENTS.md
├── .gitignore
├── docs/
│   └── plans/
├── src/
│   └── SSW.TimePro.Cli/
│       ├── SSW.TimePro.Cli.csproj
│       ├── Program.cs                          # Entry point, DI, command tree
│       │
│       ├── Infrastructure/
│       │   ├── ApiClient/
│       │   │   ├── TimeProApiClient.cs         # Core HTTP client (all API calls)
│       │   │   ├── ApiException.cs             # Typed API errors
│       │   │   └── ApiEndpoints.cs             # URL constants
│       │   ├── Config/
│       │   │   ├── ConfigPaths.cs              # ~/.config/timepro-cli/ paths
│       │   │   ├── ConfigService.cs            # Read/write config files
│       │   │   ├── GlobalConfig.cs             # Active tenant, WFH days
│       │   │   ├── TenantConfig.cs             # Token, API URL, employee ID
│       │   │   └── RepoMapping.cs              # Path -> client/project mapping
│       │   ├── Output/
│       │   │   ├── OutputContext.cs             # --json flag context
│       │   │   ├── JsonOutput.cs               # JSON serialization helpers
│       │   │   └── TableOutput.cs              # Spectre table builders
│       │   └── DependencyInjection/
│       │       ├── TypeRegistrar.cs            # Spectre.Console DI bridge
│       │       └── TypeResolver.cs
│       │
│       ├── Features/
│       │   ├── Auth/
│       │   │   ├── LoginCommand.cs             # tp login --tenant --token --api-url
│       │   │   └── LogoutCommand.cs            # tp logout [--tenant]
│       │   ├── Tenants/
│       │   │   ├── TenantSetCommand.cs         # tp tenant set <id>
│       │   │   ├── TenantInfoCommand.cs        # tp tenant info
│       │   │   └── TenantListCommand.cs        # tp tenant list
│       │   ├── Timesheets/
│       │   │   ├── GetCommand.cs               # tp timesheet get [DATE] --week --detailed
│       │   │   ├── CreateCommand.cs            # tp timesheet create --client --project ...
│       │   │   ├── UpdateCommand.cs            # tp timesheet update <ID> --location --desc
│       │   │   ├── DeleteCommand.cs            # tp timesheet delete <ID>
│       │   │   ├── SuggestCommand.cs           # tp timesheet suggest [DATE]
│       │   │   ├── AcceptCommand.cs            # tp timesheet accept <SUGGESTED_ID>
│       │   │   ├── ExportCommand.cs            # tp timesheet export --from --to --format csv
│       │   │   └── WeekRenderer.cs             # Compact + detailed week view rendering
│       │   ├── Bookings/
│       │   │   └── ListCommand.cs              # tp booking list [--date] [--week]
│       │   ├── Leave/
│       │   │   ├── ListCommand.cs              # tp leave list [--filter]
│       │   │   ├── CreateCommand.cs            # tp leave create --start --end --type
│       │   │   ├── UpdateCommand.cs            # tp leave update <ID>
│       │   │   └── CancelCommand.cs            # tp leave cancel <ID>
│       │   ├── Clients/
│       │   │   ├── SearchCommand.cs            # tp client search <QUERY>
│       │   │   └── OutstandingCommand.cs       # tp client outstanding (unbilled time)
│       │   ├── Projects/
│       │   │   └── ListCommand.cs              # tp project list --client <ID>
│       │   ├── Iterations/
│       │   │   └── ListCommand.cs              # tp iteration list --project <ID>
│       │   ├── Users/
│       │   │   ├── MeCommand.cs                # tp user me
│       │   │   └── SearchCommand.cs            # tp user search <QUERY>
│       │   ├── Rates/
│       │   │   ├── GetCommand.cs               # tp rate get --client <ID>
│       │   │   └── ListCommand.cs              # tp rate list --client <ID>
│       │   ├── Invoices/                       # accountant (read-only)
│       │   │   ├── ListCommand.cs              # tp invoice list
│       │   │   ├── GetCommand.cs               # tp invoice get <ID>
│       │   │   ├── LinesCommand.cs             # tp invoice lines <ID>
│       │   │   ├── TimesheetsCommand.cs        # tp invoice timesheets <ID> [--writeoff]
│       │   │   └── ReceiptsCommand.cs          # tp invoice receipts <ID>
│       │   ├── Receipts/                       # accountant
│       │   │   ├── ListCommand.cs              # tp receipt list
│       │   │   ├── GetCommand.cs               # tp receipt get <ID>
│       │   │   └── OutstandingCommand.cs       # tp receipt outstanding <CLIENT>
│       │   ├── CreditNotes/
│       │   │   └── ListCommand.cs              # tp creditnote list --client <ID>
│       │   ├── Products/
│       │   │   ├── ListCommand.cs              # tp product list [--prepaid]
│       │   │   ├── GetCommand.cs               # tp product get <ID>
│       │   │   └── DiscountsCommand.cs         # tp product discounts --client <ID>
│       │   ├── Recurring/
│       │   │   ├── ListCommand.cs              # tp recurring list
│       │   │   └── GetCommand.cs               # tp recurring get <ID>
│       │   ├── Prepaid/
│       │   │   ├── SummaryCommand.cs           # tp prepaid summary <INV> [--json]
│       │   │   └── StatusCommand.cs            # tp prepaid status <INV> (PDF)
│       │   ├── Unbilled/
│       │   │   └── ListCommand.cs              # tp unbilled list --client <ID>
│       │   ├── Location/
│       │   │   ├── InfoCommand.cs              # tp location info [--date]
│       │   │   └── SetCommand.cs               # tp location set Home --day Mon,Tue
│       │   ├── RepoMap/
│       │   │   ├── SetCommand.cs               # tp map set PATH --client --project
│       │   │   ├── ListCommand.cs              # tp map list
│       │   │   └── RemoveCommand.cs            # tp map remove PATH
│       │   ├── Skills/
│       │   │   └── CreateCommand.cs            # tp skills create .agents [--global]
│       │   └── Mcp/
│       │       ├── McpHostCommand.cs           # tp mcp (starts stdio MCP server)
│       │       └── Tools/
│       │           ├── TimesheetMcpTools.cs
│       │           ├── BookingMcpTools.cs
│       │           ├── LeaveMcpTools.cs
│       │           ├── LookupMcpTools.cs
│       │           ├── LocationMcpTools.cs
│       │           └── AccountingMcpTools.cs    # invoices, receipts, credit notes, products, rates, prepaid, recurring + cross-domain reads
│       │
│       └── Shared/
│           └── Models/                         # API DTOs shared across features
│               ├── TimesheetModels.cs
│               ├── BookingModels.cs
│               ├── LeaveModels.cs
│               ├── ClientModels.cs
│               ├── ProjectModels.cs
│               ├── RateModels.cs                 # rate lookup DTO (tp rate get)
│               ├── AccountingRateModels.cs      # paged rate table (tp rate list)
│               ├── InvoiceModels.cs
│               ├── ReceiptModels.cs
│               ├── CreditNoteModels.cs
│               ├── ProductModels.cs
│               ├── RecurringModels.cs
│               ├── PagedResponse.cs             # generic { total, data[] } envelope
│               ├── UserModels.cs
│               └── CommonModels.cs
│
├── tests/
│   ├── SSW.TimePro.Cli.Tests/              # Unit tests
│   └── SSW.TimePro.Cli.Integration/        # Integration tests (WireMock.Net)
│
└── scripts/
    └── e2e/                                 # E2E script tests against staging
```

## Configuration Storage

```
~/.config/timepro-cli/
├── config.json              # Global settings
├── tenants/
│   ├── ssw.json             # Per-tenant credentials & settings
│   └── northwind.json
└── repo-mappings.json       # Repo -> client/project mappings
```

### `config.json` (Global)

```json
{
  "activeTenant": "ssw",
  "wfhDays": ["Monday", "Tuesday"],
  "defaultLocation": "Office"
}
```

### `tenants/ssw.json`

```json
{
  "tenantId": "ssw",
  "apiUrl": "https://api.sswtimepro.com",
  "apiKey": "encrypted-or-plaintext-token",
  "employeeId": "TST",
  "employeeName": "Test User",
  "appName": "SSW-TimePro-CLI"
}
```

### `repo-mappings.json`

```json
{
  "mappings": [
    {
      "pathPattern": "~/Developer/git/Northwind/traders-app",
      "clientId": "NWIND",
      "projectId": "1I776Q",
      "projectName": "Northwind Traders",
      "categoryId": null
    },
    {
      "pathPattern": "~/Developer/git/Northwind/*",
      "clientId": "NWIND",
      "projectId": "1I776Q",
      "projectName": "Northwind Traders"
    }
  ]
}
```

## Full Command Tree

```
tp
├── login        --tenant TENANT [--token TOKEN] [--api-url URL]
├── logout       [--tenant TENANT]
│
├── tenant
│   ├── set      TENANT_ID
│   ├── info
│   └── list
│
├── timesheet | ts
│   ├── get      [DATE] [--week [OFFSET]] [--from DATE --to DATE]
│   │                   [--detailed] [--json]
│   ├── create   --client C --project P [--date D] [--start HH:mm]
│   │            [--end HH:mm] [--description DESC] [--location LOC]
│   │            [--category CAT] [--billable B|BPP|W] [--less MIN]
│   │            [--from-suggested ID] [--yes]
│   ├── update   ID [--location LOC] [--description DESC]
│   │            [--start HH:mm] [--end HH:mm] [--client C]
│   │            [--project P] [--category CAT] [--yes]
│   ├── delete   ID [--yes]
│   ├── suggest  [DATE] [--week [OFFSET]] [--json]
│   ├── accept   SUGGESTED_ID [--location LOC] [--notes NOTES] [--yes]
│   └── export   [--from DATE] [--to DATE] [--output FILE]
│
├── booking | bk
│   └── list     [--date DATE] [--week [OFFSET]] [--json]
│
├── leave | lv
│   ├── list     [--filter upcoming|past] [--limit N] [--json]
│   ├── create   --start DATE --end DATE --type TYPE
│   │            [--note NOTE] [--all-day] [--yes]
│   ├── update   ID [--start DATE] [--end DATE] [--type TYPE]
│   │            [--note NOTE] [--yes]
│   └── cancel   ID [--reason REASON] [--yes]
│
├── client | cl
│   ├── search   QUERY [--limit N] [--json]
│   └── get      CLIENT_ID [--json]
│
├── project | pj
│   └── list     --client CLIENT_ID [--json]
│
├── iteration | it
│   └── list     --project PROJECT_ID [--json]
│
├── user
│   ├── me       [--json]
│   └── search   QUERY [--json]
│
├── rate
│   └── get      --client CLIENT_ID [--date DATE] [--json]
│
├── location | loc
│   ├── info     [--date DATE]
│   └── set      LOCATION --day Mon,Tue,Wed,...
│
├── map
│   ├── set      PATH --client CLIENT --project PROJECT [--category CAT]
│   ├── list     [--json]
│   ├── remove   PATH
│   └── detect   [--json]   # Auto-detect repo mapping from CWD
│
├── skills
│   └── create   TARGET [--global]
│       # e.g. tp skills create .agents
│       #      tp skills create .claude --global
│
└── mcp                      # Start MCP stdio server
```

## Week View Output

### Compact (default: `tp ts get --week`)

```
 Week of Mar 10 - Mar 14, 2026
─────────────────────────────────────────────────────────────────────
 Mon 10 │ 8.0h │ NWIND Northwind Traders     4.0h  Office  B
         │      │ NWIND Internal tooling      4.0h  Home    W
 Tue 11 │ 8.0h │ NWIND Checkout API          8.0h  Office  B
 Wed 12 │ 0.0h │ No timesheets
 Thu 13 │ 8.0h │ NWIND Northwind Traders     8.0h  Home    B
 Fri 14 │ 8.0h │ NWIND Planning              8.0h  Home    W
─────────────────────────────────────────────────────────────────────
 Total: 32.0h / 40.0h  │  Billable: 20.0h  │  Missing: Wed
```

### Detailed (`tp ts get --week --detailed`)

```
── Monday, March 10 2026 ────────────────────── 8.0h ──

  #1234  NWIND │ Northwind Traders
         09:00 - 13:00 (4.0h) │ Office │ Billable
         Added product search filters - PR #69
         Invoice: #5678 (T&M) [locked]

  #1235  SSW │ Internal
         14:00 - 18:00 (4.0h) │ Home │ Write-off
         Daily standup, code review

── Tuesday, March 11 2026 ───────────────────── 8.0h ──
  ...
```

## API Client Mapping

The CLI reuses the same TimePro API endpoints from the existing MCP project:

| Feature | API Endpoints |
|---------|--------------|
| **Get timesheets** | `GET /api/Timesheets/GetTimesheetListViewModel?employeeID={id}&date={date}` |
| **Create timesheet** | `POST /api/Timesheets/SaveTimesheet?isEdit=false&isSuggested=false` |
| **Update timesheet** | `POST /api/Timesheets/SaveTimesheet?isEdit=true&isSuggested=false` |
| **Delete timesheet** | `DELETE /api/Timesheets/DeleteTimesheet/{id}` |
| **Suggested timesheets** | `GET /api/Timesheets/RefreshSuggestedTimesheets` + included in list view |
| **Accept suggested** | `POST /api/Timesheets/AcceptSuggestedTimesheet?id={id}` |
| **Search clients** | `GET /api/Timesheets/GetClientListForAddTimesheet?empID={id}&searchText={q}` |
| **List projects** | `GET /api/Timesheets/GetProjectsForClient?empID={id}&clientID={cid}` |
| **Get rate** | `GET /api/Timesheets/GetClientRate?empID={id}&clientID={cid}&timesheetDateCreated={date}` |
| **CRM bookings** | `GET /Crm/Appointments?employeeID={id}&start={epoch}&end={epoch}` |
| **Leave CRUD** | `GET/POST/PUT /api/leave/`, `PUT /api/leave/{id}/cancel` |
| **Leave types** | `GET /api/leave/types` |
| **User info** | `GET /api/v2/users/me`, `GET /api/employees/getSettingsDetails` |
| **Iterations** | `GET /api/ProjectIteration/GetIterationsForAddTimesheet?projectId={pid}` |
| **Locations** | `GET /api/Timesheets/GetTimesheetLocation` |
| **Categories** | `GET /api/Timesheets/GetTimesheetCategories` |
| **Export CSV** | `GET /Export/ExportTimesheetsToCSV?startDate={s}&endDate={e}` |
| **Employee ID** | `GET /api/Employees/GetEmployeeID` |

## Authentication

### Auth Headers (per request)

| Header | Value | Source |
|--------|-------|--------|
| `x-timepro-tenant-id` | Tenant ID (e.g., `ssw`) | TenantConfig |
| `x-timepro-api-key` | API token | TenantConfig |
| `x-timepro-api-name` | `SSW-TimePro-CLI` | Hardcoded |

### Login Flow

```
$ tp login --tenant ssw
  To get your API token, visit:
  https://ssw.sswtimepro.com/b/admin/api-key

Paste your API token: ********

  Logged in as TST (Test User) on tenant 'ssw'
  API: https://api.sswtimepro.com
```

On login:
1. Save tenant config to `~/.config/timepro-cli/tenants/ssw.json`
2. Call `GET /api/Employees/GetEmployeeID` to auto-detect employee ID
3. Call `GET /api/v2/users/me` to get employee name for confirmation
4. Set as active tenant in `config.json`

API URL defaults to `https://api.sswtimepro.com`, overridable with `--api-url`.

Token page URL pattern: `https://{tenant}.sswtimepro.com/b/admin/api-key`

## MCP Tools (Minimal Set for AI Agents)

| Tool | Description |
|------|-------------|
| `get_timesheets` | Get timesheets for a date/range (supports week) |
| `create_timesheet` | Create a new timesheet |
| `update_timesheet` | Update an existing timesheet |
| `delete_timesheet` | Delete a timesheet |
| `get_suggested_timesheets` | Get suggested timesheets for a date |
| `accept_suggested_timesheet` | Accept a suggested timesheet |
| `get_crm_bookings` | Get CRM appointments for a date range |
| `search_clients` | Search clients by name |
| `get_projects_for_client` | Get projects for a client |
| `get_client_rate` | Get rate for employee+client |
| `get_location_defaults` | Get WFH day settings |
| `get_repo_mapping` | Get client/project for a repo path |
| `get_leave` | Get leave entries |
| `create_leave` | Create leave request |

## Business Logic

### Rate Expiry Handling

When creating a timesheet:
1. Call `GetClientRate` for the employee + client + date
2. If `ExpiryDate` has passed or rate is null:
   - CLI: Show warning with expired rate info, prompt to continue or abort
   - MCP: Return error with details about expired rate, suggest contacting admin
3. If rate exists but expires soon (within 7 days): show advisory warning

### Timesheet Locking

Timesheets associated with locked invoices or locked billing periods:
- **Allowed changes**: location, description/notes
- **Blocked changes**: start/end times, client, project, category, billable type, rate
- CLI shows lock status and which fields are modifiable
- MCP returns structured error explaining the lock

### Location Defaults

`tp location set Home --day Mon,Tue` saves WFH defaults. When creating timesheets:
- Auto-apply location based on the day of week
- Can be overridden with `--location` on individual commands

## Implementation Phases

### Phase 1: Foundation
1. Project scaffold
2. Spectre.Console DI bridge
3. Config infrastructure
4. API client
5. `tp login` / `tp logout` / `tp tenant set|info|list`
6. `tp user me`
7. `tp timesheet get` (day + week views)

### Phase 2: Timesheet Operations
8. `tp client search`, `tp project list`, `tp iteration list`
9. `tp rate get`
10. `tp timesheet create|update|delete`
11. `tp timesheet suggest` + `tp timesheet accept`
12. `tp booking list`
13. `--json` output everywhere

### Phase 3: Leave, Location, Mapping, Export
14. `tp leave list|create|update|cancel`
15. `tp location info|set`
16. `tp map set|list|remove|detect`
17. `tp timesheet export`

### Phase 4: MCP + Skills + Polish
18. `tp mcp`
19. `tp skills create`
20. Comprehensive test coverage
