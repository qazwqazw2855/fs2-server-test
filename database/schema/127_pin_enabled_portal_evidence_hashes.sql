-- Pin every production-enabled portal to the exact immutable repository artifact
-- named by source_reference. A route that does not still match its verified
-- identity, coordinates, trigger, and source path is deliberately not promoted.

UPDATE `god2_game`.`portals`
SET `source_sha256` = 'DD787EED030AD5576D5D3571DC5E0D7BB745BC7FB5EEAF83DECF010EE93D56E5'
WHERE `portal_id` = 1
  AND `enabled` = 1
  AND `client_build_id` = 'god2-opt-6b127086e0c0'
  AND `source_map_id` = 1675308248 AND `source_x` = 252 AND `source_y` = 397 AND `source_radius` = 0
  AND `destination_map_id` = 557790525 AND `destination_x` = 28 AND `destination_y` = 34
  AND `identity_evidence_status` = 'Verified'
  AND `source_coordinate_evidence_status` = 'Verified'
  AND `destination_coordinate_evidence_status` = 'Verified'
  AND `trigger_evidence_status` = 'Verified'
  AND `source_reference` = 'database/schema/083_publish_latest_verified_map3_map19_portals.sql';

UPDATE `god2_game`.`portals`
SET `source_sha256` = '4BD5FCE2C10C3096081CD61928C4606A7E3CC2BE507C775BFB93AC4E2EA5D41B'
WHERE `portal_id` = 2
  AND `enabled` = 1
  AND `client_build_id` = 'god2-opt-6b127086e0c0'
  AND `source_map_id` = 557790525 AND `source_x` = 16 AND `source_y` = 20 AND `source_radius` = 1
  AND `destination_map_id` = 1675308248 AND `destination_x` = 196 AND `destination_y` = 139
  AND `identity_evidence_status` = 'Verified'
  AND `source_coordinate_evidence_status` = 'Verified'
  AND `destination_coordinate_evidence_status` = 'Verified'
  AND `trigger_evidence_status` = 'Verified'
  AND `source_reference` = 'database/schema/107_correct_map19_visual_exit_trigger.sql';

UPDATE `god2_game`.`portals`
SET `source_sha256` = '5FD4ADB8C3AE0D15761C07DB9F09AC6FA9EDC3AA720A5C27F746AD90808A27D6'
WHERE `portal_id` = 3
  AND `enabled` = 1
  AND `client_build_id` = 'god2-opt-6b127086e0c0'
  AND `source_map_id` = 130139698 AND `source_x` = 284 AND `source_y` = 452 AND `source_radius` = 8
  AND `destination_map_id` = 192354557 AND `destination_x` = 52 AND `destination_y` = 184
  AND `identity_evidence_status` = 'Verified'
  AND `source_coordinate_evidence_status` = 'Verified'
  AND `destination_coordinate_evidence_status` = 'Verified'
  AND `trigger_evidence_status` = 'Derived'
  AND `source_reference` = 'database/schema/084_publish_hongmeng_biyou_portal_pair.sql';

UPDATE `god2_game`.`portals`
SET `source_sha256` = '5FD4ADB8C3AE0D15761C07DB9F09AC6FA9EDC3AA720A5C27F746AD90808A27D6'
WHERE `portal_id` = 4
  AND `enabled` = 1
  AND `client_build_id` = 'god2-opt-6b127086e0c0'
  AND `source_map_id` = 192354557 AND `source_x` = 51 AND `source_y` = 185 AND `source_radius` = 8
  AND `destination_map_id` = 130139698 AND `destination_x` = 283 AND `destination_y` = 453
  AND `identity_evidence_status` = 'Verified'
  AND `source_coordinate_evidence_status` = 'Verified'
  AND `destination_coordinate_evidence_status` = 'Verified'
  AND `trigger_evidence_status` = 'Derived'
  AND `source_reference` = 'database/schema/084_publish_hongmeng_biyou_portal_pair.sql';

UPDATE `god2_game`.`portals`
SET `source_sha256` = '4B65E9DC8273A3F2F2B6DE05D5D2E9E93BAA37F8EADCED6CB9CE00455C8F6CFA'
WHERE `portal_id` = 170015007
  AND `enabled` = 1
  AND `client_build_id` = 'god2-opt-6b127086e0c0'
  AND `source_map_id` = 170015000 AND `source_x` = 249 AND `source_y` = 246 AND `source_radius` = 0
  AND `destination_map_id` = 170015007 AND `destination_x` = 48 AND `destination_y` = 81
  AND `identity_evidence_status` = 'Verified'
  AND `source_coordinate_evidence_status` = 'Verified'
  AND `destination_coordinate_evidence_status` = 'Verified'
  AND `trigger_evidence_status` = 'Verified'
  AND `source_reference` = 'database/schema/103_publish_live_stage3_stage7_runtime_slice.sql';

UPDATE `god2_game`.`portals`
SET `enabled` = 0,
    `admin_note` = CONCAT('Portal evidence hash gate rejected this row. ', COALESCE(`admin_note`, ''))
WHERE `enabled` = 1
  AND (`source_sha256` IS NULL OR `source_sha256` NOT REGEXP '^[0-9A-Fa-f]{64}$');

ALTER TABLE `god2_game`.`portals`
    DROP CONSTRAINT IF EXISTS `ck_portals_evidence_gate`,
    ADD CONSTRAINT `ck_portals_evidence_gate` CHECK (
        `enabled` = 0 OR (
            `client_build_id` IS NOT NULL AND
            `source_map_id` IS NOT NULL AND `source_x` IS NOT NULL AND `source_y` IS NOT NULL AND `source_radius` IS NOT NULL AND
            `destination_map_id` IS NOT NULL AND `destination_x` IS NOT NULL AND `destination_y` IS NOT NULL AND
            `identity_evidence_status` IN ('Verified','Derived') AND
            `source_coordinate_evidence_status` IN ('Verified','Derived') AND
            `destination_coordinate_evidence_status` IN ('Verified','Derived') AND
            `trigger_evidence_status` IN ('Verified','Derived') AND
            NULLIF(TRIM(`source_reference`),'') IS NOT NULL AND
            `source_sha256` REGEXP '^[0-9A-Fa-f]{64}$'));
