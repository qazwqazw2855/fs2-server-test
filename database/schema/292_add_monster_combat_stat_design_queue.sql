CREATE OR REPLACE VIEW `god2_game`.`vw_monster_combat_stat_design_queue_readable` AS
SELECT
    monster_row.`monster_id` AS `怪物ID`,
    monster_row.`code` AS `怪物代碼`,
    monster_row.`name_zh_tw` AS `怪物名稱`,
    COALESCE(monster_row.`level`, 0) AS `等級`,
    CASE
        WHEN monster_row.`level` IS NULL THEN '等級未知'
        WHEN monster_row.`level` <= 10 THEN '新手區'
        WHEN monster_row.`level` <= 30 THEN '前期練功'
        WHEN monster_row.`level` <= 60 THEN '中期練功'
        WHEN monster_row.`level` <= 90 THEN '後期練功'
        ELSE '高階或特殊'
    END AS `設計區間`,
    CASE
        WHEN monster_row.`boss` = 1 THEN '首領'
        WHEN monster_row.`elite` = 1 THEN '菁英'
        WHEN monster_row.`is_quest_monster` = 1 THEN '任務怪'
        ELSE '一般怪'
    END AS `怪物定位`,
    CASE
        WHEN monster_row.`max_hp` IS NULL AND monster_row.`max_mp` IS NULL THEN '缺 HP 與 MP'
        WHEN monster_row.`max_hp` IS NULL THEN '缺 HP'
        WHEN monster_row.`max_mp` IS NULL THEN '缺 MP'
        ELSE 'HP/MP 已建立'
    END AS `HPMP缺口`,
    CASE
        WHEN monster_row.`physical_attack` IS NULL
          AND monster_row.`physical_defense` IS NULL
          AND monster_row.`magic_attack` IS NULL
          AND monster_row.`magic_defense` IS NULL THEN '缺全部攻防'
        WHEN monster_row.`physical_attack` IS NULL
          OR monster_row.`physical_defense` IS NULL
          OR monster_row.`magic_attack` IS NULL
          OR monster_row.`magic_defense` IS NULL THEN '攻防不完整'
        ELSE '攻防已建立'
    END AS `攻防缺口`,
    CASE
        WHEN monster_row.`metal` IS NULL
          AND monster_row.`wood` IS NULL
          AND monster_row.`water` IS NULL
          AND monster_row.`fire` IS NULL
          AND monster_row.`earth` IS NULL THEN '缺全部五行'
        WHEN monster_row.`metal` IS NULL
          OR monster_row.`wood` IS NULL
          OR monster_row.`water` IS NULL
          OR monster_row.`fire` IS NULL
          OR monster_row.`earth` IS NULL THEN '五行不完整'
        ELSE '五行已建立'
    END AS `五行缺口`,
    monster_row.`combat_stat_source_zh_tw` AS `目前數值來源`,
    CASE
        WHEN monster_row.`max_hp` IS NOT NULL
         AND monster_row.`max_mp` IS NOT NULL
         AND monster_row.`physical_attack` IS NOT NULL
         AND monster_row.`physical_defense` IS NOT NULL
         AND monster_row.`magic_attack` IS NOT NULL
         AND monster_row.`magic_defense` IS NOT NULL
         AND monster_row.`metal` IS NOT NULL
         AND monster_row.`wood` IS NOT NULL
         AND monster_row.`water` IS NOT NULL
         AND monster_row.`fire` IS NOT NULL
         AND monster_row.`earth` IS NOT NULL
            THEN '可直接進戰鬥測試'
        WHEN monster_row.`boss` = 1 OR monster_row.`elite` = 1
            THEN '高優先：首領/菁英先設計完整數值'
        WHEN monster_row.`is_quest_monster` = 1
            THEN '高優先：任務怪先設計完整數值'
        WHEN monster_row.`level` IS NULL
            THEN '中優先：先補等級或歸類後再設計'
        WHEN monster_row.`level` <= 30
            THEN '中優先：前期練功怪影響新手體驗'
        ELSE '一般優先：可批次套用區間平衡規則'
    END AS `設計優先順序`,
    CASE
        WHEN monster_row.`combat_stat_source_zh_tw` = '待服務端設計'
            THEN '建議使用服務端設計值，後續用官方抓包或實戰測試校準。'
        WHEN monster_row.`combat_stat_source_zh_tw` = '既有正式數據；待來源細分'
            THEN '先可用於測試，但後續要補標為服務端設計值或官方實測值。'
        ELSE monster_row.`combat_stat_policy_zh_tw`
    END AS `建議處理方式`
FROM `god2_game`.`monsters` monster_row
WHERE monster_row.`enabled` = 1
   OR monster_row.`max_hp` IS NULL
   OR monster_row.`max_mp` IS NULL
   OR monster_row.`physical_attack` IS NULL
   OR monster_row.`physical_defense` IS NULL
   OR monster_row.`magic_attack` IS NULL
   OR monster_row.`magic_defense` IS NULL
   OR monster_row.`metal` IS NULL
   OR monster_row.`wood` IS NULL
   OR monster_row.`water` IS NULL
   OR monster_row.`fire` IS NULL
   OR monster_row.`earth` IS NULL
ORDER BY
    CASE
        WHEN monster_row.`max_hp` IS NOT NULL
         AND monster_row.`max_mp` IS NOT NULL
         AND monster_row.`physical_attack` IS NOT NULL
         AND monster_row.`physical_defense` IS NOT NULL
         AND monster_row.`magic_attack` IS NOT NULL
         AND monster_row.`magic_defense` IS NOT NULL
         AND monster_row.`metal` IS NOT NULL
         AND monster_row.`wood` IS NOT NULL
         AND monster_row.`water` IS NOT NULL
         AND monster_row.`fire` IS NOT NULL
         AND monster_row.`earth` IS NOT NULL THEN 90
        WHEN monster_row.`boss` = 1 OR monster_row.`elite` = 1 THEN 10
        WHEN monster_row.`is_quest_monster` = 1 THEN 20
        WHEN monster_row.`level` IS NULL THEN 30
        WHEN monster_row.`level` <= 30 THEN 40
        ELSE 50
    END,
    COALESCE(monster_row.`level`, 9999),
    monster_row.`monster_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_combat_stat_design_queue_summary_readable` AS
SELECT
    queue_row.`設計優先順序`,
    queue_row.`設計區間`,
    queue_row.`怪物定位`,
    COUNT(*) AS `怪物數量`,
    SUM(CASE WHEN queue_row.`HPMP缺口` = 'HP/MP 已建立' THEN 1 ELSE 0 END) AS `HPMP已建立`,
    SUM(CASE WHEN queue_row.`攻防缺口` = '攻防已建立' THEN 1 ELSE 0 END) AS `攻防已建立`,
    SUM(CASE WHEN queue_row.`五行缺口` = '五行已建立' THEN 1 ELSE 0 END) AS `五行已建立`
FROM `god2_game`.`vw_monster_combat_stat_design_queue_readable` queue_row
GROUP BY
    queue_row.`設計優先順序`,
    queue_row.`設計區間`,
    queue_row.`怪物定位`
ORDER BY
    MIN(CASE
        WHEN queue_row.`設計優先順序` LIKE '高優先：首領%' THEN 10
        WHEN queue_row.`設計優先順序` LIKE '高優先：任務怪%' THEN 20
        WHEN queue_row.`設計優先順序` LIKE '中優先：先補等級%' THEN 30
        WHEN queue_row.`設計優先順序` LIKE '中優先：前期%' THEN 40
        WHEN queue_row.`設計優先順序` LIKE '一般優先%' THEN 50
        ELSE 90
    END),
    queue_row.`設計區間`,
    queue_row.`怪物定位`;
