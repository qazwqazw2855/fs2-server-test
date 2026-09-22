#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

migration="database/schema/114_publish_official_map_catalog.sql"
legacy="db/imports/official/maps/maps.official.json"
output_json="docs/parity/map.json"
output_md="docs/parity/map.md"
container="${GOD2_DB_CONTAINER:-god2-runtime-db-test}"
client_build="god2-opt-6b127086e0c0"

for command_name in git jq awk comm sha256sum sudo docker; do
  command -v "$command_name" >/dev/null ||
    { echo "Missing command: $command_name" >&2; exit 1; }
done

mkdir -p docs/parity
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

migration_ids="$work_dir/migration.ids"
database_ids="$work_dir/database.ids"

awk '
  /INSERT INTO `god2_game`.`maps`/ { inside=1; next }
  inside && /ON DUPLICATE KEY UPDATE/ { exit }
  inside && match($0, /^[[:space:]]*\(([0-9]+),/, value) {
    print value[1]
  }
' "$migration" |
LC_ALL=C sort -u > "$migration_ids"

db_query() {
  local sql="$1"
  printf '%s\n' "$sql" |
    sudo docker exec -i "$container" sh -lc '
root_password="$(cat "$MARIADB_ROOT_PASSWORD_FILE")"
exec mariadb -uroot -p"$root_password" \
  --batch --raw --skip-column-names
'
}

db_query "
SELECT map_id
FROM god2_game.maps
WHERE client_build_id='$client_build';
" |
LC_ALL=C sort -u > "$database_ids"

comm -23 "$migration_ids" "$database_ids" > "$work_dir/missing.ids"
comm -13 "$migration_ids" "$database_ids" > "$work_dir/extra.ids"

read -r formal_count enabled_count dimension_count bounds_count disabled_count <<EOF
$(db_query "
SELECT
  COUNT(*),
  SUM(enabled=1),
  SUM(width IS NOT NULL AND height IS NOT NULL),
  SUM(minimum_x IS NOT NULL AND maximum_x IS NOT NULL
      AND minimum_y IS NOT NULL AND maximum_y IS NOT NULL),
  SUM(enabled=0)
FROM god2_game.maps
WHERE client_build_id='$client_build';
")
EOF

read -r resource_count identity_count portal_link_count <<EOF
$(db_query "
SELECT
  (SELECT COUNT(*) FROM god2_game.client_map_resources),
  (SELECT COUNT(*) FROM god2_game.client_map_resource_identities),
  (SELECT COUNT(*) FROM god2_game.portal_resource_links);
")
EOF

migration_count="$(wc -l < "$migration_ids")"
missing_count="$(wc -l < "$work_dir/missing.ids")"
extra_count="$(wc -l < "$work_dir/extra.ids")"
legacy_count="$(jq -r '.recordCount // (.records | length)' "$legacy")"
migration_hash="$(sha256sum "$migration" | awk '{print $1}')"
legacy_hash="$(sha256sum "$legacy" | awk '{print $1}')"
source_hash="$(sed -n 's/^-- Source SHA-256: //p' "$migration")"
generated_at="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

jq -Rn '[inputs | select(length > 0) | tonumber]' \
  < "$work_dir/missing.ids" > "$work_dir/missing.json"
jq -Rn '[inputs | select(length > 0) | tonumber]' \
  < "$work_dir/extra.ids" > "$work_dir/extra.json"

jq -n \
  --arg generatedAtUtc "$generated_at" \
  --arg clientBuildId "$client_build" \
  --arg sourceSha256 "$source_hash" \
  --arg migrationSha256 "$migration_hash" \
  --arg legacySha256 "$legacy_hash" \
  --argjson expectedMapCount "$migration_count" \
  --argjson formalMapCount "$formal_count" \
  --argjson enabledMapCount "$enabled_count" \
  --argjson dimensionCount "$dimension_count" \
  --argjson boundsCount "$bounds_count" \
  --argjson disabledMapCount "$disabled_count" \
  --argjson missingCount "$missing_count" \
  --argjson extraCount "$extra_count" \
  --argjson legacyCount "$legacy_count" \
  --argjson resourceCount "$resource_count" \
  --argjson identityCount "$identity_count" \
  --argjson portalLinkCount "$portal_link_count" \
  --slurpfile missing "$work_dir/missing.json" \
  --slurpfile extra "$work_dir/extra.json" \
  '{
    schema: "God2MapParityBaseline/1",
    generatedAtUtc: $generatedAtUtc,
    productionDatabaseTouched: false,
    clientBuildId: $clientBuildId,
    authorityBoundary: {
      officialCatalog: "EXACT_CURRENT_STATIC_CATALOG",
      runtimeSlice: "PREEXISTING_VERIFIED_RUNTIME_SLICE",
      legacyInventory: "SUPPLEMENTAL_RESOURCE_INVENTORY",
      rule: "Legacy resource IDs and formal map IDs use different namespaces and must not be directly joined."
    },
    sources: {
      officialCatalog: {
        path: "database/schema/114_publish_official_map_catalog.sql",
        decodedSource: "Data2/Patch/Comm/gamedata.csvZ:Map_City_Coordniate",
        sourceSha256: $sourceSha256,
        migrationSha256: $migrationSha256
      },
      legacyInventory: {
        path: "db/imports/official/maps/maps.official.json",
        sha256: $legacySha256,
        recordCount: $legacyCount
      }
    },
    inventory: {
      expectedOfficialMaps: $expectedMapCount,
      formalCurrentBuildMaps: $formalMapCount,
      enabledMaps: $enabledMapCount,
      disabledMaps: $disabledMapCount,
      mapsWithDimensions: $dimensionCount,
      mapsWithBounds: $boundsCount
    },
    diff: {
      missingFromFormal: $missing[0],
      missingCount: $missingCount,
      extraInFormal: $extra[0],
      extraCount: $extraCount,
      knownExtraClassification: {
        mapId: 557790525,
        name: "Island01/indoor/groceryL",
        authority: "PREEXISTING_VERIFIED_RUNTIME_SLICE",
        note: "Map 19 grocery interior; not part of Migration 114 official world-map catalog."
      }
    },
    provenanceLinks: {
      clientMapResources: $resourceCount,
      clientMapResourceIdentities: $identityCount,
      portalResourceLinks: $portalLinkCount,
      status: (if ($resourceCount > 0 and $identityCount > 0)
               then "PARTIAL"
               else "BLOCKED"
               end)
    },
    promotionGate: {
      catalogComplete: ($expectedMapCount == 144 and $missingCount == 0),
      extraRowsClassified: (
        $extraCount == 1 and
        ($extra[0] == [557790525])
      ),
      provenanceRestored: ($resourceCount > 0 and $identityCount > 0),
      readyForFullWorldRuntimePromotion: false
    },
    nextGate: "Rebuild immutable client resource and identity provenance from the exact current client, then diff collision and portal references."
  }' > "$output_json"

jq -r '
  [
    "# Map Parity Baseline",
    "",
    "- Client build: `" + .clientBuildId + "`",
    "- Official catalog: **" + (.inventory.expectedOfficialMaps|tostring) + "**",
    "- Formal current-build rows: **" + (.inventory.formalCurrentBuildMaps|tostring) + "**",
    "- Missing official rows: **" + (.diff.missingCount|tostring) + "**",
    "- Classified extra rows: **" + (.diff.extraCount|tostring) + "**",
    "- Enabled maps: **" + (.inventory.enabledMaps|tostring) + "**",
    "- Maps with bounds: **" + (.inventory.mapsWithBounds|tostring) + "**",
    "",
    "## Authority boundary",
    "",
    "Migration 114 contains the 144-map exact-current static catalog.",
    "Map 557790525 is a pre-existing verified Map 19 runtime slice.",
    "The legacy 69-record resource inventory is supplemental and uses a different ID namespace.",
    "",
    "## Current blocking gap",
    "",
    "- client_map_resources: " + (.provenanceLinks.clientMapResources|tostring),
    "- client_map_resource_identities: " + (.provenanceLinks.clientMapResourceIdentities|tostring),
    "- portal_resource_links: " + (.provenanceLinks.portalResourceLinks|tostring),
    "- Provenance status: **" + .provenanceLinks.status + "**",
    "",
    "No production database writes were performed."
  ] | join("\n")
' "$output_json" > "$output_md"

jq -e '
  .schema == "God2MapParityBaseline/1" and
  .promotionGate.catalogComplete == true and
  .promotionGate.extraRowsClassified == true and
  .productionDatabaseTouched == false
' "$output_json" >/dev/null

echo "Wrote $output_json"
echo "Wrote $output_md"
jq '{
  inventory,
  diff,
  provenanceLinks,
  promotionGate
}' "$output_json"
