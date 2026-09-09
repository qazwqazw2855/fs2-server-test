-- Schema 358
-- Game function: enable special-category experience milk and candy items as verified usable effect items.
-- Evidence source: these client records are category 一般/特殊, but have normal/battle use rules and explicit battle experience descriptions.

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,`effect_text_zh_tw`,
     `runtime_eligible`,`enabled`,`duration_seconds`)
SELECT item_row.`item_id`,
       965,
       '戰鬥經驗加成',
       CASE item_row.`client_item_id`
           WHEN 5561 THEN 50
           WHEN 5570 THEN 20
           WHEN 5661 THEN 20 WHEN 5662 THEN 20 WHEN 5663 THEN 30 WHEN 5664 THEN 30 WHEN 5665 THEN 40
           WHEN 5666 THEN 40 WHEN 5667 THEN 50 WHEN 5668 THEN 50 WHEN 5669 THEN 60 WHEN 5670 THEN 60
           WHEN 6227 THEN 20 WHEN 6228 THEN 20 WHEN 6229 THEN 30 WHEN 6230 THEN 30 WHEN 6231 THEN 40
           WHEN 6232 THEN 40 WHEN 6233 THEN 50 WHEN 6234 THEN 50 WHEN 6235 THEN 60 WHEN 6236 THEN 60
           WHEN 6247 THEN 20 WHEN 6248 THEN 30 WHEN 6249 THEN 40 WHEN 6250 THEN 50 WHEN 6251 THEN 60
           WHEN 6399 THEN 70
       END,
       '世界與戰鬥',
       '自身',
       CONCAT('戰鬥經驗效率提升 ', CASE item_row.`client_item_id`
           WHEN 5561 THEN 50
           WHEN 5570 THEN 20
           WHEN 5661 THEN 20 WHEN 5662 THEN 20 WHEN 5663 THEN 30 WHEN 5664 THEN 30 WHEN 5665 THEN 40
           WHEN 5666 THEN 40 WHEN 5667 THEN 50 WHEN 5668 THEN 50 WHEN 5669 THEN 60 WHEN 5670 THEN 60
           WHEN 6227 THEN 20 WHEN 6228 THEN 20 WHEN 6229 THEN 30 WHEN 6230 THEN 30 WHEN 6231 THEN 40
           WHEN 6232 THEN 40 WHEN 6233 THEN 50 WHEN 6234 THEN 50 WHEN 6235 THEN 60 WHEN 6236 THEN 60
           WHEN 6247 THEN 20 WHEN 6248 THEN 30 WHEN 6249 THEN 40 WHEN 6250 THEN 50 WHEN 6251 THEN 60
           WHEN 6399 THEN 70
       END, '%，持續 ', CASE
           WHEN item_row.`client_item_id` IN (5661,6227,6247,6248,6249,6250,6251) THEN 60
           WHEN item_row.`client_item_id` IN (5662,5664,5666,5668,5670,6228,6230,6232,6234,6236,6399) THEN 120
           ELSE 240
       END, ' 分鐘'),
       1,
       1,
       CASE
           WHEN item_row.`client_item_id` IN (5661,6227,6247,6248,6249,6250,6251) THEN 3600
           WHEN item_row.`client_item_id` IN (5662,5664,5666,5668,5670,6228,6230,6232,6234,6236,6399) THEN 7200
           ELSE 14400
       END
FROM `god2_game`.`item_registry` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
WHERE item_row.`client_item_id` IN (
    5561,5570,
    5661,5662,5663,5664,5665,5666,5667,5668,5669,5670,
    6227,6228,6229,6230,6231,6232,6233,6234,6235,6236,
    6247,6248,6249,6250,6251,
    6399
)
  AND item_row.`usable`=1
  AND item_row.`item_category`='一般'
  AND item_row.`item_family`='特殊'
  AND rule_row.`normal_use`=1
  AND rule_row.`battle_use`=1
  AND item_row.`description_zh_tw` REGEXP '戰鬥經驗加成道具'
  AND item_row.`description_zh_tw` REGEXP '[124]小時內提升玩家戰鬥經驗值效率[0-9]+％'
ON DUPLICATE KEY UPDATE
    `effect_type`=VALUES(`effect_type`),
    `numeric_value`=VALUES(`numeric_value`),
    `usage_scope`=VALUES(`usage_scope`),
    `target_policy`=VALUES(`target_policy`),
    `effect_text_zh_tw`=VALUES(`effect_text_zh_tw`),
    `runtime_eligible`=VALUES(`runtime_eligible`),
    `enabled`=VALUES(`enabled`),
    `duration_seconds`=VALUES(`duration_seconds`);
