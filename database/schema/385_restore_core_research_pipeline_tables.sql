CREATE DATABASE IF NOT EXISTS `god2_research`
    DEFAULT CHARACTER SET utf8mb4
    DEFAULT COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`content_client_table_layouts`
(
    `LayoutId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `Decoder` varchar(128) NOT NULL,
    `RecordCount` int NOT NULL,
    `MaximumFieldCount` int NOT NULL,
    `HeaderJson` longtext NOT NULL,
    `RecordBoundaryStatus` varchar(32) NOT NULL,
    `LoaderEvidenceStatus` varchar(32) NOT NULL,
    `ScannedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`LayoutId`),
    KEY `IX_ContentClientTableLayouts_Run` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`content_field_evidence`
(
    `EvidenceId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `Domain` varchar(128) NOT NULL,
    `AuthorityKey` varchar(512) NOT NULL,
    `FieldName` varchar(128) NOT NULL,
    `ValueJson` longtext NULL,
    `EvidenceState` varchar(32) NOT NULL,
    `SourceType` varchar(64) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `Confidence` varchar(32) NULL,
    `Reason` varchar(1024) NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`EvidenceId`),
    KEY `IX_ContentFieldEvidence_RunDomain` (`RunId`,`Domain`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`content_production_manifest`
(
    `Domain` varchar(128) NOT NULL,
    `AuthorityKey` varchar(512) NOT NULL,
    `RunId` char(36) NOT NULL,
    `TargetTable` varchar(128) NOT NULL,
    `TargetRowIdentity` varchar(128) NOT NULL,
    `NormalizedHash` char(64) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `LocalizationStatus` varchar(32) NOT NULL,
    `PromotedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Domain`,`AuthorityKey`),
    KEY `IX_ContentProductionManifest_Run` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`content_profile_source_archive`
(
    `FormalTable` varchar(128) NOT NULL,
    `RecordIdentity` varchar(128) NOT NULL,
    `RunId` char(36) NOT NULL,
    `SourceType` varchar(64) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `ArchiveReasonZhTw` varchar(1024) NOT NULL,
    `ArchivedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`FormalTable`,`RecordIdentity`),
    KEY `IX_ContentProfileSourceArchive_Run` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`relationship_source_archive`
(
    `FormalTable` varchar(128) NOT NULL,
    `RelationshipId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `SourceType` varchar(64) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `Confidence` varchar(32) NULL,
    `ArchivedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`FormalTable`,`RelationshipId`),
    KEY `IX_RelationshipSourceArchive_Run` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`npc_coordinate_evidence`
(
    `CoordinateEvidenceId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `NpcId` int NULL,
    `ClientNpcId` int NULL,
    `NpcNameZhTw` varchar(256) NULL,
    `MapId` int NULL,
    `MapNameZhTw` varchar(256) NULL,
    `PositionX` int NOT NULL,
    `PositionY` int NOT NULL,
    `Direction` int NULL,
    `CoordinateEvidenceStatus` varchar(32) NOT NULL,
    `ProductionSpawnEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `PromotionRunId` char(36) NULL,
    `SourceType` varchar(64) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `Confidence` varchar(32) NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`CoordinateEvidenceId`),
    KEY `IX_NpcCoordinateEvidence_Run` (`RunId`),
    KEY `IX_NpcCoordinateEvidence_Promotion` (`PromotionRunId`,`ProductionSpawnEnabled`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`quest_objective_candidates`
(
    `ObjectiveId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `ClientQuestId` int NOT NULL,
    `QuestId` int NULL,
    `ObjectiveType` varchar(64) NOT NULL,
    `ItemId` int NULL,
    `MonsterId` int NULL,
    `RequiredQuantity` int NULL,
    `ObjectiveTextZhTw` text NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `RelationshipEvidenceStatus` varchar(32) NOT NULL DEFAULT 'Candidate',
    `QuantityEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    `Enabled` tinyint(1) NOT NULL DEFAULT 0,
    `ProductionObjectiveEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `PromotionRunId` char(36) NULL,
    `SourceHash` char(64) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    PRIMARY KEY (`ObjectiveId`),
    KEY `IX_QuestObjectiveCandidates_Run` (`RunId`),
    KEY `IX_QuestObjectiveCandidates_ClientQuest` (`ClientQuestId`),
    KEY `IX_QuestObjectiveCandidates_Promotion` (`PromotionRunId`,`ProductionObjectiveEnabled`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`historical_gameplay_observations`
(
    `ObservationId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `EventName` varchar(128) NOT NULL,
    `ObservedAtUtc` datetime(6) NULL,
    `GameplayDataJson` longtext NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`ObservationId`),
    KEY `IX_HistoricalGameplayObservations_Run` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`semantic_profile_source_run_archive`
(
    `FormalTable` varchar(128) NOT NULL,
    `RecordIdentity` varchar(128) NOT NULL,
    `RunId` char(36) NOT NULL,
    `SourceRunId` char(36) NOT NULL,
    `ResourceKey` varchar(256) NULL,
    `ArchiveReasonZhTw` varchar(1024) NOT NULL,
    `ArchivedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`FormalTable`,`RecordIdentity`,`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`content_phase3_field_closure`
(
    `RunId` char(36) NOT NULL,
    `SourceRunId` char(36) NOT NULL,
    `Domain` varchar(128) NOT NULL,
    `EntityKey` varchar(256) NOT NULL,
    `FieldName` varchar(128) NOT NULL,
    `ValueJson` longtext NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `BlockingReason` varchar(1024) NULL,
    `MissingEvidenceType` varchar(128) NULL,
    `SourcesSearchedJson` longtext NOT NULL,
    `ClientFunctionsSearchedJson` longtext NOT NULL,
    `CandidateSource` varchar(256) NULL,
    `IdentityMatchMethod` varchar(128) NULL,
    `Confidence` varchar(32) NULL,
    `ConflictingCandidates` int NOT NULL DEFAULT 0,
    `PromotionRequirementsJson` longtext NOT NULL,
    `RecoveryPass` varchar(64) NOT NULL,
    `LastAttemptedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`RunId`,`Domain`,`EntityKey`,`FieldName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`content_phase3_promotions`
(
    `RunId` char(36) NOT NULL,
    `PromotionId` char(64) NOT NULL,
    `SourceRunId` char(36) NOT NULL,
    `Domain` varchar(128) NOT NULL,
    `EntityKey` varchar(256) NOT NULL,
    `TargetTable` varchar(128) NOT NULL,
    `TargetIdentity` varchar(128) NOT NULL,
    `PromotionStatus` varchar(32) NOT NULL,
    `IdentityMatchMethod` varchar(128) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `FieldStatesJson` longtext NOT NULL,
    `SourceProvenanceJson` longtext NOT NULL,
    `PromotedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`RunId`,`PromotionId`),
    KEY `IX_ContentPhase3Promotions_Target` (`TargetTable`,`TargetIdentity`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`content_recovery_run_summary_archive`
(
    `RunId` char(36) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `SummaryJson` longtext NULL,
    `ArchiveReasonZhTw` varchar(1024) NOT NULL,
    `ArchivedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.* TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.* TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.* TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.* TO 'god2_catalog_builder'@'127.0.0.1';
