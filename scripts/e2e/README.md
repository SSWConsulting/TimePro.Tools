# E2E Tests

End-to-end tests that run against the real staging API.

## Prerequisites

- `tp` CLI installed as a global tool
- Staging API credentials

## Setup

```bash
export TIMEPRO_E2E_API_KEY="your-staging-api-key"
export TIMEPRO_E2E_TENANT="ssw"
export TIMEPRO_E2E_API_URL="https://api.staging-sswtimepro.com"
```

## Run

```bash
./scripts/e2e/run-all.sh
```

## MCP stdio smoke (`test-mcp-smoke.sh`)

Drives the candidate artifact's real `tp mcp` over stdio: discovery against the checked-in golden,
Northwind reads, one uniquely marked short write, read-back, then deletion of only that row and a
check that it is gone. It refuses to run unless the resolved tenant is non-production, and a
cleanup it cannot complete fails the run and prints how to find the leftover row.

| variable | required | purpose |
|---|---|---|
| `TIMEPRO_MCP_SMOKE_PROJECT` | yes | A `NWIND` project **that uses iterations**, e.g. `1I776Q`. No default: project ids are not committed to this public repository. The script skips when it is unset. |
| `TIMEPRO_MCP_SMOKE_ITERATION` | no | Pin a specific iteration id. Otherwise the first iteration the project reports is used. |
| `TIMEPRO_MCP_SMOKE_TP` | no | How to invoke the candidate CLI. Defaults to `tp`; shell-quoted, so `"dotnet path/to/SSW.TimePro.Cli.dll"` works. |
| `TIMEPRO_MCP_SMOKE_TENANT` | no | Tenant config to bind. Defaults to `ssw-staging`. |
| `TIMEPRO_MCP_SMOKE_CATEGORY` | no | Timesheet category for the written row. Defaults to `WEBDEV`. |

```bash
export TIMEPRO_MCP_SMOKE_PROJECT="1I776Q"
TIMEPRO_MCP_SMOKE_TP="dotnet src/SSW.TimePro.Cli/bin/Release/net10.0/SSW.TimePro.Cli.dll" \
  ./scripts/e2e/test-mcp-smoke.sh
```

## Adding Tests

Create a new `test-*.sh` script in this directory. It will be picked up automatically by `run-all.sh`.
