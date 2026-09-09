DROP VIEW IF EXISTS `god2_game`.`vw_blackbox_combat_formula_test_targets_readable`;

CREATE VIEW `god2_game`.`vw_blackbox_combat_formula_test_targets_readable` AS
SELECT
    m.`monster_id` AS `怪物ID`,
    m.`code` AS `服務端代碼`,
    m.`name_zh_tw` AS `怪物名稱`,
    m.`level` AS `等級`,
    m.`max_hp` AS `最大HP`,
    m.`max_mp` AS `最大MP`,
    m.`physical_attack` AS `物理攻擊`,
    m.`physical_defense` AS `物理防禦`,
    m.`magic_attack` AS `法術攻擊`,
    m.`magic_defense` AS `法術防禦`,
    m.`metal` AS `金`,
    m.`wood` AS `木`,
    m.`water` AS `水`,
    m.`fire` AS `火`,
    m.`earth` AS `土`,
    m.`experience_reward` AS `經驗`,
    m.`currency_reward` AS `金錢`,
    COALESCE(m.`combat_stat_source_zh_tw`, '') AS `數值來源`,
    COALESCE(m.`combat_stat_policy_zh_tw`, '') AS `數值政策`,
    COALESCE(spawn_summary.`出生點數`, 0) AS `出生點數`,
    COALESCE(spawn_summary.`已啟用出生點數`, 0) AS `已啟用出生點數`,
    COALESCE(spawn_summary.`第一個地圖ID`, 0) AS `第一個地圖ID`,
    COALESCE(spawn_summary.`第一個地圖名稱`, '') AS `第一個地圖名稱`,
    spawn_summary.`第一個X` AS `第一個X`,
    spawn_summary.`第一個Y` AS `第一個Y`,
    COALESCE(drop_summary.`掉落資料數`, 0) AS `掉落資料數`,
    COALESCE(drop_summary.`已啟用掉落數`, 0) AS `已啟用掉落數`,
    COALESCE(skill_summary.`怪物技能數`, 0) AS `怪物技能數`,
    COALESCE(skill_summary.`怪物技能摘要`, '') AS `怪物技能摘要`,
    CASE
        WHEN m.`max_mp` IS NULL OR m.`max_mp` <= 0 THEN '缺MP或MP為0；優先黑箱確認怪物MP顯示與消耗'
        ELSE '已有MP；實測確認官方怪物MP、施法消耗與回合變化'
    END AS `MP測試重點`,
    CASE
        WHEN m.`enabled` = 1
             AND COALESCE(spawn_summary.`已啟用出生點數`, 0) > 0
             AND m.`max_hp` > 0
             AND m.`max_mp` IS NOT NULL
            THEN '第一優先：可實機遇怪測HP/MP與攻防公式'
        WHEN m.`enabled` = 1
             AND COALESCE(spawn_summary.`已啟用出生點數`, 0) > 0
             AND m.`max_hp` > 0
            THEN '第二優先：可實機測HP與物攻物防，MP待補'
        WHEN m.`enabled` = 1
             AND m.`max_hp` > 0
            THEN '第三優先：數值存在但缺啟用出生點'
        ELSE '擱置：怪物未啟用或缺HP'
    END AS `黑箱測試優先級`,
    CONCAT(
        '測玩家普攻、技能傷害、怪物反擊、選擇防禦時物防/魔防x1.5、五行相剋/相生、擊殺經驗金錢',
        CASE WHEN COALESCE(drop_summary.`掉落資料數`, 0) > 0 THEN '、掉落' ELSE '；掉落待補' END
    ) AS `測試重點`,
    CASE WHEN m.`enabled` = 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`monsters` m
LEFT JOIN (
    SELECT
        s.`monster_id`,
        COUNT(*) AS `出生點數`,
        SUM(CASE WHEN s.`enabled` = 1 THEN 1 ELSE 0 END) AS `已啟用出生點數`,
        MIN(s.`map_id`) AS `第一個地圖ID`,
        SUBSTRING_INDEX(GROUP_CONCAT(COALESCE(s.`map_name_cache`, '') ORDER BY s.`enabled` DESC, s.`spawn_id` SEPARATOR '\n'), '\n', 1) AS `第一個地圖名稱`,
        CAST(SUBSTRING_INDEX(GROUP_CONCAT(s.`position_x` ORDER BY s.`enabled` DESC, s.`spawn_id` SEPARATOR ','), ',', 1) AS SIGNED) AS `第一個X`,
        CAST(SUBSTRING_INDEX(GROUP_CONCAT(s.`position_y` ORDER BY s.`enabled` DESC, s.`spawn_id` SEPARATOR ','), ',', 1) AS SIGNED) AS `第一個Y`
    FROM `god2_game`.`monster_spawns` s
    GROUP BY s.`monster_id`
) spawn_summary ON spawn_summary.`monster_id` = m.`monster_id`
LEFT JOIN (
    SELECT
        d.`monster_id`,
        COUNT(*) AS `掉落資料數`,
        SUM(CASE WHEN d.`enabled` = 1 THEN 1 ELSE 0 END) AS `已啟用掉落數`
    FROM `god2_game`.`monster_drops` d
    GROUP BY d.`monster_id`
) drop_summary ON drop_summary.`monster_id` = m.`monster_id`
LEFT JOIN (
    SELECT
        ms.`monster_id`,
        COUNT(*) AS `怪物技能數`,
        GROUP_CONCAT(
            CONCAT(
                COALESCE(ms.`skill_name_cache`, ''),
                ':', COALESCE(ms.`trigger_type`, ''),
                ':', COALESCE(ms.`use_probability`, 0)
            )
            ORDER BY ms.`priority`, ms.`monster_skill_id`
            SEPARATOR '；'
        ) AS `怪物技能摘要`
    FROM `god2_game`.`monster_skills` ms
    WHERE ms.`enabled` = 1
    GROUP BY ms.`monster_id`
) skill_summary ON skill_summary.`monster_id` = m.`monster_id`;
