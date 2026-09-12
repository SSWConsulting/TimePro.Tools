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

`ConfigureRequest` also sends the client-identity headers (`User-Agent: timepro-cli/<version>`,
`x-timepro-client-surface`, `x-timepro-client-command`, and a fresh `x-timepro-client-request-id`
per attempt) and returns that id; `SendAsync` records the attempt and `EnsureSuccessAsync` puts the
effective id (server echo preferred) on `ApiException.RequestId`, which surfaces as `requestId` in
the JSON error envelope and a trailing `request id:` line on stderr. The command name comes from
`ClientContext`, an AsyncLocal set once in `Program.cs` from `CommandPathResolver` and per MCP tool
call by `McpInvocationFilter` — AsyncLocal because MCP tool calls overlap, and whitelisted because
the tool name comes from the client and becomes a header value.

**Any client call whose path carries an identifier must pass its own `routeTemplate`** (the last
argument on the HTTP helpers, e.g. `"/api/employees/{empId}"`). The route is never inferred from the
finished URL: employee, client and project ids are ordinary strings (`BOB`, `NWIND`) that no
value-shape rule can tell from a path literal. `RouteRedactionTests` drives every such call and
fails if the value reaches the route, so a new parameterised endpoint goes in both places.

Each finished invocation appends a line to the local JSONL log via `CommandLog`; it must never carry
arguments, bodies, keys or employee ids, and no diagnostic step — config read included — may fail
the command. `--verbose` is stripped from argv like `--tenant` and prints to stderr only.

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
- `tp ts update ID --iteration "Sprint 5"` - Update in place, by iteration name or ID (see below)
- `tp project recent` - Surface projects recently logged against (likely picks for new entries)
- `tp leave create --start 2026-03-30 --end 2026-03-30 --type 1 --note "..." --approved-by "email" --cc "e1,e2" --yes` - Create leave (`--dry-run --json` validates without writing)
- `tp leave update ID --start 2026-04-01 --end 2026-04-01 --note "..." --yes` - Update leave while preserving omitted API-returned fields (`--dry-run --json` previews the full payload)
- `tp leave cancel ID --reason "..." --yes` - Cancel leave
- `tp leave list --filter UPCOMING --json` - List leave
- `tp leave balance --emp-id BOB` - Leave-usage signal (days since last leave + hours taken in last 12 months)
- `tp leave balances status` - When leave balances were last imported from Xero, and whether they are stale
- `tp leave balances import ./LeaveBalances.csv --yes` - Import company-wide leave balances from a Xero CSV export (leave admins only)
- `tp feature accounting enable` - Enable accounting skills and accounting MCP tools
- `tp feature developer enable` - Enable developer diagnostics/environment comparison skills and timesheet/finance bug diagnostic skills
- `tp mcp [--tenant NAME]` - Start MCP server (optional per-session tenant binding)

### Leave-aware `tp ts check`

`ts check` merges approved leave + public holidays into weekly coverage. A full-day leave day is `covered` (never an error); a partial-day leave only expects the remaining hours. The `--json` output gives per-day `covered` / `coverReason` (`logged`, `leave-full`, `leave-partial`, `holiday`, `missing`), `leaveHours` / `leaveType`, and a top-level `allCovered`, plus a top-level `pendingSuggestions`. Fetch/merge logic lives in the shared `WeekCoverageService`; the rules — including the closing summary line — are in the pure, unit-tested `CheckEvaluator`. Exit code 1 when errors are found (CI-friendly).

Unaccepted suggested timesheets never reduce coverage, so the summary states them alongside it (`All days covered; 3 suggestions still need accepting`) instead of contradicting it, and exit stays 0. `--strict` makes pending suggestions a failure (exit 1 on both the human and `--json` paths). It gates
suggestions only — on the `--json` path a week with coverage gaps is still data, not a process failure,
so errors alone keep exit 0 there.

### Timesheet export scope

`/Export/ExportTimesheetsToCSV` takes only a date range, so it always returns every employee.
`ts export` therefore scopes the returned file client-side through `TimesheetCsvScoper`: it finds the
`EmpID` column by header name, keeps the logged-in employee's rows by default (`--emp-id` for another,
`--all` for the raw export, warned on stderr), and falls back to the raw export with a warning when
the header has no `EmpID`. The real export quotes fields containing commas and newlines, so the
scoper is RFC 4180-aware — splitting on lines would corrupt the file.

### Timesheet writes

`SaveTimesheet?isEdit=true` replaces the whole row, so every update must read the entry back and
re-send the fields the caller omitted. `TimesheetUpdateService` owns that read-merge (including
resolving an iteration by name or ID) and `TimesheetAcceptService` owns accept; the `ts update` /
`ts accept` commands and the `UpdateTimesheet` / `AcceptSuggestedTimesheet` MCP tools are adapters
over them. Never build a `TimesheetRequest` for an edit anywhere else.

`ts update` and `ts delete` refuse suggested entries locally (`tp ts accept <id>` first) rather than
letting the API answer a bare 400; the MCP delete tool shares that check. Accept fails before the API
call when the project uses iterations and none can be resolved, listing the available ones.

Accept with an iteration is two calls (accept, then update), so it can half-succeed. When the
accepted row cannot be identified unambiguously the iteration is not applied and the result carries
`iterationApplied: false` plus a `warning` whose recovery is always `tp ts update`, never a second
accept — accepting twice would duplicate the entry.

`ts create`, `ts update` and `ts accept` return the saved entry on `--json`
(`{"success":true,"timesheetId":N,"timesheet":{...}}`). The API usually answers writes with an empty
body, so the entry is re-read; callers no longer need a follow-up `ts get --week`.

### `--json` error envelope

On the `--json` path, failures emit a structured envelope to **stdout** so stdout stays valid JSON: `{"error":{"code":<int|null>,"message":"...","detail":<string|null>}}` (all keys always present), with a non-zero exit code. Human-readable error/warning text goes to **stderr**.

This covers Spectre parse/binding failures too (unknown command, unbindable argument), not just errors a command raises itself: `Program.cs` installs `CommandLineErrorHandler` as the Spectre exception handler, which checks argv for `--json` because these failures happen before settings binding. Command-line errors exit 1; an unexpected exception keeps Spectre's -1.

The same handler renders connection failures (refused, timeout, DNS): `tenant` and `apiUrl` are added under `error`, and the stderr text names the active tenant config file, its `apiUrl`, and `tp tenant set`.

API failures go through `OutputHelper.WriteApiError`, which fills `detail` from the response body
(problem details `errors`/`detail`/`message`/`title`, bare JSON strings, otherwise the raw body
truncated to 500 chars). New `catch (ApiException)` blocks should use it rather than formatting the
message by hand.

Unknown commands get a "did you mean" hint from `CommandCatalog`, a hand-mirrored copy of the
registrations in `CliConfiguration`. `CommandCatalogTests` walks the real `--help` tree and fails
when they drift, so a new command goes in both places.

`CliConfiguration` turns on Spectre's strict parsing, so an unknown or mistyped option is a parse
error through the same handler instead of being silently dropped. That means a command only accepts
`--json` if its settings declare it; the envelope is still produced for the parse error, because
`jsonRequested` is read off argv.

### MCP tools + tenant resolution

Default MCP tools cover timesheets, lookups, and leave. Accounting MCP tools are feature-gated behind `tp feature accounting enable`. Before enabling or changing MCP features, ask the user what the MCP use-case is (timesheets, accounting reconciliation, Excel/CSV comparison, Xero/other MCP composition, diagnostics, etc.) so the tool surface can be adjusted deliberately.

MCP tools must not be the only implementation of TimePro behavior. Any business logic or diagnostic report exposed through MCP must be implemented as a CLI command first, with MCP delegating to the same service/report shape. Developer diagnostics are expected to be CLI/skill workflows, not dedicated MCP guide tools.

The MCP host resolves the tenant in this order: `--tenant NAME` → global active tenant → the sole tenant config if exactly one exists (single-tenant installs work without `tp tenant set`). `--tenant` does NOT change the global active tenant.

## Tenants

The `activeTenant` in `config.json` is the **filename** (without `.json`) of the tenant config file, not the `tenantId` property inside it. This allows multiple configs for the same tenant (e.g., `ssw` for prod, `ssw-staging` for staging) where both have `"tenantId": "ssw"` but different `apiUrl` and `apiKey`.

`tp tenant list` keys everything off that filename: it shows `file`, `apiUrl` and the prod/non-prod
environment, and marks the active row by filename (`isActive` in `--json`). `tp info --json` reports
the effective tenant's `apiUrl` and `isProduction`.

`--env` is checked against the resolved config's `apiUrl`, not just its name. `--env prod` resolves
to a config with `isProduction == true` (preferring a production config that shares the base tenant
name) and otherwise fails naming the config file, its `apiUrl`, and the fix. Every other environment
name, known or not, refuses a config that points at production.

`isProduction` is derived from the `apiUrl` **host**, not a substring of the URL, so a local or
staging server cannot look production by carrying a production hostname in its path or query.

## Leave API

The leave create endpoint (`POST /api/leave/`) requires these fields in the request body:
- **Required**: `RequestedEmpId`, `StartDate` (DateTimeOffset), `EndDate` (DateTimeOffset), `LeaveTypeId`, `UserStartTime`, `UserEndTime`, `AllDay`
- **Optional**: `Note`, `OptionalEmp` (CC emails), `ApprovedBy` (email), `TimeLessOverride`

The leave update endpoint (`PUT /api/leave/`) uses the same full payload plus `Id`.
It is a replacement operation, not a patch. All CLI and MCP updates must go through
`LeaveUpdateService`, which reads the existing request and preserves omitted API-returned fields
before calling `UpdateLeaveAsync`.

Older leave-list responses omit `UserStartTime` and `UserEndTime`. Updates preserve
those values when returned; otherwise they use the current employee profile values and
then the 09:00-18:00 defaults. Callers can pass explicit workday times when required.

Partial-day (`AllDay = false`) requests derive `StartDate`/`EndDate` from the date plus
`UserStartTime`/`UserEndTime`; only all-day requests get the 00:00-23:59 normalisation.
The server rejects partial-day times that are not on the hour or half-hour, so
`LeaveRequestParser` applies that rule locally and dry-run fails with the same message.

CLI and MCP leave create/update surfaces support dry-run. Dry-run performs the same
validation and payload preparation, returns the proposed request, and must not call
`CreateLeaveAsync` or `UpdateLeaveAsync`.

The cancel endpoint (`PUT /api/leave/{id}/cancel`) requires `LeaveId` (Guid) and `CancellationReason` in the request body.

## Leave Balances (Xero CSV Sync)

`POST /api/leave/balances/import` takes the raw Xero "Leave Balances" CSV export as the
request body with content type `text/csv`. It is **not** a JSON endpoint and **not** a
multipart upload, so it must go through `PostRawAsync`, never the `PostAsync`/`PutAsync`
JSON helpers - `JsonContent` would send an escaped string literal and the server-side
parser would reject it. `LeaveBalancesApiTests` asserts the body and content type for
exactly this reason.

The endpoint is leave-admin only (`403` otherwise) and returns `422` with the CSV parser's
message as a bare JSON string when the file cannot be read. Both are translated into
readable messages by `LeaveBalanceImportService`, which owns file reading and validation
for the CLI and MCP alike. Import replaces stored balances for every matched employee;
there is no server-side dry-run, so the CLI confirms unless `--yes` is passed.

CLI and MCP surfaces take a **path** to the CSV, not its contents. Tool arguments travel
through an agent's context, where a large CSV is expensive and liable to be silently
truncated into a partial import that still looks successful. `GET /api/leave/balances/status`
is the cheap read used to decide whether a re-import is due.

The read-only MCP status tool is on the default leave surface. The destructive MCP import
tool is available only when the accounting feature pack is enabled.

Rows whose Xero employee name matches no TimePro employee, or matches several, are returned
in `unmatchedEmployees` and skipped rather than guessed at; implausible balances are returned
in `warnings`. The import succeeds regardless, so both lists must be surfaced to the user.

The list endpoint (`GET /api/leave/`) returns per-entry `daysAway`, `updatedAt`, `optionalEmp`, `timeLessOverride`, `cancellationReason` (all bound on `LeaveEntry`) plus a top-level `cancelledCount` on the list envelope. These surface in `tp leave list --json`.

## Accounting API Shapes

Three endpoints back the receipt commands and each returns a different shape, so they need
separate DTOs:

- `GET /api/v2/ClientInvoice/{id}/receipts` → payment rows (`ReceiptRow`): one row per invoice
  a receipt pays, with `paid`.
- `GET /api/receipting/PaidReceiptsPaged` → receipt rows (`PaidReceiptRow`): receipt-level, with
  `paidTotal` and an `invoiceIds` array instead of a single invoice.
- `GET /api/Receipting/details/{id}` → an envelope (`ReceiptDetailResponse`) wrapping the receipt
  under `receipt`, alongside payment-method lookups; its `saleReceiptID` is serialised as a string.

`unit` on the recurring invoice endpoints is the backend `RecurrenceUnit` ordinal
(`0` Year, `1` Month, `2` Day), not a string.

`JsonShape.AssertFullyMapped<T>` in the integration tests fails when a captured payload carries a
property no DTO property binds. Use it on new endpoint tests — it is what catches shape drift
before it shows up as a zeroed field.

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

## Definition of Done

A change is done when all of these hold, not when the code compiles:

- Both test projects pass, and every changed behaviour has a unit or WireMock test that fails without the fix.
- The change was exercised by running the CLI from source against the `ssw-staging` tenant, and the commands and trimmed output are in the PR body.
- The diff was grepped for private data (see Privacy below) and for comment noise.
- The PR body says what was verified by execution and what only by inspection, and names anything left out.
- Release notes are not edited in feature PRs; the release PR builds them from the full tag-to-main range.

## Writing Skills and Instructions

Skill templates live in `Features/Skills/Templates/` and are rendered by `tp skills create`. Keep them
lean: a short root that names the trigger, the commands to run first, and the rules that the CLI cannot
enforce itself. Do not restate `--help` or write step-by-step recipes for things an agent can work out
from `--json` output. Descriptions in `SkillModelBuilder` are one "Use when ..." sentence. Bump
`CurrentSkillVersion` whenever a template or description changes so installed copies report as outdated.

## Production Release Discipline

Before creating any new version, read `release-notes/AGENTS.md` and follow it.
Always verify the versioned release note and `release-notes/latest.md` are
correct on `main` for the full previous-release-tag-to-`origin/main` range
before dispatching the release workflow.

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

## Privacy, Documentation & Examples

This is a **public repository**. Anything you write to it or to GitHub is visible to the world.

**Never put private or identifying information into anything that leaves this machine without the user's explicit permission for that specific item.** That covers commits, commit messages, branch names, GitHub issues, PRs, PR descriptions and comments, release notes, docs, READMEs, tests, fixtures, skill files, example output blocks, and anything emitted by `tp skills create`.

Private or identifying information includes:
- Real client IDs or names (any actual SSW customer)
- Real employee IDs, names, or emails, including the user's own
- Real project codes, timesheet IDs, invoice IDs, or leave request IDs from TimePro
- Real GitHub owner/repo slugs, PR or issue titles from customer work
- Tenant config contents, API keys, hostnames of internal or local servers
- The names of local scripts, skills, or agent tooling used during the work

If real data is genuinely needed to explain a change, ask first. Do not assume permission carries over from one item to the next.

### Placeholders

Use the **Northwind** dataset. A Northwind test client exists in TimePro, so examples can be real runs against it.

- Client: `NWIND` / `"Northwind Traders"`
- Project ID: `1I776Q` (`"Northwind Traders"`)
- User: `Bob Northwind`, employee ID `BOB`, email `bob@northwind.example`
- GitHub repo: `Northwind/traders-app`, `Northwind/traders-mobile`
- PR/issue numbers: `#42`, `#108`, `#142`, any small integers
- Example descriptions: "Product search", "Checkout API", "Order history"

SSW itself and this repo are acceptable secondary examples when a Northwind example does not make sense (for example, the `ssw` tenant name in login instructions).

Before committing or opening an issue or PR, grep the change for anything that looks like a real customer name, a real employee ID, a real-world repo slug, or an internal project code. If in doubt, replace it with a Northwind variant.

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
