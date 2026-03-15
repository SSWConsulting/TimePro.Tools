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

## Adding Tests

Create a new `test-*.sh` script in this directory. It will be picked up automatically by `run-all.sh`.
