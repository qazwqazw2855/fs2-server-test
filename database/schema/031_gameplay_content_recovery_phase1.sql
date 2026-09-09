CREATE TABLE IF NOT EXISTS `content_recovery_runs` (
    `RunId` char(36) NOT NULL,
    `Phase` varchar(64) NOT NULL,
    `Status` varchar(64) NOT NULL,
    `ClientRootIdentity` varchar(128) NOT NULL,
    `ExtractorVersion` varchar(32) NOT NULL,
    `ConverterName` varchar(64) NOT NULL,
    `ConverterVersion` varchar(32) NOT NULL,
    `GlossaryVersion` varchar(32) NOT NULL,
    `StartedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `SummaryJson` json NULL,
    PRIMARY KEY (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `content_source_inventory` (
    `RunId` char(36) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `SourceType` varchar(64) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `SizeBytes` bigint NOT NULL,
    `RecordCount` int NULL,
    `Decoder` varchar(64) NULL,
    `ContentClassification` varchar(64) NOT NULL,
    `ScannedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`RunId`, `SourceIdentity`),
    KEY `IX_ContentSourceInventory_Hash` (`SourceHash`),
    CONSTRAINT `FK_ContentSourceInventory_Run`
        FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `content_raw_records` (
    `RunId` char(36) NOT NULL,
    `RecordId` char(64) NOT NULL,
    `Domain` varchar(64) NOT NULL,
    `SourceType` varchar(64) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `SourceRow` int NULL,
    `SourceOffset` bigint NULL,
    `SourceHash` char(64) NOT NULL,
    `ExtractorVersion` varchar(32) NOT NULL,
    `ExtractedAtUtc` datetime(6) NOT NULL,
    `OriginalLanguage` varchar(32) NOT NULL,
    `OriginalText` longtext NULL,
    `RawMetadata` json NOT NULL,
    `SourcePayloadHash` char(64) NOT NULL,
    `Confidence` varchar(32) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    PRIMARY KEY (`RunId`, `RecordId`),
    KEY `IX_ContentRawRecords_Domain` (`RunId`, `Domain`),
    KEY `IX_ContentRawRecords_Source` (`SourceHash`, `SourceRow`),
    CONSTRAINT `FK_ContentRawRecords_Run`
        FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `content_staging_records` (
    `RunId` char(36) NOT NULL,
    `StagingId` char(64) NOT NULL,
    `Domain` varchar(64) NOT NULL,
    `AuthorityKey` varchar(191) NOT NULL,
    `OriginalText` longtext NULL,
    `OriginalLanguage` varchar(32) NOT NULL,
    `ConvertedText` longtext NULL,
    `TargetLanguage` varchar(16) NOT NULL,
    `ConversionMethod` varchar(64) NOT NULL,
    `ConverterVersion` varchar(32) NOT NULL,
    `GlossaryVersion` varchar(32) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `ConvertedTextHash` char(64) NULL,
    `ConversionStatus` varchar(32) NOT NULL,
    `LanguageReviewStatus` varchar(32) NOT NULL,
    `TransformationRule` varchar(128) NOT NULL,
    `NormalizedData` json NOT NULL,
    `OpaqueMetadata` json NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `StagedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`RunId`, `StagingId`),
    KEY `IX_ContentStagingRecords_Domain` (`RunId`, `Domain`, `EvidenceStatus`),
    KEY `IX_ContentStagingRecords_Authority` (`Domain`, `AuthorityKey`(191)),
    CONSTRAINT `FK_ContentStagingRecords_Run`
        FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `content_localization_audit` (
    `RunId` char(36) NOT NULL,
    `LocalizationId` char(64) NOT NULL,
    `Domain` varchar(64) NOT NULL,
    `AuthorityKey` varchar(512) NOT NULL,
    `FieldName` varchar(64) NOT NULL,
    `OriginalText` longtext NOT NULL,
    `OriginalLanguage` varchar(32) NOT NULL,
    `ConvertedText` longtext NOT NULL,
    `TargetLanguage` varchar(16) NOT NULL,
    `ConversionMethod` varchar(64) NOT NULL,
    `ConverterVersion` varchar(32) NOT NULL,
    `GlossaryVersion` varchar(32) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `ConvertedTextHash` char(64) NOT NULL,
    `ConversionStatus` varchar(32) NOT NULL,
    `LanguageReviewStatus` varchar(32) NOT NULL,
    `PlaceholderPreserved` tinyint(1) NOT NULL,
    `MarkupPreserved` tinyint(1) NOT NULL,
    `ControlCodesPreserved` tinyint(1) NOT NULL,
    `ConvertedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`RunId`, `LocalizationId`),
    KEY `IX_ContentLocalizationAudit_Status` (`RunId`, `ConversionStatus`, `LanguageReviewStatus`),
    CONSTRAINT `FK_ContentLocalizationAudit_Run`
        FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `content_validation_results` (
    `RunId` char(36) NOT NULL,
    `ValidationId` char(64) NOT NULL,
    `Domain` varchar(64) NOT NULL,
    `AuthorityKey` varchar(512) NOT NULL,
    `Gate` varchar(128) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `Severity` varchar(32) NOT NULL,
    `Details` varchar(2048) NOT NULL,
    `ValidatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`RunId`, `ValidationId`),
    KEY `IX_ContentValidationResults_Status` (`RunId`, `Domain`, `Status`),
    CONSTRAINT `FK_ContentValidationResults_Run`
        FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `content_validated_records` (
    `RunId` char(36) NOT NULL,
    `ValidatedId` char(64) NOT NULL,
    `Domain` varchar(64) NOT NULL,
    `AuthorityKey` varchar(512) NOT NULL,
    `NormalizedData` json NOT NULL,
    `NormalizedHash` char(64) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `LocalizationStatus` varchar(32) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `ValidatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`RunId`, `ValidatedId`),
    KEY `IX_ContentValidatedRecords_Domain` (`RunId`, `Domain`),
    CONSTRAINT `FK_ContentValidatedRecords_Run`
        FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `content_production_manifest` (
    `Domain` varchar(64) NOT NULL,
    `AuthorityKey` varchar(191) NOT NULL,
    `RunId` char(36) NOT NULL,
    `TargetTable` varchar(64) NOT NULL,
    `TargetRowIdentity` varchar(128) NOT NULL,
    `NormalizedHash` char(64) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `LocalizationStatus` varchar(32) NOT NULL,
    `PromotedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Domain`, `AuthorityKey`),
    KEY `IX_ContentProductionManifest_Run` (`RunId`),
    CONSTRAINT `FK_ContentProductionManifest_Run`
        FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `content_localization_glossary` (
    `GlossaryVersion` varchar(32) NOT NULL,
    `SimplifiedText` varchar(512) NOT NULL,
    `TraditionalText` varchar(512) NOT NULL,
    `Category` varchar(64) NOT NULL,
    `Source` varchar(128) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `Confidence` varchar(32) NOT NULL,
    `VerifiedBy` varchar(128) NOT NULL,
    `Notes` varchar(1024) NOT NULL,
    PRIMARY KEY (`GlossaryVersion`, `SimplifiedText`, `Category`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `maps`
    ADD COLUMN IF NOT EXISTS `OriginalName` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `NameZhTw` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `EvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `LocalizationStatus` varchar(32) NOT NULL DEFAULT 'LanguageReviewRequired',
    ADD COLUMN IF NOT EXISTS `ContentRecoveryRunId` char(36) NULL;

ALTER TABLE `npcs`
    ADD COLUMN IF NOT EXISTS `OriginalName` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `NameZhTw` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `EvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `LocalizationStatus` varchar(32) NOT NULL DEFAULT 'LanguageReviewRequired',
    ADD COLUMN IF NOT EXISTS `ContentRecoveryRunId` char(36) NULL;

ALTER TABLE `monsters`
    ADD COLUMN IF NOT EXISTS `OriginalName` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `NameZhTw` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `MaxMp` bigint NULL,
    ADD COLUMN IF NOT EXISTS `MpPolicy` varchar(32) NOT NULL DEFAULT 'Unknown',
    ADD COLUMN IF NOT EXISTS `ExperienceReward` bigint NULL,
    ADD COLUMN IF NOT EXISTS `CurrencyRewardMinimum` bigint NULL,
    ADD COLUMN IF NOT EXISTS `CurrencyRewardMaximum` bigint NULL,
    ADD COLUMN IF NOT EXISTS `DropPolicy` varchar(32) NOT NULL DEFAULT 'Unknown',
    ADD COLUMN IF NOT EXISTS `EvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `LocalizationStatus` varchar(32) NOT NULL DEFAULT 'LanguageReviewRequired',
    ADD COLUMN IF NOT EXISTS `ContentRecoveryRunId` char(36) NULL;

ALTER TABLE `items`
    ADD COLUMN IF NOT EXISTS `OriginalName` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `NameZhTw` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `OriginalDescription` text NULL,
    ADD COLUMN IF NOT EXISTS `DescriptionZhTw` text NULL,
    ADD COLUMN IF NOT EXISTS `EvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `LocalizationStatus` varchar(32) NOT NULL DEFAULT 'LanguageReviewRequired',
    ADD COLUMN IF NOT EXISTS `ContentRecoveryRunId` char(36) NULL;

ALTER TABLE `skills`
    ADD COLUMN IF NOT EXISTS `OriginalName` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `NameZhTw` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `OriginalDescription` text NULL,
    ADD COLUMN IF NOT EXISTS `DescriptionZhTw` text NULL,
    ADD COLUMN IF NOT EXISTS `SkillFamily` varchar(64) NULL,
    ADD COLUMN IF NOT EXISTS `TargetPolicy` varchar(64) NULL,
    ADD COLUMN IF NOT EXISTS `MpCost` int NULL,
    ADD COLUMN IF NOT EXISTS `MpCostPolicy` varchar(32) NOT NULL DEFAULT 'Unknown',
    ADD COLUMN IF NOT EXISTS `EvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `LocalizationStatus` varchar(32) NOT NULL DEFAULT 'LanguageReviewRequired',
    ADD COLUMN IF NOT EXISTS `ContentRecoveryRunId` char(36) NULL;

ALTER TABLE `quests`
    ADD COLUMN IF NOT EXISTS `OriginalName` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `NameZhTw` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `OriginalDescription` text NULL,
    ADD COLUMN IF NOT EXISTS `DescriptionZhTw` text NULL,
    ADD COLUMN IF NOT EXISTS `EvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `LocalizationStatus` varchar(32) NOT NULL DEFAULT 'LanguageReviewRequired',
    ADD COLUMN IF NOT EXISTS `ContentRecoveryRunId` char(36) NULL;

ALTER TABLE `merchants`
    ADD COLUMN IF NOT EXISTS `OriginalName` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `NameZhTw` varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS `EvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `LocalizationStatus` varchar(32) NOT NULL DEFAULT 'LanguageReviewRequired',
    ADD COLUMN IF NOT EXISTS `ContentRecoveryRunId` char(36) NULL;

ALTER TABLE `localization_entries`
    ADD COLUMN IF NOT EXISTS `OriginalText` text NULL,
    ADD COLUMN IF NOT EXISTS `OriginalLanguage` varchar(32) NULL,
    ADD COLUMN IF NOT EXISTS `ConversionMethod` varchar(64) NULL,
    ADD COLUMN IF NOT EXISTS `ConverterVersion` varchar(32) NULL,
    ADD COLUMN IF NOT EXISTS `GlossaryVersion` varchar(32) NULL,
    ADD COLUMN IF NOT EXISTS `SourceHash` char(64) NULL,
    ADD COLUMN IF NOT EXISTS `ConvertedTextHash` char(64) NULL,
    ADD COLUMN IF NOT EXISTS `ConversionStatus` varchar(32) NULL,
    ADD COLUMN IF NOT EXISTS `LanguageReviewStatus` varchar(32) NULL,
    ADD COLUMN IF NOT EXISTS `ContentRecoveryRunId` char(36) NULL;
