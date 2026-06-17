# Testing Strategy

## Three Testing Layers

| Layer | Runner | External Dependencies | Location |
|-------|--------|-----------------------|----------|
| **Unit Tests** | `dotnet test` | None -- pure logic | `tests/SSW.TimePro.Cli.Tests/` |
| **Integration Tests** | `dotnet test` | Mocked HTTP via **WireMock.Net** | `tests/SSW.TimePro.Cli.Integration/` |
| **E2E Script Tests** | Shell scripts | Real staging API via CLI + mcp-cli | `scripts/e2e/` |

## Unit Tests (`SSW.TimePro.Cli.Tests`)

- Pure logic: config service, week rendering, date calculations, JSON output formatting, lock detection, rate expiry checks
- No HTTP, no file system (or use in-memory abstractions)
- NSubstitute for interface mocking (ConfigService, etc.)
- Fast, run on every commit

### Key test areas

- Config serialization/deserialization
- Week date range calculation (this week, last week, offset)
- Compact/detailed output rendering
- Lock detection logic
- Rate expiry detection and warnings
- Repo mapping path matching (glob patterns)
- Location default resolution by day of week

## Integration Tests (`SSW.TimePro.Cli.Integration`)

Uses **WireMock.Net** to spin up an in-process HTTP server with stubbed API responses.

### What we test

- Full command pipeline: command parsing -> API client -> response handling -> output
- Auth header injection (x-timepro-tenant-id, x-timepro-api-key, x-timepro-api-name)
- All API endpoint paths and query parameters are correct
- Error handling (401, 404, 500, rate expired, locked timesheet)
- Pagination parameters
- Response deserialization of all DTO shapes

### Fixtures

Realistic JSON response files captured from staging API, committed under `tests/SSW.TimePro.Cli.Integration/Fixtures/`:

- `timesheets-day.json` -- Day view response
- `timesheets-week.json` -- Multiple days
- `clients-search.json` -- Client search results
- `projects-list.json` -- Projects for a client
- `rate-active.json` -- Active client rate
- `rate-expired.json` -- Expired rate (for testing warnings)
- `leave-list.json` -- Leave entries
- `appointments.json` -- CRM bookings
- `user-me.json` -- Current user info
- `employee-id.json` -- Employee ID response
- `error-locked.json` -- Locked timesheet error

### Example Pattern

```csharp
// Arrange
_wireMock.Given(
    Request.Create()
        .WithPath("/api/Timesheets/GetTimesheetListViewModel")
        .WithParam("employeeID", "TST")
        .WithParam("date", "2026-03-12")
        .WithHeader("x-timepro-tenant-id", "ssw")
        .WithHeader("x-timepro-api-key", "test-key")
        .UsingGet()
).RespondWith(
    Response.Create()
        .WithStatusCode(200)
        .WithBodyFromFile("Fixtures/timesheets-day.json")
);

// Act -- run the command through the real command pipeline
var result = await RunCommand("timesheet", "get", "2026-03-12", "--json");

// Assert
result.ExitCode.Should().Be(0);
var timesheets = JsonSerializer.Deserialize<List<TimesheetDto>>(result.Output);
timesheets.Should().HaveCount(3);
```

### Test Infrastructure

`TestBase.cs` provides:
- WireMock server lifecycle (start/stop per test or per class)
- Helper method `RunCommand(params string[] args)` that runs the CLI with test config pointing to WireMock URL
- Pre-configured test tenant config pointing at WireMock base URL
- Capture of stdout/stderr for assertions

## E2E Script Tests (`scripts/e2e/`)

Shell scripts that invoke the real `tp` CLI binary and `mcp-cli` against the staging API.

### Prerequisites

- `tp` CLI installed (`dotnet tool install -g SSW.TimePro.Cli`)
- Staging credentials available via environment variables
- `mcp-cli` installed for MCP tool testing

### Environment Variables

```bash
export TIMEPRO_E2E_API_KEY="..."
export TIMEPRO_E2E_TENANT="ssw"
export TIMEPRO_E2E_API_URL="https://api.staging-sswtimepro.com"
```

### Scripts

```
scripts/e2e/
├── README.md                    # Setup instructions
├── test-login.sh                # Login flow, tenant switching
├── test-timesheet-crud.sh       # Get, create, update, delete timesheets
├── test-timesheet-week.sh       # Week view (compact + detailed)
├── test-leave.sh                # Leave CRUD
├── test-bookings.sh             # CRM bookings
├── test-lookups.sh              # Client search, projects, rates
├── test-mcp-tools.sh            # MCP tool invocations via mcp-cli
└── run-all.sh                   # Run all E2E tests
```

### Example Script

```bash
#!/bin/bash
# scripts/e2e/test-timesheet-get.sh
set -euo pipefail

echo "=== E2E: Timesheet Get ==="

# Setup
tp login --tenant "$TIMEPRO_E2E_TENANT" \
    --token "$TIMEPRO_E2E_API_KEY" \
    --api-url "$TIMEPRO_E2E_API_URL"

# Test: Get timesheets for a specific date (JSON)
result=$(tp ts get 2026-03-12 --json)
echo "$result" | jq -e 'type == "array"' > /dev/null
echo "  tp ts get [DATE] --json works"

# Test: Get week view (JSON)
result=$(tp ts get --week --json)
echo "$result" | jq -e '.weekStart' > /dev/null
echo "  tp ts get --week --json works"

# Test: Human-readable output doesn't crash
tp ts get --week > /dev/null 2>&1
echo "  tp ts get --week (human) works"

echo "=== E2E: Timesheet Get PASSED ==="
```

## NuGet Packages

| Package | Project | Purpose |
|---------|---------|---------|
| `WireMock.Net` | Integration tests | HTTP mock server |
| `FluentAssertions` | Both test projects | Assertion library |
| `xunit` | Both test projects | Test framework |
| `xunit.runner.visualstudio` | Both test projects | VS test runner |
| `NSubstitute` | Unit tests | Interface mocking |
| `Microsoft.NET.Test.Sdk` | Both test projects | Test SDK |
| `coverlet.collector` | Both test projects | Code coverage |

## CI Integration

- Unit tests + Integration tests run on every PR (no secrets needed)
- E2E tests run manually or on a schedule with secure credentials
- All tests use `dotnet test` except E2E which uses shell scripts
