ALTER TABLE `god2_game`.`monsters`
    ADD COLUMN IF NOT EXISTS `combat_stat_source_zh_tw` varchar(64) NOT NULL DEFAULT '待服務端設計' COMMENT '怪物戰鬥數值來源：官方實測、服務端設計、既有正式數據或待服務端設計' AFTER `currency_reward`,
    ADD COLUMN IF NOT EXISTS `combat_stat_policy_zh_tw` varchar(256) NOT NULL DEFAULT '尚未建立平衡規則' COMMENT '怪物戰鬥數值使用政策與注意事項' AFTER `combat_stat_source_zh_tw`;

UPDATE `god2_game`.`monsters`
SET
    `combat_stat_source_zh_tw` = CASE
        WHEN `max_hp` IS NOT NULL AND `max_mp` IS NOT NULL THEN '既有正式數據；待來源細分'
        ELSE '待服務端設計'
    END,
    `combat_stat_policy_zh_tw` = CASE
        WHEN `max_hp` IS NOT NULL AND `max_mp` IS NOT NULL
            THEN '已具備可供 runtime 使用的 HP/MP，但尚未細分為官方實測或服務端設計；不得偽裝成官方數據。'
        ELSE '可依等級、地圖區間、怪物類型、任務/首領定位建立服務端設計值，再用官方抓包或實戰資料校準。'
    END
WHERE `combat_stat_source_zh_tw` IN ('待服務端設計', '既有正式數據；待來源細分')
   OR `combat_stat_policy_zh_tw` IN ('尚未建立平衡規則', '');

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_combat_stat_source_readable` AS
SELECT
    monster_row.`monster_id` AS `怪物ID`,
    monster_row.`code` AS `怪物代碼`,
    monster_row.`name_zh_tw` AS `怪物名稱`,
    COALESCE(monster_row.`level`, 0) AS `等級`,
    CASE
        WHEN monster_row.`boss` = 1 THEN '首領'
        WHEN monster_row.`elite` = 1 THEN '菁英'
        WHEN monster_row.`is_quest_monster` = 1 THEN '任務怪'
        ELSE '一般怪'
    END AS `怪物定位`,
    CASE
        WHEN monster_row.`max_hp` IS NULL THEN 'HP 未建立'
        ELSE CONCAT('HP ', monster_row.`max_hp`)
    END AS `HP狀態`,
    CASE
        WHEN monster_row.`max_mp` IS NULL THEN 'MP 未建立'
        ELSE CONCAT('MP ', monster_row.`max_mp`)
    END AS `MP狀態`,
    CASE
        WHEN monster_row.`physical_attack` IS NULL
          OR monster_row.`physical_defense` IS NULL
          OR monster_row.`magic_attack` IS NULL
          OR monster_row.`magic_defense` IS NULL
            THEN '攻防資料未完整'
        ELSE CONCAT('物攻 ', monster_row.`physical_attack`, '；物防 ', monster_row.`physical_defense`, '；魔攻 ', monster_row.`magic_attack`, '；魔防 ', monster_row.`magic_defense`)
    END AS `攻防狀態`,
    CASE
        WHEN monster_row.`metal` IS NULL
          OR monster_row.`wood` IS NULL
          OR monster_row.`water` IS NULL
          OR monster_row.`fire` IS NULL
          OR monster_row.`earth` IS NULL
            THEN '五行資料未完整'
        ELSE CONCAT('金 ', monster_row.`metal`, '；木 ', monster_row.`wood`, '；水 ', monster_row.`water`, '；火 ', monster_row.`fire`, '；土 ', monster_row.`earth`)
    END AS `五行狀態`,
    monster_row.`combat_stat_source_zh_tw` AS `數值來源`,
    monster_row.`combat_stat_policy_zh_tw` AS `使用政策`,
    CASE
        WHEN monster_row.`max_hp` IS NOT NULL
         AND monster_row.`max_mp` IS NOT NULL
         AND monster_row.`physical_attack` IS NOT NULL
         AND monster_row.`physical_defense` IS NOT NULL
         AND monster_row.`magic_attack` IS NOT NULL
         AND monster_row.`magic_defense` IS NOT NULL
            THEN '可進戰鬥測試'
        WHEN monster_row.`max_hp` IS NOT NULL AND monster_row.`max_mp` IS NOT NULL
            THEN '可做 HP/MP 基礎測試；攻防仍需設計或校準'
        ELSE '需補服務端設計值或官方校準值'
    END AS `服務端可用狀態`,
    monster_row.`updated_at_utc` AS `更新時間UTC`
FROM `god2_game`.`monsters` monster_row;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_combat_stat_source_summary_readable` AS
SELECT
    monster_row.`combat_stat_source_zh_tw` AS `數值來源`,
    COUNT(*) AS `怪物數量`,
    SUM(CASE WHEN monster_row.`max_hp` IS NOT NULL AND monster_row.`max_mp` IS NOT NULL THEN 1 ELSE 0 END) AS `HPMP已建立`,
    SUM(CASE WHEN monster_row.`max_hp` IS NULL OR monster_row.`max_mp` IS NULL THEN 1 ELSE 0 END) AS `HPMP待設計`,
    SUM(CASE WHEN monster_row.`physical_attack` IS NOT NULL
              AND monster_row.`physical_defense` IS NOT NULL
              AND monster_row.`magic_attack` IS NOT NULL
              AND monster_row.`magic_defense` IS NOT NULL THEN 1 ELSE 0 END) AS `攻防已建立`,
    SUM(CASE WHEN monster_row.`metal` IS NOT NULL
              AND monster_row.`wood` IS NOT NULL
              AND monster_row.`water` IS NOT NULL
              AND monster_row.`fire` IS NOT NULL
              AND monster_row.`earth` IS NOT NULL THEN 1 ELSE 0 END) AS `五行已建立`
FROM `god2_game`.`monsters` monster_row
GROUP BY monster_row.`combat_stat_source_zh_tw`
ORDER BY `怪物數量` DESC, `數值來源`;
