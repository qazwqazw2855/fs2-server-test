DROP VIEW IF EXISTS `god2_game`.`vw_magic_skill_damage_coefficients_readable`;
DROP TRIGGER IF EXISTS `god2_game`.`trg_adm_u_god2_game_magic_skill_damage_coefficients`;

SET @god2_has_old_meditation_column := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='magic_skill_damage_coefficients'
      AND `COLUMN_NAME`='character_meditation_candidate'
);
SET @god2_meditation_rename_sql := IF(
    @god2_has_old_meditation_column=1,
    'ALTER TABLE `god2_game`.`magic_skill_damage_coefficients` CHANGE COLUMN `character_meditation_candidate` `xiandao_meditation_level1_bonus` decimal(8,4) NULL COMMENT ''仙道同系冥思一階加到技能係數的修正值''',
    'SELECT 1'
);
PREPARE god2_meditation_rename_statement FROM @god2_meditation_rename_sql;
EXECUTE god2_meditation_rename_statement;
DEALLOCATE PREPARE god2_meditation_rename_statement;

SET @god2_has_level2_meditation_column := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='magic_skill_damage_coefficients'
      AND `COLUMN_NAME`='xiandao_meditation_level2_bonus'
);
SET @god2_meditation_level2_sql := IF(
    @god2_has_level2_meditation_column=0,
    'ALTER TABLE `god2_game`.`magic_skill_damage_coefficients` ADD COLUMN `xiandao_meditation_level2_bonus` decimal(8,4) NULL COMMENT ''仙道同系冥思二階加到技能係數的修正值'' AFTER `xiandao_meditation_level1_bonus`',
    'SELECT 1'
);
PREPARE god2_meditation_level2_statement FROM @god2_meditation_level2_sql;
EXECUTE god2_meditation_level2_statement;
DEALLOCATE PREPARE god2_meditation_level2_statement;

INSERT INTO `god2_game`.`magic_skill_damage_coefficients`
    (`attack_element`,`skill_tier`,`minimum_multiplier`,`midpoint_multiplier`,`maximum_multiplier`,
     `attacker_same_element_divisor`,`target_weak_element_divisor`,`target_counter_element_divisor`,
     `target_same_element_divisor`,`pet_meditation_multiplier`,`xiandao_meditation_level1_bonus`,
     `xiandao_meditation_level2_bonus`,
     `damage_formula_zh_tw`,`evidence_status`,`meditation_evidence_status`,`origin_version`,
     `region_compatibility_status`,`source_url`,`enabled`,`admin_note`)
VALUES
    ('Metal',1,1.0,1.1,1.2,5,5,5,10,1,0.05,0.10,
     '（魔攻＋攻擊者同系屬性／5＋目標被剋屬性／5－魔防－目標反剋屬性／5－目標同系屬性／10）×技能倍率×冥思修正',
     'ProductionCompatibilityAccepted','Accepted','MainlandCommunityTest','ProductionAccepted',
     'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=41&sn=1484',1,'服務端正式採用的實測相容公式。'),
    ('Wood',1,1.0,1.1,1.2,5,5,5,10,1,0.05,0.10,
     '（魔攻＋攻擊者同系屬性／5＋目標被剋屬性／5－魔防－目標反剋屬性／5－目標同系屬性／10）×技能倍率×冥思修正',
     'ProductionCompatibilityAccepted','Accepted','MainlandCommunityTest','ProductionAccepted',
     'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=41&sn=1484',1,'服務端正式採用的實測相容公式。'),
    ('Water',1,1.0,1.1,1.2,5,5,5,10,1,0.05,0.10,
     '（魔攻＋攻擊者同系屬性／5＋目標被剋屬性／5－魔防－目標反剋屬性／5－目標同系屬性／10）×技能倍率×冥思修正',
     'ProductionCompatibilityAccepted','Accepted','MainlandCommunityTest','ProductionAccepted',
     'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=41&sn=1484',1,'服務端正式採用的實測相容公式。'),
    ('Fire',1,1.0,1.1,1.2,5,5,5,10,1,0.05,0.10,
     '（魔攻＋攻擊者同系屬性／5＋目標被剋屬性／5－魔防－目標反剋屬性／5－目標同系屬性／10）×技能倍率×冥思修正',
     'ProductionCompatibilityAccepted','Accepted','MainlandCommunityTest','ProductionAccepted',
     'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=41&sn=1484',1,'服務端正式採用的實測相容公式。'),
    ('Earth',1,1.0,1.1,1.2,5,5,5,10,1,0.05,0.10,
     '（魔攻＋攻擊者同系屬性／5＋目標被剋屬性／5－魔防－目標反剋屬性／5－目標同系屬性／10）×技能倍率×冥思修正',
     'ProductionCompatibilityAccepted','Accepted','MainlandCommunityTest','ProductionAccepted',
     'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=41&sn=1484',1,'服務端正式採用的實測相容公式。'),
    ('Metal',2,0.7,0.8,0.9,5,5,5,10,1,0.05,0.10,
     '（魔攻＋攻擊者同系屬性／5＋目標被剋屬性／5－魔防－目標反剋屬性／5－目標同系屬性／10）×技能倍率×冥思修正',
     'ProductionCompatibilityAccepted','Accepted','MainlandCommunityTest','ProductionAccepted',
     'https://forum.gamer.com.tw/G2.php?bsn=8395&parent=41&sn=1484',1,'服務端正式採用的實測相容公式。')
ON DUPLICATE KEY UPDATE
    `minimum_multiplier`=VALUES(`minimum_multiplier`),
    `midpoint_multiplier`=VALUES(`midpoint_multiplier`),
    `maximum_multiplier`=VALUES(`maximum_multiplier`),
    `attacker_same_element_divisor`=VALUES(`attacker_same_element_divisor`),
    `target_weak_element_divisor`=VALUES(`target_weak_element_divisor`),
    `target_counter_element_divisor`=VALUES(`target_counter_element_divisor`),
    `target_same_element_divisor`=VALUES(`target_same_element_divisor`),
    `pet_meditation_multiplier`=VALUES(`pet_meditation_multiplier`),
    `xiandao_meditation_level1_bonus`=VALUES(`xiandao_meditation_level1_bonus`),
    `xiandao_meditation_level2_bonus`=VALUES(`xiandao_meditation_level2_bonus`),
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
       coefficient.`midpoint_multiplier` AS `中間倍率`,
       coefficient.`maximum_multiplier` AS `最高倍率`,
       coefficient.`damage_formula_zh_tw` AS `完整傷害公式`,
       coefficient.`pet_meditation_multiplier` AS `無冥思倍率`,
       coefficient.`xiandao_meditation_level1_bonus` AS `仙道同系冥思一階係數加成`,
       coefficient.`xiandao_meditation_level2_bonus` AS `仙道同系冥思二階係數加成`,
       coefficient.`evidence_status` AS `服務端採用狀態`,
       coefficient.`region_compatibility_status` AS `相容狀態`,
       coefficient.`source_url` AS `資料來源`,
       coefficient.`enabled` AS `正式啟用`
FROM `god2_game`.`magic_skill_damage_coefficients` coefficient
ORDER BY FIELD(coefficient.`attack_element`,'Metal','Wood','Water','Fire','Earth'),
         coefficient.`skill_tier`;
