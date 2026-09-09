ALTER TABLE `god2_game`.`portals`
    ADD COLUMN IF NOT EXISTS `client_build_id` varchar(64) NULL COMMENT 'Client build used to verify this portal',
    ADD COLUMN IF NOT EXISTS `evidence_source_type` varchar(40) NOT NULL DEFAULT 'Unknown' COMMENT 'PacketCapture, ClientResource, ExternalGuide, or Unknown',
    ADD COLUMN IF NOT EXISTS `identity_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT 'Source and destination map identity evidence',
    ADD COLUMN IF NOT EXISTS `source_coordinate_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT 'Source trigger coordinate evidence',
    ADD COLUMN IF NOT EXISTS `destination_coordinate_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT 'Destination coordinate evidence',
    ADD COLUMN IF NOT EXISTS `trigger_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT 'Trigger mode and radius evidence',
    ADD COLUMN IF NOT EXISTS `source_reference` varchar(1024) NULL COMMENT 'Immutable evidence reference',
    ADD COLUMN IF NOT EXISTS `source_sha256` char(64) NULL COMMENT 'Evidence artifact SHA-256';

UPDATE `god2_game`.`portals`
SET `client_build_id` = 'god2-opt-6b127086e0c0',
    `evidence_source_type` = 'PacketCapture',
    `identity_evidence_status` = 'Verified',
    `source_coordinate_evidence_status` = 'Verified',
    `destination_coordinate_evidence_status` = 'Verified',
    `trigger_evidence_status` = CASE WHEN `portal_id` IN (3,4) THEN 'Derived' ELSE 'Verified' END,
    `source_reference` = CASE `portal_id`
        WHEN 1 THEN 'database/schema/083_publish_latest_verified_map3_map19_portals.sql'
        WHEN 2 THEN 'database/schema/107_correct_map19_visual_exit_trigger.sql'
        WHEN 3 THEN 'database/schema/084_publish_hongmeng_biyou_portal_pair.sql'
        WHEN 4 THEN 'database/schema/084_publish_hongmeng_biyou_portal_pair.sql'
        WHEN 170015007 THEN 'database/schema/103_publish_live_stage3_stage7_runtime_slice.sql'
        ELSE `source_reference`
    END
WHERE `portal_id` IN (1,2,3,4,170015007)
  AND `enabled` = 1
  AND `source_map_id` IS NOT NULL
  AND `source_x` IS NOT NULL
  AND `source_y` IS NOT NULL
  AND `source_radius` IS NOT NULL
  AND `destination_map_id` IS NOT NULL
  AND `destination_x` IS NOT NULL
  AND `destination_y` IS NOT NULL;

UPDATE `god2_game`.`portals`
SET `enabled` = 0,
    `admin_note` = COALESCE(`admin_note`, 'Portal evidence gate rejected this row; exact placement remains pending.')
WHERE `enabled` = 1
  AND (
      `client_build_id` IS NULL OR
      `source_map_id` IS NULL OR `source_x` IS NULL OR `source_y` IS NULL OR `source_radius` IS NULL OR
      `destination_map_id` IS NULL OR `destination_x` IS NULL OR `destination_y` IS NULL OR
      `identity_evidence_status` NOT IN ('Verified','Derived') OR
      `source_coordinate_evidence_status` NOT IN ('Verified','Derived') OR
      `destination_coordinate_evidence_status` NOT IN ('Verified','Derived') OR
      `trigger_evidence_status` NOT IN ('Verified','Derived') OR
      NULLIF(TRIM(`source_reference`),'') IS NULL);

ALTER TABLE `god2_game`.`portals`
    ADD KEY IF NOT EXISTS `ix_portals_evidence_gate` (`enabled`,`identity_evidence_status`,`source_coordinate_evidence_status`,`destination_coordinate_evidence_status`,`trigger_evidence_status`),
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
            NULLIF(TRIM(`source_reference`),'') IS NOT NULL));
