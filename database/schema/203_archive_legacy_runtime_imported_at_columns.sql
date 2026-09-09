CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_runtime_imported_at_evidence` (
  `source_schema` varchar(64) NOT NULL,
  `source_table` varchar(64) NOT NULL,
  `source_identity` varchar(128) NOT NULL,
  `imported_at_utc` datetime(6) NOT NULL,
  `archived_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
  PRIMARY KEY (`source_schema`, `source_table`, `source_identity`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='封存舊 god2 遊戲資料表的匯入時間欄位，避免正式資料庫保留非遊戲內容欄位。';

INSERT INTO `god2_research`.`legacy_runtime_imported_at_evidence`
  (`source_schema`, `source_table`, `source_identity`, `imported_at_utc`, `archived_at_utc`)
SELECT 'god2', 'maps', CAST(`Id` AS char), `ImportedAtUtc`, UTC_TIMESTAMP(6)
FROM `god2`.`maps`
WHERE `ImportedAtUtc` IS NOT NULL
ON DUPLICATE KEY UPDATE
  `imported_at_utc` = VALUES(`imported_at_utc`),
  `archived_at_utc` = VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_runtime_imported_at_evidence`
  (`source_schema`, `source_table`, `source_identity`, `imported_at_utc`, `archived_at_utc`)
SELECT 'god2', 'items', CAST(`Id` AS char), `ImportedAtUtc`, UTC_TIMESTAMP(6)
FROM `god2`.`items`
WHERE `ImportedAtUtc` IS NOT NULL
ON DUPLICATE KEY UPDATE
  `imported_at_utc` = VALUES(`imported_at_utc`),
  `archived_at_utc` = VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_runtime_imported_at_evidence`
  (`source_schema`, `source_table`, `source_identity`, `imported_at_utc`, `archived_at_utc`)
SELECT 'god2', 'skills', CAST(`Id` AS char), `ImportedAtUtc`, UTC_TIMESTAMP(6)
FROM `god2`.`skills`
WHERE `ImportedAtUtc` IS NOT NULL
ON DUPLICATE KEY UPDATE
  `imported_at_utc` = VALUES(`imported_at_utc`),
  `archived_at_utc` = VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_runtime_imported_at_evidence`
  (`source_schema`, `source_table`, `source_identity`, `imported_at_utc`, `archived_at_utc`)
SELECT 'god2', 'npcs', CAST(`Id` AS char), `ImportedAtUtc`, UTC_TIMESTAMP(6)
FROM `god2`.`npcs`
WHERE `ImportedAtUtc` IS NOT NULL
ON DUPLICATE KEY UPDATE
  `imported_at_utc` = VALUES(`imported_at_utc`),
  `archived_at_utc` = VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_runtime_imported_at_evidence`
  (`source_schema`, `source_table`, `source_identity`, `imported_at_utc`, `archived_at_utc`)
SELECT 'god2', 'monsters', CAST(`Id` AS char), `ImportedAtUtc`, UTC_TIMESTAMP(6)
FROM `god2`.`monsters`
WHERE `ImportedAtUtc` IS NOT NULL
ON DUPLICATE KEY UPDATE
  `imported_at_utc` = VALUES(`imported_at_utc`),
  `archived_at_utc` = VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_runtime_imported_at_evidence`
  (`source_schema`, `source_table`, `source_identity`, `imported_at_utc`, `archived_at_utc`)
SELECT 'god2', 'quests', CAST(`Id` AS char), `ImportedAtUtc`, UTC_TIMESTAMP(6)
FROM `god2`.`quests`
WHERE `ImportedAtUtc` IS NOT NULL
ON DUPLICATE KEY UPDATE
  `imported_at_utc` = VALUES(`imported_at_utc`),
  `archived_at_utc` = VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_runtime_imported_at_evidence`
  (`source_schema`, `source_table`, `source_identity`, `imported_at_utc`, `archived_at_utc`)
SELECT 'god2', 'merchants', CAST(`Id` AS char), `ImportedAtUtc`, UTC_TIMESTAMP(6)
FROM `god2`.`merchants`
WHERE `ImportedAtUtc` IS NOT NULL
ON DUPLICATE KEY UPDATE
  `imported_at_utc` = VALUES(`imported_at_utc`),
  `archived_at_utc` = VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_runtime_imported_at_evidence`
  (`source_schema`, `source_table`, `source_identity`, `imported_at_utc`, `archived_at_utc`)
SELECT 'god2', 'portals', CAST(`Id` AS char), `ImportedAtUtc`, UTC_TIMESTAMP(6)
FROM `god2`.`portals`
WHERE `ImportedAtUtc` IS NOT NULL
ON DUPLICATE KEY UPDATE
  `imported_at_utc` = VALUES(`imported_at_utc`),
  `archived_at_utc` = VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_runtime_imported_at_evidence`
  (`source_schema`, `source_table`, `source_identity`, `imported_at_utc`, `archived_at_utc`)
SELECT 'god2', 'drop_tables', CAST(`Id` AS char), `ImportedAtUtc`, UTC_TIMESTAMP(6)
FROM `god2`.`drop_tables`
WHERE `ImportedAtUtc` IS NOT NULL
ON DUPLICATE KEY UPDATE
  `imported_at_utc` = VALUES(`imported_at_utc`),
  `archived_at_utc` = VALUES(`archived_at_utc`);

INSERT INTO `god2_research`.`legacy_runtime_imported_at_evidence`
  (`source_schema`, `source_table`, `source_identity`, `imported_at_utc`, `archived_at_utc`)
SELECT 'god2', 'dialogs', CAST(`Id` AS char), `ImportedAtUtc`, UTC_TIMESTAMP(6)
FROM `god2`.`dialogs`
WHERE `ImportedAtUtc` IS NOT NULL
ON DUPLICATE KEY UPDATE
  `imported_at_utc` = VALUES(`imported_at_utc`),
  `archived_at_utc` = VALUES(`archived_at_utc`);

ALTER TABLE `god2`.`maps` DROP COLUMN IF EXISTS `ImportedAtUtc`;
ALTER TABLE `god2`.`items` DROP COLUMN IF EXISTS `ImportedAtUtc`;
ALTER TABLE `god2`.`skills` DROP COLUMN IF EXISTS `ImportedAtUtc`;
ALTER TABLE `god2`.`npcs` DROP COLUMN IF EXISTS `ImportedAtUtc`;
ALTER TABLE `god2`.`monsters` DROP COLUMN IF EXISTS `ImportedAtUtc`;
ALTER TABLE `god2`.`quests` DROP COLUMN IF EXISTS `ImportedAtUtc`;
ALTER TABLE `god2`.`merchants` DROP COLUMN IF EXISTS `ImportedAtUtc`;
ALTER TABLE `god2`.`portals` DROP COLUMN IF EXISTS `ImportedAtUtc`;
ALTER TABLE `god2`.`drop_tables` DROP COLUMN IF EXISTS `ImportedAtUtc`;
ALTER TABLE `god2`.`dialogs` DROP COLUMN IF EXISTS `ImportedAtUtc`;
