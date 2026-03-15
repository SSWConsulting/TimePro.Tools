#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "================================="
echo "SSW TimePro CLI - E2E Test Suite"
echo "================================="
echo ""

# Check prerequisites
if ! command -v tp &> /dev/null; then
    echo "Error: 'tp' CLI not found. Install with:"
    echo "  dotnet pack src/SSW.TimePro.Cli/"
    echo "  dotnet tool install -g --add-source src/SSW.TimePro.Cli/nupkg SSW.TimePro.Cli"
    exit 1
fi

if [ -z "${TIMEPRO_E2E_API_KEY:-}" ]; then
    echo "Error: TIMEPRO_E2E_API_KEY not set"
    echo "Usage: TIMEPRO_E2E_API_KEY=<key> TIMEPRO_E2E_TENANT=ssw ./run-all.sh"
    exit 1
fi

TENANT="${TIMEPRO_E2E_TENANT:-ssw}"
API_URL="${TIMEPRO_E2E_API_URL:-https://api.staging-sswtimepro.com}"

echo "Tenant: $TENANT"
echo "API URL: $API_URL"
echo ""

# Login
echo "--- Login ---"
tp login --tenant "$TENANT" --token "$TIMEPRO_E2E_API_KEY" --api-url "$API_URL"
echo ""

# Run test scripts
PASSED=0
FAILED=0

for test_script in "$SCRIPT_DIR"/test-*.sh; do
    test_name=$(basename "$test_script" .sh)
    echo "--- Running: $test_name ---"
    if bash "$test_script"; then
        echo "  PASSED: $test_name"
        PASSED=$((PASSED + 1))
    else
        echo "  FAILED: $test_name"
        FAILED=$((FAILED + 1))
    fi
    echo ""
done

echo "================================="
echo "Results: $PASSED passed, $FAILED failed"
echo "================================="

[ "$FAILED" -eq 0 ]
