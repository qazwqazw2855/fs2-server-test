-- Schema 349
-- Game function: consumable item use restores HP/MP by percentage of maximum values.
-- Evidence source: Traditional Chinese client item descriptions that explicitly say use/click restores HP/MP percentage.
-- Scope note: this migration only enables instant use recovery. Carry effects, per-turn effects, and status cleansing remain disabled until separately verified.

ALTER TABLE `god2_game`.`item_effects`
    DROP CONSTRAINT IF EXISTS `ck_item_effects_type`;

ALTER TABLE `god2_game`.`item_effects`
    ADD CONSTRAINT `ck_item_effects_type` CHECK (`effect_type` in ('RestoreHp','RestoreMp','RestoreHpPercent','RestoreMpPercent','生命恢復','法力恢復','生命百分比恢復','法力百分比恢復'));

DELETE FROM `god2_game`.`item_effects`
WHERE `effect_type` IN ('生命百分比恢復','法力百分比恢復','RestoreHpPercent','RestoreMpPercent');

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,`effect_text_zh_tw`,`runtime_eligible`,`enabled`)
SELECT item_row.`item_id`, payload.`effect_index`, payload.`effect_type`, payload.`numeric_value`, payload.`usage_scope`,
       CASE WHEN item_row.`use_on_other`=1 THEN '可對他人' ELSE '自身' END AS `target_policy`,
       payload.`effect_text_zh_tw`, payload.`runtime_eligible`, payload.`enabled`
FROM (
        SELECT 995744571 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 15 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 15%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 995744571 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 15 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 15%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1467666730 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 10%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1661748673 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 15 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 15%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1554342001 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 15 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 15%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 934576436 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 5 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 5%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1257305165 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 3 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 3%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1388927218 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 3 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 3%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 784224904 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 5 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 5%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 951512088 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 20 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 20%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 951512088 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 10%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 2061526495 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 90 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 90%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 2061526495 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 50 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 50%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1412195763 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 50 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 50%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1934608312 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 30 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 30%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1490707597 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 10%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 321925573 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 6 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 6%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 499495456 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 10%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1675329704 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 20 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 20%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 965480478 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 25 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 25%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 965480478 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 15 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 15%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 903870307 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 5 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 5%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 837207638 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 10%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1685637331 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 10%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1685637331 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 10%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 2112624257 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 20 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 20%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 685942960 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 10%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1259157897 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 20 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 20%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1290487983 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 10%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 463999902 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 20 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 20%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1754362618 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 10%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 24314208 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 99 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 99%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 24314208 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 99 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 99%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 231854348 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 30 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 30%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 231854348 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 30 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 30%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 82554063 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 99 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 99%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 82554063 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 99 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 99%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1160147032 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 10%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1440953271 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 30 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 30%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 2065357139 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 50 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 50%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 2065357139 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 50 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 50%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1655955381 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 99 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 99%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1655955381 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 99 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 99%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1078835181 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 99 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 99%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1078835181 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 99 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 99%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 202660122 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 50 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 50%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 202660122 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 50 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 50%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 2083137694 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 49 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 49%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 2083137694 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 49 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 49%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 550313058 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 35 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 35%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 550313058 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 35 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 35%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 89479587 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 30 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 30%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 509107608 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 20 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 20%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1404051771 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 80 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 80%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1436473348 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 30 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 30%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1097393865 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 50 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 50%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1214771036 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 30 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 30%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1574779189 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 50 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 50%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1308075268 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 30 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 30%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 8270782 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 50 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 50%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 8270782 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 10 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 10%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1412858344 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 80 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 80%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1663026490 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 40 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 40%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 214899786 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 50 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 50%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1771946175 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 45 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 45%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1771946175 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 40 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 40%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 1396380291 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 40 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 40%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 349710441 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 35 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 35%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 218315499 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 75 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 75%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 218315499 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 50 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 50%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 959105247 AS `item_id`, 901 AS `effect_index`, '生命百分比恢復' AS `effect_type`, 80 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復生命 80%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
        UNION ALL
        SELECT 959105247 AS `item_id`, 902 AS `effect_index`, '法力百分比恢復' AS `effect_type`, 30 AS `numeric_value`, '世界與戰鬥' AS `usage_scope`, '恢復法力 30%' AS `effect_text_zh_tw`, 1 AS `runtime_eligible`, 1 AS `enabled`
) payload
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=payload.`item_id`
WHERE item_row.`item_category`='消耗品'
  AND item_row.`item_family`='消耗品'
  AND item_row.`usable`=1;

INSERT INTO `god2_game`.`item_usage_rules`
    (`item_id`,`normal_use`,`battle_use`,`equippable`,`use_on_other`,`hotkey_allowed`,`runtime_eligible`,`enabled`)
SELECT item_row.`item_id`,1,1,0,item_row.`use_on_other`,1,1,1
FROM `god2_game`.`item_registry` item_row
WHERE item_row.`item_id` IN (995744571,1467666730,1661748673,1554342001,934576436,1257305165,1388927218,784224904,951512088,2061526495,1412195763,1934608312,1490707597,321925573,499495456,1675329704,965480478,903870307,837207638,1685637331,2112624257,685942960,1259157897,1290487983,463999902,1754362618,24314208,231854348,82554063,1160147032,1440953271,2065357139,1655955381,1078835181,202660122,2083137694,550313058,89479587,509107608,1404051771,1436473348,1097393865,1214771036,1574779189,1308075268,8270782,1412858344,1663026490,214899786,1771946175,1396380291,349710441,218315499,959105247)
ON DUPLICATE KEY UPDATE
    `normal_use`=1,
    `battle_use`=1,
    `runtime_eligible`=1,
    `enabled`=1;

CREATE OR REPLACE VIEW `god2_game`.`vw_percent_recovery_consumables_runtime_readable` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`code` AS `服務端代碼`,
       item_row.`name_zh_tw` AS `道具名稱`,
       effect_row.`effect_type` AS `效果類型`,
       effect_row.`numeric_value` AS `百分比`,
       effect_row.`effect_text_zh_tw` AS `效果文字`,
       rule_row.`normal_use` AS `平時可用`,
       rule_row.`battle_use` AS `戰鬥可用`,
       '文字證據：百分比使用恢復' AS `證據狀態`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
WHERE effect_row.`effect_type` IN ('生命百分比恢復','法力百分比恢復')
  AND effect_row.`runtime_eligible`=1
  AND effect_row.`enabled`=1;
