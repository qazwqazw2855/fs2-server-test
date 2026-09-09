CREATE TABLE IF NOT EXISTS `god2_research`.`content_recovery_run_summary_archive` (
    `RunId` char(36) NOT NULL,
    `Status` varchar(64) NOT NULL DEFAULT '',
    `SummaryJson` longtext NULL,
    `ArchiveReasonZhTw` varchar(512) NOT NULL,
    `ArchivedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`RunId`),
    CONSTRAINT `CK_ContentRecoveryRunSummaryArchive_SummaryJson`
        CHECK (`SummaryJson` IS NULL OR JSON_VALID(`SummaryJson`))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @has_content_recovery_summary := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'content_recovery_runs'
      AND `COLUMN_NAME` = 'SummaryJson'
);

SET @archive_content_recovery_summary_sql := IF(
    @has_content_recovery_summary > 0,
    'INSERT INTO `god2_research`.`content_recovery_run_summary_archive`
        (`RunId`,`Status`,`SummaryJson`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
     SELECT `RunId`, `Status`, `SummaryJson`, ''Recovery run 摘要 JSON 已移入 research；正式 run ledger 只保留流程狀態與時間。'', UTC_TIMESTAMP(6)
     FROM `content_recovery_runs`
     WHERE `SummaryJson` IS NOT NULL
     ON DUPLICATE KEY UPDATE `Status`=VALUES(`Status`),`SummaryJson`=VALUES(`SummaryJson`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)',
    'SELECT 1'
);
PREPARE archive_content_recovery_summary_stmt FROM @archive_content_recovery_summary_sql;
EXECUTE archive_content_recovery_summary_stmt;
DEALLOCATE PREPARE archive_content_recovery_summary_stmt;

SET @drop_content_recovery_summary_sql := IF(
    @has_content_recovery_summary > 0,
    'ALTER TABLE `content_recovery_runs` DROP COLUMN `SummaryJson`',
    'SELECT 1'
);
PREPARE drop_content_recovery_summary_stmt FROM @drop_content_recovery_summary_sql;
EXECUTE drop_content_recovery_summary_stmt;
DEALLOCATE PREPARE drop_content_recovery_summary_stmt;
