-- Schema 352
-- Game function: timed consumable stat buffs keep their duration as structured server data.
-- Scope note: these item effects remain runtime disabled until timed status application is bridged.

ALTER TABLE `god2_game`.`item_effects`
    ADD COLUMN IF NOT EXISTS `duration_seconds` int NULL COMMENT '持續時間秒數；僅限限時效果';

UPDATE `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
SET effect_row.`duration_seconds` = CASE
        WHEN item_row.`description_zh_tw` REGEXP '持續60分鐘' THEN 3600
        WHEN item_row.`description_zh_tw` REGEXP '持續90分鐘' THEN 5400
        WHEN item_row.`description_zh_tw` REGEXP '持續120分鐘' THEN 7200
        ELSE NULL
    END,
    effect_row.`effect_text_zh_tw` = CASE
        WHEN effect_row.`effect_type`='提升腕力' THEN CONCAT('提升腕力 ', effect_row.`numeric_value`, ' 點，持續 ', CASE
            WHEN item_row.`description_zh_tw` REGEXP '持續60分鐘' THEN '60'
            WHEN item_row.`description_zh_tw` REGEXP '持續90分鐘' THEN '90'
            WHEN item_row.`description_zh_tw` REGEXP '持續120分鐘' THEN '120'
            ELSE '未知'
        END, ' 分鐘')
        WHEN effect_row.`effect_type`='提升智力' THEN CONCAT('提升智力 ', effect_row.`numeric_value`, ' 點，持續 ', CASE
            WHEN item_row.`description_zh_tw` REGEXP '持續60分鐘' THEN '60'
            WHEN item_row.`description_zh_tw` REGEXP '持續90分鐘' THEN '90'
            WHEN item_row.`description_zh_tw` REGEXP '持續120分鐘' THEN '120'
            ELSE '未知'
        END, ' 分鐘')
        WHEN effect_row.`effect_type`='提升速度' THEN CONCAT('提升速度 ', effect_row.`numeric_value`, ' 點，持續 ', CASE
            WHEN item_row.`description_zh_tw` REGEXP '持續60分鐘' THEN '60'
            WHEN item_row.`description_zh_tw` REGEXP '持續90分鐘' THEN '90'
            WHEN item_row.`description_zh_tw` REGEXP '持續120分鐘' THEN '120'
            ELSE '未知'
        END, ' 分鐘')
        ELSE effect_row.`effect_text_zh_tw`
    END
WHERE effect_row.`effect_type` IN ('提升腕力','提升智力','提升速度')
  AND effect_row.`runtime_eligible`=0
  AND effect_row.`enabled`=0;

CREATE OR REPLACE VIEW `god2_game`.`vw_timed_stat_buff_consumable_candidates_readable` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`code` AS `服務端代碼`,
       item_row.`name_zh_tw` AS `道具名稱`,
       effect_row.`effect_type` AS `候選效果`,
       effect_row.`numeric_value` AS `提升數值`,
       effect_row.`duration_seconds` AS `持續秒數`,
       effect_row.`usage_scope` AS `使用場景`,
       '候選：等待道具使用限時狀態橋接' AS `證據狀態`,
       item_row.`description_zh_tw` AS `文字證據`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
WHERE effect_row.`effect_type` IN ('提升腕力','提升智力','提升速度')
  AND effect_row.`runtime_eligible`=0
  AND effect_row.`enabled`=0;
