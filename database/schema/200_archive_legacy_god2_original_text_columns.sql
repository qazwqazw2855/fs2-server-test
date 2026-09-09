-- Move legacy source/original display text out of the formal god2 runtime tables.
-- Formal runtime tables keep the zh-TW readable values; archived source text is
-- retained only in god2_research for evidence traceability.

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2_original_text_evidence` (
    `source_schema` varchar(64) NOT NULL,
    `source_table` varchar(64) NOT NULL,
    `source_identity` varchar(191) NOT NULL,
    `source_field` varchar(64) NOT NULL,
    `original_text` longtext NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`source_schema`, `source_table`, `source_identity`, `source_field`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='舊 god2 正式表移出的原文文字證據；正式 runtime 表不再暴露原文欄位';

INSERT INTO `god2_research`.`legacy_god2_original_text_evidence`
    (`source_schema`,`source_table`,`source_identity`,`source_field`,`original_text`,`archived_at_utc`)
SELECT 'god2','items',CAST(`Id` AS char),'OriginalName',`OriginalName`,UTC_TIMESTAMP(6)
FROM `god2`.`items`
WHERE `OriginalName` IS NOT NULL AND `OriginalName` <> ''
ON DUPLICATE KEY UPDATE `original_text`=VALUES(`original_text`),`archived_at_utc`=VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_god2_original_text_evidence`
    (`source_schema`,`source_table`,`source_identity`,`source_field`,`original_text`,`archived_at_utc`)
SELECT 'god2','items',CAST(`Id` AS char),'OriginalDescription',`OriginalDescription`,UTC_TIMESTAMP(6)
FROM `god2`.`items`
WHERE `OriginalDescription` IS NOT NULL AND `OriginalDescription` <> ''
ON DUPLICATE KEY UPDATE `original_text`=VALUES(`original_text`),`archived_at_utc`=VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_god2_original_text_evidence`
    (`source_schema`,`source_table`,`source_identity`,`source_field`,`original_text`,`archived_at_utc`)
SELECT 'god2','maps',CAST(`Id` AS char),'OriginalName',`OriginalName`,UTC_TIMESTAMP(6)
FROM `god2`.`maps`
WHERE `OriginalName` IS NOT NULL AND `OriginalName` <> ''
ON DUPLICATE KEY UPDATE `original_text`=VALUES(`original_text`),`archived_at_utc`=VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_god2_original_text_evidence`
    (`source_schema`,`source_table`,`source_identity`,`source_field`,`original_text`,`archived_at_utc`)
SELECT 'god2','merchants',CAST(`Id` AS char),'OriginalName',`OriginalName`,UTC_TIMESTAMP(6)
FROM `god2`.`merchants`
WHERE `OriginalName` IS NOT NULL AND `OriginalName` <> ''
ON DUPLICATE KEY UPDATE `original_text`=VALUES(`original_text`),`archived_at_utc`=VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_god2_original_text_evidence`
    (`source_schema`,`source_table`,`source_identity`,`source_field`,`original_text`,`archived_at_utc`)
SELECT 'god2','monsters',CAST(`Id` AS char),'OriginalName',`OriginalName`,UTC_TIMESTAMP(6)
FROM `god2`.`monsters`
WHERE `OriginalName` IS NOT NULL AND `OriginalName` <> ''
ON DUPLICATE KEY UPDATE `original_text`=VALUES(`original_text`),`archived_at_utc`=VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_god2_original_text_evidence`
    (`source_schema`,`source_table`,`source_identity`,`source_field`,`original_text`,`archived_at_utc`)
SELECT 'god2','npcs',CAST(`Id` AS char),'OriginalName',`OriginalName`,UTC_TIMESTAMP(6)
FROM `god2`.`npcs`
WHERE `OriginalName` IS NOT NULL AND `OriginalName` <> ''
ON DUPLICATE KEY UPDATE `original_text`=VALUES(`original_text`),`archived_at_utc`=VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_god2_original_text_evidence`
    (`source_schema`,`source_table`,`source_identity`,`source_field`,`original_text`,`archived_at_utc`)
SELECT 'god2','quests',CAST(`Id` AS char),'OriginalName',`OriginalName`,UTC_TIMESTAMP(6)
FROM `god2`.`quests`
WHERE `OriginalName` IS NOT NULL AND `OriginalName` <> ''
ON DUPLICATE KEY UPDATE `original_text`=VALUES(`original_text`),`archived_at_utc`=VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_god2_original_text_evidence`
    (`source_schema`,`source_table`,`source_identity`,`source_field`,`original_text`,`archived_at_utc`)
SELECT 'god2','quests',CAST(`Id` AS char),'OriginalDescription',`OriginalDescription`,UTC_TIMESTAMP(6)
FROM `god2`.`quests`
WHERE `OriginalDescription` IS NOT NULL AND `OriginalDescription` <> ''
ON DUPLICATE KEY UPDATE `original_text`=VALUES(`original_text`),`archived_at_utc`=VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_god2_original_text_evidence`
    (`source_schema`,`source_table`,`source_identity`,`source_field`,`original_text`,`archived_at_utc`)
SELECT 'god2','skills',CAST(`Id` AS char),'OriginalName',`OriginalName`,UTC_TIMESTAMP(6)
FROM `god2`.`skills`
WHERE `OriginalName` IS NOT NULL AND `OriginalName` <> ''
ON DUPLICATE KEY UPDATE `original_text`=VALUES(`original_text`),`archived_at_utc`=VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_god2_original_text_evidence`
    (`source_schema`,`source_table`,`source_identity`,`source_field`,`original_text`,`archived_at_utc`)
SELECT 'god2','skills',CAST(`Id` AS char),'OriginalDescription',`OriginalDescription`,UTC_TIMESTAMP(6)
FROM `god2`.`skills`
WHERE `OriginalDescription` IS NOT NULL AND `OriginalDescription` <> ''
ON DUPLICATE KEY UPDATE `original_text`=VALUES(`original_text`),`archived_at_utc`=VALUES(`archived_at_utc`);

ALTER TABLE `god2`.`items`
    DROP COLUMN IF EXISTS `OriginalName`,
    DROP COLUMN IF EXISTS `OriginalDescription`;

ALTER TABLE `god2`.`maps`
    DROP COLUMN IF EXISTS `OriginalName`;

ALTER TABLE `god2`.`merchants`
    DROP COLUMN IF EXISTS `OriginalName`;

ALTER TABLE `god2`.`monsters`
    DROP COLUMN IF EXISTS `OriginalName`;

ALTER TABLE `god2`.`npcs`
    DROP COLUMN IF EXISTS `OriginalName`;

ALTER TABLE `god2`.`quests`
    DROP COLUMN IF EXISTS `OriginalName`,
    DROP COLUMN IF EXISTS `OriginalDescription`;

ALTER TABLE `god2`.`skills`
    DROP COLUMN IF EXISTS `OriginalName`,
    DROP COLUMN IF EXISTS `OriginalDescription`;
