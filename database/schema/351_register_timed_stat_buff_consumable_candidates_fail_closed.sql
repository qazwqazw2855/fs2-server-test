-- Schema 351
-- Game function: consumable items that temporarily raise Strength, Intelligence, or Speed.
-- Scope note: candidates are registered for readable evidence tracking only.
-- Runtime execution remains disabled until item-use requests can create timed character/battle status effects.

ALTER TABLE `god2_game`.`item_effects`
    DROP CONSTRAINT IF EXISTS `ck_item_effects_type`;

ALTER TABLE `god2_game`.`item_effects`
    ADD CONSTRAINT `ck_item_effects_type` CHECK (`effect_type` in (
        'RestoreHp','RestoreMp','RestoreHpPercent','RestoreMpPercent',
        'CleansePoison','CleanseSleep','CleanseSeal','CleansePetrify','CleanseConfusion',
        'ApplyStrengthBuff','ApplyIntelligenceBuff','ApplySpeedBuff',
        '生命恢復','法力恢復','生命百分比恢復','法力百分比恢復',
        '解除中毒','解除睡眠','解除神仙封','解除石化','解除混亂',
        '提升腕力','提升智力','提升速度'
    ));

DELETE FROM `god2_game`.`item_effects`
WHERE `effect_type` IN ('提升腕力','提升智力','提升速度',
                        'ApplyStrengthBuff','ApplyIntelligenceBuff','ApplySpeedBuff')
  AND `effect_index` BETWEEN 961 AND 963;

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,`effect_text_zh_tw`,`runtime_eligible`,`enabled`)
SELECT item_row.`item_id`,
       CASE
           WHEN item_row.`description_zh_tw` REGEXP '腕力' THEN 961
           WHEN item_row.`description_zh_tw` REGEXP '智力' THEN 962
           WHEN item_row.`description_zh_tw` REGEXP '速度' THEN 963
           ELSE 960
       END AS `effect_index`,
       CASE
           WHEN item_row.`description_zh_tw` REGEXP '腕力' THEN '提升腕力'
           WHEN item_row.`description_zh_tw` REGEXP '智力' THEN '提升智力'
           WHEN item_row.`description_zh_tw` REGEXP '速度' THEN '提升速度'
           ELSE '提升腕力'
       END AS `effect_type`,
       CASE
           WHEN item_row.`description_zh_tw` REGEXP '提升60點' THEN 60
           WHEN item_row.`description_zh_tw` REGEXP '提升80點' THEN 80
           WHEN item_row.`description_zh_tw` REGEXP '提升100點' THEN 100
           WHEN item_row.`description_zh_tw` REGEXP '提升120點' THEN 120
           WHEN item_row.`description_zh_tw` REGEXP '提升150點' THEN 150
           ELSE 1
       END AS `numeric_value`,
       '世界與戰鬥' AS `usage_scope`,
       '自身' AS `target_policy`,
       CASE
           WHEN item_row.`description_zh_tw` REGEXP '腕力' THEN CONCAT('提升腕力 ', CASE
               WHEN item_row.`description_zh_tw` REGEXP '提升60點' THEN '60'
               WHEN item_row.`description_zh_tw` REGEXP '提升80點' THEN '80'
               WHEN item_row.`description_zh_tw` REGEXP '提升100點' THEN '100'
               WHEN item_row.`description_zh_tw` REGEXP '提升120點' THEN '120'
               WHEN item_row.`description_zh_tw` REGEXP '提升150點' THEN '150'
               ELSE '未知'
           END, ' 點')
           WHEN item_row.`description_zh_tw` REGEXP '智力' THEN CONCAT('提升智力 ', CASE
               WHEN item_row.`description_zh_tw` REGEXP '提升60點' THEN '60'
               WHEN item_row.`description_zh_tw` REGEXP '提升80點' THEN '80'
               WHEN item_row.`description_zh_tw` REGEXP '提升100點' THEN '100'
               WHEN item_row.`description_zh_tw` REGEXP '提升120點' THEN '120'
               WHEN item_row.`description_zh_tw` REGEXP '提升150點' THEN '150'
               ELSE '未知'
           END, ' 點')
           WHEN item_row.`description_zh_tw` REGEXP '速度' THEN CONCAT('提升速度 ', CASE
               WHEN item_row.`description_zh_tw` REGEXP '提升60點' THEN '60'
               WHEN item_row.`description_zh_tw` REGEXP '提升80點' THEN '80'
               WHEN item_row.`description_zh_tw` REGEXP '提升100點' THEN '100'
               WHEN item_row.`description_zh_tw` REGEXP '提升120點' THEN '120'
               WHEN item_row.`description_zh_tw` REGEXP '提升150點' THEN '150'
               ELSE '未知'
           END, ' 點')
           ELSE '能力提升候選'
       END AS `effect_text_zh_tw`,
       0 AS `runtime_eligible`,
       0 AS `enabled`
FROM `god2_game`.`item_registry` item_row
WHERE item_row.`usable`=1
  AND item_row.`item_category`='消耗品'
  AND item_row.`item_family`='消耗品'
  AND item_row.`description_zh_tw` REGEXP '提升[0-9]+點(腕力|智力|速度)，持續[0-9]+分鐘';

CREATE OR REPLACE VIEW `god2_game`.`vw_timed_stat_buff_consumable_candidates_readable` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`code` AS `服務端代碼`,
       item_row.`name_zh_tw` AS `道具名稱`,
       effect_row.`effect_type` AS `候選效果`,
       effect_row.`numeric_value` AS `提升數值`,
       effect_row.`usage_scope` AS `使用場景`,
       '候選：等待道具使用限時狀態橋接' AS `證據狀態`,
       item_row.`description_zh_tw` AS `文字證據`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
WHERE effect_row.`effect_type` IN ('提升腕力','提升智力','提升速度')
  AND effect_row.`runtime_eligible`=0
  AND effect_row.`enabled`=0;
