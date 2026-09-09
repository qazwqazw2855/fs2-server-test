CREATE TABLE IF NOT EXISTS `god2_research`.`item_usage_rule_evidence` (
    `item_id` int NOT NULL,
    `field_evidence_status` varchar(30) NOT NULL,
    `text_rule_evidence_status` varchar(30) NOT NULL,
    `activation_evidence_status` varchar(64) NOT NULL,
    `source_reference_zh_tw` varchar(500) NULL,
    `exact_client_source_sha256` char(64) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`item_effect_evidence` (
    `item_id` int NOT NULL,
    `effect_index` int NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `activation_evidence_status` varchar(64) NOT NULL,
    `source_reference_zh_tw` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`item_id`,`effect_index`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`client_item_effect_visual_evidence` (
    `effect_id` int NOT NULL,
    `source_path` varchar(260) NOT NULL,
    `source_row` int NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `evidence_status` varchar(64) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`effect_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`client_item_usage_flag_addition_evidence` (
    `source_section` varchar(16) NOT NULL,
    `client_item_id` int NOT NULL,
    `source_row` int NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `evidence_status` varchar(64) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`source_section`,`client_item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`item_usage_rule_evidence`
    (`item_id`,`field_evidence_status`,`text_rule_evidence_status`,`activation_evidence_status`,`source_reference_zh_tw`,`exact_client_source_sha256`)
SELECT `item_id`,`field_evidence_status`,`text_rule_evidence_status`,`activation_evidence_status`,`source_reference_zh_tw`,`exact_client_source_sha256`
FROM `god2_game`.`item_usage_rules`
ON DUPLICATE KEY UPDATE
    `field_evidence_status`=VALUES(`field_evidence_status`),
    `text_rule_evidence_status`=VALUES(`text_rule_evidence_status`),
    `activation_evidence_status`=VALUES(`activation_evidence_status`),
    `source_reference_zh_tw`=VALUES(`source_reference_zh_tw`),
    `exact_client_source_sha256`=VALUES(`exact_client_source_sha256`);

INSERT INTO `god2_research`.`item_effect_evidence`
    (`item_id`,`effect_index`,`evidence_status`,`activation_evidence_status`,`source_reference_zh_tw`)
SELECT `item_id`,`effect_index`,`evidence_status`,`activation_evidence_status`,`source_reference_zh_tw`
FROM `god2_game`.`item_effects`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `activation_evidence_status`=VALUES(`activation_evidence_status`),
    `source_reference_zh_tw`=VALUES(`source_reference_zh_tw`);

INSERT INTO `god2_research`.`client_item_effect_visual_evidence`
    (`effect_id`,`source_path`,`source_row`,`source_sha256`,`evidence_status`)
SELECT `effect_id`,`source_path`,`source_row`,`source_sha256`,`evidence_status`
FROM `god2_game`.`client_item_effect_visuals`
ON DUPLICATE KEY UPDATE
    `source_path`=VALUES(`source_path`),
    `source_row`=VALUES(`source_row`),
    `source_sha256`=VALUES(`source_sha256`),
    `evidence_status`=VALUES(`evidence_status`);

INSERT INTO `god2_research`.`client_item_usage_flag_addition_evidence`
    (`source_section`,`client_item_id`,`source_row`,`source_sha256`,`evidence_status`)
SELECT `source_section`,`client_item_id`,`source_row`,`source_sha256`,`evidence_status`
FROM `god2_game`.`client_item_usage_flag_additions`
ON DUPLICATE KEY UPDATE
    `source_row`=VALUES(`source_row`),
    `source_sha256`=VALUES(`source_sha256`),
    `evidence_status`=VALUES(`evidence_status`);

DROP VIEW IF EXISTS `god2_game`.`vw_client_item_effect_visuals_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_item_effects_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_items_classified`;
DROP VIEW IF EXISTS `god2_game`.`vw_item_catalog_health`;

ALTER TABLE `god2_game`.`client_item_effect_visuals`
    DROP CONSTRAINT IF EXISTS `ck_client_item_effect_visual_evidence`,
    DROP COLUMN IF EXISTS `source_path`,
    DROP COLUMN IF EXISTS `source_row`,
    DROP COLUMN IF EXISTS `source_sha256`,
    DROP COLUMN IF EXISTS `evidence_status`;

ALTER TABLE `god2_game`.`client_item_usage_flag_additions`
    DROP COLUMN IF EXISTS `source_row`,
    DROP COLUMN IF EXISTS `source_sha256`,
    DROP COLUMN IF EXISTS `evidence_status`;

ALTER TABLE `god2_game`.`item_usage_rules`
    DROP CONSTRAINT IF EXISTS `ck_item_usage_rules_field_evidence`,
    DROP CONSTRAINT IF EXISTS `ck_item_usage_rules_text_evidence`,
    DROP CONSTRAINT IF EXISTS `ck_item_usage_rule_exact_source`,
    DROP CONSTRAINT IF EXISTS `ck_item_usage_rule_runtime_gate`,
    DROP COLUMN IF EXISTS `field_evidence_status`,
    DROP COLUMN IF EXISTS `text_rule_evidence_status`,
    DROP COLUMN IF EXISTS `source_reference_zh_tw`,
    DROP COLUMN IF EXISTS `exact_client_source_sha256`,
    DROP COLUMN IF EXISTS `activation_evidence_status`,
    ADD CONSTRAINT `ck_item_usage_rule_runtime_gate` CHECK (`runtime_eligible`=0 AND `enabled`=0);

ALTER TABLE `god2_game`.`item_effects`
    DROP CONSTRAINT IF EXISTS `ck_item_effects_evidence`,
    DROP CONSTRAINT IF EXISTS `ck_item_effect_runtime_gate`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `activation_evidence_status`,
    DROP COLUMN IF EXISTS `source_reference_zh_tw`,
    ADD CONSTRAINT `ck_item_effect_runtime_gate` CHECK (`runtime_eligible`=0 AND `enabled`=0);

CREATE OR REPLACE VIEW `god2_game`.`vw_client_item_effect_visuals_readable` AS
SELECT
    `effect_id` AS `特效編號`,
    `source_table` AS `來源表`,
    `rom_resource_path` AS `ROM資源`,
    `rtg_resource_path` AS `RTG資源`,
    `sound_index` AS `音效索引`,
    `visual_description_zh_tw` AS `視覺描述`,
    `authored_mechanical_description_zh_tw` AS `原始機制提示`,
    `mechanical_semantics_status` AS `服務端語意狀態`,
    `catalog_enabled` AS `目錄啟用`,
    `runtime_eligible` AS `可執行`
FROM `god2_game`.`client_item_effect_visuals`
WHERE `catalog_enabled`=1;

CREATE OR REPLACE VIEW `god2_game`.`vw_item_effects_readable` AS
SELECT
    effect_row.`item_id` AS `物品編號`,
    item_row.`name_zh_tw` AS `物品名稱`,
    effect_row.`effect_index` AS `效果序號`,
    effect_row.`effect_type` AS `效果類型`,
    effect_row.`numeric_value` AS `效果數值`,
    effect_row.`usage_scope` AS `使用場景`,
    effect_row.`target_policy` AS `目標規則`,
    effect_row.`runtime_eligible` AS `可執行`,
    effect_row.`enabled` AS `服務端啟用`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_items_classified` AS
SELECT
    item_row.`item_id`,
    item_row.`client_item_id`,
    item_row.`code`,
    item_row.`name_zh_tw`,
    item_row.`item_category`,
    item_row.`catalog_type_zh_tw`,
    item_row.`maximum_stack`,
    item_row.`usable`,
    rule_row.`normal_use`,
    rule_row.`battle_use`,
    item_row.`equippable`,
    item_row.`use_on_other`,
    item_row.`required_level`,
    rule_row.`class_restriction_zh_tw`,
    rule_row.`minimum_rebirth`,
    rule_row.`gender_restriction_zh_tw`,
    rule_row.`runtime_eligible` AS `usage_runtime_eligible`,
    item_row.`enabled`
FROM `god2_game`.`vw_all_item_definitions` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_item_catalog_health` AS
SELECT
    item_row.`item_id`,
    item_row.`name_zh_tw`,
    item_row.`item_category`,
    item_row.`enabled`,
    CASE
        WHEN item_row.`enabled`=0 THEN '停用'
        WHEN item_row.`name_zh_tw` IS NULL OR item_row.`name_zh_tw`='' THEN '缺少名稱'
        WHEN item_row.`usable`=1 AND rule_row.`runtime_eligible`<>1 THEN '使用規則未開放'
        ELSE '可用'
    END AS `服務端狀態`
FROM `god2_game`.`vw_all_item_definitions` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`;
