-- Schema 356
-- Game function: consumable items temporarily raise Constitution for 60 minutes.
-- Evidence source: Traditional Chinese client item descriptions explicitly say raise Constitution and duration.
-- Runtime bridge: MariaDbItemUseRepository writes character_item_timed_effects atomically before consuming the item.

ALTER TABLE `god2_game`.`item_effects`
    DROP CONSTRAINT `ck_item_effects_type`,
    ADD CONSTRAINT `ck_item_effects_type` CHECK (`effect_type` IN (
        'RestoreHp','RestoreMp','RestoreHpPercent','RestoreMpPercent',
        'CleansePoison','CleanseSleep','CleanseSeal','CleansePetrify','CleanseConfusion',
        'ApplyStrengthBuff','ApplyConstitutionBuff','ApplyIntelligenceBuff','ApplySpeedBuff',
        '生命恢復','法力恢復','生命百分比恢復','法力百分比恢復',
        '解除中毒','解除睡眠','解除神仙封','解除石化','解除混亂',
        '提升腕力','提升體力','提升智力','提升速度'
    ));

ALTER TABLE `god2_player`.`character_item_timed_effects`
    DROP CONSTRAINT `ck_character_item_timed_effects_type`,
    ADD CONSTRAINT `ck_character_item_timed_effects_type` CHECK (`effect_type` IN ('提升腕力','提升體力','提升智力','提升速度')),
    DROP CONSTRAINT `ck_character_item_timed_effects_stat`,
    ADD CONSTRAINT `ck_character_item_timed_effects_stat` CHECK (`stat_type` IN ('腕力','體力','智力','速度'));

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,`effect_text_zh_tw`,
     `runtime_eligible`,`enabled`,`duration_seconds`)
SELECT item_row.`item_id`,
       964,
       '提升體力',
       CASE item_row.`client_item_id`
           WHEN 3005 THEN 60
           WHEN 3006 THEN 80
           WHEN 3007 THEN 100
           WHEN 3008 THEN 120
           WHEN 3018 THEN 120
       END,
       '世界與戰鬥',
       '自身',
       CONCAT('提升體力 ', CASE item_row.`client_item_id`
           WHEN 3005 THEN 60
           WHEN 3006 THEN 80
           WHEN 3007 THEN 100
           WHEN 3008 THEN 120
           WHEN 3018 THEN 120
       END, ' 點，持續 60 分鐘'),
       1,
       1,
       3600
FROM `god2_game`.`item_registry` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
WHERE item_row.`client_item_id` IN (3005,3006,3007,3008,3018)
  AND item_row.`usable`=1
  AND item_row.`item_category`='消耗品'
  AND item_row.`item_family`='消耗品'
  AND rule_row.`normal_use`=1
  AND rule_row.`battle_use`=1
  AND item_row.`description_zh_tw` REGEXP '提升(60|80|100|120)點體力，持續60分鐘'
ON DUPLICATE KEY UPDATE
    `effect_type`=VALUES(`effect_type`),
    `numeric_value`=VALUES(`numeric_value`),
    `usage_scope`=VALUES(`usage_scope`),
    `target_policy`=VALUES(`target_policy`),
    `effect_text_zh_tw`=VALUES(`effect_text_zh_tw`),
    `runtime_eligible`=VALUES(`runtime_eligible`),
    `enabled`=VALUES(`enabled`),
    `duration_seconds`=VALUES(`duration_seconds`);

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
WHERE effect_row.`effect_type` IN ('提升腕力','提升體力','提升智力','提升速度')
  AND effect_row.`runtime_eligible`=1
  AND effect_row.`enabled`=1;

CREATE OR REPLACE VIEW `god2_game`.`vw_constitution_timed_stat_buff_consumables_runtime_readable` AS
SELECT *
FROM `god2_game`.`vw_timed_stat_buff_consumables_runtime_readable`
WHERE `效果類型`='提升體力';
