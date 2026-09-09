CREATE TABLE IF NOT EXISTS `god2_research`.`content_runtime_release_evidence` (
    `release_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `source_run_id` char(36) NOT NULL,
    `evidence_reference` varchar(512) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`release_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'content_runtime_releases'
          AND COLUMN_NAME = 'EvidenceReference'
    ),
    'INSERT INTO `god2_research`.`content_runtime_release_evidence` (`release_id`,`source_run_id`,`evidence_reference`) SELECT `ReleaseId`,`SourceRunId`,`EvidenceReference` FROM `content_runtime_releases` ON DUPLICATE KEY UPDATE `source_run_id`=VALUES(`source_run_id`),`evidence_reference`=VALUES(`evidence_reference`)',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `content_runtime_releases`
    DROP COLUMN IF EXISTS `EvidenceReference`;
