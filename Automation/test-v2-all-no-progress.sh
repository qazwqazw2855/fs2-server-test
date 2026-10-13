#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
test "${GOD2_RUN_DB_INTEGRATION:-}" = "1"

results="$(mktemp -d /tmp/god2-v2-grouped-XXXXXX)"
printf '分組測試紀錄：%s\n' "$results"

special=(
  CharacterPositionCommittedTests
  MerchantPurchaseIntegrationTests
  CharacterEconomySnapshotIntegrationTests
  MerchantPurchaseTcpReconnectIntegrationTests
  MerchantPurchaseTcpLostResultIntegrationTests
  MerchantCommandIntegrationTests
  MerchantSaleTcpLostResultIntegrationTests
  InventoryRestoreTcpBlockedIntegrationTests
  QuestProcessRecoveryTests
  MerchantBuySellTcpIntegrationTests
  MerchantSaleIntegrationTests
  MerchantPurchaseJournalIntegrationTests
  MerchantPurchaseTcpIntegrationTests
  InventoryGrantStackCommittedTests
  MerchantPurchaseTcpNewServerIntegrationTests
  MerchantPurchaseJournalProcessRecoveryTests
  MerchantSaleJournalProcessRecoveryTests
  QuestRewardClaimCommittedTests
  QuestProgressClosedLoopTests
  QuestAcceptanceClosedLoopTests
  InventoryGrantConcurrencyTests
)
filter=""
for name in "${special[@]}"; do
  test -z "$filter" || filter+="&"
  filter+="FullyQualifiedName!~$name"
done

for suite in Core Application Session Protocol Network Persistence.Integration; do
  if test "$suite" = "Persistence.Integration"; then
    project="tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj"
    args=(--filter "$filter")
  else
    project="tests/God2.ServerV2.$suite.Tests/God2.ServerV2.$suite.Tests.csproj"
    args=()
  fi

  log="$results/$suite.log"
  if dotnet test "$project" -c Release "${args[@]}" \
    --logger "trx;LogFileName=$suite.trx" \
    --results-directory "$results" --verbosity minimal >"$log" 2>&1
  then
    rg 'Passed!|Failed!' "$log"
  else
    tail -n 55 "$log"
    exit 1
  fi
done

scripts=(
  test-v2-position-committed-isolated.sh
  test-v2-merchant-purchase-isolated.sh
  test-v2-economy-snapshot-isolated.sh
  test-v2-merchant-tcp-reconnect-isolated.sh
  test-v2-merchant-tcp-lost-result-isolated.sh
  test-v2-merchant-command-isolated.sh
  test-v2-inventory-restore-blocked-isolated.sh
  test-v2-quest-process-recovery-isolated.sh
  test-v2-merchant-buy-sell-tcp-isolated.sh
  test-v2-merchant-sale-isolated.sh
  test-v2-merchant-tcp-isolated.sh
  test-v2-inventory-stack-committed-isolated.sh
  test-v2-inventory-grant-concurrency-isolated.sh
  test-v2-merchant-tcp-new-server-isolated.sh
  test-v2-merchant-recovery-isolated.sh
)

for script in "${scripts[@]}"; do
  printf '\n=== %s ===\n' "$script"
  log="$results/$script.log"
  if bash "Automation/$script" >"$log" 2>&1; then
    rg 'Passed!|remaining_|exit code:|exit code' "$log" || true
  else
    tail -n 60 "$log"
    exit 1
  fi
done

python3 - "$results" <<'PY'
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1])
paths = sorted(root.glob("*.trx"))
assert len(paths) == 6
total = passed = 0
for path in paths:
    doc = ET.parse(path).getroot()
    a = next(n for n in doc.iter() if n.tag.endswith("Counters")).attrib
    n = int(a["total"])
    p = int(a.get("passed", 0))
    assert n > 0 and p == n and int(a.get("executed", 0)) == n, path
    total += n
    passed += p
print(f"六套基礎分組：{passed}/{total}，0 failed，0 skipped")

logs = sorted(root.glob("test-v2-*.sh.log"))
assert len(logs) == 15
executions = 0
pattern = r"Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)"
for path in logs:
    matches = re.findall(pattern, path.read_text())
    assert matches, f"缺少測試摘要：{path}"
    for f, p, s, n in matches:
        f, p, s, n = map(int, (f, p, s, n))
        assert n > 0 and p == n and f == s == 0, path
        executions += n
print(f"15 支 fixture 腳本：{executions} 次測試執行全部通過")
print("fixture 執行含前置案例重跑，不與基礎分組相加為唯一測試數。")
PY
