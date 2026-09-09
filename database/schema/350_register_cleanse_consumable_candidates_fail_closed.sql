-- Schema 350
-- Game function: consumable items that remove battle debuffs.
-- Scope note: candidates are registered for readable evidence tracking only.
-- Runtime execution remains disabled until item-use requests carry BattleInstanceId and ParticipantId for status removals.

ALTER TABLE `god2_game`.`item_effects`
    DROP CONSTRAINT IF EXISTS `ck_item_effects_type`;

ALTER TABLE `god2_game`.`item_effects`
    ADD CONSTRAINT `ck_item_effects_type` CHECK (`effect_type` in (
        'RestoreHp','RestoreMp','RestoreHpPercent','RestoreMpPercent',
        'CleansePoison','CleanseSleep','CleanseSeal','CleansePetrify','CleanseConfusion',
        '生命恢復','法力恢復','生命百分比恢復','法力百分比恢復',
        '解除中毒','解除睡眠','解除神仙封','解除石化','解除混亂'
    ));

DELETE FROM `god2_game`.`item_effects`
WHERE `effect_type` IN ('解除中毒','解除睡眠','解除神仙封','解除石化','解除混亂',
                        'CleansePoison','CleanseSleep','CleanseSeal','CleansePetrify','CleanseConfusion')
  AND `effect_index` BETWEEN 951 AND 955;

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,`effect_text_zh_tw`,`runtime_eligible`,`enabled`)
SELECT item_row.`item_id`,
       CASE
           WHEN item_row.`description_zh_tw` REGEXP '中毒' THEN 951
           WHEN item_row.`description_zh_tw` REGEXP '睡眠' THEN 952
           WHEN item_row.`description_zh_tw` REGEXP '神仙封|封印' THEN 953
           WHEN item_row.`description_zh_tw` REGEXP '石化' THEN 954
           WHEN item_row.`description_zh_tw` REGEXP '混亂' THEN 955
           ELSE 950
       END AS `effect_index`,
       CASE
           WHEN item_row.`description_zh_tw` REGEXP '中毒' THEN '解除中毒'
           WHEN item_row.`description_zh_tw` REGEXP '睡眠' THEN '解除睡眠'
           WHEN item_row.`description_zh_tw` REGEXP '神仙封|封印' THEN '解除神仙封'
           WHEN item_row.`description_zh_tw` REGEXP '石化' THEN '解除石化'
           WHEN item_row.`description_zh_tw` REGEXP '混亂' THEN '解除混亂'
           ELSE '解除中毒'
       END AS `effect_type`,
       1 AS `numeric_value`,
       '戰鬥' AS `usage_scope`,
       CASE WHEN item_row.`use_on_other`=1 THEN '可對他人' ELSE '自身' END AS `target_policy`,
       CASE
           WHEN item_row.`description_zh_tw` REGEXP '中毒' THEN '解除中毒'
           WHEN item_row.`description_zh_tw` REGEXP '睡眠' THEN '解除睡眠'
           WHEN item_row.`description_zh_tw` REGEXP '神仙封|封印' THEN '解除神仙封'
           WHEN item_row.`description_zh_tw` REGEXP '石化' THEN '解除石化'
           WHEN item_row.`description_zh_tw` REGEXP '混亂' THEN '解除混亂'
           ELSE '解除異常狀態'
       END AS `effect_text_zh_tw`,
       0 AS `runtime_eligible`,
       0 AS `enabled`
FROM `god2_game`.`item_registry` item_row
WHERE item_row.`usable`=1
  AND item_row.`item_category`='消耗品'
  AND item_row.`item_family`='消耗品'
  AND item_row.`description_zh_tw` REGEXP '解除(石化|混亂|中毒|神仙封|封印|睡眠|狀態|異常|全部)';

CREATE OR REPLACE VIEW `god2_game`.`vw_cleanse_consumable_candidates_readable` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`code` AS `服務端代碼`,
       item_row.`name_zh_tw` AS `道具名稱`,
       effect_row.`effect_type` AS `候選效果`,
       effect_row.`usage_scope` AS `使用場景`,
       effect_row.`target_policy` AS `目標規則`,
       '候選：等待道具使用戰鬥狀態橋接' AS `證據狀態`,
       item_row.`description_zh_tw` AS `文字證據`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
WHERE effect_row.`effect_type` IN ('解除中毒','解除睡眠','解除神仙封','解除石化','解除混亂')
  AND effect_row.`runtime_eligible`=0
  AND effect_row.`enabled`=0;
