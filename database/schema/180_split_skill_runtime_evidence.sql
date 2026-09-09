CREATE TABLE IF NOT EXISTS `god2_research`.`status_effect_catalog_evidence` (
    `status_effect_id` bigint NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`status_effect_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`crafting_recipe_evidence` (
    `recipe_id` bigint NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`recipe_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`skill_client_metadata_evidence` (
    `official_client_item_id` int NOT NULL,
    `evidence_status` varchar(40) NOT NULL,
    `target_shape_evidence_status` varchar(40) NOT NULL,
    `observed_at_utc` datetime(6) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`official_client_item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`skill_client_metadata_mapping_evidence` (
    `official_client_item_id` int NOT NULL,
    `skill_id` bigint NOT NULL,
    `evidence_status` varchar(40) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`official_client_item_id`,`skill_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`public_beta_skill_effect_evidence` (
    `global_record_id` int NOT NULL,
    `level_candidate` int NULL,
    `packet_evidence_boundary` varchar(1500) NOT NULL,
    `source_file` varchar(128) NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`global_record_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`status_effect_catalog_evidence`
    (`status_effect_id`,`evidence_status`,`admin_note`)
SELECT `status_effect_id`,`evidence_status`,`admin_note`
FROM `god2_game`.`status_effects`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`crafting_recipe_evidence`
    (`recipe_id`,`evidence_status`,`admin_note`)
SELECT `recipe_id`,`evidence_status`,`admin_note`
FROM `god2_game`.`crafting_recipes`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`skill_client_metadata_evidence`
    (`official_client_item_id`,`evidence_status`,`target_shape_evidence_status`,`observed_at_utc`)
SELECT `official_client_item_id`,`evidence_status`,`target_shape_evidence_status`,`observed_at_utc`
FROM `god2_game`.`skill_client_metadata`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `target_shape_evidence_status`=VALUES(`target_shape_evidence_status`),
    `observed_at_utc`=VALUES(`observed_at_utc`);

INSERT INTO `god2_research`.`skill_client_metadata_mapping_evidence`
    (`official_client_item_id`,`skill_id`,`evidence_status`)
SELECT `official_client_item_id`,`skill_id`,`evidence_status`
FROM `god2_game`.`skill_client_metadata_mappings`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`);

INSERT INTO `god2_research`.`public_beta_skill_effect_evidence`
    (`global_record_id`,`level_candidate`,`packet_evidence_boundary`,`source_file`,`source_sha256`)
SELECT `global_record_id`,`level_candidate`,`packet_evidence_boundary`,`source_file`,`source_sha256`
FROM `god2_game`.`public_beta_skill_effect_v0`
ON DUPLICATE KEY UPDATE
    `level_candidate`=VALUES(`level_candidate`),
    `packet_evidence_boundary`=VALUES(`packet_evidence_boundary`),
    `source_file`=VALUES(`source_file`),
    `source_sha256`=VALUES(`source_sha256`);

DROP VIEW IF EXISTS `god2_game`.`vw_public_beta_skill_effect_v0_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_verified_skill_target_shapes`;

ALTER TABLE `god2_game`.`public_beta_skill_effect_v0`
    DROP COLUMN IF EXISTS `skill_level`,
    CHANGE COLUMN `level_candidate` `skill_level` int NULL;

ALTER TABLE `god2_game`.`status_effects`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`crafting_recipes`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`skill_client_metadata`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `target_shape_evidence_status`,
    DROP COLUMN IF EXISTS `observed_at_utc`;

ALTER TABLE `god2_game`.`skill_client_metadata_mappings`
    DROP COLUMN IF EXISTS `evidence_status`;

ALTER TABLE `god2_game`.`public_beta_skill_effect_v0`
    DROP COLUMN IF EXISTS `packet_evidence_boundary`,
    DROP COLUMN IF EXISTS `source_file`,
    DROP COLUMN IF EXISTS `source_sha256`;

CREATE OR REPLACE VIEW `god2_game`.`vw_public_beta_skill_effect_v0_readable` AS
SELECT `global_record_id` AS `技能記錄ID`,`name_zh_tw` AS `技能名稱`,`description_zh_tw` AS `技能說明`,
       `implementation_family` AS `服務端功能族群`,`effect_kind` AS `功能種類`,`status_or_axis` AS `狀態或能力軸`,
       `skill_level` AS `技能階級`,`mp_cost` AS `MP消耗`,`numeric_effect_fields` AS `數值效果欄位`,
       `minimum_working_server_rule` AS `目前服務端套用規則`,`enabled` AS `啟用`
FROM `god2_game`.`public_beta_skill_effect_v0`;

CREATE OR REPLACE VIEW `god2_game`.`vw_verified_skill_target_shapes` AS
SELECT metadata.`official_client_item_id` AS `官方技能物品編號`,
       metadata.`name_zh_tw` AS `技能名稱`,
       metadata.`target_scope_zh_tw` AS `官方作用範圍`,
       metadata.`target_shape` AS `正規化作用形狀`,
       'OfficialClientLiveVerified' AS `作用形狀證據`,
       metadata.`live_verified` AS `實機驗證`,
       mapping.`skill_id` AS `服務端技能編號`
FROM `god2_game`.`skill_client_metadata` metadata
LEFT JOIN `god2_game`.`skill_client_metadata_mappings` mapping
  ON mapping.`official_client_item_id`=metadata.`official_client_item_id`
WHERE metadata.`live_verified`=1
  AND metadata.`target_shape`='SingleTarget';

GRANT SELECT ON `god2_game`.`vw_verified_skill_target_shapes` TO `god2_runtime_role`;
