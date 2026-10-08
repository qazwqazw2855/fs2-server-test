#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# Explicit test-only mode preserves the existing fixture chain.
if test "${GOD2_V2_TEST_ONLY:-}" = "1"; then
  exec bash "$ROOT/Automation/test-v2-all-no-progress.sh"
fi

echo '========================================'
echo ' God2 Server V2 Progress Update'
echo '========================================'

echo
echo '[1/2] 執行 V2 六個測試專案並更新 verification...'
./Automation/update-v2-test-progress.sh

echo
echo '[2/2] 更新 Roadmap / Full Playable / timestamp...'
./Automation/update-server-progress.sh

echo
echo '=== FINAL PROGRESS ==='
jq '{
  overall_percent,
  updated_at,
  verification: {
    build: .verification.build,
    passed: .verification.passed_tests,
    total: .verification.total_tests
  },
  roadmap: {
    current_key: .roadmap.current_key,
    promotion_ready: .roadmap.promotion_ready
  },
  playability: {
    percent: .playability.percent,
    ready: .playability.ready
  }
}' progress.json

echo
echo '=== GIT STATUS ==='
git status --short

echo
echo 'Progress 更新完成。'
echo '注意：尚未自動 commit / push。'
