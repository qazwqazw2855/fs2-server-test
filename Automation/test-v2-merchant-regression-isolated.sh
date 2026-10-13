#!/usr/bin/env bash
set -euo pipefail
test "$#" -eq 0
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
test "$(git branch --show-current)" = "codex/inventory-grant-20261003"

evidence="$(mktemp -d docs/handoff/merchant-3310-regression-$(date +%Y%m%d-%H%M%S)-XXXXXX)"
git rev-parse HEAD >"$evidence/source-commit.txt"
echo "evidence=$evidence"

scenarios=(
  buy sell command tcp basic newserver reconnect
  lostresult economy restoreblocked atomicbuy atomicsell
)

for scenario in "${scenarios[@]}"; do
  echo "===== $scenario ====="
  log="$evidence/$scenario.log"
  if GOD2_MERCHANT_RECOVERY_SCENARIO="$scenario" \
    bash Automation/test-v2-merchant-isolated-3310.sh >"$log" 2>&1
  then
    rg 'Passed!|PASS: exit cleanup verification completed' "$log"
  else
    tail -n 60 "$log"
    exit 1
  fi
done

project="tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj"
echo "===== VALIDATOR / DATABASE GUARD ====="
if dotnet test "$project" --configuration Release --no-build \
  --filter 'FullyQualifiedName~MerchantRecoveryReceiptValidatorTests|FullyQualifiedName~MerchantRecoveryDatabaseGuardTests' \
  --logger 'trx;LogFileName=unit.trx' \
  --results-directory "$evidence" --verbosity minimal \
  >"$evidence/unit.log" 2>&1
then
  rg 'Passed!' "$evidence/unit.log"
else
  tail -n 60 "$evidence/unit.log"
  exit 1
fi

python3 - "$evidence" <<'PY_VERIFY'
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

folder = Path(sys.argv[1])
scenarios = [
    "buy", "sell", "command", "tcp", "basic", "newserver",
    "reconnect", "lostresult", "economy", "restoreblocked",
    "atomicbuy", "atomicsell",
]
pattern = (
    r"Failed:\s*(\d+),\s*Passed:\s*(\d+),"
    r"\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)"
)
total = 0
for scenario in scenarios:
    log = (folder / f"{scenario}.log").read_text()
    matches = re.findall(pattern, log)
    expected = 2 if scenario in ("buy", "sell") else 1
    assert len(matches) == expected, f"STOP: phase count: {scenario}"
    for match in matches:
        assert tuple(map(int, match)) == (0, 1, 0, 1), scenario
        total += 1
    assert log.count("PASS: exit cleanup verification completed") == 1, scenario
    assert "PASS: temporary DB user removed" in log, scenario
    assert "PASS: transactional cleanup committed; fixture identities absent" in log, scenario
    if scenario in ("atomicbuy", "atomicsell"):
        assert "PASS: all atomic fault users and triggers absent" in log, scenario

counts = ET.parse(folder / "unit.trx").getroot().find(".//{*}Counters")
assert counts is not None
assert all(counts.get(k) == "36" for k in ("total", "executed", "passed"))
assert counts.get("failed") == "0"
assert counts.get("notExecuted") == "0"
assert total == 14

summary = (
    "PASS: isolated merchant integration 14/14\n"
    "PASS: validator/database guard 36/36\n"
    "PASS: all 12 fixture cleanup runs completed\n"
    "Full grouped regression not executed; official evidence gates remain Blocked.\n"
)
(folder / "summary.txt").write_text(summary)
print(summary, end="")
PY_VERIFY
