#!/bin/bash
# Staging MCP stdio smoke: discovery against the checked-in golden, Northwind reads, one marked
# write on a project that uses iterations, and cleanup. Cleanup failure fails the run and names
# the leftover row.
#
#   TIMEPRO_MCP_SMOKE_TP      how to invoke the candidate CLI (default: tp)
#   TIMEPRO_MCP_SMOKE_TENANT  tenant config to bind (default: ssw-staging)
#   TIMEPRO_MCP_SMOKE_PROJECT Northwind project that uses iterations (default: 8W52M2)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "  Testing: tp mcp (stdio) against staging"

if ! command -v python3 &> /dev/null; then
    echo "    SKIPPED: python3 not found"
    exit 0
fi

TP_COMMAND=${TIMEPRO_MCP_SMOKE_TP:-tp}

# Serialise the command as JSON so "dotnet run --project ... --" works as well as a bare "tp".
TIMEPRO_MCP_SMOKE_TP=$(python3 -c 'import json,sys; print(json.dumps(sys.argv[1].split()))' "$TP_COMMAND")
export TIMEPRO_MCP_SMOKE_TP

python3 "$SCRIPT_DIR/mcp_smoke.py"
