#!/usr/bin/env bash
set -euo pipefail
test "$#" -eq 0
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
GOD2_MERCHANT_RECOVERY_SCENARIO=stack \
  bash Automation/test-v2-merchant-isolated-3310.sh
