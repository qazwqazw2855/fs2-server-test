CREATE TABLE IF NOT EXISTS `god2_research`.`client_map_resource_evidence` (
    `resource_key` varchar(512) NOT NULL,
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

CREATE TABLE IF NOT EXISTS `god2_research`.`npc_appearance_identity_evidence` (
    `appearance_identity_id` bigint NOT NULL,
    `source_row` int NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `raw_fields_json` longtext NULL,
    `identity_evidence_status` varchar(30) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`appearance_identity_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`client_map_resource_evidence`
    (`resource_key`,`resource_evidence_status`,`map_identity_evidence_status`,
     `portal_placement_evidence_status`,`navigation_evidence_status`,`admin_note`)
SELECT `resource_key`,`resource_evidence_status`,`map_identity_evidence_status`,
       `portal_placement_evidence_status`,`navigation_evidence_status`,`admin_note`
FROM `god2_game`.`client_map_resources`
ON DUPLICATE KEY UPDATE
    `resource_evidence_status`=VALUES(`resource_evidence_status`),
    `map_identity_evidence_status`=VALUES(`map_identity_evidence_status`),
    `portal_placement_evidence_status`=VALUES(`portal_placement_evidence_status`),
    `navigation_evidence_status`=VALUES(`navigation_evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`client_map_resource_identity_evidence`
    (`map_identity_id`,`source_row`,`source_sha256`,`identity_evidence_status`,`coordinate_evidence_status`)
SELECT `map_identity_id`,`source_row`,`source_sha256`,`identity_evidence_status`,`coordinate_evidence_status`
FROM `god2_game`.`client_map_resource_identities`
ON DUPLICATE KEY UPDATE
    `source_row`=VALUES(`source_row`),
    `source_sha256`=VALUES(`source_sha256`),
    `identity_evidence_status`=VALUES(`identity_evidence_status`),
    `coordinate_evidence_status`=VALUES(`coordinate_evidence_status`);

INSERT INTO `god2_research`.`portal_resource_link_evidence`
    (`portal_link_id`,`can_relative_path`,`can_record_index`,`can_record_type`,`source_sha256`,
     `identity_evidence_status`,`source_coordinate_evidence_status`,`destination_coordinate_evidence_status`,`trigger_evidence_status`)
SELECT `portal_link_id`,`can_relative_path`,`can_record_index`,`can_record_type`,`source_sha256`,
       `identity_evidence_status`,`source_coordinate_evidence_status`,`destination_coordinate_evidence_status`,`trigger_evidence_status`
FROM `god2_game`.`portal_resource_links`
ON DUPLICATE KEY UPDATE
    `can_relative_path`=VALUES(`can_relative_path`),
    `can_record_index`=VALUES(`can_record_index`),
    `can_record_type`=VALUES(`can_record_type`),
    `source_sha256`=VALUES(`source_sha256`),
    `identity_evidence_status`=VALUES(`identity_evidence_status`),
    `source_coordinate_evidence_status`=VALUES(`source_coordinate_evidence_status`),
    `destination_coordinate_evidence_status`=VALUES(`destination_coordinate_evidence_status`),
    `trigger_evidence_status`=VALUES(`trigger_evidence_status`);

INSERT INTO `god2_research`.`npc_appearance_identity_evidence`
    (`appearance_identity_id`,`source_row`,`source_sha256`,`raw_fields_json`,`identity_evidence_status`)
SELECT `appearance_identity_id`,`source_row`,`source_sha256`,`raw_fields_json`,`identity_evidence_status`
FROM `god2_game`.`npc_appearance_identities`
ON DUPLICATE KEY UPDATE
    `source_row`=VALUES(`source_row`),
    `source_sha256`=VALUES(`source_sha256`),
    `raw_fields_json`=VALUES(`raw_fields_json`),
    `identity_evidence_status`=VALUES(`identity_evidence_status`);

ALTER TABLE `god2_game`.`client_map_resources`
    DROP INDEX IF EXISTS `ix_client_map_resources_navigation`,
    DROP CONSTRAINT IF EXISTS `ck_client_map_resources_gate`,
    DROP CONSTRAINT IF EXISTS `ck_client_map_resources_navigation_gate`;

ALTER TABLE `god2_game`.`client_map_resource_identities`
    DROP CONSTRAINT IF EXISTS `ck_client_map_resource_identity_enabled`;

ALTER TABLE `god2_game`.`portal_resource_links`
    DROP CONSTRAINT IF EXISTS `ck_portal_resource_links_enabled`;

ALTER TABLE `god2_game`.`client_map_resources`
    DROP COLUMN IF EXISTS `resource_evidence_status`,
    DROP COLUMN IF EXISTS `map_identity_evidence_status`,
    DROP COLUMN IF EXISTS `portal_placement_evidence_status`,
    DROP COLUMN IF EXISTS `navigation_evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`,
    ADD CONSTRAINT `ck_client_map_resources_gate` CHECK (
        `enabled`=0 OR (`canonical_map_id` IS NOT NULL AND `client_map_id` IS NOT NULL AND `client_area_id` IS NOT NULL)),
    ADD CONSTRAINT `ck_client_map_resources_navigation_gate` CHECK (
        `enabled`=0 OR (`grid_width`>0 AND `grid_height`>0 AND `minimum_x` IS NOT NULL AND `maximum_x` IS NOT NULL
            AND `minimum_y` IS NOT NULL AND `maximum_y` IS NOT NULL));

ALTER TABLE `god2_game`.`client_map_resource_identities`
    DROP COLUMN IF EXISTS `source_row`,
    DROP COLUMN IF EXISTS `source_sha256`,
    DROP COLUMN IF EXISTS `identity_evidence_status`,
    DROP COLUMN IF EXISTS `coordinate_evidence_status`,
    ADD CONSTRAINT `ck_client_map_resource_identity_enabled` CHECK (`enabled` IN (0,1));

ALTER TABLE `god2_game`.`portal_resource_links`
    DROP COLUMN IF EXISTS `can_relative_path`,
    DROP COLUMN IF EXISTS `can_record_index`,
    DROP COLUMN IF EXISTS `can_record_type`,
    DROP COLUMN IF EXISTS `source_sha256`,
    DROP COLUMN IF EXISTS `identity_evidence_status`,
    DROP COLUMN IF EXISTS `source_coordinate_evidence_status`,
    DROP COLUMN IF EXISTS `destination_coordinate_evidence_status`,
    DROP COLUMN IF EXISTS `trigger_evidence_status`,
    ADD CONSTRAINT `ck_portal_resource_links_enabled` CHECK (`enabled` IN (0,1));

ALTER TABLE `god2_game`.`npc_appearance_identities`
    DROP COLUMN IF EXISTS `source_row`,
    DROP COLUMN IF EXISTS `source_sha256`,
    DROP COLUMN IF EXISTS `raw_fields_json`,
    DROP COLUMN IF EXISTS `identity_evidence_status`;
