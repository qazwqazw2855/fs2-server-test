-- synchronization_watermarks was an early catalog-sync helper table. Current
-- tools and runtime do not read it, and formal production has no rows. Archive
-- any legacy rows before removing the unused formal metadata table.

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_synchronization_watermark_evidence` (
    `watermark_key` varchar(128) NOT NULL,
    `watermark_value` varchar(500) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`watermark_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='封存已停用的同步浮標資料；正式 metadata 不再保留此表';

SET @archive_synchronization_watermarks_sql = IF(
    EXISTS (
        SELECT 1
        FROM `information_schema`.`TABLES`
        WHERE `TABLE_SCHEMA`='god2_game_meta'
          AND `TABLE_NAME`='synchronization_watermarks'
    ),
    'INSERT INTO `god2_research`.`legacy_synchronization_watermark_evidence`
        (`watermark_key`,`watermark_value`,`updated_at_utc`,`archived_at_utc`)
     SELECT `watermark_key`,`watermark_value`,`updated_at_utc`,UTC_TIMESTAMP(6)
     FROM `god2_game_meta`.`synchronization_watermarks`
     ON DUPLICATE KEY UPDATE
        `watermark_value`=VALUES(`watermark_value`),
        `updated_at_utc`=VALUES(`updated_at_utc`),
        `archived_at_utc`=VALUES(`archived_at_utc`)',
    'DO 0'
);
PREPARE archive_synchronization_watermarks_stmt FROM @archive_synchronization_watermarks_sql;
EXECUTE archive_synchronization_watermarks_stmt;
DEALLOCATE PREPARE archive_synchronization_watermarks_stmt;

DROP TABLE IF EXISTS `god2_game_meta`.`synchronization_watermarks`;
