#!/usr/bin/env bash
set -euo pipefail
test "$#" -eq 0
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
test "$(git branch --show-current)" = "codex/inventory-grant-20261003"

for scenario in buy sell; do
  echo "===== ISOLATED 3310: $scenario ====="
  GOD2_MERCHANT_RECOVERY_SCENARIO="$scenario" \
    bash Automation/test-v2-merchant-isolated-3310.sh
done

echo "PASS: isolated BUY/SELL recovery wrapper completed"
