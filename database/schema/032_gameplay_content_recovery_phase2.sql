CREATE TABLE IF NOT EXISTS `content_client_table_layouts` (
    `LayoutId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `Decoder` varchar(64) NOT NULL,
    `RecordCount` int NOT NULL,
    `MaximumFieldCount` int NOT NULL,
    `HeaderJson` json NOT NULL,
    `RecordBoundaryStatus` varchar(32) NOT NULL,
    `LoaderEvidenceStatus` varchar(32) NOT NULL,
    `ScannedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`LayoutId`),
    KEY `IX_ContentClientTableLayouts_Run` (`RunId`),
    CONSTRAINT `FK_ContentClientTableLayouts_Run`
        FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `content_field_evidence` (
    `EvidenceId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `Domain` varchar(64) NOT NULL,
    `AuthorityKey` varchar(512) NOT NULL,
    `FieldName` varchar(128) NOT NULL,
    `ValueJson` json NULL,
    `EvidenceState` varchar(32) NOT NULL,
    `SourceType` varchar(64) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `Confidence` varchar(32) NOT NULL,
    `Reason` varchar(1024) NOT NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`EvidenceId`),
    KEY `IX_ContentFieldEvidence_Entity` (`Domain`, `AuthorityKey`(191), `FieldName`),
    KEY `IX_ContentFieldEvidence_State` (`EvidenceState`),
    CONSTRAINT `FK_ContentFieldEvidence_Run`
        FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `monster_drop_relationships` (
    `RelationshipId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `MonsterId` int NOT NULL,
    `DropTableId` int NULL,
    `DropGroupId` varchar(128) NULL,
    `DropEntryId` varchar(128) NOT NULL,
    `ItemId` int NOT NULL,
    `MinimumQuantity` int NULL,
    `MaximumQuantity` int NULL,
    `OriginalDropChance` decimal(18,9) NULL,
    `EffectiveDropChance` decimal(18,9) NOT NULL DEFAULT 0,
    `Weight` decimal(18,9) NULL,
    `RollType` varchar(32) NULL,
    `ExclusiveGroup` varchar(128) NULL,
    `Guaranteed` tinyint(1) NULL,
    `QuestCondition` varchar(512) NULL,
    `MapCondition` varchar(512) NULL,
    `LevelCondition` varchar(512) NULL,
    `EventCondition` varchar(512) NULL,
    `ChanceEvidenceStatus` varchar(32) NOT NULL,
    `DropRelationshipStatus` varchar(32) NOT NULL,
    `IsDropEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `SourceType` varchar(64) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `Confidence` varchar(32) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`RelationshipId`),
    KEY `IX_MonsterDropRelationships_Monster` (`MonsterId`, `IsDropEnabled`),
    KEY `IX_MonsterDropRelationships_Item` (`ItemId`),
    KEY `IX_MonsterDropRelationships_State` (`DropRelationshipStatus`, `ChanceEvidenceStatus`),
    CONSTRAINT `FK_MonsterDropRelationships_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_MonsterDropRelationships_Monster` FOREIGN KEY (`MonsterId`) REFERENCES `monsters` (`Id`),
    CONSTRAINT `FK_MonsterDropRelationships_DropTable` FOREIGN KEY (`DropTableId`) REFERENCES `drop_tables` (`Id`),
    CONSTRAINT `FK_MonsterDropRelationships_Item` FOREIGN KEY (`ItemId`) REFERENCES `items` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `npc_coordinate_evidence` (
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
    `SourceType` varchar(64) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `Confidence` varchar(32) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`CoordinateEvidenceId`),
    KEY `IX_NpcCoordinateEvidence_Npc` (`NpcId`, `CoordinateEvidenceStatus`),
    KEY `IX_NpcCoordinateEvidence_ClientNpc` (`ClientNpcId`),
    CONSTRAINT `FK_NpcCoordinateEvidence_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_NpcCoordinateEvidence_Npc` FOREIGN KEY (`NpcId`) REFERENCES `npcs` (`Id`),
    CONSTRAINT `FK_NpcCoordinateEvidence_Map` FOREIGN KEY (`MapId`) REFERENCES `maps` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `item_content_profiles` (
    `ItemId` int NOT NULL,
    `RunId` char(36) NOT NULL,
    `ClientItemId` int NOT NULL,
    `SourceSection` varchar(32) NOT NULL,
    `ItemFamily` varchar(64) NOT NULL,
    `OfficialCategoryZhTw` varchar(256) NOT NULL,
    `Stackable` tinyint(1) NULL,
    `MaximumStack` int NULL,
    `MaximumStackEvidenceStatus` varchar(32) NOT NULL,
    `TradePolicy` varchar(32) NOT NULL,
    `WarehousePolicy` varchar(32) NOT NULL,
    `EquipmentSlot` varchar(32) NULL,
    `RequiredLevel` int NULL,
    `PhysicalAttackBonus` int NULL,
    `MagicAttackBonus` int NULL,
    `PhysicalDefenseBonus` int NULL,
    `MagicDefenseBonus` int NULL,
    `HpBonus` int NULL,
    `MpBonus` int NULL,
    `SpeedBonus` int NULL,
    `IconKey` varchar(256) NULL,
    `ModelKey` varchar(256) NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`ItemId`),
    UNIQUE KEY `UX_ItemContentProfiles_Client` (`SourceSection`, `ClientItemId`),
    CONSTRAINT `FK_ItemContentProfiles_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_ItemContentProfiles_Item` FOREIGN KEY (`ItemId`) REFERENCES `items` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `skill_content_profiles` (
    `ProfileId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `SkillId` int NULL,
    `ClientSkillId` int NULL,
    `NameZhTw` varchar(256) NULL,
    `SkillFamily` varchar(64) NOT NULL,
    `SkillFamilyEvidenceStatus` varchar(32) NOT NULL,
    `TargetPolicy` varchar(64) NOT NULL,
    `TargetPolicyEvidenceStatus` varchar(32) NOT NULL,
    `MpCostPolicy` varchar(32) NOT NULL,
    `MpCost` int NULL,
    `MpCostEvidenceStatus` varchar(32) NOT NULL,
    `EffectReferencesJson` json NOT NULL,
    `StatusReferencesJson` json NOT NULL,
    `AnimationKey` varchar(256) NULL,
    `PresentationKey` varchar(256) NULL,
    `Enabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceStatus` varchar(32) NOT NULL,
    `SourceType` varchar(64) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`ProfileId`),
    KEY `IX_SkillContentProfiles_Skill` (`SkillId`),
    KEY `IX_SkillContentProfiles_Family` (`SkillFamily`, `SkillFamilyEvidenceStatus`),
    CONSTRAINT `FK_SkillContentProfiles_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_SkillContentProfiles_Skill` FOREIGN KEY (`SkillId`) REFERENCES `skills` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `merchant_inventory_candidates` (
    `CandidateId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `MerchantId` int NULL,
    `ClientInventoryGroupId` int NOT NULL,
    `ItemId` int NOT NULL,
    `BuyPrice` bigint NULL,
    `SellPrice` bigint NULL,
    `QuantityLimit` int NULL,
    `RefreshPolicy` varchar(32) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `Enabled` tinyint(1) NOT NULL DEFAULT 0,
    `SourceHash` char(64) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`CandidateId`),
    KEY `IX_MerchantInventoryCandidates_Group` (`ClientInventoryGroupId`),
    KEY `IX_MerchantInventoryCandidates_Item` (`ItemId`),
    CONSTRAINT `FK_MerchantInventoryCandidates_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_MerchantInventoryCandidates_Merchant` FOREIGN KEY (`MerchantId`) REFERENCES `merchants` (`Id`),
    CONSTRAINT `FK_MerchantInventoryCandidates_Item` FOREIGN KEY (`ItemId`) REFERENCES `items` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `quest_content_profiles` (
    `ProfileId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `QuestId` int NULL,
    `ClientQuestId` int NOT NULL,
    `NameZhTw` varchar(256) NOT NULL,
    `DescriptionZhTw` longtext NULL,
    `StepsJson` json NOT NULL,
    `StartNpcClientId` int NULL,
    `EndNpcClientId` int NULL,
    `RewardTextZhTw` text NULL,
    `ObjectiveEvidenceStatus` varchar(32) NOT NULL,
    `RewardEvidenceStatus` varchar(32) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`ProfileId`),
    KEY `IX_QuestContentProfiles_ClientQuest` (`ClientQuestId`),
    CONSTRAINT `FK_QuestContentProfiles_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_QuestContentProfiles_Quest` FOREIGN KEY (`QuestId`) REFERENCES `quests` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `quest_objective_candidates` (
    `ObjectiveId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `ClientQuestId` int NOT NULL,
    `ObjectiveType` varchar(32) NOT NULL,
    `ItemId` int NULL,
    `MonsterId` int NULL,
    `RequiredQuantity` int NULL,
    `ObjectiveTextZhTw` text NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `Enabled` tinyint(1) NOT NULL DEFAULT 0,
    `SourceHash` char(64) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    PRIMARY KEY (`ObjectiveId`),
    KEY `IX_QuestObjectiveCandidates_Quest` (`ClientQuestId`),
    CONSTRAINT `FK_QuestObjectiveCandidates_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_QuestObjectiveCandidates_Item` FOREIGN KEY (`ItemId`) REFERENCES `items` (`Id`),
    CONSTRAINT `FK_QuestObjectiveCandidates_Monster` FOREIGN KEY (`MonsterId`) REFERENCES `monsters` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `equipment_set_definitions` (
    `SetId` int NOT NULL,
    `RunId` char(36) NOT NULL,
    `NameZhTw` varchar(256) NOT NULL,
    `RequiredPieces` int NOT NULL,
    `EffectsZhTw` text NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    PRIMARY KEY (`SetId`),
    CONSTRAINT `FK_EquipmentSetDefinitions_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `equipment_set_members` (
    `SetId` int NOT NULL,
    `ItemId` int NOT NULL,
    `SlotName` varchar(32) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    PRIMARY KEY (`SetId`, `ItemId`),
    CONSTRAINT `FK_EquipmentSetMembers_Set` FOREIGN KEY (`SetId`) REFERENCES `equipment_set_definitions` (`SetId`),
    CONSTRAINT `FK_EquipmentSetMembers_Item` FOREIGN KEY (`ItemId`) REFERENCES `items` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `container_item_relationships` (
    `RelationshipId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `ContainerItemId` int NOT NULL,
    `ContainedItemId` int NOT NULL,
    `Quantity` int NULL,
    `OriginalProbability` decimal(18,9) NULL,
    `EffectiveProbability` decimal(18,9) NOT NULL DEFAULT 0,
    `ProbabilityEvidenceStatus` varchar(32) NOT NULL,
    `RelationshipStatus` varchar(32) NOT NULL,
    `Enabled` tinyint(1) NOT NULL DEFAULT 0,
    `SourceHash` char(64) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    PRIMARY KEY (`RelationshipId`),
    KEY `IX_ContainerItemRelationships_Container` (`ContainerItemId`),
    CONSTRAINT `FK_ContainerItemRelationships_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_ContainerItemRelationships_Container` FOREIGN KEY (`ContainerItemId`) REFERENCES `items` (`Id`),
    CONSTRAINT `FK_ContainerItemRelationships_Item` FOREIGN KEY (`ContainedItemId`) REFERENCES `items` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `pet_content_profiles` (
    `ProfileId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `ClientPetId` int NOT NULL,
    `ItemId` int NULL,
    `NameZhTw` varchar(256) NOT NULL,
    `PetFamily` varchar(64) NOT NULL,
    `GrowthType` varchar(64) NULL,
    `BaseStatsJson` json NOT NULL,
    `SkillReferencesJson` json NOT NULL,
    `EvolutionReferencesJson` json NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    PRIMARY KEY (`ProfileId`),
    KEY `IX_PetContentProfiles_ClientPet` (`ClientPetId`),
    CONSTRAINT `FK_PetContentProfiles_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_PetContentProfiles_Item` FOREIGN KEY (`ItemId`) REFERENCES `items` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `pet_innate_definitions` (
    `InnateId` int NOT NULL,
    `RunId` char(36) NOT NULL,
    `NameZhTw` varchar(256) NOT NULL,
    `DescriptionZhTw` text NULL,
    `EffectReference` varchar(256) NULL,
    `EffectValue` int NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    PRIMARY KEY (`InnateId`),
    CONSTRAINT `FK_PetInnateDefinitions_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `pet_egg_relationships` (
    `RelationshipId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `EggItemId` int NOT NULL,
    `HatchResultItemId` int NULL,
    `HatchResultNameZhTw` varchar(256) NULL,
    `OriginalProbability` decimal(18,9) NULL,
    `EffectiveProbability` decimal(18,9) NOT NULL DEFAULT 0,
    `ProbabilityEvidenceStatus` varchar(32) NOT NULL,
    `RelationshipStatus` varchar(32) NOT NULL,
    `Enabled` tinyint(1) NOT NULL DEFAULT 0,
    `SourceHash` char(64) NOT NULL,
    PRIMARY KEY (`RelationshipId`),
    CONSTRAINT `FK_PetEggRelationships_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `FK_PetEggRelationships_Egg` FOREIGN KEY (`EggItemId`) REFERENCES `items` (`Id`),
    CONSTRAINT `FK_PetEggRelationships_Result` FOREIGN KEY (`HatchResultItemId`) REFERENCES `items` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `historical_gameplay_observations` (
    `ObservationId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `EventName` varchar(128) NOT NULL,
    `ObservedAtUtc` datetime(6) NULL,
    `GameplayDataJson` json NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `SourceFile` varchar(768) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`ObservationId`),
    KEY `IX_HistoricalGameplayObservations_Event` (`EventName`),
    CONSTRAINT `FK_HistoricalGameplayObservations_Run` FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `items`
    ADD COLUMN IF NOT EXISTS `ItemFamily` varchar(64) NULL,
    ADD COLUMN IF NOT EXISTS `StackPolicy` varchar(32) NOT NULL DEFAULT 'Unknown',
    ADD COLUMN IF NOT EXISTS `TradePolicy` varchar(32) NOT NULL DEFAULT 'Unknown';

ALTER TABLE `npcs`
    ADD COLUMN IF NOT EXISTS `NpcType` varchar(64) NULL,
    ADD COLUMN IF NOT EXISTS `InteractionFamily` varchar(64) NULL,
    ADD COLUMN IF NOT EXISTS `CoordinateEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `ProductionSpawnEnabled` tinyint(1) NOT NULL DEFAULT 0;

ALTER TABLE `skills`
    ADD COLUMN IF NOT EXISTS `SkillFamilyEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `TargetPolicyEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `MpCostEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked';

