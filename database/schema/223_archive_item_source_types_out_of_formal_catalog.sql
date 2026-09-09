INSERT INTO `god2_research`.`item_catalog_evidence`
    (`catalog_table`,`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`)
SELECT 'item_registry',item_row.`item_id`,item_row.`client_item_id`,item_row.`source_item_type`,
       COALESCE(evidence_row.`evidence_status`,'EvidenceMovedToResearch'),evidence_row.`admin_note`
FROM `god2_game`.`item_registry` item_row
LEFT JOIN `god2_research`.`item_catalog_evidence` evidence_row
    ON evidence_row.`catalog_table`='item_registry' AND evidence_row.`item_id`=item_row.`item_id`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`item_catalog_evidence`
    (`catalog_table`,`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`)
SELECT 'items',item_row.`item_id`,item_row.`client_item_id`,item_row.`source_item_type`,
       COALESCE(evidence_row.`evidence_status`,'EvidenceMovedToResearch'),evidence_row.`admin_note`
FROM `god2_game`.`items` item_row
LEFT JOIN `god2_research`.`item_catalog_evidence` evidence_row
    ON evidence_row.`catalog_table`='items' AND evidence_row.`item_id`=item_row.`item_id`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`item_catalog_evidence`
    (`catalog_table`,`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`)
SELECT 'weapons',item_row.`item_id`,item_row.`client_item_id`,item_row.`source_item_type`,
       COALESCE(evidence_row.`evidence_status`,'EvidenceMovedToResearch'),evidence_row.`admin_note`
FROM `god2_game`.`weapons` item_row
LEFT JOIN `god2_research`.`item_catalog_evidence` evidence_row
    ON evidence_row.`catalog_table`='weapons' AND evidence_row.`item_id`=item_row.`item_id`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`item_catalog_evidence`
    (`catalog_table`,`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`)
SELECT 'equipment',item_row.`item_id`,item_row.`client_item_id`,item_row.`source_item_type`,
       COALESCE(evidence_row.`evidence_status`,'EvidenceMovedToResearch'),evidence_row.`admin_note`
FROM `god2_game`.`equipment` item_row
LEFT JOIN `god2_research`.`item_catalog_evidence` evidence_row
    ON evidence_row.`catalog_table`='equipment' AND evidence_row.`item_id`=item_row.`item_id`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`item_catalog_evidence`
    (`catalog_table`,`item_id`,`client_item_id`,`source_item_type`,`evidence_status`,`admin_note`)
SELECT 'magic_treasures',item_row.`item_id`,item_row.`client_item_id`,item_row.`source_item_type`,
       COALESCE(evidence_row.`evidence_status`,'EvidenceMovedToResearch'),evidence_row.`admin_note`
FROM `god2_game`.`magic_treasures` item_row
LEFT JOIN `god2_research`.`item_catalog_evidence` evidence_row
    ON evidence_row.`catalog_table`='magic_treasures' AND evidence_row.`item_id`=item_row.`item_id`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

DROP VIEW IF EXISTS `god2_game`.`vw_item_catalog_health`;
DROP VIEW IF EXISTS `god2_game`.`vw_items_classified`;
DROP VIEW IF EXISTS `god2_game`.`vw_all_item_definitions`;

ALTER TABLE `god2_game`.`item_registry`
    DROP INDEX IF EXISTS `ix_items_source_item_type`,
    DROP COLUMN IF EXISTS `source_item_type`;

ALTER TABLE `god2_game`.`items`
    DROP INDEX IF EXISTS `ix_items_source_item_type`,
    DROP COLUMN IF EXISTS `source_item_type`;

ALTER TABLE `god2_game`.`weapons`
    DROP INDEX IF EXISTS `ix_items_source_item_type`,
    DROP COLUMN IF EXISTS `source_item_type`;

ALTER TABLE `god2_game`.`equipment`
    DROP COLUMN IF EXISTS `source_item_type`;

ALTER TABLE `god2_game`.`magic_treasures`
    DROP INDEX IF EXISTS `ix_items_source_item_type`,
    DROP COLUMN IF EXISTS `source_item_type`;

CREATE OR REPLACE VIEW `god2_game`.`vw_all_item_definitions` AS
SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`description_zh_tw`,`item_category`,`item_family`,
       `required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
       `stackable`,`usable`,`equippable`,`use_on_other`,`enabled`,`created_at_utc`,`updated_at_utc`,'道具' AS `catalog_type_zh_tw`
FROM `god2_game`.`items`
UNION ALL
SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`description_zh_tw`,`item_category`,`item_family`,
       `required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
       `stackable`,`usable`,`equippable`,`use_on_other`,`enabled`,`created_at_utc`,`updated_at_utc`,'武器' AS `catalog_type_zh_tw`
FROM `god2_game`.`weapons`
UNION ALL
SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`description_zh_tw`,`item_category`,`item_family`,
       `required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
       `stackable`,`usable`,`equippable`,`use_on_other`,`enabled`,`created_at_utc`,`updated_at_utc`,'裝備' AS `catalog_type_zh_tw`
FROM `god2_game`.`equipment`
UNION ALL
SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`description_zh_tw`,`item_category`,`item_family`,
       `required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
       `stackable`,`usable`,`equippable`,`use_on_other`,`enabled`,`created_at_utc`,`updated_at_utc`,'法寶' AS `catalog_type_zh_tw`
FROM `god2_game`.`magic_treasures`;

CREATE OR REPLACE VIEW `god2_game`.`vw_items_classified` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`code` AS `可讀代碼`,
       item_row.`name_zh_tw` AS `繁體名稱`,
       item_row.`description_zh_tw` AS `用途說明`,
       item_row.`catalog_type_zh_tw` AS `資料表分類`,
       item_row.`item_category` AS `主要分類`,
       item_row.`item_family` AS `細分類`,
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
       CAST(NULL AS char(30)) AS `使用旗標證據`,
       CAST(NULL AS char(30)) AS `文字限制證據`,
       GROUP_CONCAT(effect_row.`effect_text_zh_tw` ORDER BY effect_row.`effect_index` SEPARATOR '、') AS `已實裝效果`,
       item_row.`enabled` AS `服務端啟用`
FROM `god2_game`.`vw_all_item_definitions` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
LEFT JOIN `god2_game`.`item_effects` effect_row ON effect_row.`item_id`=item_row.`item_id` AND effect_row.`enabled`=1
GROUP BY item_row.`item_id`,item_row.`client_item_id`,item_row.`code`,item_row.`name_zh_tw`,item_row.`description_zh_tw`,
         item_row.`catalog_type_zh_tw`,item_row.`item_category`,item_row.`item_family`,rule_row.`normal_use`,rule_row.`battle_use`,
         item_row.`equippable`,item_row.`tradable`,item_row.`droppable`,item_row.`storable`,item_row.`stackable`,item_row.`use_on_other`,
         rule_row.`class_restriction_zh_tw`,rule_row.`minimum_rebirth`,rule_row.`gender_restriction_zh_tw`,
         rule_row.`equipment_target_restriction_zh_tw`,item_row.`enabled`;

CREATE OR REPLACE VIEW `god2_game`.`vw_item_catalog_health` AS
SELECT item_row.`item_id` AS `item_id`,
       item_row.`name_zh_tw` AS `name_zh_tw`,
       item_row.`item_category` AS `item_category`,
       item_row.`enabled` AS `enabled`,
       CASE
           WHEN item_row.`enabled`=0 THEN '停用'
           WHEN item_row.`name_zh_tw` IS NULL OR item_row.`name_zh_tw`='' THEN '缺少名稱'
           WHEN item_row.`usable`=1 AND rule_row.`runtime_eligible`<>1 THEN '使用規則未開放'
           ELSE '可用'
       END AS `服務端狀態`
FROM `god2_game`.`vw_all_item_definitions` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`;
