CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

DROP VIEW IF EXISTS `god2_game`.`vw_magic_skill_damage_coefficients_readable`;

CREATE TABLE IF NOT EXISTS `god2_research`.`magic_skill_damage_coefficient_evidence` (
    `attack_element` varchar(20) NOT NULL,
    `skill_tier` int NOT NULL,
    `evidence_status` varchar(50) NOT NULL,
    `meditation_evidence_status` varchar(50) NOT NULL,
    `origin_version` varchar(100) NOT NULL,
    `region_compatibility_status` varchar(50) NOT NULL,
    `source_url` varchar(500) NOT NULL,
    `admin_note` varchar(500) NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`attack_element`,`skill_tier`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='魔法技能傷害公式與仙道冥思係數的證據狀態、版本與來源；正式表只保留計算參數';

INSERT INTO `god2_research`.`magic_skill_damage_coefficient_evidence`
    (`attack_element`,`skill_tier`,`evidence_status`,`meditation_evidence_status`,
     `origin_version`,`region_compatibility_status`,`source_url`,`admin_note`)
SELECT `attack_element`,`skill_tier`,`evidence_status`,`meditation_evidence_status`,
       `origin_version`,`region_compatibility_status`,`source_url`,`admin_note`
FROM `god2_game`.`magic_skill_damage_coefficients`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `meditation_evidence_status`=VALUES(`meditation_evidence_status`),
    `origin_version`=VALUES(`origin_version`),
    `region_compatibility_status`=VALUES(`region_compatibility_status`),
    `source_url`=VALUES(`source_url`),
    `admin_note`=VALUES(`admin_note`);

ALTER TABLE `god2_game`.`magic_skill_damage_coefficients`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `meditation_evidence_status`,
    DROP COLUMN IF EXISTS `origin_version`,
    DROP COLUMN IF EXISTS `region_compatibility_status`,
    DROP COLUMN IF EXISTS `source_url`,
    DROP COLUMN IF EXISTS `admin_note`;

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
       coefficient.`enabled` AS `正式啟用`
FROM `god2_game`.`magic_skill_damage_coefficients` coefficient
ORDER BY FIELD(coefficient.`attack_element`,'Metal','Wood','Water','Fire','Earth'),
         coefficient.`skill_tier`;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`magic_skill_damage_coefficient_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`magic_skill_damage_coefficient_evidence` TO 'god2_server'@'127.0.0.1';
