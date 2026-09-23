#!/usr/bin/env bash
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

container="${GOD2_DB_CONTAINER:-god2-runtime-db-test}"
artifact="db/imports/official/portals/portals.official.json"
output_json="docs/parity/portal.json"
output_md="docs/parity/portal.md"

for command_name in git jq comm sha256sum sudo docker; do
  command -v "$command_name" >/dev/null ||
    { echo "Missing command: $command_name" >&2; exit 1; }
done

test -f "$artifact" || {
  echo "Missing input: $artifact" >&2
  exit 1
}

mkdir -p docs/parity
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

db_query() {
  local sql="$1"
  printf '%s\n' "$sql" |
    sudo docker exec -i "$container" sh -lc '
root_password="$(cat "$MARIADB_ROOT_PASSWORD_FILE")"
exec mariadb -uroot -p"$root_password" \
  --batch --raw --skip-column-names
'
}

printf '%s\n' 1 2 3 4 170015007 |
  LC_ALL=C sort -u > "$work_dir/expected.ids"

db_query '
SELECT portal_id
FROM god2_game.portals
WHERE enabled=1;
' |
LC_ALL=C sort -u > "$work_dir/current.ids"

comm -23 \
  "$work_dir/expected.ids" \
  "$work_dir/current.ids" \
  > "$work_dir/missing.ids"

comm -13 \
  "$work_dir/expected.ids" \
  "$work_dir/current.ids" \
  > "$work_dir/unexpected.ids"

db_query '
SELECT JSON_OBJECT(
  "portalId", portal_id,
  "nameZhTw", name_zh_tw,
  "sourceMapId", source_map_id,
  "sourceX", source_x,
  "sourceY", source_y,
  "sourceRadius", source_radius,
  "destinationMapId", destination_map_id,
  "destinationX", destination_x,
  "destinationY", destination_y,
  "enabled", enabled
)
FROM god2_game.portals
ORDER BY portal_id;
' |
jq -s '.' > "$work_dir/formal.json"

jq -Rn \
  '[inputs | select(length > 0) | tonumber]' \
  < "$work_dir/missing.ids" \
  > "$work_dir/missing.json"

jq -Rn \
  '[inputs | select(length > 0) | tonumber]' \
  < "$work_dir/unexpected.ids" \
  > "$work_dir/unexpected.json"

expected_count="$(wc -l < "$work_dir/expected.ids")"
formal_count="$(jq 'length' "$work_dir/formal.json")"
enabled_count="$(
  jq '[.[] | select(.enabled == 1)] | length' \
    "$work_dir/formal.json"
)"

read -r portal_link_count derived_link_count candidate_link_count enabled_link_count bound_link_count <<EOF
$(db_query '
SELECT
  (SELECT COUNT(*) FROM god2_game.portal_resource_links),
  (SELECT COUNT(*) FROM god2_research.portal_resource_link_evidence
   WHERE identity_evidence_status="Derived"),
  (SELECT COUNT(*) FROM god2_research.portal_resource_link_evidence
   WHERE identity_evidence_status="Candidate"),
  (SELECT COUNT(*) FROM god2_game.portal_resource_links
   WHERE enabled=1),
  (SELECT COUNT(*) FROM god2_game.portal_resource_links
   WHERE portal_id IS NOT NULL);
')
EOF

missing_count="$(jq 'length' "$work_dir/missing.json")"
unexpected_count="$(jq 'length' "$work_dir/unexpected.json")"
legacy_count="$(jq -r '.recordCount' "$artifact")"
legacy_hash="$(sha256sum "$artifact" | awk '{print $1}')"
generated_at="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

jq -e '
  .recordCount == 68 and
  .canDirectImportToGameplay == false and
  .verificationStatus == "Recovered; TransferTriggerUnverified"
' "$artifact" >/dev/null

jq -n \
  --arg generatedAtUtc "$generated_at" \
  --arg legacySha256 "$legacy_hash" \
  --argjson expectedCount "$expected_count" \
  --argjson formalCount "$formal_count" \
  --argjson enabledCount "$enabled_count" \
  --argjson missingCount "$missing_count" \
  --argjson unexpectedCount "$unexpected_count" \
  --argjson legacyCount "$legacy_count" \
  --argjson portalLinkCount "$portal_link_count" \
  --argjson derivedLinkCount "$derived_link_count" \
  --argjson candidateLinkCount "$candidate_link_count" \
  --argjson enabledLinkCount "$enabled_link_count" \
  --argjson boundLinkCount "$bound_link_count" \
  --slurpfile formal "$work_dir/formal.json" \
  --slurpfile missing "$work_dir/missing.json" \
  --slurpfile unexpected "$work_dir/unexpected.json" \
  '{
    schema: "God2PortalParityBaseline/1",
    generatedAtUtc: $generatedAtUtc,
      clientBuildId: "god2-opt-6b127086e0c0",
    productionDatabaseTouched: false,
    authorityBoundary: {
      formalPresence: "Formal DB presence alone is not proof of Taiwan official gameplay.",
      currentBuildRuntime: "Migrations 083 and 084 preserve current-build runtime observations with bounded scope.",
      taiwanLiveClient: "Portal 170015007 completed original Taiwan client to Server V2 acceptance.",
      legacyInventory: "The 68 recovered CAN links are supplemental candidates and cannot be imported directly."
    },
    sources: {
      historicalMap3Map19: [
        "database/schema/083_publish_latest_verified_map3_map19_portals.sql",
        "database/schema/107_correct_map19_visual_exit_trigger.sql"
      ],
      historicalHongmengBiyou:
        "database/schema/084_publish_hongmeng_biyou_portal_pair.sql",
      taiwanLiveStageRoute:
        "database/schema/103_publish_live_stage3_stage7_runtime_slice.sql",
      supplementalInventory: {
        path: "db/imports/official/portals/portals.official.json",
        sha256: $legacySha256,
        recordCount: $legacyCount,
        verificationStatus: "Recovered; TransferTriggerUnverified",
        canDirectImportToGameplay: false
      },
      stagingResourceLinks: {
        migration: "database/schema/476_repair_forge_client_map_resource_provenance_drift.sql",
        authority: "PINNED_STAGING_PROVENANCE",
        exactClientFileProvenanceComplete: false
      }
    },
    expectedRoutes: [
      {
        portalId: 1,
        route: "Area 4 Map 3 -> Map 19",
        authority: "CURRENT_BUILD_RUNTIME_CAPTURE",
        formalExpected: true
      },
      {
        portalId: 2,
        route: "Area 4 Map 19 -> Map 3",
        authority: "CURRENT_BUILD_RUNTIME_CAPTURE",
        formalExpected: true
      },
      {
        portalId: 3,
        route: "Area 2 Map 0 -> Map 43",
        authority: "CURRENT_BUILD_RUNTIME_CAPTURE",
        formalExpected: true
      },
      {
        portalId: 4,
        route: "Area 2 Map 43 -> Map 0",
        authority: "CURRENT_BUILD_RUNTIME_CAPTURE",
        formalExpected: true
      },
      {
        portalId: 170015007,
        route: "Area 15 Map 0 -> Map 7",
        authority: "TW_LIVE_CLIENT_V2_ACCEPTANCE",
        formalExpected: true
      }
    ],
    inventory: {
      expectedEvidenceBackedRoutes: $expectedCount,
      formalRoutes: $formalCount,
      enabledFormalRoutes: $enabledCount,
      supplementalCandidates: $legacyCount,
      stagingPortalResourceLinks: $portalLinkCount,
      derivedStagingLinks: $derivedLinkCount,
      candidateStagingLinks: $candidateLinkCount,
      enabledStagingLinks: $enabledLinkCount,
      runtimeBoundStagingLinks: $boundLinkCount
    },
    formalRows: $formal[0],
    diff: {
      missingEvidenceBackedRoutes: $missing[0],
      missingCount: $missingCount,
      unexpectedFormalRoutes: $unexpected[0],
      unexpectedCount: $unexpectedCount
    },
    regression: {
      status: (
        if $missingCount > 0
        then "BLOCKED"
        else "REPAIRED"
        end
      ),
      detail: (
        if $missingCount > 0
        then "Portal 1 and 2 were published and evidence-gated by applied migrations but are absent from the current Formal DB."
        else "The evidence-backed Portal catalog is complete; the forged-seed omission of Portal 1 and 2 has been repaired."
        end
      ),
      cause: "FORGE_SEED_LEDGER_DRIFT"
    },
    promotionGate: {
      evidenceBackedCatalogComplete: ($missingCount == 0),
      taiwanLiveRoutePresent:
        ([$formal[0][].portalId] | index(170015007) != null),
      supplementalImportAllowed: false,
      stagingResourceLinksRestored: (
        $portalLinkCount == 65 and
        $derivedLinkCount == 44 and
        $candidateLinkCount == 21 and
        $enabledLinkCount == 0 and
        $boundLinkCount == 0
      ),
      exactClientFileProvenanceComplete: false,
      readyForFullPortalPromotion: false
    },
    nextGate: "Validate the 65 disabled staging portal links against exact-current client files, resolve route references, then run controlled real-client transitions for Portal 1, 2, 3 and 4."
  }' > "$output_json"

jq -r '
  . as $root |
  [
    "# Portal Parity Baseline",
    "",
    "- Evidence-backed expected routes: **" + (.inventory.expectedEvidenceBackedRoutes|tostring) + "**",
    "- Formal routes: **" + (.inventory.formalRoutes|tostring) + "**",
    "- Missing evidence-backed routes: **" + (.diff.missingCount|tostring) + "**",
    "- Supplemental CAN-link candidates: **" + (.inventory.supplementalCandidates|tostring) + "**",
    "- Staging portal resource links: **" + (.inventory.stagingPortalResourceLinks|tostring) + "**",
    "- Derived / Candidate staging links: **" +
      (.inventory.derivedStagingLinks|tostring) + " / " +
      (.inventory.candidateStagingLinks|tostring) + "**",
    "- Enabled / runtime-bound staging links: **" +
      (.inventory.enabledStagingLinks|tostring) + " / " +
      (.inventory.runtimeBoundStagingLinks|tostring) + "**",
    "",
    "## Formal routes",
    "",
    "| Portal | Route | Authority | Formal status |",
    "|---:|---|---|---|",
    (
      .expectedRoutes[] |
      "| " + (.portalId|tostring) +
      " | " + .route +
      " | " + .authority +
      " | " +
      (if (.portalId as $id | [$root.formalRows[].portalId] | index($id)) != null
       then "PRESENT"
       else "MISSING"
       end) +
      " |"
    ),
    "",
    "## Regression",
    "",
    "- Status: **" + .regression.status + "**",
    "- Missing IDs: `" + (.diff.missingEvidenceBackedRoutes | map(tostring) | join(", ")) + "`",
    "- Cause: `" + .regression.cause + "`",
    "",
    "The 68 legacy CAN-link candidates remain TransferTriggerUnverified and cannot be imported into gameplay.",
    "Pinned staging identity cross-check: `docs/parity/portal-static-links.md` (regenerate with `python3 Automation/check-portal-static-links.py`). It does not verify transfer triggers.",
    "",
    "No production database writes were performed."
  ] | join("\n")
' "$output_json" > "$output_md"

jq -e '
  .schema == "God2PortalParityBaseline/1" and
  .inventory.expectedEvidenceBackedRoutes == 5 and
  .sources.supplementalInventory.recordCount == 68 and
  .sources.supplementalInventory.canDirectImportToGameplay == false and
  .promotionGate.taiwanLiveRoutePresent == true and
  .productionDatabaseTouched == false
' "$output_json" >/dev/null

echo "Wrote $output_json"
echo "Wrote $output_md"
jq '{
  inventory,
  diff,
  regression,
  promotionGate
}' "$output_json"
