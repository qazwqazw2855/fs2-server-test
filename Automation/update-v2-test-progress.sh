#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PROGRESS="progress.json"
RESULT_DIR="$(mktemp -d)"
trap 'rm -rf "$RESULT_DIR"' EXIT

projects=(
  "Core|tests/God2.ServerV2.Core.Tests/God2.ServerV2.Core.Tests.csproj"
  "Application|tests/God2.ServerV2.Application.Tests/God2.ServerV2.Application.Tests.csproj"
  "Session|tests/God2.ServerV2.Session.Tests/God2.ServerV2.Session.Tests.csproj"
  "Protocol|tests/God2.ServerV2.Protocol.Tests/God2.ServerV2.Protocol.Tests.csproj"
  "Network|tests/God2.ServerV2.Network.Tests/God2.ServerV2.Network.Tests.csproj"
  "Persistence Integration|tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj"
)

suites='[]'
total_passed=0
total_failed=0

for entry in "${projects[@]}"; do
    name="${entry%%|*}"
    project="${entry#*|}"

    echo "=== TEST: $name ==="

    safe="$(printf '%s' "$name" | tr ' /' '__')"
    trx="$RESULT_DIR/$safe.trx"

    dotnet test "$project" \
      --configuration Release \
      --logger "trx;LogFileName=$safe.trx" \
      --results-directory "$RESULT_DIR" \
      --verbosity minimal

    read -r passed failed <<<"$(
      python3 - "$trx" <<'PY'
import sys
import xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()

counters = None
for node in root.iter():
    if node.tag.endswith("Counters"):
        counters = node
        break

if counters is None:
    raise SystemExit("TRX Counters not found")

print(
    int(counters.attrib.get("passed", 0)),
    int(counters.attrib.get("failed", 0))
)
PY
    )"

    total_passed=$((total_passed + passed))
    total_failed=$((total_failed + failed))

    suites="$(
      jq \
        --arg name "$name" \
        --argjson passed "$passed" \
        --argjson failed "$failed" \
        '. + [{
            name: $name,
            passed: $passed,
            failed: $failed
        }]' <<<"$suites"
    )"
done

total=$((total_passed + total_failed))

tmp="$(mktemp)"

jq \
  --argjson suites "$suites" \
  --argjson total "$total" \
  --argjson passed "$total_passed" \
  --argjson failed "$total_failed" \
  '
    .verification.suites = $suites
    | .verification.total_tests = $total
    | .verification.passed_tests = $passed
    | .verification.failed_tests = $failed
    | .verification.build =
        (if $failed == 0 then "passing" else "failing" end)
  ' "$PROGRESS" > "$tmp"

mv "$tmp" "$PROGRESS"

echo
echo "=== VERIFICATION UPDATED ==="
jq '.verification | {
    build,
    total_tests,
    passed_tests,
    failed_tests,
    suites
}' "$PROGRESS"

if (( total_failed != 0 )); then
    echo "ERROR: V2 tests failed."
    exit 1
fi
