-- Owner-facing black-box item-use target view.
-- This keeps item effect testing focused on likely usable/effect-bearing
-- items instead of forcing manual testing across the full item catalog.

CREATE OR REPLACE VIEW `god2_game`.`vw_blackbox_item_use_test_targets_readable` AS
SELECT
    item_row.`item_id` AS `物品ID`,
    item_row.`client_item_id` AS `客戶端物品ID`,
    item_row.`code` AS `代碼`,
    item_row.`name_zh_tw` AS `名稱`,
    item_row.`description_zh_tw` AS `說明`,
    item_row.`item_category` AS `分類`,
    item_row.`item_family` AS `細分類`,
    rule_row.`normal_use` AS `平時可用`,
    rule_row.`battle_use` AS `戰鬥可用`,
    rule_row.`use_on_other` AS `可對他人使用`,
    rule_row.`runtime_eligible` AS `規則可進runtime`,
    rule_row.`enabled` AS `規則已啟用`,
    effect_row.`effect_index` AS `效果序`,
    effect_row.`effect_type` AS `效果類型`,
    effect_row.`numeric_value` AS `效果數值`,
    effect_row.`usage_scope` AS `效果使用範圍`,
    effect_row.`target_policy` AS `效果目標`,
    effect_row.`effect_text_zh_tw` AS `效果說明`,
    effect_row.`runtime_eligible` AS `效果可進runtime`,
    effect_row.`enabled` AS `效果已啟用`,
    CASE
        WHEN effect_row.`item_id` IS NOT NULL AND rule_row.`battle_use` = 1 THEN '第一批：已有候選效果且戰鬥可用'
        WHEN effect_row.`item_id` IS NOT NULL THEN '第一批：已有候選效果'
        WHEN rule_row.`battle_use` = 1 THEN '第二批：戰鬥可用但效果待測'
        WHEN rule_row.`normal_use` = 1 THEN '第三批：平時可用但效果待測'
        ELSE '文字命中：需要人工確認'
    END AS `黑箱優先級`
FROM `god2_game`.`items` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row
  ON rule_row.`item_id` = item_row.`item_id`
LEFT JOIN `god2_game`.`item_effects` effect_row
  ON effect_row.`item_id` = item_row.`item_id`
WHERE item_row.`enabled` = 1
  AND (
      effect_row.`item_id` IS NOT NULL OR
      rule_row.`battle_use` = 1 OR
      rule_row.`normal_use` = 1 OR
      item_row.`description_zh_tw` REGEXP 'HP|MP|氣血|法力|恢復|回復|補|藥|丹|符|狀態|中毒|解除|復活'
  )
ORDER BY
    CASE
        WHEN effect_row.`item_id` IS NOT NULL AND rule_row.`battle_use` = 1 THEN 0
        WHEN effect_row.`item_id` IS NOT NULL THEN 1
        WHEN rule_row.`battle_use` = 1 THEN 2
        WHEN rule_row.`normal_use` = 1 THEN 3
        ELSE 4
    END,
    item_row.`item_id`,
    effect_row.`effect_index`;
