# Testing Strategy

The test suite is split into fast local tests, WireMock-backed integration tests, and optional staging E2E scripts.

## Test Layers

| Layer | Command | External dependency | Purpose |
|-------|---------|---------------------|---------|
| Unit tests | `dotnet test tests/SSW.TimePro.Cli.Tests/` | None | Pure logic and serialization checks. |
| Integration tests | `dotnet test tests/SSW.TimePro.Cli.Integration/` | WireMock.Net only | Command/API integration without live TimePro calls. |
| E2E scripts | `./scripts/e2e/run-all.sh` | Staging TimePro credentials | Smoke test the installed CLI against staging. |

## Unit Tests

Unit tests cover code that can run without HTTP or live credentials:

- Config serialization and deserialization
- Tenant summary serialization that omits secrets
- JSON output formatting
- Date and week calculations
- Repo mapping path and remote matching
- Location default resolution
- Small command helpers where behavior is deterministic

Keep unit tests fast and deterministic. Prefer test doubles for interfaces and temporary directories for file-system checks.

## Integration Tests

Integration tests use WireMock.Net to run the real command/API path against stubbed HTTP responses.

They should cover:

- Command parsing through the real command pipeline
- Auth header injection
- API endpoint paths and query parameters
- Request body shape for write commands
- Response deserialization
- Error handling for common TimePro failures
- JSON output that downstream tools can parse

Fixtures live under `tests/SSW.TimePro.Cli.Integration/Fixtures/`. Fixture data must be sanitized and use Northwind placeholders for client, project, repo, and user-facing example values.

## E2E Scripts

E2E scripts live under `scripts/e2e/` and run against staging through the installed `tp` tool.

Required environment variables:

```bash
export TIMEPRO_E2E_API_KEY="..."
export TIMEPRO_E2E_TENANT="ssw"
export TIMEPRO_E2E_API_URL="https://api.staging-sswtimepro.com"
```

Use E2E tests for smoke coverage only. Do not put raw credentials, live client names, or live project names in scripts, fixtures, logs, or docs.

## CI Expectations

- Unit and integration tests should be safe to run on every PR.
- E2E tests should be manual or scheduled with secured staging credentials.
- Tests that verify secret hygiene should assert on field names and JSON structure, never on real secret values.
