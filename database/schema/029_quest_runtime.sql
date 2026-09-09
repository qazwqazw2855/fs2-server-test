CREATE TABLE IF NOT EXISTS `quest_instances` (
    `QuestInstanceId` char(36) NOT NULL,
    `CharacterId` bigint NOT NULL,
    `QuestDefinitionId` int NOT NULL,
    `State` varchar(32) NOT NULL,
    `QuestVersion` bigint NOT NULL,
    `DefinitionContentVersion` varchar(128) NOT NULL,
    `RepeatIteration` int NOT NULL,
    `RewardState` varchar(32) NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `InstanceJson` longtext NOT NULL,
    `AcceptedAtUtc` datetime(6) NOT NULL,
    `ReadyAtUtc` datetime(6) NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `AbandonedAtUtc` datetime(6) NULL,
    `LastProgressAtUtc` datetime(6) NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`QuestInstanceId`),
    KEY `IX_QuestInstance_CharacterState` (`CharacterId`, `State`, `UpdatedAtUtc`),
    KEY `IX_QuestInstance_CharacterDefinition` (`CharacterId`, `QuestDefinitionId`, `RepeatIteration`),
    CONSTRAINT `FK_QuestInstance_Character`
        FOREIGN KEY (`CharacterId`) REFERENCES `characters` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `CK_QuestInstance_Version` CHECK (`QuestVersion` >= 0),
    CONSTRAINT `CK_QuestInstance_Repeat` CHECK (`RepeatIteration` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `quest_objective_states` (
    `QuestInstanceId` char(36) NOT NULL,
    `ObjectiveDefinitionId` varchar(128) NOT NULL,
    `ObjectiveIndex` int NOT NULL,
    `ObjectiveType` varchar(32) NOT NULL,
    `TargetTemplateId` int NULL,
    `RequiredCount` bigint NOT NULL,
    `CurrentProgress` bigint NOT NULL,
    `State` varchar(32) NOT NULL,
    `ObjectiveVersion` bigint NOT NULL,
    `LastSemanticEventSafeId` varchar(32) NULL,
    `PolicyStatus` varchar(32) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`QuestInstanceId`, `ObjectiveDefinitionId`),
    UNIQUE KEY `UX_QuestObjective_Index` (`QuestInstanceId`, `ObjectiveIndex`),
    CONSTRAINT `FK_QuestObjective_Instance`
        FOREIGN KEY (`QuestInstanceId`) REFERENCES `quest_instances` (`QuestInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_QuestObjective_Count`
        CHECK (`RequiredCount` >= 0 AND `CurrentProgress` >= 0 AND `CurrentProgress` <= `RequiredCount`),
    CONSTRAINT `CK_QuestObjective_Version` CHECK (`ObjectiveVersion` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `quest_operation_idempotency` (
    `Operation` varchar(32) NOT NULL,
    `IdempotencyKeyHash` char(64) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `QuestIntentId` char(36) NOT NULL,
    `QuestInstanceId` char(36) NULL,
    `CharacterId` bigint NOT NULL,
    `ResultCode` varchar(32) NOT NULL,
    `ResultJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Operation`, `IdempotencyKeyHash`),
    KEY `IX_QuestOperation_Character` (`CharacterId`, `CompletedAtUtc`),
    CONSTRAINT `FK_QuestOperation_Character`
        FOREIGN KEY (`CharacterId`) REFERENCES `characters` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `quest_progress_mutations` (
    `ProgressMutationId` char(36) NOT NULL,
    `SemanticEventId` char(36) NOT NULL,
    `QuestInstanceId` char(36) NOT NULL,
    `ObjectiveDefinitionId` varchar(128) NOT NULL,
    `CharacterId` bigint NOT NULL,
    `ProgressBefore` bigint NOT NULL,
    `ProgressDelta` bigint NOT NULL,
    `ProgressAfter` bigint NOT NULL,
    `ExpectedQuestVersion` bigint NOT NULL,
    `ExpectedObjectiveVersion` bigint NOT NULL,
    `IdempotencyKeyHash` char(64) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `ResultCode` varchar(32) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `PlanJson` longtext NOT NULL,
    `ResultJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`ProgressMutationId`),
    UNIQUE KEY `UX_QuestProgress_EventObjective`
        (`SemanticEventId`, `QuestInstanceId`, `ObjectiveDefinitionId`),
    UNIQUE KEY `UX_QuestProgress_Idempotency` (`IdempotencyKeyHash`),
    KEY `IX_QuestProgress_Instance` (`QuestInstanceId`, `CreatedAtUtc`),
    CONSTRAINT `FK_QuestProgress_Instance`
        FOREIGN KEY (`QuestInstanceId`) REFERENCES `quest_instances` (`QuestInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_QuestProgress_Value`
        CHECK (`ProgressBefore` >= 0 AND `ProgressAfter` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `quest_reward_finalization` (
    `QuestRewardPlanId` char(36) NOT NULL,
    `CompletionPlanId` char(36) NOT NULL,
    `QuestInstanceId` char(36) NOT NULL,
    `CharacterId` bigint NOT NULL,
    `IdempotencyKeyHash` char(64) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `State` varchar(32) NOT NULL,
    `ResultCode` varchar(32) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `RewardJson` longtext NOT NULL,
    `ResultJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`QuestRewardPlanId`),
    UNIQUE KEY `UX_QuestReward_Idempotency` (`IdempotencyKeyHash`),
    UNIQUE KEY `UX_QuestReward_Instance` (`QuestInstanceId`),
    CONSTRAINT `FK_QuestReward_Instance`
        FOREIGN KEY (`QuestInstanceId`) REFERENCES `quest_instances` (`QuestInstanceId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `quest_recovery_state` (
    `RecoveryId` char(36) NOT NULL,
    `QuestInstanceId` char(36) NOT NULL,
    `CharacterId` bigint NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `RecoveryJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`RecoveryId`),
    UNIQUE KEY `UX_QuestRecovery_Instance` (`QuestInstanceId`),
    KEY `IX_QuestRecovery_Pending` (`RecoveryState`, `UpdatedAtUtc`),
    CONSTRAINT `FK_QuestRecovery_Instance`
        FOREIGN KEY (`QuestInstanceId`) REFERENCES `quest_instances` (`QuestInstanceId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `quest_audit` (
    `AuditId` char(36) NOT NULL,
    `QuestIntentId` char(36) NULL,
    `QuestInstanceId` char(36) NULL,
    `QuestDefinitionId` int NULL,
    `ObjectiveDefinitionId` varchar(128) NULL,
    `ProgressMutationId` char(36) NULL,
    `SemanticEventId` char(36) NULL,
    `CompletionPlanId` char(36) NULL,
    `RewardPlanId` char(36) NULL,
    `SafeIdempotencyIdentifier` varchar(32) NOT NULL,
    `CorrelationId` varchar(128) NOT NULL,
    `SessionSafeHash` varchar(32) NOT NULL,
    `CharacterId` bigint NOT NULL,
    `Operation` varchar(64) NOT NULL,
    `Result` varchar(32) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `AuditJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`AuditId`),
    KEY `IX_QuestAudit_Character` (`CharacterId`, `CompletedAtUtc`),
    KEY `IX_QuestAudit_Instance` (`QuestInstanceId`, `CompletedAtUtc`),
    CONSTRAINT `FK_QuestAudit_Character`
        FOREIGN KEY (`CharacterId`) REFERENCES `characters` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
