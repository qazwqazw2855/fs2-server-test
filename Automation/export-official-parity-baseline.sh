#!/usr/bin/env bash
set -Eeuo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"
registry="docs/parity/official-parity-registry.json"
mappings="protocol/evidence/current-build/runtime-mappings.json"
gates="protocol/evidence/current-build/classification-gates.json"
classification="protocol/evidence/current-build/live-classification.json"
filter="Automation/official-parity-baseline.jq"
map_baseline="docs/parity/map.json"
json_out="docs/parity/official-parity-baseline.json"
md_out="docs/parity/official-parity-baseline.md"

for file in "$registry" "$mappings" "$gates" "$classification" "$map_baseline" progress.json; do
  test -f "$file" || { echo "Missing input: $file" >&2; exit 1; }
  jq empty "$file"
done

jq -e '
  length > 0 and
  all(.[]; .status == "RuntimeMutationBlocked") and
  all(.[]; .runtimeIntegrated == false) and
  all(.[]; .directDatabaseAccessAllowed == false) and
  all(.[]; .exactlyOnceRequired == true)
' "$mappings" >/dev/null

jq -e '
  (.systems | length) > 0 and
  ([.systems[].id] | length) == ([.systems[].id] | unique | length)
' "$registry" >/dev/null

json_tmp="$(mktemp)"
md_tmp="$(mktemp)"
trap 'rm -f "$json_tmp" "$md_tmp"' EXIT

jq -n \
  --slurpfile registry "$registry" \
  --slurpfile mappings "$mappings" \
  --slurpfile gates "$gates" \
  --slurpfile classification "$classification" \
  --slurpfile mapBaseline "$map_baseline" \
  --slurpfile progress progress.json \
  -f "$filter" >"$json_tmp"

{
  echo '# God2 Official Parity Baseline'
  echo
  jq -r '"Progress source: `\(.generatedFromProgressAt)`\n\nOverall: **\(.projectProgress.overallPercent)%**\n\n## Evidence-set boundary\n\n- Runtime mappings: **\(.protocolEvidenceSets.runtimeMappings.count)**; blocked: **\(.protocolEvidenceSets.runtimeMappings.runtimeMutationBlocked)**; integrated: **\(.protocolEvidenceSets.runtimeMappings.runtimeIntegrated)**.\n- Evidence routes: **\(.protocolEvidenceSets.evidenceRoutes.count)**; candidate gate count: **\(.protocolEvidenceSets.evidenceRoutes.candidateGateCount)**.\n- Unknown-new: **\(.protocolEvidenceSets.liveClassification.unknownNew)**; actionable: **\(.protocolEvidenceSets.liveClassification.unknownActionable)**.\n- Classification build id: `\(.protocolEvidenceSets.liveClassification.clientBuildId)`.\n\n> \(.protocolEvidenceSets.boundary)\n\n## System gap registry\n\n| Priority | System | Data | Logic | Wire | Real client | Next gate |\n|---:|---|---|---|---|---|---|"' "$json_tmp"
  jq -r '.systems[] | "| \(.priority) | \(.name) | \(.data) | \(.logic) | \(.wire) | \(.realClient) | \(.nextGate) |"' "$json_tmp"
  echo
  echo '## Proven scope and gaps'
  jq -r '.systems[] | "\n### \(.name)\n\n- Proven scope: \(.scope)\n- Gap: \(.gap)\n- Authorities: `\(.authorities | join("`, `"))`"' "$json_tmp"
} >"$md_tmp"

mv "$json_tmp" "$json_out"
mv "$md_tmp" "$md_out"
trap - EXIT
jq empty "$json_out"
echo "Wrote $json_out"
echo "Wrote $md_out"
