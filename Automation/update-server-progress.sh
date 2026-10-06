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

# current_key 不由 updater 自動猜測。
# Portal / NPC / Gameplay 可以同時 in_progress，
# 因此目前主線必須由 progress.json 明確指定。

protected_before="$(
    jq -c '{
        current_focus,
        current_validation,
        open_questions,
        paused_items,
        next_steps,
        recent_activity
    }' "$PROGRESS"
)"

# Validate the manually selected development focus.
jq -e '
    .roadmap.current_key as $key
    | ($key | type) == "string"
      and ($key | length) > 0
      and any(.roadmap.stages[]; .key == $key)
' "$PROGRESS" >/dev/null || {
    echo "ERROR: invalid roadmap.current_key"
    exit 1
}

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

jq \
  --arg updated_at "$updated_at" \
  --argjson playable "$playability_percent" \
  '
    .updated_at = $updated_at
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

protected_after="$(
    jq -c '{
        current_focus,
        current_validation,
        open_questions,
        paused_items,
        next_steps,
        recent_activity
    }' "$tmp"
)"

if [[ "$protected_before" != "$protected_after" ]]; then
    echo "ERROR: protected progress fields changed"
    echo "The updater is not allowed to modify:"
    echo "  current_focus"
    echo "  current_validation"
    echo "  open_questions"
    echo "  paused_items"
    echo "  next_steps"
    echo "  recent_activity"
    exit 1
fi

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
