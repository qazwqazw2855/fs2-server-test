#!/usr/bin/env bash
set -Eeuo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

map_source="database/schema/114_publish_official_map_catalog.sql"
portal_source="database/schema/115_publish_official_portal_resource_links.sql"
output="database/schema/476_repair_forge_client_map_resource_provenance_drift.sql"

for source_file in "$map_source" "$portal_source"; do
  test -f "$source_file" || {
    echo "Missing source: $source_file" >&2
    exit 1
  }
done

map_sha="$(sha256sum "$map_source" | awk '{print $1}')"
portal_sha="$(sha256sum "$portal_source" | awk '{print $1}')"

test "$map_sha" = "bf6f02191dd2313f95c366eb96cd81135fe5b783d7167502131fefd01826881e"
test "$portal_sha" = "1a9f11e5d219877e3f01589695262c05e78b361c0c1023343f7ab4c04e6c436a"

identity_values="$(
  awk '
    /INSERT INTO `god2_game`.`client_map_resource_identities`/ {
      target=1
      next
    }
    target && /^VALUES$/ {
      values=1
      next
    }
    values && /^ON DUPLICATE KEY UPDATE$/ {
      exit
    }
    values {
      print
    }
  ' "$map_source"
)"

portal_values="$(
  awk '
    /INSERT INTO `god2_game`.`portal_resource_links`/ {
      target=1
      next
    }
    target && /^VALUES$/ {
      values=1
      next
    }
    values && /^ON DUPLICATE KEY UPDATE$/ {
      exit
    }
    values {
      print
    }
  ' "$portal_source"
)"

test "$(printf '%s\n' "$identity_values" | rg -c '^\s*\(')" = "144"
test "$(printf '%s\n' "$portal_values" | rg -c '^\s*\(')" = "65"

{
  cat <<'SQL'
-- Repair client map resource provenance omitted by the forged canonical seed.
-- This migration restores only evidence-backed static/staging data.
-- It never updates god2_game.maps, god2_game.portals or player state.

CREATE TEMPORARY TABLE `god2_client_resource_repair_guard` (
    `ready` tinyint NOT NULL,
    CONSTRAINT `ck_god2_client_resource_repair_guard`
        CHECK (`ready` = 1)
);

INSERT INTO `god2_client_resource_repair_guard` (`ready`)
SELECT IF(
    (
        SELECT COUNT(*)
        FROM `god2`.`__schemaversion`
        WHERE (`Version`='114'
               AND `Name`='114_publish_official_map_catalog.sql'
               AND `Checksum`='bf6f02191dd2313f95c366eb96cd81135fe5b783d7167502131fefd01826881e')
           OR (`Version`='115'
               AND `Name`='115_publish_official_portal_resource_links.sql'
               AND `Checksum`='1a9f11e5d219877e3f01589695262c05e78b361c0c1023343f7ab4c04e6c436a')
           OR (`Version`='179'
               AND `Name`='179_split_client_resource_link_evidence.sql'
               AND `Checksum`='635e7f4810560c48da7f68652af5bde0d07cf9f634269674ae998647c500e2b9')
           OR (`Version`='226'
               AND `Name`='226_archive_client_map_resource_file_evidence.sql'
               AND `Checksum`='8c7a418ab422b513c68e2652788958a775c8697454af8088d30e268cbbacfcf7')
    ) = 4
    AND (
        (
            (SELECT COUNT(*) FROM `god2_game`.`client_map_resources`) = 0
            AND (SELECT COUNT(*) FROM `god2_game`.`client_map_resource_identities`) = 0
            AND (SELECT COUNT(*) FROM `god2_game`.`portal_resource_links`) = 0
        )
        OR
        (
            (SELECT COUNT(*) FROM `god2_game`.`client_map_resources`) = 158
            AND (SELECT COUNT(*) FROM `god2_game`.`client_map_resource_identities`) = 144
            AND (SELECT COUNT(*) FROM `god2_game`.`portal_resource_links`) = 65
        )
    ),
    1,
    0
);

DROP TEMPORARY TABLE `god2_client_resource_repair_guard`;

CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`client_map_resource_evidence` (
    `resource_key` varchar(512) NOT NULL,
    `client_executable_sha256` char(64) NULL,
    `can_record_index` int NULL,
    `can_record_type` tinyint unsigned NULL,
    `can_relative_path` varchar(512) NULL,
    `can_sha256` char(64) NULL,
    `mbd_relative_path` varchar(512) NULL,
    `mbd_sha256` char(64) NULL,
    `resource_file_relative_path` varchar(512) NULL,
    `resource_file_sha256` char(64) NULL,
    `navigation_relative_path` varchar(512) NULL,
    `navigation_sha256` char(64) NULL,
    `resource_evidence_status` varchar(30) NOT NULL,
    `map_identity_evidence_status` varchar(30) NOT NULL,
    `portal_placement_evidence_status` varchar(30) NOT NULL,
    `navigation_evidence_status` varchar(30) NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`resource_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`client_map_resource_identity_evidence` (
    `map_identity_id` bigint NOT NULL,
    `source_row` int NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `identity_evidence_status` varchar(30) NOT NULL,
    `coordinate_evidence_status` varchar(30) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`map_identity_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`portal_resource_link_evidence` (
    `portal_link_id` varchar(191) NOT NULL,
    `can_relative_path` varchar(512) NOT NULL,
    `can_record_index` int NOT NULL,
    `can_record_type` tinyint unsigned NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `identity_evidence_status` varchar(30) NOT NULL,
    `source_coordinate_evidence_status` varchar(30) NOT NULL,
    `destination_coordinate_evidence_status` varchar(30) NOT NULL,
    `trigger_evidence_status` varchar(30) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`portal_link_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TEMPORARY TABLE `god2_map_identity_stage` (
    `map_identity_id` bigint NOT NULL,
    `client_build_id` varchar(64) NOT NULL,
    `client_area_id` int NOT NULL,
    `client_map_id` int NOT NULL,
    `map_id` bigint NOT NULL,
    `resource_key` varchar(512) NOT NULL,
    `world_map_x` int NOT NULL,
    `world_map_y` int NOT NULL,
    `source_row` int NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `identity_evidence_status` varchar(30) NOT NULL,
    `coordinate_evidence_status` varchar(30) NOT NULL,
    `source_enabled` tinyint NOT NULL,
    PRIMARY KEY (`map_identity_id`)
) ENGINE=InnoDB;

INSERT INTO `god2_map_identity_stage`
VALUES
SQL

  printf '%s\n' "$identity_values"

  cat <<'SQL'
;

CREATE TEMPORARY TABLE `god2_portal_link_stage` (
    `portal_link_id` varchar(191) NOT NULL,
    `client_build_id` varchar(64) NOT NULL,
    `source_resource_key` varchar(512) NOT NULL,
    `destination_resource_key` varchar(512) NOT NULL,
    `source_map_id` bigint NOT NULL,
    `destination_map_id` bigint NULL,
    `can_relative_path` varchar(512) NOT NULL,
    `can_record_index` int NOT NULL,
    `can_record_type` tinyint unsigned NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `identity_evidence_status` varchar(30) NOT NULL,
    `source_enabled` tinyint NOT NULL,
    PRIMARY KEY (`portal_link_id`)
) ENGINE=InnoDB;

INSERT INTO `god2_portal_link_stage`
VALUES
SQL

  printf '%s\n' "$portal_values"

  cat <<'SQL'
;

INSERT INTO `god2_game`.`client_map_resources`
    (`resource_key`,`area_code`,`resource_name`,
     `grid_width`,`grid_height`,`minimum_x`,`maximum_x`,`minimum_y`,`maximum_y`,
     `canonical_map_id`,`client_build_id`,`client_map_id`,`client_area_id`,
     `enabled`,`resource_format`,`navigation_format`)
SELECT
    identity_group.`resource_key`,
    SUBSTRING_INDEX(
        SUBSTRING(identity_group.`resource_key`,LENGTH('client:map/')+1),
        '/',1),
    map_row.`resource_identity`,
    map_row.`width`,map_row.`height`,
    map_row.`minimum_x`,map_row.`maximum_x`,
    map_row.`minimum_y`,map_row.`maximum_y`,
    identity_group.`canonical_map_id`,
    identity_group.`client_build_id`,
    identity_group.`client_map_id`,
    identity_group.`client_area_id`,
    0,
    LOWER(SUBSTRING_INDEX(map_row.`resource_identity`,'.',-1)),
    NULL
FROM (
    SELECT
        `resource_key`,
        MIN(`map_id`) AS `canonical_map_id`,
        MIN(`client_build_id`) AS `client_build_id`,
        MIN(`client_map_id`) AS `client_map_id`,
        MIN(`client_area_id`) AS `client_area_id`
    FROM `god2_map_identity_stage`
    GROUP BY `resource_key`
) identity_group
JOIN `god2_game`.`maps` map_row
  ON map_row.`map_id`=identity_group.`canonical_map_id`
ON DUPLICATE KEY UPDATE
    `canonical_map_id`=VALUES(`canonical_map_id`),
    `client_build_id`=VALUES(`client_build_id`),
    `client_map_id`=VALUES(`client_map_id`),
    `client_area_id`=VALUES(`client_area_id`),
    `enabled`=0;

INSERT INTO `god2_game`.`client_map_resources`
    (`resource_key`,`area_code`,`resource_name`,
     `canonical_map_id`,`client_build_id`,`client_map_id`,`client_area_id`,
     `enabled`,`resource_format`,`navigation_format`)
SELECT DISTINCT
    link_row.`destination_resource_key`,
    SUBSTRING_INDEX(
        SUBSTRING(link_row.`destination_resource_key`,LENGTH('client:map/')+1),
        '/',1) AS `area_code`,
    CONCAT(
        SUBSTRING(
            link_row.`destination_resource_key`,
            LENGTH('client:map/')
            + LENGTH(SUBSTRING_INDEX(
                SUBSTRING(link_row.`destination_resource_key`,LENGTH('client:map/')+1),
                '/',1))
            + 2),
        '.hmd'),
    NULL,
    link_row.`client_build_id`,
    NULL,
    NULL,
    0,
    'hmd',
    NULL
FROM `god2_portal_link_stage` link_row
LEFT JOIN `god2_game`.`client_map_resources` resource_row
  ON resource_row.`resource_key`=link_row.`destination_resource_key`
WHERE resource_row.`resource_key` IS NULL
  AND link_row.`can_record_type`=1
ON DUPLICATE KEY UPDATE
    `enabled`=0;

INSERT INTO `god2_game`.`client_map_resource_identities`
    (`map_identity_id`,`client_build_id`,`client_area_id`,`client_map_id`,
     `map_id`,`resource_key`,`world_map_x`,`world_map_y`,`enabled`)
SELECT
    `map_identity_id`,`client_build_id`,`client_area_id`,`client_map_id`,
    `map_id`,`resource_key`,`world_map_x`,`world_map_y`,0
FROM `god2_map_identity_stage`
ON DUPLICATE KEY UPDATE
    `map_id`=VALUES(`map_id`),
    `resource_key`=VALUES(`resource_key`),
    `world_map_x`=VALUES(`world_map_x`),
    `world_map_y`=VALUES(`world_map_y`),
    `enabled`=0;

INSERT INTO `god2_research`.`client_map_resource_identity_evidence`
    (`map_identity_id`,`source_row`,`source_sha256`,
     `identity_evidence_status`,`coordinate_evidence_status`)
SELECT
    `map_identity_id`,`source_row`,`source_sha256`,
    `identity_evidence_status`,`coordinate_evidence_status`
FROM `god2_map_identity_stage`
ON DUPLICATE KEY UPDATE
    `source_row`=VALUES(`source_row`),
    `source_sha256`=VALUES(`source_sha256`),
    `identity_evidence_status`=VALUES(`identity_evidence_status`),
    `coordinate_evidence_status`=VALUES(`coordinate_evidence_status`);

INSERT INTO `god2_research`.`client_map_resource_evidence`
    (`resource_key`,`resource_evidence_status`,`map_identity_evidence_status`,
     `portal_placement_evidence_status`,`navigation_evidence_status`,`admin_note`)
SELECT
    resource_row.`resource_key`,
    'Recovered',
    'Verified',
    'Unknown',
    CASE
      WHEN resource_row.`grid_width` IS NOT NULL
       AND resource_row.`grid_height` IS NOT NULL
      THEN 'Derived'
      ELSE 'EvidenceBlocked'
    END,
    'Rebuilt from pinned Migration 114 after FORGE_SEED_LEDGER_DRIFT; disabled pending exact-client file provenance.'
FROM `god2_game`.`client_map_resources` resource_row
WHERE EXISTS (
    SELECT 1
    FROM `god2_map_identity_stage` identity_row
    WHERE identity_row.`resource_key`=resource_row.`resource_key`
)
ON DUPLICATE KEY UPDATE
    `resource_evidence_status`=VALUES(`resource_evidence_status`),
    `map_identity_evidence_status`=VALUES(`map_identity_evidence_status`),
    `portal_placement_evidence_status`=VALUES(`portal_placement_evidence_status`),
    `navigation_evidence_status`=VALUES(`navigation_evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`client_map_resource_evidence`
    (`resource_key`,`can_record_index`,`can_record_type`,`can_relative_path`,
     `resource_evidence_status`,`map_identity_evidence_status`,
     `portal_placement_evidence_status`,`navigation_evidence_status`,`admin_note`)
SELECT
    resource_row.`resource_key`,
    MIN(link_row.`can_record_index`),
    MIN(link_row.`can_record_type`),
    MIN(link_row.`can_relative_path`),
    'Recovered',
    'Unknown',
    'Candidate',
    'EvidenceBlocked',
    'HMD resource recovered from Migration 115 CAN inventory; no numeric map identity, dimensions or file hash.'
FROM `god2_game`.`client_map_resources` resource_row
JOIN `god2_portal_link_stage` link_row
  ON link_row.`destination_resource_key`=resource_row.`resource_key`
WHERE NOT EXISTS (
    SELECT 1
    FROM `god2_map_identity_stage` identity_row
    WHERE identity_row.`resource_key`=resource_row.`resource_key`
)
GROUP BY resource_row.`resource_key`
ON DUPLICATE KEY UPDATE
    `can_record_index`=VALUES(`can_record_index`),
    `can_record_type`=VALUES(`can_record_type`),
    `can_relative_path`=VALUES(`can_relative_path`),
    `resource_evidence_status`=VALUES(`resource_evidence_status`),
    `map_identity_evidence_status`=VALUES(`map_identity_evidence_status`),
    `portal_placement_evidence_status`=VALUES(`portal_placement_evidence_status`),
    `navigation_evidence_status`=VALUES(`navigation_evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_game`.`portal_resource_links`
    (`portal_link_id`,`portal_id`,`client_build_id`,
     `source_resource_key`,`destination_resource_key`,
     `source_map_id`,`destination_map_id`,`enabled`)
SELECT
    `portal_link_id`,NULL,`client_build_id`,
    `source_resource_key`,`destination_resource_key`,
    `source_map_id`,`destination_map_id`,0
FROM `god2_portal_link_stage`
ON DUPLICATE KEY UPDATE
    `portal_id`=NULL,
    `source_resource_key`=VALUES(`source_resource_key`),
    `destination_resource_key`=VALUES(`destination_resource_key`),
    `source_map_id`=VALUES(`source_map_id`),
    `destination_map_id`=VALUES(`destination_map_id`),
    `enabled`=0;

INSERT INTO `god2_research`.`portal_resource_link_evidence`
    (`portal_link_id`,`can_relative_path`,`can_record_index`,`can_record_type`,
     `source_sha256`,`identity_evidence_status`,
     `source_coordinate_evidence_status`,
     `destination_coordinate_evidence_status`,`trigger_evidence_status`)
SELECT
    `portal_link_id`,`can_relative_path`,`can_record_index`,`can_record_type`,
    `source_sha256`,`identity_evidence_status`,
    'Unknown','Unknown','Unknown'
FROM `god2_portal_link_stage`
ON DUPLICATE KEY UPDATE
    `can_relative_path`=VALUES(`can_relative_path`),
    `can_record_index`=VALUES(`can_record_index`),
    `can_record_type`=VALUES(`can_record_type`),
    `source_sha256`=VALUES(`source_sha256`),
    `identity_evidence_status`=VALUES(`identity_evidence_status`),
    `source_coordinate_evidence_status`='Unknown',
    `destination_coordinate_evidence_status`='Unknown',
    `trigger_evidence_status`='Unknown';

CREATE TEMPORARY TABLE `god2_client_resource_result_guard` (
    `ready` tinyint NOT NULL,
    CONSTRAINT `ck_god2_client_resource_result_guard`
        CHECK (`ready` = 1)
);

INSERT INTO `god2_client_resource_result_guard` (`ready`)
SELECT IF(
    (SELECT COUNT(*) FROM `god2_game`.`client_map_resources`) = 158
    AND (SELECT COUNT(*) FROM `god2_game`.`client_map_resource_identities`) = 144
    AND (SELECT COUNT(*) FROM `god2_game`.`portal_resource_links`) = 65
    AND (SELECT COUNT(*) FROM `god2_research`.`client_map_resource_evidence`) = 158
    AND (SELECT COUNT(*) FROM `god2_research`.`client_map_resource_identity_evidence`) = 144
    AND (SELECT COUNT(*) FROM `god2_research`.`portal_resource_link_evidence`) = 65
    AND (SELECT COUNT(*) FROM `god2_game`.`client_map_resources` WHERE `enabled`<>0) = 0
    AND (SELECT COUNT(*) FROM `god2_game`.`client_map_resource_identities` WHERE `enabled`<>0) = 0
    AND (SELECT COUNT(*) FROM `god2_game`.`portal_resource_links` WHERE `enabled`<>0) = 0,
    1,
    0
);

DROP TEMPORARY TABLE `god2_client_resource_result_guard`;
DROP TEMPORARY TABLE `god2_portal_link_stage`;
DROP TEMPORARY TABLE `god2_map_identity_stage`;
SQL
} > "$output"

chmod 0644 "$output"

echo "Wrote $output"
echo "identity_rows=$(printf '%s\n' "$identity_values" | rg -c '^\s*\(')"
echo "portal_link_rows=$(printf '%s\n' "$portal_values" | rg -c '^\s*\(')"
