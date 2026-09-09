-- Schema 354
-- Game function: consumable items that temporarily raise Strength, Intelligence, or Speed.
-- Evidence source: Traditional Chinese client item descriptions with explicit bonus amount and duration.
-- Runtime bridge: MariaDbItemUseRepository writes character_item_timed_effects atomically before consuming the item.

UPDATE `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
SET effect_row.`runtime_eligible`=1,
    effect_row.`enabled`=1
WHERE effect_row.`effect_type` IN ('提升腕力','提升智力','提升速度')
  AND effect_row.`numeric_value` > 0
  AND effect_row.`duration_seconds` IN (3600,5400,7200)
  AND effect_row.`usage_scope`='世界與戰鬥'
  AND item_row.`usable`=1
  AND item_row.`item_category`='消耗品'
  AND item_row.`item_family`='消耗品'
  AND rule_row.`normal_use`=1
  AND rule_row.`battle_use`=1
  AND item_row.`description_zh_tw` REGEXP '提升[0-9]+點(腕力|智力|速度)，持續(60|90|120)分鐘';

CREATE OR REPLACE VIEW `god2_game`.`vw_timed_stat_buff_consumables_runtime_readable` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`code` AS `服務端代碼`,
       item_row.`name_zh_tw` AS `道具名稱`,
       effect_row.`effect_type` AS `效果類型`,
       effect_row.`numeric_value` AS `提升數值`,
       effect_row.`duration_seconds` AS `持續秒數`,
       rule_row.`normal_use` AS `平時可用`,
       rule_row.`battle_use` AS `戰鬥可用`,
       '文字證據：限時能力提升' AS `證據狀態`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
WHERE effect_row.`effect_type` IN ('提升腕力','提升智力','提升速度')
  AND effect_row.`runtime_eligible`=1
  AND effect_row.`enabled`=1;
