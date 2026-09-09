-- The runtime localization table must only expose the text the server reads.
-- Legacy import tracking for localization entries is archived as research
-- evidence instead of remaining in the formal runtime table.

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_localization_import_tracking_evidence` (
    `language` varchar(32) NOT NULL,
    `text_key` varchar(191) NOT NULL,
    `recovery_status` varchar(32) NOT NULL,
    `source_reference` varchar(512) NOT NULL,
    `imported_at_utc` datetime(6) NULL,
    `archived_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`language`, `text_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='舊 localization_entries 移出的匯入追蹤證據；正式 runtime 表只保留繁中顯示文字';

INSERT INTO `god2_research`.`legacy_localization_import_tracking_evidence`
    (`language`,`text_key`,`recovery_status`,`source_reference`,`imported_at_utc`,`archived_at_utc`)
SELECT
    `Language`,
    `TextKey`,
    `RecoveryStatus`,
    `SourceReference`,
    `ImportedAtUtc`,
    UTC_TIMESTAMP(6)
FROM `god2`.`localization_entries`
WHERE `RecoveryStatus` <> 'Recovered'
   OR `SourceReference` <> ''
   OR `ImportedAtUtc` IS NOT NULL
ON DUPLICATE KEY UPDATE
    `recovery_status`=VALUES(`recovery_status`),
    `source_reference`=VALUES(`source_reference`),
    `imported_at_utc`=VALUES(`imported_at_utc`),
    `archived_at_utc`=VALUES(`archived_at_utc`);

ALTER TABLE `god2`.`localization_entries`
    DROP COLUMN IF EXISTS `RecoveryStatus`,
    DROP COLUMN IF EXISTS `SourceReference`,
    DROP COLUMN IF EXISTS `ImportedAtUtc`;
