CREATE TABLE IF NOT EXISTS `god2_game`.`magic_skill_damage_coefficients` (
    `attack_element` varchar(20) NOT NULL COMMENT '法術攻擊五行',
    `skill_tier` int NOT NULL COMMENT '法術技能階級',
    `minimum_multiplier` decimal(8,4) NOT NULL COMMENT '社群實測最低技能倍率',
    `midpoint_multiplier` decimal(8,4) NOT NULL COMMENT '近似運算建議技能倍率',
    `maximum_multiplier` decimal(8,4) NOT NULL COMMENT '社群實測最高技能倍率',
    `attacker_same_element_divisor` decimal(8,4) NOT NULL DEFAULT 5 COMMENT '攻擊者同系屬性加成除數',
    `target_weak_element_divisor` decimal(8,4) NOT NULL DEFAULT 5 COMMENT '目標被剋屬性加成除數',
    `target_counter_element_divisor` decimal(8,4) NOT NULL DEFAULT 5 COMMENT '目標反剋屬性防禦除數',
    `target_same_element_divisor` decimal(8,4) NOT NULL DEFAULT 10 COMMENT '目標同系屬性防禦除數',
    `pet_meditation_multiplier` decimal(8,4) NOT NULL DEFAULT 1 COMMENT '戰寵無冥思修正',
    `character_meditation_candidate` decimal(8,4) NULL COMMENT '人物冥思候選修正，尚未確認',
    `damage_formula_zh_tw` varchar(500) NOT NULL COMMENT '繁體中文魔法傷害公式',
    `evidence_status` varchar(50) NOT NULL COMMENT '實測證據狀態',
    `meditation_evidence_status` varchar(50) NOT NULL COMMENT '冥思證據狀態',
    `origin_version` varchar(100) NOT NULL COMMENT '資料實測版本',
    `region_compatibility_status` varchar(50) NOT NULL COMMENT '台版相容狀態',
    `source_url` varchar(500) NOT NULL COMMENT '資料來源',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否可供近似公式解析',
    `admin_note` varchar(500) NULL COMMENT '管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`attack_element`,`skill_tier`),
    CONSTRAINT `ck_magic_skill_coefficients_element` CHECK (`attack_element` IN ('Metal','Wood','Water','Fire','Earth')),
    CONSTRAINT `ck_magic_skill_coefficients_tier` CHECK (`skill_tier` > 0),
    CONSTRAINT `ck_magic_skill_coefficients_range` CHECK (`minimum_multiplier` > 0 AND `minimum_multiplier` <= `midpoint_multiplier` AND `midpoint_multiplier` <= `maximum_multiplier`),
    CONSTRAINT `ck_magic_skill_coefficients_divisors` CHECK (`attacker_same_element_divisor` > 0 AND `target_weak_element_divisor` > 0 AND `target_counter_element_divisor` > 0 AND `target_same_element_divisor` > 0),
    CONSTRAINT `ck_magic_skill_coefficients_meditation` CHECK (`pet_meditation_multiplier`=1 AND (`character_meditation_candidate` IS NULL OR `character_meditation_candidate` > 0)),
    CONSTRAINT `ck_magic_skill_coefficients_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='跨版本社群實測魔法傷害公式；台版相容性尚待驗證';

INSERT INTO `god2_game`.`magic_skill_damage_coefficients`
    (`attack_element`,`skill_tier`,`minimum_multiplier`,`midpoint_multiplier`,`maximum_multiplier`,
     `attacker_same_element_divisor`,`target_weak_element_divisor`,`target_counter_element_divisor`,
     `target_same_element_divisor`,`pet_meditation_multiplier`,`character_meditation_candidate`,
     `damage_formula_zh_tw`,`evidence_status`,`meditation_evidence_status`,`origin_version`,
     `region_compatibility_status`,`source_url`,`enabled`,`admin_note`)
VALUES
    ('Metal',1,1.0,1.1,1.2,5,5,5,10,1,1.05,
     '（魔攻＋攻擊者金屬性÷5＋目標木屬性÷5－魔防－目標火屬性÷5－目標金屬性÷10）×技能倍率×冥思修正',
     'CommunityExperimentReproduced','Candidate','MainlandCommunityTest','TaiwanUnverified',
     'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=41&sn=1484',1,
     '文章以多組戰鬥數據重現金一倍率；來源自述為大陸版，台版必須另行校準。'),
    ('Metal',2,0.7,0.8,0.9,5,5,5,10,1,1.05,
     '（魔攻＋攻擊者金屬性÷5＋目標木屬性÷5－魔防－目標火屬性÷5－目標金屬性÷10）×技能倍率×冥思修正',
     'CommunityExperimentReproduced','Candidate','MainlandCommunityTest','TaiwanUnverified',
     'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=41&sn=1484',1,
     '文章以多組戰鬥數據重現金二倍率；來源自述為大陸版，台版必須另行校準。')
ON DUPLICATE KEY UPDATE
    `minimum_multiplier`=VALUES(`minimum_multiplier`),
    `midpoint_multiplier`=VALUES(`midpoint_multiplier`),
    `maximum_multiplier`=VALUES(`maximum_multiplier`),
    `attacker_same_element_divisor`=VALUES(`attacker_same_element_divisor`),
    `target_weak_element_divisor`=VALUES(`target_weak_element_divisor`),
    `target_counter_element_divisor`=VALUES(`target_counter_element_divisor`),
    `target_same_element_divisor`=VALUES(`target_same_element_divisor`),
    `pet_meditation_multiplier`=VALUES(`pet_meditation_multiplier`),
    `character_meditation_candidate`=VALUES(`character_meditation_candidate`),
    `damage_formula_zh_tw`=VALUES(`damage_formula_zh_tw`),
    `evidence_status`=VALUES(`evidence_status`),
    `meditation_evidence_status`=VALUES(`meditation_evidence_status`),
    `origin_version`=VALUES(`origin_version`),
    `region_compatibility_status`=VALUES(`region_compatibility_status`),
    `source_url`=VALUES(`source_url`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

CREATE OR REPLACE VIEW `god2_game`.`vw_magic_skill_damage_coefficients_readable` AS
SELECT CASE coefficient.`attack_element`
           WHEN 'Metal' THEN '金系'
           WHEN 'Wood' THEN '木系'
           WHEN 'Water' THEN '水系'
           WHEN 'Fire' THEN '火系'
           WHEN 'Earth' THEN '土系'
       END AS `法術系別`,
       coefficient.`skill_tier` AS `技能階級`,
       coefficient.`minimum_multiplier` AS `最低倍率`,
       coefficient.`midpoint_multiplier` AS `建議倍率`,
       coefficient.`maximum_multiplier` AS `最高倍率`,
       coefficient.`damage_formula_zh_tw` AS `完整傷害公式`,
       coefficient.`pet_meditation_multiplier` AS `戰寵冥思修正`,
       coefficient.`character_meditation_candidate` AS `人物冥思候選修正`,
       '候選，尚未確認' AS `人物冥思證據`,
       '大陸版玩家實測' AS `來源版本`,
       '台版尚待驗證' AS `台版相容狀態`,
       coefficient.`source_url` AS `資料來源`,
       coefficient.`enabled` AS `近似公式可解析`
FROM `god2_game`.`magic_skill_damage_coefficients` coefficient
ORDER BY FIELD(coefficient.`attack_element`,'Metal','Wood','Water','Fire','Earth'),
         coefficient.`skill_tier`;
