-- Schema 360
-- Game function: Identification Magnifier restores HP and MP by percentage when used.
-- Evidence source: Traditional Chinese item description explicitly says 使用可增加生命50％魔力10％.
-- Scope note: the passive carried attack bonus remains evidence-recorded only; this migration enables the explicit use effect.

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,`effect_text_zh_tw`,
     `runtime_eligible`,`enabled`,`duration_seconds`)
SELECT item_row.`item_id`,
       effect_seed.`effect_index`,
       effect_seed.`effect_type`,
       effect_seed.`numeric_value`,
       '世界與戰鬥',
       '自身',
       effect_seed.`effect_text_zh_tw`,
       1,
       1,
       NULL
FROM `god2_game`.`item_registry` item_row
JOIN (
    SELECT 967 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 50 AS `numeric_value`, '恢復生命上限 50%' AS `effect_text_zh_tw`
    UNION ALL
    SELECT 968 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '恢復魔力上限 10%' AS `effect_text_zh_tw`
) effect_seed
WHERE item_row.`client_item_id`=18048
  AND item_row.`usable`=1
  AND item_row.`description_zh_tw` REGEXP '使用可增加生命50％魔力10％'
ON DUPLICATE KEY UPDATE
    `effect_type`=VALUES(`effect_type`),
    `numeric_value`=VALUES(`numeric_value`),
    `usage_scope`=VALUES(`usage_scope`),
    `target_policy`=VALUES(`target_policy`),
    `effect_text_zh_tw`=VALUES(`effect_text_zh_tw`),
    `runtime_eligible`=VALUES(`runtime_eligible`),
    `enabled`=VALUES(`enabled`),
    `duration_seconds`=VALUES(`duration_seconds`);

CREATE OR REPLACE VIEW `god2_game`.`vw_identification_magnifier_percent_recovery_runtime_readable` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`code` AS `服務端代碼`,
       item_row.`name_zh_tw` AS `道具名稱`,
       effect_row.`effect_index` AS `效果序`,
       effect_row.`effect_type` AS `效果類型`,
       effect_row.`numeric_value` AS `恢復百分比`,
       effect_row.`usage_scope` AS `使用範圍`,
       '文字證據：使用可增加生命50％魔力10％' AS `證據狀態`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
WHERE item_row.`client_item_id`=18048
  AND effect_row.`effect_type` IN ('生命百分比恢復','法力百分比恢復')
  AND effect_row.`runtime_eligible`=1
  AND effect_row.`enabled`=1;
