-- Physically split ordinary items, weapons, equipment and magic treasures.
-- item_registry is the shared FK target; gameplay content is read from the split tables.

DROP VIEW IF EXISTS `god2_game`.`vw_items_classified`;

RENAME TABLE `god2_game`.`items` TO `god2_game`.`item_registry`;

CREATE TABLE `god2_game`.`items` LIKE `god2_game`.`item_registry`;
CREATE TABLE `god2_game`.`weapons` LIKE `god2_game`.`item_registry`;
CREATE TABLE `god2_game`.`magic_treasures` LIKE `god2_game`.`item_registry`;

ALTER TABLE `god2_game`.`weapons`
    ADD COLUMN `weapon_type_zh_tw` varchar(100) NULL AFTER `source_item_type`,
    ADD COLUMN `attack_range` int NULL AFTER `weapon_type_zh_tw`,
    ADD COLUMN `physical_attack_bonus` int NULL AFTER `attack_range`,
    ADD COLUMN `magic_attack_bonus` int NULL AFTER `physical_attack_bonus`,
    ADD COLUMN `base_durability` int NULL AFTER `magic_attack_bonus`,
    ADD COLUMN `maximum_enhancement` int NULL AFTER `base_durability`,
    ADD COLUMN `socket_count` int NULL AFTER `maximum_enhancement`,
    ADD COLUMN `class_restriction_zh_tw` varchar(200) NULL AFTER `socket_count`,
    ADD CONSTRAINT `fk_weapons_registry` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE;

ALTER TABLE `god2_game`.`magic_treasures`
    ADD COLUMN `treasure_type_zh_tw` varchar(100) NULL AFTER `source_item_type`,
    ADD COLUMN `battle_usable` tinyint(1) NULL AFTER `treasure_type_zh_tw`,
    ADD COLUMN `effect_description_zh_tw` text NULL AFTER `battle_usable`,
    ADD COLUMN `base_durability` int NULL AFTER `effect_description_zh_tw`,
    ADD COLUMN `maximum_enhancement` int NULL AFTER `base_durability`,
    ADD COLUMN `socket_count` int NULL AFTER `maximum_enhancement`,
    ADD COLUMN `class_restriction_zh_tw` varchar(200) NULL AFTER `socket_count`,
    ADD CONSTRAINT `fk_magic_treasures_registry` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE;

ALTER TABLE `god2_game`.`equipment`
    ADD COLUMN IF NOT EXISTS `client_item_id` int NULL AFTER `item_id`,
    ADD COLUMN IF NOT EXISTS `code` varchar(100) NULL AFTER `client_item_id`,
    ADD COLUMN IF NOT EXISTS `name_zh_tw` varchar(200) NULL AFTER `code`,
    ADD COLUMN IF NOT EXISTS `name_original` varchar(200) NULL AFTER `name_zh_tw`,
    ADD COLUMN IF NOT EXISTS `description_zh_tw` text NULL AFTER `name_original`,
    ADD COLUMN IF NOT EXISTS `item_category` varchar(50) NULL AFTER `description_zh_tw`,
    ADD COLUMN IF NOT EXISTS `item_family` varchar(50) NULL AFTER `item_category`,
    ADD COLUMN IF NOT EXISTS `source_item_type` varchar(32) NULL AFTER `item_family`,
    ADD COLUMN IF NOT EXISTS `required_level` int NULL AFTER `source_item_type`,
    ADD COLUMN IF NOT EXISTS `maximum_stack` int NULL AFTER `required_level`,
    ADD COLUMN IF NOT EXISTS `weight` int NULL AFTER `maximum_stack`,
    ADD COLUMN IF NOT EXISTS `buy_price` bigint NULL AFTER `weight`,
    ADD COLUMN IF NOT EXISTS `sell_price` bigint NULL AFTER `buy_price`,
    ADD COLUMN IF NOT EXISTS `droppable` tinyint(1) NULL AFTER `sell_price`,
    ADD COLUMN IF NOT EXISTS `tradable` tinyint(1) NULL AFTER `droppable`,
    ADD COLUMN IF NOT EXISTS `storable` tinyint(1) NULL AFTER `tradable`,
    ADD COLUMN IF NOT EXISTS `stackable` tinyint(1) NULL AFTER `storable`,
    ADD COLUMN IF NOT EXISTS `usable` tinyint(1) NULL AFTER `stackable`,
    ADD COLUMN IF NOT EXISTS `equippable` tinyint(1) NULL AFTER `usable`,
    ADD COLUMN IF NOT EXISTS `use_on_other` tinyint(1) NULL AFTER `equippable`,
    ADD COLUMN IF NOT EXISTS `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' AFTER `use_on_other`,
    ADD COLUMN IF NOT EXISTS `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) AFTER `admin_note`,
    ADD COLUMN IF NOT EXISTS `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) AFTER `created_at_utc`,
    ADD UNIQUE KEY IF NOT EXISTS `ux_equipment_client_item_id` (`client_item_id`),
    ADD UNIQUE KEY IF NOT EXISTS `ux_equipment_code` (`code`);

INSERT INTO `god2_game`.`weapons`
    (`item_id`,`client_item_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`item_category`,`item_family`,`source_item_type`,
     `icon_id`,`model_id`,`required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
     `stackable`,`usable`,`equippable`,`use_on_other`,`evidence_status`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`,
     `weapon_type_zh_tw`,`attack_range`,`physical_attack_bonus`,`magic_attack_bonus`,`base_durability`,`maximum_enhancement`,`socket_count`,`class_restriction_zh_tw`)
SELECT registry_row.`item_id`,registry_row.`client_item_id`,registry_row.`code`,registry_row.`name_zh_tw`,registry_row.`name_original`,
       registry_row.`description_zh_tw`,registry_row.`item_category`,registry_row.`item_family`,registry_row.`source_item_type`,
       registry_row.`icon_id`,registry_row.`model_id`,registry_row.`required_level`,registry_row.`maximum_stack`,registry_row.`weight`,
       registry_row.`buy_price`,registry_row.`sell_price`,registry_row.`droppable`,registry_row.`tradable`,registry_row.`storable`,
       registry_row.`stackable`,registry_row.`usable`,registry_row.`equippable`,registry_row.`use_on_other`,registry_row.`evidence_status`,
       registry_row.`enabled`,registry_row.`admin_note`,registry_row.`created_at_utc`,registry_row.`updated_at_utc`,
       COALESCE(NULLIF(profile_row.`EquipmentSlot`, ''), '武器'),
       CASE
           WHEN registry_row.`description_zh_tw` REGEXP '攻擊距離[[:space:]]*[0-9]+'
               THEN CAST(REGEXP_REPLACE(
                   REGEXP_SUBSTR(registry_row.`description_zh_tw`, '攻擊距離[[:space:]]*[0-9]+'),
                   '[^0-9]', '') AS UNSIGNED)
           ELSE NULL
       END,
       profile_row.`PhysicalAttackBonus`,profile_row.`MagicAttackBonus`,NULL,NULL,NULL,rule_row.`class_restriction_zh_tw`
FROM `god2_game`.`item_registry` registry_row
LEFT JOIN `god2`.`item_content_profiles` profile_row ON profile_row.`ItemId` = registry_row.`item_id`
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id` = registry_row.`item_id`
WHERE registry_row.`source_item_type` IN ('WPN','TWP','GWP','SSW');

INSERT INTO `god2_game`.`magic_treasures`
    (`item_id`,`client_item_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`item_category`,`item_family`,`source_item_type`,
     `icon_id`,`model_id`,`required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
     `stackable`,`usable`,`equippable`,`use_on_other`,`evidence_status`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`,
     `treasure_type_zh_tw`,`battle_usable`,`effect_description_zh_tw`,`base_durability`,`maximum_enhancement`,`socket_count`,`class_restriction_zh_tw`)
SELECT registry_row.`item_id`,registry_row.`client_item_id`,registry_row.`code`,registry_row.`name_zh_tw`,registry_row.`name_original`,
       registry_row.`description_zh_tw`,registry_row.`item_category`,registry_row.`item_family`,registry_row.`source_item_type`,
       registry_row.`icon_id`,registry_row.`model_id`,registry_row.`required_level`,registry_row.`maximum_stack`,registry_row.`weight`,
       registry_row.`buy_price`,registry_row.`sell_price`,registry_row.`droppable`,registry_row.`tradable`,registry_row.`storable`,
       registry_row.`stackable`,registry_row.`usable`,registry_row.`equippable`,registry_row.`use_on_other`,registry_row.`evidence_status`,
       registry_row.`enabled`,registry_row.`admin_note`,registry_row.`created_at_utc`,registry_row.`updated_at_utc`,
       '法寶',rule_row.`battle_use`,registry_row.`description_zh_tw`,NULL,NULL,NULL,rule_row.`class_restriction_zh_tw`
FROM `god2_game`.`item_registry` registry_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id` = registry_row.`item_id`
WHERE registry_row.`source_item_type` = 'TLI';

DELETE FROM `god2_game`.`equipment`
WHERE `item_id` IN (
    SELECT `item_id` FROM `god2_game`.`item_registry`
    WHERE `source_item_type` IN ('WPN','TWP','GWP','SSW','TLI')
);

UPDATE `god2_game`.`equipment` equipment_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id` = equipment_row.`item_id`
SET equipment_row.`client_item_id`=registry_row.`client_item_id`,equipment_row.`code`=registry_row.`code`,
    equipment_row.`name_zh_tw`=registry_row.`name_zh_tw`,equipment_row.`name_original`=registry_row.`name_original`,
    equipment_row.`description_zh_tw`=registry_row.`description_zh_tw`,equipment_row.`item_category`=registry_row.`item_category`,
    equipment_row.`item_family`=registry_row.`item_family`,equipment_row.`source_item_type`=registry_row.`source_item_type`,
    equipment_row.`required_level`=registry_row.`required_level`,equipment_row.`maximum_stack`=registry_row.`maximum_stack`,
    equipment_row.`weight`=registry_row.`weight`,equipment_row.`buy_price`=registry_row.`buy_price`,
    equipment_row.`sell_price`=registry_row.`sell_price`,equipment_row.`droppable`=registry_row.`droppable`,
    equipment_row.`tradable`=registry_row.`tradable`,equipment_row.`storable`=registry_row.`storable`,
    equipment_row.`stackable`=registry_row.`stackable`,equipment_row.`usable`=registry_row.`usable`,
    equipment_row.`equippable`=registry_row.`equippable`,equipment_row.`use_on_other`=registry_row.`use_on_other`,
    equipment_row.`evidence_status`=registry_row.`evidence_status`,equipment_row.`created_at_utc`=registry_row.`created_at_utc`,
    equipment_row.`updated_at_utc`=registry_row.`updated_at_utc`;

INSERT INTO `god2_game`.`items`
SELECT registry_row.*
FROM `god2_game`.`item_registry` registry_row
WHERE registry_row.`source_item_type` NOT IN ('WPN','TWP','GWP','SSW','TLI')
  AND COALESCE(registry_row.`equippable`,0) = 0;

CREATE OR REPLACE VIEW `god2_game`.`vw_all_item_definitions` AS
SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`item_category`,`item_family`,
       `source_item_type`,`required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
       `stackable`,`usable`,`equippable`,`use_on_other`,`evidence_status`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`,'道具' AS `catalog_type_zh_tw`
FROM `god2_game`.`items`
UNION ALL
SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`item_category`,`item_family`,
       `source_item_type`,`required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
       `stackable`,`usable`,`equippable`,`use_on_other`,`evidence_status`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`,'武器' AS `catalog_type_zh_tw`
FROM `god2_game`.`weapons`
UNION ALL
SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`item_category`,`item_family`,
       `source_item_type`,`required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
       `stackable`,`usable`,`equippable`,`use_on_other`,`evidence_status`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`,'裝備' AS `catalog_type_zh_tw`
FROM `god2_game`.`equipment`
UNION ALL
SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`item_category`,`item_family`,
       `source_item_type`,`required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,
       `stackable`,`usable`,`equippable`,`use_on_other`,`evidence_status`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`,'法寶' AS `catalog_type_zh_tw`
FROM `god2_game`.`magic_treasures`;

CREATE OR REPLACE VIEW `god2_game`.`vw_items_classified` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,item_row.`code` AS `可讀代碼`,item_row.`name_zh_tw` AS `繁體名稱`,
       item_row.`description_zh_tw` AS `用途說明`,item_row.`catalog_type_zh_tw` AS `資料表分類`,item_row.`item_category` AS `主要分類`,
       item_row.`item_family` AS `細分類`,item_row.`source_item_type` AS `來源類型`,rule_row.`normal_use` AS `平時可用`,
       rule_row.`battle_use` AS `戰鬥可用`,item_row.`equippable` AS `可裝備`,item_row.`tradable` AS `可交易`,
       item_row.`droppable` AS `可丟棄`,item_row.`storable` AS `可存倉`,item_row.`stackable` AS `可堆疊`,
       item_row.`use_on_other` AS `可對他人使用`,rule_row.`class_restriction_zh_tw` AS `職業限制`,
       rule_row.`minimum_rebirth` AS `最低轉生`,rule_row.`gender_restriction_zh_tw` AS `性別限制`,
       rule_row.`equipment_target_restriction_zh_tw` AS `裝備部位限制`,rule_row.`text_rule_evidence_status` AS `限制證據狀態`,
       item_row.`evidence_status` AS `資料證據狀態`,item_row.`enabled` AS `服務端啟用`
FROM `god2_game`.`vw_all_item_definitions` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id` = item_row.`item_id`;
