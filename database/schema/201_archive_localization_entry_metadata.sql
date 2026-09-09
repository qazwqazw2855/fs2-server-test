-- Keep localization_entries as the formal runtime text table only.
-- Source and conversion metadata belongs in god2_research, not in the runtime
-- localization table that the server reads for Traditional Chinese text.

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_localization_entry_metadata_evidence` (
    `language` varchar(32) NOT NULL,
    `text_key` varchar(191) NOT NULL,
    `original_text` longtext NULL,
    `original_language` varchar(32) NULL,
    `conversion_method` varchar(64) NULL,
    `converter_version` varchar(32) NULL,
    `glossary_version` varchar(32) NULL,
    `source_hash` char(64) NULL,
    `converted_text_hash` char(64) NULL,
    `conversion_status` varchar(32) NULL,
    `language_review_status` varchar(32) NULL,
    `content_recovery_run_id` char(36) NULL,
    `archived_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`language`, `text_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='舊 localization_entries 移出的來源與轉換證據；正式 runtime 表只保留繁中顯示文字';

INSERT INTO `god2_research`.`legacy_localization_entry_metadata_evidence`
    (`language`,`text_key`,`original_text`,`original_language`,`conversion_method`,`converter_version`,
     `glossary_version`,`source_hash`,`converted_text_hash`,`conversion_status`,`language_review_status`,
     `content_recovery_run_id`,`archived_at_utc`)
SELECT
    `Language`,
    `TextKey`,
    `OriginalText`,
    `OriginalLanguage`,
    `ConversionMethod`,
    `ConverterVersion`,
    `GlossaryVersion`,
    `SourceHash`,
    `ConvertedTextHash`,
    `ConversionStatus`,
    `LanguageReviewStatus`,
    `ContentRecoveryRunId`,
    UTC_TIMESTAMP(6)
FROM `god2`.`localization_entries`
WHERE `OriginalText` IS NOT NULL
   OR `OriginalLanguage` IS NOT NULL
   OR `ConversionMethod` IS NOT NULL
   OR `ConverterVersion` IS NOT NULL
   OR `GlossaryVersion` IS NOT NULL
   OR `SourceHash` IS NOT NULL
   OR `ConvertedTextHash` IS NOT NULL
   OR `ConversionStatus` IS NOT NULL
   OR `LanguageReviewStatus` IS NOT NULL
   OR `ContentRecoveryRunId` IS NOT NULL
ON DUPLICATE KEY UPDATE
    `original_text`=VALUES(`original_text`),
    `original_language`=VALUES(`original_language`),
    `conversion_method`=VALUES(`conversion_method`),
    `converter_version`=VALUES(`converter_version`),
    `glossary_version`=VALUES(`glossary_version`),
    `source_hash`=VALUES(`source_hash`),
    `converted_text_hash`=VALUES(`converted_text_hash`),
    `conversion_status`=VALUES(`conversion_status`),
    `language_review_status`=VALUES(`language_review_status`),
    `content_recovery_run_id`=VALUES(`content_recovery_run_id`),
    `archived_at_utc`=VALUES(`archived_at_utc`);

ALTER TABLE `god2`.`localization_entries`
    DROP COLUMN IF EXISTS `OriginalText`,
    DROP COLUMN IF EXISTS `OriginalLanguage`,
    DROP COLUMN IF EXISTS `ConversionMethod`,
    DROP COLUMN IF EXISTS `ConverterVersion`,
    DROP COLUMN IF EXISTS `GlossaryVersion`,
    DROP COLUMN IF EXISTS `SourceHash`,
    DROP COLUMN IF EXISTS `ConvertedTextHash`,
    DROP COLUMN IF EXISTS `ConversionStatus`,
    DROP COLUMN IF EXISTS `LanguageReviewStatus`,
    DROP COLUMN IF EXISTS `ContentRecoveryRunId`;
