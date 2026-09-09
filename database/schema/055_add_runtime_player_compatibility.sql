-- God2 Classic Server canonical runtime compatibility.
-- This migration only extends the canonical schemas; the evidence database `god2` is read-only.

ALTER TABLE `god2_player`.`accounts`
    ADD COLUMN IF NOT EXISTS `current_session_id` VARCHAR(64) NULL COMMENT '目前登入工作階段識別；NULL 表示未登入' AFTER `last_login_at_utc`,
    ADD COLUMN IF NOT EXISTS `concurrency_token` CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '' COMMENT '帳號樂觀鎖定 token' AFTER `current_session_id`;

ALTER TABLE `god2_player`.`characters`
    ADD COLUMN IF NOT EXISTS `class_code` VARCHAR(32) NULL COMMENT '正式客戶端職業代碼' AFTER `class_id`,
    ADD COLUMN IF NOT EXISTS `gender_code` VARCHAR(32) NULL COMMENT '正式客戶端性別代碼' AFTER `class_name_cache`,
    ADD COLUMN IF NOT EXISTS `life_skill_code` VARCHAR(32) NULL COMMENT '建立角色時選定的生活技能代碼' AFTER `gender_code`,
    ADD COLUMN IF NOT EXISTS `appearance_code` VARCHAR(255) NULL COMMENT '正式客戶端外觀代碼' AFTER `life_skill_code`,
    ADD COLUMN IF NOT EXISTS `deleted_at_utc` DATETIME(6) NULL COMMENT '軟刪除時間；NULL 表示尚未刪除' AFTER `last_played_at_utc`,
    ADD COLUMN IF NOT EXISTS `concurrency_token` CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '' COMMENT '角色樂觀鎖定 token' AFTER `deleted_at_utc`;

SET @god2_sync_mode := 1;

UPDATE `god2_player`.`accounts` canonical
JOIN `god2`.`accounts` evidence ON evidence.`Id` = canonical.`account_id`
SET canonical.`current_session_id` = evidence.`CurrentSessionId`,
    canonical.`concurrency_token` = COALESCE(NULLIF(evidence.`ConcurrencyToken`, ''), MD5(CONCAT('account:', canonical.`account_id`)))
WHERE canonical.`concurrency_token` = '';

UPDATE `god2_player`.`characters` canonical
JOIN `god2`.`characters` evidence ON evidence.`Id` = canonical.`character_id`
SET canonical.`class_code` = evidence.`Class`,
    canonical.`gender_code` = evidence.`Gender`,
    canonical.`life_skill_code` = evidence.`LifeSkill`,
    canonical.`appearance_code` = evidence.`Appearance`,
    canonical.`deleted_at_utc` = evidence.`DeletedAtUtc`,
    canonical.`concurrency_token` = COALESCE(NULLIF(evidence.`ConcurrencyToken`, ''), MD5(CONCAT('character:', canonical.`character_id`)))
WHERE canonical.`concurrency_token` = '';

UPDATE `god2_player`.`accounts`
SET `concurrency_token` = MD5(CONCAT('account:', `account_id`))
WHERE `concurrency_token` = '';

UPDATE `god2_player`.`characters`
SET `concurrency_token` = MD5(CONCAT('character:', `character_id`))
WHERE `concurrency_token` = '';

SET @god2_sync_mode := 0;

CREATE TABLE IF NOT EXISTS `god2_player`.`character_lifecycle_idempotency` (
    `account_id` BIGINT NOT NULL COMMENT '帳號 ID',
    `idempotency_key_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '請求識別碼 SHA-256',
    `operation` VARCHAR(16) NOT NULL COMMENT 'Create、Rename 或 Delete',
    `payload_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '請求內容 SHA-256',
    `result_code` VARCHAR(32) NOT NULL COMMENT '持久化結果碼',
    `result_json` LONGTEXT NOT NULL COMMENT '可重播的結果 JSON',
    `created_at_utc` DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立時間 UTC',
    PRIMARY KEY (`account_id`, `idempotency_key_hash`),
    KEY `ix_character_lifecycle_created` (`created_at_utc`),
    CONSTRAINT `fk_character_lifecycle_account` FOREIGN KEY (`account_id`)
        REFERENCES `god2_player`.`accounts` (`account_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_character_lifecycle_operation` CHECK (`operation` IN ('Create','Rename','Delete')),
    CONSTRAINT `ck_character_lifecycle_json` CHECK (JSON_VALID(`result_json`))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='角色建立、改名與刪除的冪等結果；Runtime 唯一權威位於 god2_player';

CREATE TABLE IF NOT EXISTS `god2_game`.`character_creation_profiles` (
    `profile_id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '角色建立設定 ID',
    `client_build_id` VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '適用的正式客戶端 build ID',
    `class_code` VARCHAR(32) NOT NULL COMMENT '正式客戶端職業代碼',
    `gender_code` VARCHAR(32) NOT NULL COMMENT '正式客戶端性別代碼',
    `life_skill_code` VARCHAR(32) NOT NULL COMMENT '正式客戶端生活技能代碼',
    `map_id` BIGINT NOT NULL COMMENT '出生地圖 ID',
    `position_x` INT NOT NULL COMMENT '出生 X 座標',
    `position_y` INT NOT NULL COMMENT '出生 Y 座標',
    `evidence_status` VARCHAR(32) NOT NULL COMMENT 'Verified 或 Recovered 才可啟用',
    `evidence_reference` VARCHAR(512) NULL COMMENT '可追溯的證據參照',
    `enabled` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '1 才可由 Runtime 使用',
    `admin_note` VARCHAR(500) NULL COMMENT '管理員備註',
    `created_at_utc` DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立時間 UTC',
    `updated_at_utc` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新時間 UTC',
    PRIMARY KEY (`profile_id`),
    UNIQUE KEY `uq_character_creation_profile` (`client_build_id`, `class_code`, `gender_code`, `life_skill_code`),
    KEY `ix_character_creation_map` (`map_id`),
    CONSTRAINT `fk_character_creation_map` FOREIGN KEY (`map_id`)
        REFERENCES `god2_game`.`maps` (`map_id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_character_creation_enabled` CHECK (`enabled` IN (0,1)),
    CONSTRAINT `ck_character_creation_evidence` CHECK (`evidence_status` IN ('Verified','Recovered','Candidate','Derived','EvidenceBlocked')),
    CONSTRAINT `ck_character_creation_gate` CHECK (`enabled` = 0 OR `evidence_status` IN ('Verified','Recovered')),
    CONSTRAINT `ck_character_creation_coordinates` CHECK (`position_x` >= 0 AND `position_y` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='正式角色建立出生設定；缺少可信證據時保持空表並 fail-closed';

CREATE OR REPLACE VIEW `god2_game`.`vw_character_creation_profiles_full` AS
SELECT profile.`profile_id` AS `設定ID`, profile.`client_build_id` AS `客戶端版本`,
       profile.`class_code` AS `職業代碼`, profile.`gender_code` AS `性別代碼`,
       profile.`life_skill_code` AS `生活技能代碼`, map_row.`name_zh_tw` AS `出生地圖`,
       profile.`position_x` AS `出生X`, profile.`position_y` AS `出生Y`,
       profile.`evidence_status` AS `證據狀態`, profile.`enabled` AS `啟用狀態`,
       profile.`admin_note` AS `管理備註`
FROM `god2_game`.`character_creation_profiles` profile
JOIN `god2_game`.`maps` map_row ON map_row.`map_id` = profile.`map_id`;

GRANT SELECT ON `god2_game`.`character_creation_profiles` TO `god2_runtime_role`;
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_player`.`character_lifecycle_idempotency` TO `god2_runtime_role`;
