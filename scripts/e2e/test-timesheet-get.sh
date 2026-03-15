#!/bin/bash
set -euo pipefail

echo "  Testing: tp ts get (JSON)"
result=$(tp ts get --json 2>/dev/null || true)
if echo "$result" | python3 -c "import sys,json; json.load(sys.stdin)" 2>/dev/null; then
    echo "    tp ts get --json: valid JSON"
else
    echo "    tp ts get --json: FAILED - invalid JSON output"
    exit 1
fi

echo "  Testing: tp ts get --week (JSON)"
result=$(tp ts get --week --json 2>/dev/null || true)
if echo "$result" | python3 -c "import sys,json; d=json.load(sys.stdin); assert 'weekStart' in d" 2>/dev/null; then
    echo "    tp ts get --week --json: valid JSON with weekStart"
else
    echo "    tp ts get --week --json: FAILED"
    exit 1
fi

echo "  Testing: tp ts get --week (human output)"
tp ts get --week > /dev/null 2>&1
echo "    tp ts get --week: renders without error"
