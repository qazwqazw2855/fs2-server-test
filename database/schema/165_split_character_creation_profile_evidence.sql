CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_character_creation_profile_evidence` (
    `ClientBuildId` varchar(64) NOT NULL,
    `Class` varchar(32) NOT NULL,
    `Gender` varchar(32) NOT NULL,
    `LifeSkill` varchar(32) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `EvidenceReference` varchar(512) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`ClientBuildId`,`Class`,`Gender`,`LifeSkill`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`legacy_character_creation_profile_evidence`
    (`ClientBuildId`,`Class`,`Gender`,`LifeSkill`,`EvidenceStatus`,`EvidenceReference`)
SELECT `ClientBuildId`,`Class`,`Gender`,`LifeSkill`,`EvidenceStatus`,`EvidenceReference`
FROM `god2`.`character_creation_profiles`
ON DUPLICATE KEY UPDATE
    `EvidenceStatus`=VALUES(`EvidenceStatus`),
    `EvidenceReference`=VALUES(`EvidenceReference`);

ALTER TABLE `god2`.`character_creation_profiles`
    DROP CONSTRAINT IF EXISTS `CK_CharacterCreationProfiles_ProductionGate`,
    DROP CONSTRAINT IF EXISTS `CK_CharacterCreationProfiles_EvidenceStatus`,
    DROP CONSTRAINT IF EXISTS `CK_CharacterCreationProfiles_EvidenceReference`,
    DROP COLUMN IF EXISTS `EvidenceStatus`,
    DROP COLUMN IF EXISTS `EvidenceReference`;

DROP VIEW IF EXISTS `god2_game`.`vw_character_creation_profiles_full`;

CREATE TABLE IF NOT EXISTS `god2_research`.`character_creation_profile_evidence` (
    `profile_id` bigint NOT NULL,
    `client_build_id` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `class_code` varchar(32) NOT NULL,
    `gender_code` varchar(32) NOT NULL,
    `life_skill_code` varchar(32) NOT NULL,
    `evidence_status` varchar(32) NOT NULL,
    `evidence_reference` varchar(512) NULL,
    `admin_note` varchar(500) NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`profile_id`),
    UNIQUE KEY `uq_character_creation_profile_evidence_identity`
        (`client_build_id`,`class_code`,`gender_code`,`life_skill_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='角色建立出生設定的證據狀態與來源；正式表只保留 Runtime 需要的出生設定';

INSERT INTO `god2_research`.`character_creation_profile_evidence`
    (`profile_id`,`client_build_id`,`class_code`,`gender_code`,`life_skill_code`,
     `evidence_status`,`evidence_reference`,`admin_note`)
SELECT `profile_id`,`client_build_id`,`class_code`,`gender_code`,`life_skill_code`,
       `evidence_status`,`evidence_reference`,`admin_note`
FROM `god2_game`.`character_creation_profiles`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `evidence_reference`=VALUES(`evidence_reference`),
    `admin_note`=VALUES(`admin_note`);

ALTER TABLE `god2_game`.`character_creation_profiles`
    DROP CONSTRAINT IF EXISTS `ck_character_creation_gate`,
    DROP CONSTRAINT IF EXISTS `ck_character_creation_evidence`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `evidence_reference`,
    DROP COLUMN IF EXISTS `admin_note`;

CREATE OR REPLACE VIEW `god2_game`.`vw_character_creation_profiles_full` AS
SELECT profile.`profile_id` AS `設定ID`,
       profile.`client_build_id` AS `客戶端版本`,
       profile.`class_code` AS `職業代碼`,
       profile.`gender_code` AS `性別代碼`,
       profile.`life_skill_code` AS `生活技能代碼`,
       map_row.`name_zh_tw` AS `出生地圖`,
       profile.`position_x` AS `出生X`,
       profile.`position_y` AS `出生Y`,
       profile.`enabled` AS `服務端啟用`
FROM `god2_game`.`character_creation_profiles` profile
JOIN `god2_game`.`maps` map_row ON map_row.`map_id` = profile.`map_id`;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`legacy_character_creation_profile_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`legacy_character_creation_profile_evidence` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`character_creation_profile_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`character_creation_profile_evidence` TO 'god2_server'@'127.0.0.1';
