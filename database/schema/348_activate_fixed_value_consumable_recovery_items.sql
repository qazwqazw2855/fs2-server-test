-- Activates fixed-value consumable HP/MP recovery effects that already have
-- exact readable Traditional Chinese recovery text in the formal item catalog.
-- Game function: consumable item use for fixed HP/MP recovery in world and battle.

ALTER TABLE `god2_game`.`item_effects`
    DROP CONSTRAINT IF EXISTS `ck_item_effect_runtime_gate`;

ALTER TABLE `god2_game`.`item_usage_rules`
    DROP CONSTRAINT IF EXISTS `ck_item_usage_rule_runtime_gate`;

UPDATE `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
SET
    effect_row.`runtime_eligible`=1,
    effect_row.`enabled`=1,
    effect_row.`updated_at_utc`=UTC_TIMESTAMP(6)
WHERE item_row.`item_category`='消耗品'
  AND item_row.`usable`=1
  AND effect_row.`effect_type` IN ('生命恢復','法力恢復')
  AND effect_row.`usage_scope`='世界與戰鬥'
  AND effect_row.`target_policy` IN ('自身','可對他人')
  AND effect_row.`numeric_value`>0
  AND effect_row.`effect_text_zh_tw` REGEXP '^恢復(生命|法力) [0-9]+$';

UPDATE `god2_game`.`item_usage_rules` rule_row
JOIN (
    SELECT DISTINCT effect_row.`item_id`
    FROM `god2_game`.`item_effects` effect_row
    JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
    WHERE item_row.`item_category`='消耗品'
      AND item_row.`usable`=1
      AND effect_row.`effect_type` IN ('生命恢復','法力恢復')
      AND effect_row.`usage_scope`='世界與戰鬥'
      AND effect_row.`target_policy` IN ('自身','可對他人')
      AND effect_row.`numeric_value`>0
      AND effect_row.`effect_text_zh_tw` REGEXP '^恢復(生命|法力) [0-9]+$'
      AND effect_row.`runtime_eligible`=1
      AND effect_row.`enabled`=1
) activated_item ON activated_item.`item_id`=rule_row.`item_id`
SET
    rule_row.`runtime_eligible`=1,
    rule_row.`enabled`=1,
    rule_row.`updated_at_utc`=UTC_TIMESTAMP(6)
WHERE rule_row.`normal_use`=1
  AND rule_row.`battle_use`=1;

CREATE OR REPLACE VIEW `god2_game`.`vw_fixed_recovery_consumables_runtime_readable` AS
SELECT
    item_row.`client_item_id` AS `客戶端物品ID`,
    item_row.`name_zh_tw` AS `名稱`,
    effect_row.`effect_type` AS `效果類型`,
    effect_row.`numeric_value` AS `恢復數值`,
    effect_row.`usage_scope` AS `使用範圍`,
    effect_row.`target_policy` AS `目標`,
    effect_row.`effect_text_zh_tw` AS `效果證據文字`,
    rule_row.`normal_use` AS `平時可用`,
    rule_row.`battle_use` AS `戰鬥可用`,
    effect_row.`runtime_eligible` AS `效果Runtime可用`,
    effect_row.`enabled` AS `效果啟用`,
    rule_row.`runtime_eligible` AS `規則Runtime可用`,
    rule_row.`enabled` AS `規則啟用`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=effect_row.`item_id`
WHERE item_row.`item_category`='消耗品'
  AND effect_row.`effect_text_zh_tw` REGEXP '^恢復(生命|法力) [0-9]+$'
ORDER BY item_row.`client_item_id`, effect_row.`effect_index`;
