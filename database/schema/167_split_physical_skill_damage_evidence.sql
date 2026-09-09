CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

DROP VIEW IF EXISTS `god2_game`.`vw_physical_skill_damage_coefficients_readable`;

CREATE TABLE IF NOT EXISTS `god2_research`.`physical_skill_damage_coefficient_evidence` (
    `skill_family` varchar(30) NOT NULL,
    `skill_tier` int NOT NULL,
    `evidence_status` varchar(40) NOT NULL,
    `independent_source_count` int NOT NULL,
    `primary_source_url` varchar(500) NOT NULL,
    `corroborating_source_url` varchar(500) NULL,
    `admin_note` varchar(500) NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`skill_family`,`skill_tier`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='物理技能傷害倍率的社群實測證據與來源；正式表只保留傷害計算參數';

INSERT INTO `god2_research`.`physical_skill_damage_coefficient_evidence`
    (`skill_family`,`skill_tier`,`evidence_status`,`independent_source_count`,
     `primary_source_url`,`corroborating_source_url`,`admin_note`)
SELECT `skill_family`,`skill_tier`,`evidence_status`,`independent_source_count`,
       `primary_source_url`,`corroborating_source_url`,`admin_note`
FROM `god2_game`.`physical_skill_damage_coefficients`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `independent_source_count`=VALUES(`independent_source_count`),
    `primary_source_url`=VALUES(`primary_source_url`),
    `corroborating_source_url`=VALUES(`corroborating_source_url`),
    `admin_note`=VALUES(`admin_note`);

ALTER TABLE `god2_game`.`physical_skill_damage_coefficients`
    DROP CONSTRAINT IF EXISTS `ck_physical_skill_coefficients_sources`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `independent_source_count`,
    DROP COLUMN IF EXISTS `primary_source_url`,
    DROP COLUMN IF EXISTS `corroborating_source_url`,
    DROP COLUMN IF EXISTS `admin_note`;

CREATE OR REPLACE VIEW `god2_game`.`vw_physical_skill_damage_coefficients_readable` AS
SELECT CASE coefficient.`skill_family`
           WHEN 'Blade' THEN '刀'
           WHEN 'Sword' THEN '劍'
           WHEN 'Staff' THEN '棍'
           WHEN 'Whip' THEN '鞭'
           WHEN 'Spear' THEN '槍'
           WHEN 'ThrowingKnife' THEN '飛刀'
       END AS `物理技能系別`,
       coefficient.`skill_tier` AS `技能階級`,
       coefficient.`minimum_multiplier` AS `最低倍率`,
       coefficient.`midpoint_multiplier` AS `中間倍率`,
       coefficient.`maximum_multiplier` AS `最高倍率`,
       coefficient.`damage_formula_zh_tw` AS `完整傷害公式`,
       coefficient.`enabled` AS `正式啟用`
FROM `god2_game`.`physical_skill_damage_coefficients` coefficient
ORDER BY FIELD(coefficient.`skill_family`,'Blade','Sword','Staff','Whip','Spear','ThrowingKnife'),
         coefficient.`skill_tier`;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`physical_skill_damage_coefficient_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`physical_skill_damage_coefficient_evidence` TO 'god2_server'@'127.0.0.1';
