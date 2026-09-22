-- Repair FORGE_SEED_LEDGER_DRIFT without restoring evidence columns to the
-- production runtime table. The forged seed retained migration history through
-- 468 while omitting Portal 1/2 and the god2_research evidence schema.

-- Fail closed before publishing evidence when this database is not the known
-- forged runtime shape. A zero-row INSERT must never be recorded as repaired.
CREATE TEMPORARY TABLE `god2_portal_repair_guard` (
    `ready` tinyint NOT NULL,
    CONSTRAINT `ck_god2_portal_repair_guard`
        CHECK (`ready` = 1)
);

INSERT INTO `god2_portal_repair_guard` (`ready`)
SELECT IF(
    EXISTS (
        SELECT 1 FROM `god2_game`.`maps`
        WHERE `client_area_id` = 4 AND `client_map_id` = 3 AND `enabled` = 1
    )
    AND EXISTS (
        SELECT 1 FROM `god2_game`.`maps`
        WHERE `client_area_id` = 4 AND `client_map_id` = 19 AND `enabled` = 1
    )
    AND (
        SELECT COUNT(*)
        FROM `god2_game`.`portals`
        WHERE `portal_id` IN (3,4,170015007)
          AND `enabled` = 1
    ) = 3,
    1,
    0
);

DROP TEMPORARY TABLE `god2_portal_repair_guard`;

CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`portal_evidence` (
    `portal_id` bigint NOT NULL,
    `client_build_id` varchar(64) NULL,
    `evidence_source_type` varchar(40) NOT NULL,
    `identity_evidence_status` varchar(30) NOT NULL,
    `source_coordinate_evidence_status` varchar(30) NOT NULL,
    `destination_coordinate_evidence_status` varchar(30) NOT NULL,
    `trigger_evidence_status` varchar(30) NOT NULL,
    `source_reference` varchar(1024) NULL,
    `source_sha256` char(64) NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`portal_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_game`.`portals`
    (`portal_id`, `name_zh_tw`,
     `source_map_id`, `source_x`, `source_y`, `source_radius`,
     `destination_map_id`, `destination_x`, `destination_y`,
     `destination_direction`,
     `required_level`, `required_quest_id`,
     `cost_item_id`, `cost_quantity`,
     `enabled`, `updated_at_utc`)
SELECT
    1, '崑崙仙界三至雜貨店',
    source_map.`map_id`, 252, 397, 0,
    destination_map.`map_id`, 28, 34,
    NULL, NULL, NULL, NULL, NULL,
    1, UTC_TIMESTAMP(6)
FROM `god2_game`.`maps` source_map
JOIN `god2_game`.`maps` destination_map
  ON destination_map.`client_area_id` = 4
 AND destination_map.`client_map_id` = 19
 AND destination_map.`enabled` = 1
WHERE source_map.`client_area_id` = 4
  AND source_map.`client_map_id` = 3
  AND source_map.`enabled` = 1
ON DUPLICATE KEY UPDATE
    `name_zh_tw` = VALUES(`name_zh_tw`),
    `source_map_id` = VALUES(`source_map_id`),
    `source_x` = VALUES(`source_x`),
    `source_y` = VALUES(`source_y`),
    `source_radius` = VALUES(`source_radius`),
    `destination_map_id` = VALUES(`destination_map_id`),
    `destination_x` = VALUES(`destination_x`),
    `destination_y` = VALUES(`destination_y`),
    `enabled` = VALUES(`enabled`),
    `updated_at_utc` = VALUES(`updated_at_utc`);

INSERT INTO `god2_game`.`portals`
    (`portal_id`, `name_zh_tw`,
     `source_map_id`, `source_x`, `source_y`, `source_radius`,
     `destination_map_id`, `destination_x`, `destination_y`,
     `destination_direction`,
     `required_level`, `required_quest_id`,
     `cost_item_id`, `cost_quantity`,
     `enabled`, `updated_at_utc`)
SELECT
    2, '雜貨店至崑崙仙界三',
    source_map.`map_id`, 16, 20, 1,
    destination_map.`map_id`, 196, 139,
    NULL, NULL, NULL, NULL, NULL,
    1, UTC_TIMESTAMP(6)
FROM `god2_game`.`maps` source_map
JOIN `god2_game`.`maps` destination_map
  ON destination_map.`client_area_id` = 4
 AND destination_map.`client_map_id` = 3
 AND destination_map.`enabled` = 1
WHERE source_map.`client_area_id` = 4
  AND source_map.`client_map_id` = 19
  AND source_map.`enabled` = 1
ON DUPLICATE KEY UPDATE
    `name_zh_tw` = VALUES(`name_zh_tw`),
    `source_map_id` = VALUES(`source_map_id`),
    `source_x` = VALUES(`source_x`),
    `source_y` = VALUES(`source_y`),
    `source_radius` = VALUES(`source_radius`),
    `destination_map_id` = VALUES(`destination_map_id`),
    `destination_x` = VALUES(`destination_x`),
    `destination_y` = VALUES(`destination_y`),
    `enabled` = VALUES(`enabled`),
    `updated_at_utc` = VALUES(`updated_at_utc`);

INSERT INTO `god2_research`.`portal_evidence`
    (`portal_id`, `client_build_id`, `evidence_source_type`,
     `identity_evidence_status`,
     `source_coordinate_evidence_status`,
     `destination_coordinate_evidence_status`,
     `trigger_evidence_status`,
     `source_reference`, `source_sha256`, `admin_note`)
VALUES
    (1, 'god2-opt-6b127086e0c0', 'PacketCapture',
     'Verified', 'Verified', 'Verified', 'Verified',
     'database/schema/083_publish_latest_verified_map3_map19_portals.sql',
     'DD787EED030AD5576D5D3571DC5E0D7BB745BC7FB5EEAF83DECF010EE93D56E5',
     'Restored after FORGE_SEED_LEDGER_DRIFT; runtime route remains bounded to the original observation.'),
    (2, 'god2-opt-6b127086e0c0', 'PacketCapture',
     'Verified', 'Verified', 'Verified', 'Verified',
     'database/schema/107_correct_map19_visual_exit_trigger.sql',
     '4BD5FCE2C10C3096081CD61928C4606A7E3CC2BE507C775BFB93AC4E2EA5D41B',
     'Restored after FORGE_SEED_LEDGER_DRIFT; source trigger is the corrected visual exit at 16,20.'),
    (3, 'god2-opt-6b127086e0c0', 'PacketCapture',
     'Verified', 'Verified', 'Verified', 'Derived',
     'database/schema/084_publish_hongmeng_biyou_portal_pair.sql',
     '5FD4ADB8C3AE0D15761C07DB9F09AC6FA9EDC3AA720A5C27F746AD90808A27D6',
     'Evidence row restored from immutable migration source.'),
    (4, 'god2-opt-6b127086e0c0', 'PacketCapture',
     'Verified', 'Verified', 'Verified', 'Derived',
     'database/schema/084_publish_hongmeng_biyou_portal_pair.sql',
     '5FD4ADB8C3AE0D15761C07DB9F09AC6FA9EDC3AA720A5C27F746AD90808A27D6',
     'Evidence row restored from immutable migration source.'),
    (170015007, 'god2-opt-6b127086e0c0', 'PacketCapture',
     'Verified', 'Verified', 'Verified', 'Verified',
     'database/schema/103_publish_live_stage3_stage7_runtime_slice.sql',
     '4B65E9DC8273A3F2F2B6DE05D5D2E9E93BAA37F8EADCED6CB9CE00455C8F6CFA',
     'Taiwan original-client to Server V2 acceptance route; evidence row restored after forge drift.')
ON DUPLICATE KEY UPDATE
    `client_build_id` = VALUES(`client_build_id`),
    `evidence_source_type` = VALUES(`evidence_source_type`),
    `identity_evidence_status` = VALUES(`identity_evidence_status`),
    `source_coordinate_evidence_status` = VALUES(`source_coordinate_evidence_status`),
    `destination_coordinate_evidence_status` = VALUES(`destination_coordinate_evidence_status`),
    `trigger_evidence_status` = VALUES(`trigger_evidence_status`),
    `source_reference` = VALUES(`source_reference`),
    `source_sha256` = VALUES(`source_sha256`),
    `admin_note` = VALUES(`admin_note`),
    `moved_at_utc` = UTC_TIMESTAMP(6);
