CREATE TABLE IF NOT EXISTS `god2_research`.`map_catalog_evidence` (
    `map_id` bigint NOT NULL,
    `identity_evidence_status` varchar(30) NOT NULL,
    `coordinate_evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`map_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`npc_catalog_evidence` (
    `npc_id` bigint NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`npc_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`npc_spawn_evidence` (
    `spawn_id` bigint NOT NULL,
    `wire_evidence_status` varchar(30) NOT NULL,
    `identity_evidence_status` varchar(30) NOT NULL,
    `coordinate_evidence_status` varchar(30) NOT NULL,
    `service_evidence_status` varchar(30) NOT NULL,
    `application_message_sha256` char(64) NULL,
    `opaque_template_sha256` char(64) NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`spawn_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

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

ALTER TABLE `god2_research`.`portal_evidence`
    ADD COLUMN IF NOT EXISTS `client_build_id` varchar(64) NULL AFTER `portal_id`,
    ADD COLUMN IF NOT EXISTS `source_reference` varchar(1024) NULL AFTER `trigger_evidence_status`,
    ADD COLUMN IF NOT EXISTS `source_sha256` char(64) NULL AFTER `source_reference`;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='god2_game' AND TABLE_NAME='maps' AND COLUMN_NAME='identity_evidence_status'),
    'INSERT INTO `god2_research`.`map_catalog_evidence` (`map_id`,`identity_evidence_status`,`coordinate_evidence_status`,`admin_note`) SELECT `map_id`,`identity_evidence_status`,`coordinate_evidence_status`,`admin_note` FROM `god2_game`.`maps` ON DUPLICATE KEY UPDATE `identity_evidence_status`=VALUES(`identity_evidence_status`),`coordinate_evidence_status`=VALUES(`coordinate_evidence_status`),`admin_note`=VALUES(`admin_note`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='god2_game' AND TABLE_NAME='npcs' AND COLUMN_NAME='evidence_status'),
    'INSERT INTO `god2_research`.`npc_catalog_evidence` (`npc_id`,`evidence_status`,`admin_note`) SELECT `npc_id`,`evidence_status`,`admin_note` FROM `god2_game`.`npcs` ON DUPLICATE KEY UPDATE `evidence_status`=VALUES(`evidence_status`),`admin_note`=VALUES(`admin_note`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='god2_game' AND TABLE_NAME='npc_spawns' AND COLUMN_NAME='wire_evidence_status'),
    'INSERT INTO `god2_research`.`npc_spawn_evidence` (`spawn_id`,`wire_evidence_status`,`identity_evidence_status`,`coordinate_evidence_status`,`service_evidence_status`,`application_message_sha256`,`opaque_template_sha256`,`admin_note`) SELECT `spawn_id`,`wire_evidence_status`,`identity_evidence_status`,`coordinate_evidence_status`,`service_evidence_status`,`application_message_sha256`,`opaque_template_sha256`,`admin_note` FROM `god2_game`.`npc_spawns` ON DUPLICATE KEY UPDATE `wire_evidence_status`=VALUES(`wire_evidence_status`),`identity_evidence_status`=VALUES(`identity_evidence_status`),`coordinate_evidence_status`=VALUES(`coordinate_evidence_status`),`service_evidence_status`=VALUES(`service_evidence_status`),`application_message_sha256`=VALUES(`application_message_sha256`),`opaque_template_sha256`=VALUES(`opaque_template_sha256`),`admin_note`=VALUES(`admin_note`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='god2_game' AND TABLE_NAME='portals' AND COLUMN_NAME='evidence_source_type'),
    'INSERT INTO `god2_research`.`portal_evidence` (`portal_id`,`client_build_id`,`evidence_source_type`,`identity_evidence_status`,`source_coordinate_evidence_status`,`destination_coordinate_evidence_status`,`trigger_evidence_status`,`source_reference`,`source_sha256`,`admin_note`) SELECT `portal_id`,`client_build_id`,`evidence_source_type`,`identity_evidence_status`,`source_coordinate_evidence_status`,`destination_coordinate_evidence_status`,`trigger_evidence_status`,`source_reference`,`source_sha256`,`admin_note` FROM `god2_game`.`portals` ON DUPLICATE KEY UPDATE `client_build_id`=VALUES(`client_build_id`),`evidence_source_type`=VALUES(`evidence_source_type`),`identity_evidence_status`=VALUES(`identity_evidence_status`),`source_coordinate_evidence_status`=VALUES(`source_coordinate_evidence_status`),`destination_coordinate_evidence_status`=VALUES(`destination_coordinate_evidence_status`),`trigger_evidence_status`=VALUES(`trigger_evidence_status`),`source_reference`=VALUES(`source_reference`),`source_sha256`=VALUES(`source_sha256`),`admin_note`=VALUES(`admin_note`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

DROP VIEW IF EXISTS `god2_game`.`vw_npcs_full`;
DROP VIEW IF EXISTS `god2_game`.`vw_npc_spawns_full`;

ALTER TABLE `god2_game`.`portals`
    DROP INDEX IF EXISTS `ix_portals_evidence_gate`,
    DROP CONSTRAINT IF EXISTS `ck_portals_evidence_gate`,
    DROP CONSTRAINT IF EXISTS `ck_portals_enabled`;

ALTER TABLE `god2_game`.`maps`
    DROP COLUMN IF EXISTS `identity_evidence_status`,
    DROP COLUMN IF EXISTS `coordinate_evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`npcs`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`npc_spawns`
    DROP COLUMN IF EXISTS `wire_evidence_status`,
    DROP COLUMN IF EXISTS `identity_evidence_status`,
    DROP COLUMN IF EXISTS `coordinate_evidence_status`,
    DROP COLUMN IF EXISTS `service_evidence_status`,
    DROP COLUMN IF EXISTS `application_message_sha256`,
    DROP COLUMN IF EXISTS `opaque_template_sha256`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`portals`
    DROP COLUMN IF EXISTS `client_build_id`,
    DROP COLUMN IF EXISTS `evidence_source_type`,
    DROP COLUMN IF EXISTS `identity_evidence_status`,
    DROP COLUMN IF EXISTS `source_coordinate_evidence_status`,
    DROP COLUMN IF EXISTS `destination_coordinate_evidence_status`,
    DROP COLUMN IF EXISTS `trigger_evidence_status`,
    DROP COLUMN IF EXISTS `source_reference`,
    DROP COLUMN IF EXISTS `source_sha256`,
    DROP COLUMN IF EXISTS `admin_note`,
    ADD CONSTRAINT `ck_portals_enabled` CHECK (`enabled` IN (0,1));

CREATE OR REPLACE VIEW `god2_game`.`vw_npcs_full` AS
SELECT
    npc_row.`npc_id` AS `NPC編號`,
    npc_row.`code` AS `服務端代碼`,
    npc_row.`name_zh_tw` AS `NPC名稱`,
    npc_row.`name_original` AS `原始名稱`,
    npc_row.`npc_type` AS `NPC類型`,
    npc_row.`resource_id` AS `資源編號`,
    npc_row.`resource_key` AS `資源鍵`,
    npc_row.`interaction_family` AS `互動類型`,
    npc_row.`default_dialog_id` AS `預設對話`,
    npc_row.`merchant_id` AS `商店編號`,
    npc_row.`quest_provider` AS `提供任務`,
    CASE npc_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`npcs` npc_row;

CREATE OR REPLACE VIEW `god2_game`.`vw_npc_spawns_full` AS
SELECT
    spawn_row.`spawn_id` AS `出生點編號`,
    spawn_row.`npc_id` AS `NPC編號`,
    npc_row.`name_zh_tw` AS `NPC名稱`,
    spawn_row.`map_id` AS `地圖編號`,
    map_row.`name_zh_tw` AS `地圖名稱`,
    spawn_row.`position_x` AS `座標X`,
    spawn_row.`position_y` AS `座標Y`,
    spawn_row.`direction` AS `朝向`,
    spawn_row.`instance_key` AS `實體鍵`,
    spawn_row.`client_build_id` AS `客戶端版本`,
    spawn_row.`observed_client_entity_handle` AS `客戶端實體Handle`,
    spawn_row.`official_resource_type` AS `官方資源類型`,
    spawn_row.`official_resource_ordinal` AS `官方資源序號`,
    spawn_row.`official_direction_code` AS `官方方向碼`,
    spawn_row.`official_state_code` AS `官方狀態碼`,
    CASE spawn_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`npc_spawns` spawn_row
JOIN `god2_game`.`npcs` npc_row ON npc_row.`npc_id`=spawn_row.`npc_id`
JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=spawn_row.`map_id`;
