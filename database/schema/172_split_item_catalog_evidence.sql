CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

DROP VIEW IF EXISTS `god2_game`.`vw_item_catalog_health`;
DROP VIEW IF EXISTS `god2_game`.`vw_items_classified`;
DROP VIEW IF EXISTS `god2_game`.`vw_items_full`;
DROP VIEW IF EXISTS `god2_game`.`vw_equipment_full`;
DROP VIEW IF EXISTS `god2_game`.`vw_all_item_definitions`;

CREATE TABLE IF NOT EXISTS `god2_research`.`item_catalog_evidence` (
    `catalog_table` varchar(40) NOT NULL,
    `item_id` bigint NOT NULL,
    `client_item_id` int NULL,
    `source_item_type` varchar(10) NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`catalog_table`,`item_id`),
    KEY `ix_item_catalog_evidence_client` (`client_item_id`,`source_item_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Item, weapon, equipment, magic treasure, and registry evidence split from runtime catalog tables';

INSERT INTO `god2_research`.`item_catalog_evidence`
    (`catalog_table`,`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`)
SELECT 'item_registry',`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`
FROM `god2_game`.`item_registry`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`item_catalog_evidence`
    (`catalog_table`,`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`)
SELECT 'items',`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`
FROM `god2_game`.`items`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`item_catalog_evidence`
    (`catalog_table`,`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`)
SELECT 'weapons',`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`
FROM `god2_game`.`weapons`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`item_catalog_evidence`
    (`catalog_table`,`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`)
SELECT 'equipment',`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`
FROM `god2_game`.`equipment`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`item_catalog_evidence`
    (`catalog_table`,`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`)
SELECT 'magic_treasures',`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`
FROM `god2_game`.`magic_treasures`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

ALTER TABLE `god2_game`.`item_registry`
    DROP CONSTRAINT IF EXISTS `ck_item_registry_evidence_status`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`items`
    DROP CONSTRAINT IF EXISTS `ck_items_evidence_status`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`weapons`
    DROP CONSTRAINT IF EXISTS `ck_weapons_evidence_status`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`equipment`
    DROP CONSTRAINT IF EXISTS `ck_equipment_evidence_status`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`magic_treasures`
    DROP CONSTRAINT IF EXISTS `ck_magic_treasures_evidence_status`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

CREATE OR REPLACE VIEW `god2_game`.`vw_all_item_definitions` AS
SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`item_category`,`item_family`,
       `source_item_type`,`required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
       `stackable`,`usable`,`equippable`,`use_on_other`,`enabled`,`created_at_utc`,`updated_at_utc`,'道具' AS `catalog_type_zh_tw`
FROM `god2_game`.`items`
UNION ALL
SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`item_category`,`item_family`,
       `source_item_type`,`required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
       `stackable`,`usable`,`equippable`,`use_on_other`,`enabled`,`created_at_utc`,`updated_at_utc`,'武器' AS `catalog_type_zh_tw`
FROM `god2_game`.`weapons`
UNION ALL
SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`item_category`,`item_family`,
       `source_item_type`,`required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
       `stackable`,`usable`,`equippable`,`use_on_other`,`enabled`,`created_at_utc`,`updated_at_utc`,'裝備' AS `catalog_type_zh_tw`
FROM `god2_game`.`equipment`
UNION ALL
SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`item_category`,`item_family`,
       `source_item_type`,`required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
       `stackable`,`usable`,`equippable`,`use_on_other`,`enabled`,`created_at_utc`,`updated_at_utc`,'法寶' AS `catalog_type_zh_tw`
FROM `god2_game`.`magic_treasures`;

CREATE OR REPLACE VIEW `god2_game`.`vw_items_full` AS
SELECT `item_id` AS `物品ID`,
       `code` AS `代碼`,
       `name_zh_tw` AS `名稱`,
       `name_original` AS `原文名稱`,
       `item_category` AS `分類`,
       `item_family` AS `家族`,
       `required_level` AS `需求等級`,
       `maximum_stack` AS `最大堆疊`,
       `buy_price` AS `買價`,
       `sell_price` AS `賣價`,
       `enabled` AS `啟用狀態`
FROM `god2_game`.`items`;

CREATE OR REPLACE VIEW `god2_game`.`vw_items_classified` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`code` AS `可讀代碼`,
       item_row.`name_zh_tw` AS `繁體名稱`,
       item_row.`description_zh_tw` AS `用途說明`,
       item_row.`catalog_type_zh_tw` AS `資料表分類`,
       item_row.`item_category` AS `主要分類`,
       item_row.`item_family` AS `細分類`,
       item_row.`source_item_type` AS `來源類型`,
       rule_row.`normal_use` AS `平時可用`,
       rule_row.`battle_use` AS `戰鬥可用`,
       item_row.`equippable` AS `可裝備`,
       item_row.`tradable` AS `可交易`,
       item_row.`droppable` AS `可丟棄`,
       item_row.`storable` AS `可存倉`,
       item_row.`stackable` AS `可堆疊`,
       item_row.`use_on_other` AS `可對他人使用`,
       rule_row.`class_restriction_zh_tw` AS `職業限制`,
       rule_row.`minimum_rebirth` AS `最低轉生`,
       rule_row.`gender_restriction_zh_tw` AS `性別限制`,
       rule_row.`equipment_target_restriction_zh_tw` AS `裝備部位限制`,
       rule_row.`field_evidence_status` AS `使用旗標證據`,
       rule_row.`text_rule_evidence_status` AS `文字限制證據`,
       GROUP_CONCAT(effect_row.`effect_text_zh_tw` ORDER BY effect_row.`effect_index` SEPARATOR '、') AS `已實裝效果`,
       item_row.`enabled` AS `服務端啟用`
FROM `god2_game`.`vw_all_item_definitions` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
LEFT JOIN `god2_game`.`item_effects` effect_row ON effect_row.`item_id`=item_row.`item_id` AND effect_row.`enabled`=1
GROUP BY item_row.`item_id`,item_row.`client_item_id`,item_row.`code`,item_row.`name_zh_tw`,item_row.`description_zh_tw`,
         item_row.`catalog_type_zh_tw`,item_row.`item_category`,item_row.`item_family`,item_row.`source_item_type`,
         rule_row.`normal_use`,rule_row.`battle_use`,item_row.`equippable`,item_row.`tradable`,item_row.`droppable`,
         item_row.`storable`,item_row.`stackable`,item_row.`use_on_other`,rule_row.`class_restriction_zh_tw`,
         rule_row.`minimum_rebirth`,rule_row.`gender_restriction_zh_tw`,rule_row.`equipment_target_restriction_zh_tw`,
         rule_row.`field_evidence_status`,rule_row.`text_rule_evidence_status`,item_row.`enabled`;

CREATE OR REPLACE VIEW `god2_game`.`vw_item_catalog_health` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`name_zh_tw` AS `繁體名稱`,
       item_row.`catalog_type_zh_tw` AS `資料表分類`,
       item_row.`item_category` AS `主要分類`,
       CASE
           WHEN item_row.`item_category`='Consumable' AND item_row.`usable`=1 AND effect_row.`item_id` IS NULL THEN '可使用消耗品尚無已驗證效果'
           WHEN rule_row.`item_id` IS NULL OR rule_row.`enabled`=0 THEN '使用限制尚未啟用'
           ELSE '可用'
       END AS `健康狀態`,
       item_row.`enabled` AS `服務端啟用`
FROM `god2_game`.`vw_all_item_definitions` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
LEFT JOIN (SELECT DISTINCT `item_id` FROM `god2_game`.`item_effects` WHERE `enabled`=1) effect_row ON effect_row.`item_id`=item_row.`item_id`;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`item_catalog_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`item_catalog_evidence` TO 'god2_server'@'127.0.0.1';
