#!/bin/bash
# Staging MCP stdio smoke: discovery against the checked-in golden, Northwind reads, one marked
# write on a project that uses iterations, and cleanup. Cleanup failure fails the run and names
# the leftover row. See README.md for the environment variables.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "  Testing: tp mcp (stdio) against staging"

if ! command -v python3 &> /dev/null; then
    echo "    SKIPPED: python3 not found"
    exit 0
fi

if [ -z "${TIMEPRO_MCP_SMOKE_PROJECT:-}" ]; then
    echo "    SKIPPED: TIMEPRO_MCP_SMOKE_PROJECT not set (see scripts/e2e/README.md)"
    exit 0
fi

# Serialise the command so "dotnet path/to.dll" works as well as a bare "tp", with shell quoting.
TIMEPRO_MCP_SMOKE_TP=$(python3 -c 'import json,shlex,sys; print(json.dumps(shlex.split(sys.argv[1])))' \
    "${TIMEPRO_MCP_SMOKE_TP:-tp}")
export TIMEPRO_MCP_SMOKE_TP

python3 "$SCRIPT_DIR/mcp_smoke.py"
