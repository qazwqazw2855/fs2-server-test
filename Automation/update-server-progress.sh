#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PROGRESS="progress.json"

command -v jq >/dev/null || {
    echo "ERROR: jq not found"
    exit 1
}

jq -e . "$PROGRESS" >/dev/null

updated_at="$(TZ=Asia/Taipei date '+%Y-%m-%dT%H:%M:%S+08:00')"

# Full Playable：
# completed = 100% 權重
# in_progress = 50% 權重
# pending = 0%
playability_percent="$(
    jq '
      [
        .playability.gates[] |
        if .status == "completed" then .weight
        elif .status == "in_progress" then (.weight / 2)
        else 0
        end
      ] | add
    ' "$PROGRESS"
)"

# 找第一個進行中的 Roadmap 階段作為 current_key。
current_key="$(
    jq -r '
      [.roadmap.stages[] | select(.status == "in_progress")][0].key
      // .roadmap.current_key
    ' "$PROGRESS"
)"

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

jq \
  --arg updated_at "$updated_at" \
  --arg current_key "$current_key" \
  --argjson playable "$playability_percent" \
  '
    .updated_at = $updated_at
    | .roadmap.current_key = $current_key
    | .playability.percent = $playable
    | .playability.ready =
        ([.playability.gates[].status] | all(. == "completed"))
    | .roadmap.promotion_ready =
        ([.roadmap.stages[].status] | all(. == "completed"))
  ' "$PROGRESS" > "$tmp"

jq -e '
    (.roadmap.stages | length) > 0
    and (.playability.gates | length) > 0
    and (.playability.percent >= 0)
    and (.playability.percent <= 100)
' "$tmp" >/dev/null

mv "$tmp" "$PROGRESS"
trap - EXIT

echo "=== SERVER PROGRESS UPDATED ==="
jq '{
    updated_at,
    overall_percent,
    current_focus: .current_focus.title,
    current_key: .roadmap.current_key,
    promotion_ready: .roadmap.promotion_ready,
    full_playable_percent: .playability.percent,
    full_playable_ready: .playability.ready
}' "$PROGRESS"

echo
echo "注意：overall_percent 不由腳本自行推算。"
echo "Roadmap / Playability Gate 狀態仍必須由 evidence 明確升級。"
