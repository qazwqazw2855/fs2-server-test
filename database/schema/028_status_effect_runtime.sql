CREATE TABLE IF NOT EXISTS `battle_status_participant_versions` (
    `BattleInstanceId` char(36) NOT NULL,
    `ParticipantId` char(36) NOT NULL,
    `StatusVersion` bigint NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`BattleInstanceId`, `ParticipantId`),
    CONSTRAINT `FK_BattleStatusVersion_Participant`
        FOREIGN KEY (`BattleInstanceId`, `ParticipantId`)
        REFERENCES `battle_participants` (`BattleInstanceId`, `ParticipantId`) ON DELETE CASCADE,
    CONSTRAINT `CK_BattleStatusVersion_Value` CHECK (`StatusVersion` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_status_instances` (
    `StatusInstanceId` char(36) NOT NULL,
    `StatusDefinitionId` int NOT NULL,
    `BattleInstanceId` char(36) NOT NULL,
    `TargetParticipantId` char(36) NOT NULL,
    `SourceParticipantId` char(36) NOT NULL,
    `SourceActionId` char(36) NOT NULL,
    `SourceSkillExecutionId` char(36) NULL,
    `SourceEffectExecutionId` char(36) NULL,
    `StackCount` int NOT NULL,
    `MaximumStacks` int NOT NULL,
    `AppliedRound` int NOT NULL,
    `LastRefreshedRound` int NOT NULL,
    `ExpiresAfterRound` int NULL,
    `RemainingRounds` int NULL,
    `LifecycleState` varchar(32) NOT NULL,
    `TriggerCursor` int NOT NULL,
    `RuntimeVersion` bigint NOT NULL,
    `DefinitionContentVersion` varchar(128) NOT NULL,
    `PolicyStatus` varchar(32) NOT NULL,
    `InstanceJson` longtext NOT NULL,
    `AppliedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `RemovedAtUtc` datetime(6) NULL,
    `RemovalReason` varchar(32) NULL,
    PRIMARY KEY (`StatusInstanceId`),
    KEY `IX_BattleStatus_Target` (`BattleInstanceId`, `TargetParticipantId`, `LifecycleState`),
    KEY `IX_BattleStatus_Definition` (`BattleInstanceId`, `StatusDefinitionId`, `LifecycleState`),
    CONSTRAINT `FK_BattleStatus_Target`
        FOREIGN KEY (`BattleInstanceId`, `TargetParticipantId`)
        REFERENCES `battle_participants` (`BattleInstanceId`, `ParticipantId`) ON DELETE CASCADE,
    CONSTRAINT `FK_BattleStatus_Source`
        FOREIGN KEY (`BattleInstanceId`, `SourceParticipantId`)
        REFERENCES `battle_participants` (`BattleInstanceId`, `ParticipantId`) ON DELETE CASCADE,
    CONSTRAINT `CK_BattleStatus_Stack` CHECK (`StackCount` >= 1 AND `MaximumStacks` >= `StackCount`),
    CONSTRAINT `CK_BattleStatus_Round` CHECK (`AppliedRound` >= 1 AND `LastRefreshedRound` >= `AppliedRound`),
    CONSTRAINT `CK_BattleStatus_Duration` CHECK (`RemainingRounds` IS NULL OR `RemainingRounds` >= 0),
    CONSTRAINT `CK_BattleStatus_Version` CHECK (`RuntimeVersion` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_status_applications` (
    `StatusApplicationId` char(36) NOT NULL,
    `StatusApplicationPlanId` char(36) NOT NULL,
    `StatusInstanceId` char(36) NOT NULL,
    `BattleInstanceId` char(36) NOT NULL,
    `RoundNumber` int NOT NULL,
    `TargetParticipantId` char(36) NOT NULL,
    `StatusDefinitionId` int NOT NULL,
    `IdempotencyKeyHash` char(64) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `State` varchar(32) NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `ApplicationJson` longtext NOT NULL,
    `ResultJson` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`StatusApplicationId`),
    UNIQUE KEY `UX_StatusApplication_Idempotency` (`IdempotencyKeyHash`),
    KEY `IX_StatusApplication_Recovery` (`State`, `RecoveryState`, `UpdatedAtUtc`),
    CONSTRAINT `FK_StatusApplication_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_StatusApplication_Round` CHECK (`RoundNumber` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_status_removals` (
    `StatusRemovalId` char(36) NOT NULL,
    `StatusRemovalPlanId` char(36) NOT NULL,
    `StatusInstanceId` char(36) NOT NULL,
    `BattleInstanceId` char(36) NOT NULL,
    `RoundNumber` int NOT NULL,
    `TargetParticipantId` char(36) NOT NULL,
    `StatusDefinitionId` int NOT NULL,
    `RemovalReason` varchar(32) NOT NULL,
    `IdempotencyKeyHash` char(64) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `State` varchar(32) NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `RemovalJson` longtext NOT NULL,
    `ResultJson` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`StatusRemovalId`),
    UNIQUE KEY `UX_StatusRemoval_Idempotency` (`IdempotencyKeyHash`),
    KEY `IX_StatusRemoval_Instance` (`StatusInstanceId`, `UpdatedAtUtc`),
    CONSTRAINT `FK_StatusRemoval_Instance`
        FOREIGN KEY (`StatusInstanceId`) REFERENCES `battle_status_instances` (`StatusInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_StatusRemoval_Round` CHECK (`RoundNumber` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_status_trigger_plans` (
    `StatusTriggerExecutionPlanId` char(36) NOT NULL,
    `BattleInstanceId` char(36) NOT NULL,
    `RoundNumber` int NOT NULL,
    `TriggerPhase` varchar(32) NOT NULL,
    `SourceActionId` char(36) NOT NULL,
    `ResolutionCursor` int NOT NULL,
    `State` varchar(32) NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `PlanJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`StatusTriggerExecutionPlanId`),
    UNIQUE KEY `UX_StatusTriggerPlan_Phase` (`BattleInstanceId`, `RoundNumber`, `TriggerPhase`, `SourceActionId`),
    KEY `IX_StatusTriggerPlan_Recovery` (`State`, `RecoveryState`, `UpdatedAtUtc`),
    CONSTRAINT `FK_StatusTriggerPlan_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_StatusTriggerPlan_Round` CHECK (`RoundNumber` >= 1),
    CONSTRAINT `CK_StatusTriggerPlan_Cursor` CHECK (`ResolutionCursor` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_status_trigger_results` (
    `TriggerExecutionId` char(36) NOT NULL,
    `StatusTriggerExecutionPlanId` char(36) NOT NULL,
    `StatusInstanceId` char(36) NOT NULL,
    `TriggerIndex` int NOT NULL,
    `ExecutionOrder` int NOT NULL,
    `State` varchar(32) NOT NULL,
    `Result` varchar(32) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `Damage` bigint NOT NULL,
    `Heal` bigint NOT NULL,
    `HpBefore` bigint NOT NULL,
    `HpAfter` bigint NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `ResultJson` longtext NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`TriggerExecutionId`),
    UNIQUE KEY `UX_StatusTriggerResult_Order` (`StatusTriggerExecutionPlanId`, `ExecutionOrder`),
    CONSTRAINT `FK_StatusTriggerResult_Plan`
        FOREIGN KEY (`StatusTriggerExecutionPlanId`)
        REFERENCES `battle_status_trigger_plans` (`StatusTriggerExecutionPlanId`) ON DELETE CASCADE,
    CONSTRAINT `FK_StatusTriggerResult_Instance`
        FOREIGN KEY (`StatusInstanceId`) REFERENCES `battle_status_instances` (`StatusInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_StatusTriggerResult_Order` CHECK (`TriggerIndex` >= 0 AND `ExecutionOrder` >= 0),
    CONSTRAINT `CK_StatusTriggerResult_Hp` CHECK (`Damage` >= 0 AND `Heal` >= 0 AND `HpBefore` >= 0 AND `HpAfter` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_status_idempotency` (
    `Scope` varchar(32) NOT NULL,
    `IdempotencyKeyHash` char(64) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `BattleInstanceId` char(36) NOT NULL,
    `OperationId` char(36) NOT NULL,
    `ResultJson` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`Scope`, `IdempotencyKeyHash`),
    KEY `IX_StatusIdempotency_Battle` (`BattleInstanceId`, `CompletedAtUtc`),
    CONSTRAINT `FK_StatusIdempotency_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_status_recovery` (
    `RecoveryId` char(36) NOT NULL,
    `BattleInstanceId` char(36) NOT NULL,
    `OperationType` varchar(32) NOT NULL,
    `OperationId` char(36) NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `ResolutionCursor` int NOT NULL,
    `RecoveryJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`RecoveryId`),
    UNIQUE KEY `UX_StatusRecovery_Operation` (`OperationType`, `OperationId`),
    KEY `IX_StatusRecovery_Pending` (`RecoveryState`, `UpdatedAtUtc`),
    CONSTRAINT `FK_StatusRecovery_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_StatusRecovery_Cursor` CHECK (`ResolutionCursor` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_status_audit` (
    `AuditId` char(36) NOT NULL,
    `BattleInstanceId` char(36) NOT NULL,
    `RoundNumber` int NOT NULL,
    `StatusApplicationId` char(36) NULL,
    `StatusRemovalId` char(36) NULL,
    `StatusTriggerExecutionPlanId` char(36) NULL,
    `TriggerExecutionId` char(36) NULL,
    `StatusInstanceId` char(36) NULL,
    `StatusDefinitionId` int NULL,
    `IdempotencySafeId` varchar(32) NOT NULL,
    `Operation` varchar(64) NOT NULL,
    `Result` varchar(64) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `AuditJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`AuditId`),
    KEY `IX_StatusAudit_Battle` (`BattleInstanceId`, `RoundNumber`, `CompletedAtUtc`),
    KEY `IX_StatusAudit_Instance` (`StatusInstanceId`, `CompletedAtUtc`),
    CONSTRAINT `FK_StatusAudit_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_StatusAudit_Round` CHECK (`RoundNumber` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
