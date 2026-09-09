CREATE TABLE IF NOT EXISTS `god2_game`.`monster_combat_stat_design_rules` (
    `rule_id` int NOT NULL AUTO_INCREMENT COMMENT '怪物戰鬥數值設計規則 ID',
    `rule_code` varchar(64) NOT NULL COMMENT '穩定規則代碼',
    `level_min` int NOT NULL COMMENT '適用最低等級',
    `level_max` int NOT NULL COMMENT '適用最高等級',
    `monster_role_zh_tw` varchar(32) NOT NULL COMMENT '怪物定位：一般怪、任務怪、菁英、首領或等級未知',
    `hp_multiplier` decimal(8,3) NOT NULL COMMENT 'HP 設計倍率',
    `mp_multiplier` decimal(8,3) NOT NULL COMMENT 'MP 設計倍率',
    `attack_multiplier` decimal(8,3) NOT NULL COMMENT '攻擊設計倍率',
    `defense_multiplier` decimal(8,3) NOT NULL COMMENT '防禦設計倍率',
    `magic_bias_multiplier` decimal(8,3) NOT NULL COMMENT '魔攻魔防相對物攻物防倍率',
    `element_total` int NOT NULL COMMENT '五行總點數設計值',
    `experience_multiplier` decimal(8,3) NOT NULL COMMENT '經驗獎勵設計倍率',
    `currency_multiplier` decimal(8,3) NOT NULL COMMENT '貨幣獎勵設計倍率',
    `source_policy_zh_tw` varchar(256) NOT NULL COMMENT '來源政策與使用限制',
    `enabled` tinyint NOT NULL DEFAULT 1 COMMENT '是否啟用此設計規則',
    `created_at_utc` datetime NOT NULL DEFAULT utc_timestamp(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime NOT NULL DEFAULT utc_timestamp(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`rule_id`),
    UNIQUE KEY `ux_monster_combat_stat_design_rules_code` (`rule_code`),
    KEY `ix_monster_combat_stat_design_rules_lookup` (`enabled`, `monster_role_zh_tw`, `level_min`, `level_max`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='服務端自訂怪物戰鬥數值設計規則；非官方實測資料';

INSERT INTO `god2_game`.`monster_combat_stat_design_rules`
    (`rule_code`,`level_min`,`level_max`,`monster_role_zh_tw`,`hp_multiplier`,`mp_multiplier`,`attack_multiplier`,`defense_multiplier`,`magic_bias_multiplier`,`element_total`,`experience_multiplier`,`currency_multiplier`,`source_policy_zh_tw`,`enabled`)
VALUES
    ('unknown_general_default',0,0,'等級未知',1.000,1.000,1.000,1.000,0.850,20,1.000,1.000,'等級未知怪物的臨時保底設計；正式啟用前應補等級或歸類。',1),
    ('general_001_010',1,10,'一般怪',1.000,0.650,1.000,0.850,0.750,20,1.000,0.800,'新手區一般怪，偏低傷害與低 MP，優先保證可打死與不秒殺玩家。',1),
    ('general_011_030',11,30,'一般怪',1.050,0.750,1.050,0.900,0.800,35,1.100,0.900,'前期練功一般怪，作為新手到前期地圖的標準曲線。',1),
    ('general_031_060',31,60,'一般怪',1.150,0.900,1.150,1.000,0.900,55,1.250,1.050,'中期練功一般怪，允許較完整攻防與五行差異。',1),
    ('general_061_090',61,90,'一般怪',1.300,1.050,1.300,1.150,1.000,75,1.450,1.200,'後期練功一般怪，需搭配地圖與職業成長再校準。',1),
    ('general_091_999',91,999,'一般怪',1.500,1.200,1.500,1.300,1.100,100,1.700,1.400,'高階一般怪，僅作高階區間保底設計，應以實戰測試調整。',1),
    ('quest_001_999',1,999,'任務怪',1.200,0.850,1.100,1.000,0.900,50,1.250,1.000,'任務怪需避免卡任務；血量略高但傷害不宜過度。',1),
    ('elite_001_999',1,999,'菁英',2.200,1.300,1.550,1.350,1.050,80,2.200,1.800,'菁英怪作為小挑戰，允許較高血量與攻防。',1),
    ('boss_001_999',1,999,'首領',5.000,2.200,2.100,1.800,1.200,120,5.000,3.500,'首領怪只作服務端平衡候選；正式開服前需要至少實戰測試校準。',1)
ON DUPLICATE KEY UPDATE
    `level_min`=VALUES(`level_min`),
    `level_max`=VALUES(`level_max`),
    `monster_role_zh_tw`=VALUES(`monster_role_zh_tw`),
    `hp_multiplier`=VALUES(`hp_multiplier`),
    `mp_multiplier`=VALUES(`mp_multiplier`),
    `attack_multiplier`=VALUES(`attack_multiplier`),
    `defense_multiplier`=VALUES(`defense_multiplier`),
    `magic_bias_multiplier`=VALUES(`magic_bias_multiplier`),
    `element_total`=VALUES(`element_total`),
    `experience_multiplier`=VALUES(`experience_multiplier`),
    `currency_multiplier`=VALUES(`currency_multiplier`),
    `source_policy_zh_tw`=VALUES(`source_policy_zh_tw`),
    `enabled`=VALUES(`enabled`),
    `updated_at_utc`=utc_timestamp(6);

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_combat_stat_design_rules_readable` AS
SELECT
    rule_row.`rule_code` AS `規則代碼`,
    CONCAT(rule_row.`level_min`, ' 到 ', rule_row.`level_max`) AS `等級範圍`,
    rule_row.`monster_role_zh_tw` AS `怪物定位`,
    CONCAT('HP 倍率 ', rule_row.`hp_multiplier`, '；MP 倍率 ', rule_row.`mp_multiplier`) AS `HPMP倍率`,
    CONCAT('攻擊倍率 ', rule_row.`attack_multiplier`, '；防禦倍率 ', rule_row.`defense_multiplier`, '；魔法偏向 ', rule_row.`magic_bias_multiplier`) AS `攻防倍率`,
    rule_row.`element_total` AS `五行總點數`,
    CONCAT('經驗倍率 ', rule_row.`experience_multiplier`, '；貨幣倍率 ', rule_row.`currency_multiplier`) AS `獎勵倍率`,
    rule_row.`source_policy_zh_tw` AS `使用政策`,
    CASE rule_row.`enabled`
        WHEN 1 THEN '啟用'
        ELSE '停用'
    END AS `規則狀態`
FROM `god2_game`.`monster_combat_stat_design_rules` rule_row;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_combat_stat_design_candidates_readable` AS
SELECT
    monster_row.`monster_id` AS `怪物ID`,
    monster_row.`code` AS `怪物代碼`,
    monster_row.`name_zh_tw` AS `怪物名稱`,
    COALESCE(monster_row.`level`, 0) AS `等級`,
    CASE
        WHEN monster_row.`boss` = 1 THEN '首領'
        WHEN monster_row.`elite` = 1 THEN '菁英'
        WHEN monster_row.`is_quest_monster` = 1 THEN '任務怪'
        WHEN monster_row.`level` IS NULL THEN '等級未知'
        ELSE '一般怪'
    END AS `怪物定位`,
    rule_row.`rule_code` AS `套用規則`,
    COALESCE(monster_row.`max_hp`, GREATEST(20, ROUND(((COALESCE(monster_row.`level`, 1) * COALESCE(monster_row.`level`, 1) * 4) + (COALESCE(monster_row.`level`, 1) * 30) + 30) * rule_row.`hp_multiplier`))) AS `建議HP`,
    COALESCE(monster_row.`max_mp`, GREATEST(0, ROUND((COALESCE(monster_row.`level`, 1) * 8 + 10) * rule_row.`mp_multiplier`))) AS `建議MP`,
    COALESCE(monster_row.`physical_attack`, GREATEST(1, ROUND((COALESCE(monster_row.`level`, 1) * 3 + 8) * rule_row.`attack_multiplier`))) AS `建議物攻`,
    COALESCE(monster_row.`physical_defense`, GREATEST(1, ROUND((COALESCE(monster_row.`level`, 1) * 2 + 6) * rule_row.`defense_multiplier`))) AS `建議物防`,
    COALESCE(monster_row.`magic_attack`, GREATEST(1, ROUND((COALESCE(monster_row.`level`, 1) * 3 + 8) * rule_row.`attack_multiplier` * rule_row.`magic_bias_multiplier`))) AS `建議魔攻`,
    COALESCE(monster_row.`magic_defense`, GREATEST(1, ROUND((COALESCE(monster_row.`level`, 1) * 2 + 6) * rule_row.`defense_multiplier` * rule_row.`magic_bias_multiplier`))) AS `建議魔防`,
    COALESCE(monster_row.`metal`, ROUND(rule_row.`element_total` / 5)) AS `建議金`,
    COALESCE(monster_row.`wood`, ROUND(rule_row.`element_total` / 5)) AS `建議木`,
    COALESCE(monster_row.`water`, ROUND(rule_row.`element_total` / 5)) AS `建議水`,
    COALESCE(monster_row.`fire`, ROUND(rule_row.`element_total` / 5)) AS `建議火`,
    COALESCE(monster_row.`earth`, rule_row.`element_total` - (ROUND(rule_row.`element_total` / 5) * 4)) AS `建議土`,
    COALESCE(monster_row.`experience_reward`, GREATEST(1, ROUND((COALESCE(monster_row.`level`, 1) * 12 + 8) * rule_row.`experience_multiplier`))) AS `建議經驗`,
    COALESCE(monster_row.`currency_reward`, GREATEST(0, ROUND((COALESCE(monster_row.`level`, 1) * 3) * rule_row.`currency_multiplier`))) AS `建議貨幣`,
    monster_row.`combat_stat_source_zh_tw` AS `目前數值來源`,
    '服務端設計候選；套用前需經過批次檢查與戰鬥測試。' AS `候選值政策`
FROM `god2_game`.`monsters` monster_row
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
