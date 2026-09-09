CREATE TABLE IF NOT EXISTS `content_phase3_field_closure` (
    `RunId` char(36) NOT NULL,
    `SourceRunId` char(36) NOT NULL,
    `Domain` varchar(64) NOT NULL,
    `EntityKey` varchar(191) NOT NULL,
    `FieldName` varchar(128) NOT NULL,
    `ValueJson` json NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `BlockingReason` varchar(1024) NOT NULL,
    `MissingEvidenceType` varchar(128) NOT NULL,
    `SourcesSearchedJson` json NOT NULL,
    `ClientFunctionsSearchedJson` json NOT NULL,
    `CandidateSource` varchar(256) NULL,
    `IdentityMatchMethod` varchar(128) NOT NULL,
    `Confidence` varchar(32) NOT NULL,
    `ConflictingCandidates` int NOT NULL DEFAULT 0,
    `PromotionRequirementsJson` json NOT NULL,
    `RecoveryPass` int NOT NULL,
    `LastAttemptedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`RunId`, `Domain`, `EntityKey`, `FieldName`),
    KEY `IX_Phase3FieldClosure_State` (`RunId`, `EvidenceStatus`),
    KEY `IX_Phase3FieldClosure_Entity` (`Domain`, `EntityKey`, `FieldName`),
    CONSTRAINT `FK_Phase3FieldClosure_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_Phase3FieldClosure_SourceRun` FOREIGN KEY (`SourceRunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `CK_Phase3FieldClosure_State` CHECK (`EvidenceStatus` IN
        ('Verified','Derived','Candidate','EvidenceBlocked','ExplicitOfficialZero','DefaultDisabledZero','NotApplicable','Deprecated'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `content_phase3_promotions` (
    `RunId` char(36) NOT NULL,
    `PromotionId` char(64) NOT NULL,
    `SourceRunId` char(36) NOT NULL,
    `Domain` varchar(64) NOT NULL,
    `EntityKey` varchar(191) NOT NULL,
    `TargetTable` varchar(128) NOT NULL,
    `TargetIdentity` varchar(191) NOT NULL,
    `PromotionStatus` varchar(32) NOT NULL,
    `IdentityMatchMethod` varchar(128) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `FieldStatesJson` json NOT NULL,
    `SourceProvenanceJson` json NOT NULL,
    `PromotedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`RunId`, `PromotionId`),
    KEY `IX_Phase3Promotions_Domain` (`RunId`, `Domain`, `PromotionStatus`),
    CONSTRAINT `FK_Phase3Promotions_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_Phase3Promotions_SourceRun` FOREIGN KEY (`SourceRunId`) REFERENCES `content_recovery_runs` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `monster_spawn_semantics` (
    `RunId` char(36) NOT NULL,
    `SourceRunId` char(36) NOT NULL,
    `MonsterId` int NOT NULL,
    `MapId` int NULL,
    `AreaId` int NULL,
    `PositionX` int NULL,
    `PositionY` int NULL,
    `Direction` int NULL,
    `SpawnGroup` varchar(128) NULL,
    `EncounterGroup` varchar(128) NULL,
    `FormationGroup` varchar(128) NULL,
    `MinCount` int NULL,
    `MaxCount` int NULL,
    `RespawnSeconds` int NULL,
    `EncounterRadius` int NULL,
    `SpawnCondition` varchar(512) NULL,
    `MapRelationshipStatus` varchar(32) NOT NULL,
    `CoordinateEvidenceStatus` varchar(32) NOT NULL,
    `RespawnPolicy` varchar(32) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `ProductionSpawnEnabled` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`RunId`, `MonsterId`),
    KEY `IX_MonsterSpawnSemantics_Enabled` (`RunId`, `ProductionSpawnEnabled`),
    CONSTRAINT `FK_MonsterSpawnSemantics_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_MonsterSpawnSemantics_SourceRun` FOREIGN KEY (`SourceRunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_MonsterSpawnSemantics_Monster` FOREIGN KEY (`MonsterId`) REFERENCES `monsters` (`Id`),
    CONSTRAINT `FK_MonsterSpawnSemantics_Map` FOREIGN KEY (`MapId`) REFERENCES `maps` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `monster_semantic_profiles` (
    `RunId` char(36) NOT NULL,
    `SourceRunId` char(36) NOT NULL,
    `MonsterId` int NOT NULL,
    `Level` int NULL,
    `LevelEvidenceStatus` varchar(32) NOT NULL,
    `MaxHp` bigint NULL,
    `HpPolicy` varchar(32) NOT NULL,
    `MaxMp` bigint NULL,
    `MpPolicy` varchar(32) NOT NULL,
    `PhysicalAttack` int NULL,
    `MagicAttack` int NULL,
    `PhysicalDefense` int NULL,
    `MagicDefense` int NULL,
    `Speed` int NULL,
    `Initiative` int NULL,
    `Accuracy` int NULL,
    `Evasion` int NULL,
    `CriticalRate` decimal(18,9) NULL,
    `Element` varchar(64) NULL,
    `Race` varchar(64) NULL,
    `AiFamily` varchar(64) NULL,
    `ExperienceReward` bigint NULL,
    `CurrencyMinimum` bigint NULL,
    `CurrencyMaximum` bigint NULL,
    `StatEvidenceStatus` varchar(32) NOT NULL,
    `RewardEvidenceStatus` varchar(32) NOT NULL,
    `DropPolicyStatus` varchar(32) NOT NULL,
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`RunId`, `MonsterId`),
    CONSTRAINT `FK_MonsterSemanticProfiles_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_MonsterSemanticProfiles_SourceRun` FOREIGN KEY (`SourceRunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_MonsterSemanticProfiles_Monster` FOREIGN KEY (`MonsterId`) REFERENCES `monsters` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `skill_semantic_profiles` (
    `RunId` char(36) NOT NULL,
    `SourceRunId` char(36) NOT NULL,
    `SkillId` int NOT NULL,
    `ClientProfileId` char(64) NULL,
    `ClientSkillId` int NULL,
    `Profession` varchar(64) NULL,
    `SkillRank` int NULL,
    `ResourceKey` varchar(256) NULL,
    `DisplayNameKey` varchar(256) NULL,
    `SkillFamily` varchar(64) NOT NULL,
    `TargetPolicy` varchar(64) NOT NULL,
    `MpCostPolicy` varchar(32) NOT NULL,
    `MpCost` int NULL,
    `EffectReferencesJson` json NOT NULL,
    `StatusReferencesJson` json NOT NULL,
    `IdentityEvidenceStatus` varchar(32) NOT NULL,
    `FamilyEvidenceStatus` varchar(32) NOT NULL,
    `TargetEvidenceStatus` varchar(32) NOT NULL,
    `MpEvidenceStatus` varchar(32) NOT NULL,
    `EffectEvidenceStatus` varchar(32) NOT NULL,
    `SemanticEvidenceStatus` varchar(32) NOT NULL,
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`RunId`, `SkillId`),
    KEY `IX_SkillSemanticProfiles_Identity` (`RunId`, `IdentityEvidenceStatus`),
    CONSTRAINT `FK_SkillSemanticProfiles_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_SkillSemanticProfiles_SourceRun` FOREIGN KEY (`SourceRunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_SkillSemanticProfiles_Skill` FOREIGN KEY (`SkillId`) REFERENCES `skills` (`Id`),
    CONSTRAINT `FK_SkillSemanticProfiles_ClientProfile` FOREIGN KEY (`ClientProfileId`) REFERENCES `skill_content_profiles` (`ProfileId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `npcs`
    ADD COLUMN IF NOT EXISTS `OfficialIdentityStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `Direction` int NULL;

ALTER TABLE `npc_coordinate_evidence`
    ADD COLUMN IF NOT EXISTS `IdentityEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `BoundsValidationStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `PromotionRunId` char(36) NULL;

ALTER TABLE `monster_drop_relationships`
    ADD COLUMN IF NOT EXISTS `QuantityEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `ProductionDropEnabled` tinyint(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `PromotionRunId` char(36) NULL;

ALTER TABLE `skill_content_profiles`
    ADD COLUMN IF NOT EXISTS `IdentityEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `PromotionRunId` char(36) NULL;

ALTER TABLE `merchant_inventory_candidates`
    ADD COLUMN IF NOT EXISTS `RelationshipEvidenceStatus` varchar(32) NOT NULL DEFAULT 'Candidate',
    ADD COLUMN IF NOT EXISTS `PriceEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `ProductionSaleEnabled` tinyint(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `PromotionRunId` char(36) NULL;

ALTER TABLE `quest_content_profiles`
    ADD COLUMN IF NOT EXISTS `IdentityEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `ProductionProfileEnabled` tinyint(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `PromotionRunId` char(36) NULL;

ALTER TABLE `quest_objective_candidates`
    ADD COLUMN IF NOT EXISTS `QuestId` int NULL,
    ADD COLUMN IF NOT EXISTS `RelationshipEvidenceStatus` varchar(32) NOT NULL DEFAULT 'Candidate',
    ADD COLUMN IF NOT EXISTS `QuantityEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `ProductionObjectiveEnabled` tinyint(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `PromotionRunId` char(36) NULL;

ALTER TABLE `equipment_set_definitions`
    ADD COLUMN IF NOT EXISTS `SetRelationshipStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `SetBonusEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `ProductionRelationshipEnabled` tinyint(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `ProductionBonusEnabled` tinyint(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `PromotionRunId` char(36) NULL;

ALTER TABLE `equipment_set_members`
    ADD COLUMN IF NOT EXISTS `ProductionRelationshipEnabled` tinyint(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `PromotionRunId` char(36) NULL;

ALTER TABLE `container_item_relationships`
    ADD COLUMN IF NOT EXISTS `QuantityEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `ProductionRelationshipEnabled` tinyint(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `ProductionRecipeEnabled` tinyint(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `PromotionRunId` char(36) NULL;

ALTER TABLE `pet_innate_definitions`
    ADD COLUMN IF NOT EXISTS `EffectEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `PromotionRunId` char(36) NULL;

ALTER TABLE `pet_egg_relationships`
    ADD COLUMN IF NOT EXISTS `ProductionHatchEnabled` tinyint(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `PromotionRunId` char(36) NULL;
