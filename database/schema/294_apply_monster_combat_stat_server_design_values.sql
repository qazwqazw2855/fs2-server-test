UPDATE `god2_game`.`monsters` monster_row
JOIN `god2_game`.`monster_combat_stat_design_rules` rule_row
    ON rule_row.`enabled` = 1
   AND rule_row.`monster_role_zh_tw` = CASE
        WHEN monster_row.`boss` = 1 THEN '首領'
        WHEN monster_row.`elite` = 1 THEN '菁英'
        WHEN monster_row.`is_quest_monster` = 1 THEN '任務怪'
        WHEN monster_row.`level` IS NULL THEN '等級未知'
        ELSE '一般怪'
   END
   AND COALESCE(monster_row.`level`, 0) BETWEEN rule_row.`level_min` AND rule_row.`level_max`
SET
    monster_row.`max_hp` = COALESCE(monster_row.`max_hp`, GREATEST(20, ROUND(((COALESCE(monster_row.`level`, 1) * COALESCE(monster_row.`level`, 1) * 4) + (COALESCE(monster_row.`level`, 1) * 30) + 30) * rule_row.`hp_multiplier`))),
    monster_row.`max_mp` = COALESCE(monster_row.`max_mp`, GREATEST(0, ROUND((COALESCE(monster_row.`level`, 1) * 8 + 10) * rule_row.`mp_multiplier`))),
    monster_row.`physical_attack` = COALESCE(monster_row.`physical_attack`, GREATEST(1, ROUND((COALESCE(monster_row.`level`, 1) * 3 + 8) * rule_row.`attack_multiplier`))),
    monster_row.`physical_defense` = COALESCE(monster_row.`physical_defense`, GREATEST(1, ROUND((COALESCE(monster_row.`level`, 1) * 2 + 6) * rule_row.`defense_multiplier`))),
    monster_row.`magic_attack` = COALESCE(monster_row.`magic_attack`, GREATEST(1, ROUND((COALESCE(monster_row.`level`, 1) * 3 + 8) * rule_row.`attack_multiplier` * rule_row.`magic_bias_multiplier`))),
    monster_row.`magic_defense` = COALESCE(monster_row.`magic_defense`, GREATEST(1, ROUND((COALESCE(monster_row.`level`, 1) * 2 + 6) * rule_row.`defense_multiplier` * rule_row.`magic_bias_multiplier`))),
    monster_row.`metal` = COALESCE(monster_row.`metal`, ROUND(rule_row.`element_total` / 5)),
    monster_row.`wood` = COALESCE(monster_row.`wood`, ROUND(rule_row.`element_total` / 5)),
    monster_row.`water` = COALESCE(monster_row.`water`, ROUND(rule_row.`element_total` / 5)),
    monster_row.`fire` = COALESCE(monster_row.`fire`, ROUND(rule_row.`element_total` / 5)),
    monster_row.`earth` = COALESCE(monster_row.`earth`, rule_row.`element_total` - (ROUND(rule_row.`element_total` / 5) * 4)),
    monster_row.`experience_reward` = COALESCE(monster_row.`experience_reward`, GREATEST(1, ROUND((COALESCE(monster_row.`level`, 1) * 12 + 8) * rule_row.`experience_multiplier`))),
    monster_row.`currency_reward` = COALESCE(monster_row.`currency_reward`, GREATEST(0, ROUND((COALESCE(monster_row.`level`, 1) * 3) * rule_row.`currency_multiplier`))),
    monster_row.`combat_stat_source_zh_tw` = CASE
        WHEN monster_row.`combat_stat_source_zh_tw` = '既有正式數據；待來源細分'
            THEN '既有正式數據與服務端設計值混合'
        ELSE '服務端設計值'
    END,
    monster_row.`combat_stat_policy_zh_tw` = CASE
        WHEN monster_row.`combat_stat_source_zh_tw` = '既有正式數據；待來源細分'
            THEN CONCAT('既有正式 HP/MP 保留，其餘缺漏欄位依服務端規則 ', rule_row.`rule_code`, ' 補齊；不得標成官方實測。')
        ELSE CONCAT('依服務端規則 ', rule_row.`rule_code`, ' 建立 provisional 戰鬥數值；後續可用官方抓包或實戰測試校準。')
    END,
    monster_row.`enabled` = CASE
        WHEN monster_row.`enabled` = 1 THEN 1
        ELSE 0
    END,
    monster_row.`updated_at_utc` = utc_timestamp(6)
WHERE monster_row.`max_hp` IS NULL
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
   OR monster_row.`experience_reward` IS NULL
   OR monster_row.`currency_reward` IS NULL;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_combat_stat_runtime_readiness_readable` AS
SELECT
    monster_row.`combat_stat_source_zh_tw` AS `數值來源`,
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
            THEN '戰鬥數值完整'
        ELSE '仍有缺值'
    END AS `戰鬥數值狀態`,
    COUNT(*) AS `怪物數量`,
    SUM(CASE WHEN monster_row.`enabled` = 1 THEN 1 ELSE 0 END) AS `已啟用怪物`,
    SUM(CASE WHEN monster_row.`boss` = 1 THEN 1 ELSE 0 END) AS `首領數量`,
    SUM(CASE WHEN monster_row.`elite` = 1 THEN 1 ELSE 0 END) AS `菁英數量`,
    SUM(CASE WHEN monster_row.`is_quest_monster` = 1 THEN 1 ELSE 0 END) AS `任務怪數量`
FROM `god2_game`.`monsters` monster_row
GROUP BY
    monster_row.`combat_stat_source_zh_tw`,
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
            THEN '戰鬥數值完整'
        ELSE '仍有缺值'
    END
ORDER BY `怪物數量` DESC, `數值來源`;
