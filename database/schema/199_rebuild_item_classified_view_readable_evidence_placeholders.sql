-- Rebuild the readable item classification view so placeholder evidence columns
-- are typed as text instead of MariaDB exposing them as binary(0).

CREATE OR REPLACE VIEW `god2_game`.`vw_items_classified` AS
SELECT
    item_row.`client_item_id` AS `客戶端道具ID`,
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
    CAST(NULL AS char(30)) AS `使用旗標證據`,
    CAST(NULL AS char(30)) AS `文字限制證據`,
    GROUP_CONCAT(effect_row.`effect_text_zh_tw` ORDER BY effect_row.`effect_index` SEPARATOR '、') AS `已實裝效果`,
    item_row.`enabled` AS `服務端啟用`
FROM `god2_game`.`vw_all_item_definitions` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row
    ON rule_row.`item_id` = item_row.`item_id`
LEFT JOIN `god2_game`.`item_effects` effect_row
    ON effect_row.`item_id` = item_row.`item_id`
   AND effect_row.`enabled` = 1
GROUP BY
    item_row.`item_id`,
    item_row.`client_item_id`,
    item_row.`code`,
    item_row.`name_zh_tw`,
    item_row.`description_zh_tw`,
    item_row.`catalog_type_zh_tw`,
    item_row.`item_category`,
    item_row.`item_family`,
    item_row.`source_item_type`,
    rule_row.`normal_use`,
    rule_row.`battle_use`,
    item_row.`equippable`,
    item_row.`tradable`,
    item_row.`droppable`,
    item_row.`storable`,
    item_row.`stackable`,
    item_row.`use_on_other`,
    rule_row.`class_restriction_zh_tw`,
    rule_row.`minimum_rebirth`,
    rule_row.`gender_restriction_zh_tw`,
    rule_row.`equipment_target_restriction_zh_tw`,
    item_row.`enabled`;
