CREATE SCHEMA IF NOT EXISTS `god2_research`;

CREATE TABLE IF NOT EXISTS `god2_research`.`formal_source_reference_archive` (
    `formal_table` varchar(128) NOT NULL,
    `record_identity` varchar(128) NOT NULL,
    `source_reference` varchar(512) NOT NULL,
    `recovery_status` varchar(32) NOT NULL,
    `archive_reason_zh_tw` varchar(255) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`formal_table`, `record_identity`)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

SET @has_source_reference := (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='maps' AND COLUMN_NAME='SourceReference');
SET @archive_source_reference_sql := IF(@has_source_reference > 0, 'INSERT INTO `god2_research`.`formal_source_reference_archive` (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`archive_reason_zh_tw`,`archived_at_utc`) SELECT ''maps'', CAST(`Id` AS char), `SourceReference`, `RecoveryStatus`, ''舊正式內容表的匯入來源字串已移入 research 封存；正式 runtime 表只保留遊戲服務需要的資料。'', UTC_TIMESTAMP(6) FROM `maps` WHERE `SourceReference` IS NOT NULL AND `SourceReference`<>'''' ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),`archived_at_utc`=VALUES(`archived_at_utc`)', 'SELECT 1');
PREPARE archive_source_reference_stmt FROM @archive_source_reference_sql;
EXECUTE archive_source_reference_stmt;
DEALLOCATE PREPARE archive_source_reference_stmt;
ALTER TABLE `maps` DROP COLUMN IF EXISTS `SourceReference`;

SET @has_source_reference := (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='items' AND COLUMN_NAME='SourceReference');
SET @archive_source_reference_sql := IF(@has_source_reference > 0, 'INSERT INTO `god2_research`.`formal_source_reference_archive` (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`archive_reason_zh_tw`,`archived_at_utc`) SELECT ''items'', CAST(`Id` AS char), `SourceReference`, `RecoveryStatus`, ''舊正式內容表的匯入來源字串已移入 research 封存；正式 runtime 表只保留遊戲服務需要的資料。'', UTC_TIMESTAMP(6) FROM `items` WHERE `SourceReference` IS NOT NULL AND `SourceReference`<>'''' ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),`archived_at_utc`=VALUES(`archived_at_utc`)', 'SELECT 1');
PREPARE archive_source_reference_stmt FROM @archive_source_reference_sql;
EXECUTE archive_source_reference_stmt;
DEALLOCATE PREPARE archive_source_reference_stmt;
ALTER TABLE `items` DROP COLUMN IF EXISTS `SourceReference`;

SET @has_source_reference := (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='skills' AND COLUMN_NAME='SourceReference');
SET @archive_source_reference_sql := IF(@has_source_reference > 0, 'INSERT INTO `god2_research`.`formal_source_reference_archive` (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`archive_reason_zh_tw`,`archived_at_utc`) SELECT ''skills'', CAST(`Id` AS char), `SourceReference`, `RecoveryStatus`, ''舊正式內容表的匯入來源字串已移入 research 封存；正式 runtime 表只保留遊戲服務需要的資料。'', UTC_TIMESTAMP(6) FROM `skills` WHERE `SourceReference` IS NOT NULL AND `SourceReference`<>'''' ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),`archived_at_utc`=VALUES(`archived_at_utc`)', 'SELECT 1');
PREPARE archive_source_reference_stmt FROM @archive_source_reference_sql;
EXECUTE archive_source_reference_stmt;
DEALLOCATE PREPARE archive_source_reference_stmt;
ALTER TABLE `skills` DROP COLUMN IF EXISTS `SourceReference`;

SET @has_source_reference := (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='npcs' AND COLUMN_NAME='SourceReference');
SET @archive_source_reference_sql := IF(@has_source_reference > 0, 'INSERT INTO `god2_research`.`formal_source_reference_archive` (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`archive_reason_zh_tw`,`archived_at_utc`) SELECT ''npcs'', CAST(`Id` AS char), `SourceReference`, `RecoveryStatus`, ''舊正式內容表的匯入來源字串已移入 research 封存；正式 runtime 表只保留遊戲服務需要的資料。'', UTC_TIMESTAMP(6) FROM `npcs` WHERE `SourceReference` IS NOT NULL AND `SourceReference`<>'''' ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),`archived_at_utc`=VALUES(`archived_at_utc`)', 'SELECT 1');
PREPARE archive_source_reference_stmt FROM @archive_source_reference_sql;
EXECUTE archive_source_reference_stmt;
DEALLOCATE PREPARE archive_source_reference_stmt;
ALTER TABLE `npcs` DROP COLUMN IF EXISTS `SourceReference`;

SET @has_source_reference := (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='monsters' AND COLUMN_NAME='SourceReference');
SET @archive_source_reference_sql := IF(@has_source_reference > 0, 'INSERT INTO `god2_research`.`formal_source_reference_archive` (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`archive_reason_zh_tw`,`archived_at_utc`) SELECT ''monsters'', CAST(`Id` AS char), `SourceReference`, `RecoveryStatus`, ''舊正式內容表的匯入來源字串已移入 research 封存；正式 runtime 表只保留遊戲服務需要的資料。'', UTC_TIMESTAMP(6) FROM `monsters` WHERE `SourceReference` IS NOT NULL AND `SourceReference`<>'''' ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),`archived_at_utc`=VALUES(`archived_at_utc`)', 'SELECT 1');
PREPARE archive_source_reference_stmt FROM @archive_source_reference_sql;
EXECUTE archive_source_reference_stmt;
DEALLOCATE PREPARE archive_source_reference_stmt;
ALTER TABLE `monsters` DROP COLUMN IF EXISTS `SourceReference`;

SET @has_source_reference := (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='quests' AND COLUMN_NAME='SourceReference');
SET @archive_source_reference_sql := IF(@has_source_reference > 0, 'INSERT INTO `god2_research`.`formal_source_reference_archive` (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`archive_reason_zh_tw`,`archived_at_utc`) SELECT ''quests'', CAST(`Id` AS char), `SourceReference`, `RecoveryStatus`, ''舊正式內容表的匯入來源字串已移入 research 封存；正式 runtime 表只保留遊戲服務需要的資料。'', UTC_TIMESTAMP(6) FROM `quests` WHERE `SourceReference` IS NOT NULL AND `SourceReference`<>'''' ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),`archived_at_utc`=VALUES(`archived_at_utc`)', 'SELECT 1');
PREPARE archive_source_reference_stmt FROM @archive_source_reference_sql;
EXECUTE archive_source_reference_stmt;
DEALLOCATE PREPARE archive_source_reference_stmt;
ALTER TABLE `quests` DROP COLUMN IF EXISTS `SourceReference`;

SET @has_source_reference := (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='merchants' AND COLUMN_NAME='SourceReference');
SET @archive_source_reference_sql := IF(@has_source_reference > 0, 'INSERT INTO `god2_research`.`formal_source_reference_archive` (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`archive_reason_zh_tw`,`archived_at_utc`) SELECT ''merchants'', CAST(`Id` AS char), `SourceReference`, `RecoveryStatus`, ''舊正式內容表的匯入來源字串已移入 research 封存；正式 runtime 表只保留遊戲服務需要的資料。'', UTC_TIMESTAMP(6) FROM `merchants` WHERE `SourceReference` IS NOT NULL AND `SourceReference`<>'''' ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),`archived_at_utc`=VALUES(`archived_at_utc`)', 'SELECT 1');
PREPARE archive_source_reference_stmt FROM @archive_source_reference_sql;
EXECUTE archive_source_reference_stmt;
DEALLOCATE PREPARE archive_source_reference_stmt;
ALTER TABLE `merchants` DROP COLUMN IF EXISTS `SourceReference`;

SET @has_source_reference := (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='portals' AND COLUMN_NAME='SourceReference');
SET @archive_source_reference_sql := IF(@has_source_reference > 0, 'INSERT INTO `god2_research`.`formal_source_reference_archive` (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`archive_reason_zh_tw`,`archived_at_utc`) SELECT ''portals'', CAST(`Id` AS char), `SourceReference`, `RecoveryStatus`, ''舊正式內容表的匯入來源字串已移入 research 封存；正式 runtime 表只保留遊戲服務需要的資料。'', UTC_TIMESTAMP(6) FROM `portals` WHERE `SourceReference` IS NOT NULL AND `SourceReference`<>'''' ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),`archived_at_utc`=VALUES(`archived_at_utc`)', 'SELECT 1');
PREPARE archive_source_reference_stmt FROM @archive_source_reference_sql;
EXECUTE archive_source_reference_stmt;
DEALLOCATE PREPARE archive_source_reference_stmt;
ALTER TABLE `portals` DROP COLUMN IF EXISTS `SourceReference`;

SET @has_source_reference := (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='drop_tables' AND COLUMN_NAME='SourceReference');
SET @archive_source_reference_sql := IF(@has_source_reference > 0, 'INSERT INTO `god2_research`.`formal_source_reference_archive` (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`archive_reason_zh_tw`,`archived_at_utc`) SELECT ''drop_tables'', CAST(`Id` AS char), `SourceReference`, `RecoveryStatus`, ''舊正式內容表的匯入來源字串已移入 research 封存；正式 runtime 表只保留遊戲服務需要的資料。'', UTC_TIMESTAMP(6) FROM `drop_tables` WHERE `SourceReference` IS NOT NULL AND `SourceReference`<>'''' ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),`archived_at_utc`=VALUES(`archived_at_utc`)', 'SELECT 1');
PREPARE archive_source_reference_stmt FROM @archive_source_reference_sql;
EXECUTE archive_source_reference_stmt;
DEALLOCATE PREPARE archive_source_reference_stmt;
ALTER TABLE `drop_tables` DROP COLUMN IF EXISTS `SourceReference`;

SET @has_source_reference := (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='dialogs' AND COLUMN_NAME='SourceReference');
SET @archive_source_reference_sql := IF(@has_source_reference > 0, 'INSERT INTO `god2_research`.`formal_source_reference_archive` (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`archive_reason_zh_tw`,`archived_at_utc`) SELECT ''dialogs'', CAST(`Id` AS char), `SourceReference`, `RecoveryStatus`, ''舊正式內容表的匯入來源字串已移入 research 封存；正式 runtime 表只保留遊戲服務需要的資料。'', UTC_TIMESTAMP(6) FROM `dialogs` WHERE `SourceReference` IS NOT NULL AND `SourceReference`<>'''' ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),`archived_at_utc`=VALUES(`archived_at_utc`)', 'SELECT 1');
PREPARE archive_source_reference_stmt FROM @archive_source_reference_sql;
EXECUTE archive_source_reference_stmt;
DEALLOCATE PREPARE archive_source_reference_stmt;
ALTER TABLE `dialogs` DROP COLUMN IF EXISTS `SourceReference`;


