CREATE TABLE IF NOT EXISTS `skill_executions` (
    `SkillExecutionId` char(36) NOT NULL,
    `BattleInstanceId` char(36) NOT NULL,
    `RoundNumber` int NOT NULL,
    `BattleActionId` char(36) NOT NULL,
    `ParticipantId` char(36) NOT NULL,
    `SkillDefinitionId` int NOT NULL,
    `State` varchar(32) NOT NULL,
    `ResolutionCursor` int NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `PlanJson` longtext NOT NULL,
    `ResultJson` longtext NULL,
    `StartedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`SkillExecutionId`),
    UNIQUE KEY `UX_SkillExecution_BattleAction` (`BattleInstanceId`, `RoundNumber`, `BattleActionId`),
    KEY `IX_SkillExecution_Recovery` (`State`, `RecoveryState`, `UpdatedAtUtc`),
    CONSTRAINT `FK_SkillExecution_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_SkillExecution_Round` CHECK (`RoundNumber` >= 1),
    CONSTRAINT `CK_SkillExecution_Cursor` CHECK (`ResolutionCursor` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `skill_effect_executions` (
    `EffectExecutionId` char(36) NOT NULL,
    `SkillExecutionId` char(36) NOT NULL,
    `EffectDefinitionId` varchar(128) NOT NULL,
    `EffectIndex` int NOT NULL,
    `TargetIndex` int NOT NULL,
    `TargetParticipantId` char(36) NOT NULL,
    `EffectType` varchar(32) NOT NULL,
    `State` varchar(32) NOT NULL,
    `HpBefore` bigint NOT NULL,
    `Damage` bigint NOT NULL,
    `Heal` bigint NOT NULL,
    `HpAfter` bigint NOT NULL,
    `RuntimeVersionBefore` bigint NOT NULL,
    `RuntimeVersionAfter` bigint NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `EffectJson` longtext NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`EffectExecutionId`),
    UNIQUE KEY `UX_SkillEffect_Order` (`SkillExecutionId`, `EffectIndex`, `TargetIndex`),
    CONSTRAINT `FK_SkillEffect_Execution`
        FOREIGN KEY (`SkillExecutionId`) REFERENCES `skill_executions` (`SkillExecutionId`) ON DELETE CASCADE,
    CONSTRAINT `CK_SkillEffect_Index` CHECK (`EffectIndex` >= 0),
    CONSTRAINT `CK_SkillEffect_TargetIndex` CHECK (`TargetIndex` >= 0),
    CONSTRAINT `CK_SkillEffect_Hp` CHECK (`HpBefore` >= 0 AND `HpAfter` >= 0),
    CONSTRAINT `CK_SkillEffect_Value` CHECK (`Damage` >= 0 AND `Heal` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `skill_cost_reservations` (
    `ReservationId` char(36) NOT NULL,
    `SkillExecutionId` char(36) NOT NULL,
    `ParticipantId` char(36) NOT NULL,
    `SkillDefinitionId` int NOT NULL,
    `ResourceType` varchar(32) NOT NULL,
    `Amount` bigint NOT NULL,
    `State` varchar(32) NOT NULL,
    `RuntimeVersionBefore` bigint NOT NULL,
    `IdempotencySafeId` varchar(32) NOT NULL,
    `ReservationJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CommittedAtUtc` datetime(6) NULL,
    `ReleasedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`ReservationId`),
    UNIQUE KEY `UX_SkillCost_Execution` (`SkillExecutionId`),
    CONSTRAINT `FK_SkillCost_Execution`
        FOREIGN KEY (`SkillExecutionId`) REFERENCES `skill_executions` (`SkillExecutionId`) ON DELETE CASCADE,
    CONSTRAINT `CK_SkillCost_Amount` CHECK (`Amount` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_skill_usage` (
    `BattleInstanceId` char(36) NOT NULL,
    `ParticipantId` char(36) NOT NULL,
    `SkillDefinitionId` int NOT NULL,
    `LastUsedRound` int NULL,
    `AvailableAtRound` int NOT NULL,
    `UsageCount` int NOT NULL,
    `RuntimeVersion` bigint NOT NULL,
    `IdempotencySafeId` varchar(32) NOT NULL,
    `PolicyStatus` varchar(32) NOT NULL,
    `UsageJson` longtext NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`BattleInstanceId`, `ParticipantId`, `SkillDefinitionId`),
    CONSTRAINT `FK_SkillUsage_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_SkillUsage_Round` CHECK (`AvailableAtRound` >= 1),
    CONSTRAINT `CK_SkillUsage_Count` CHECK (`UsageCount` >= 0),
    CONSTRAINT `CK_SkillUsage_Version` CHECK (`RuntimeVersion` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `skill_idempotency` (
    `Scope` varchar(32) NOT NULL,
    `IdempotencyKeyHash` char(64) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `SkillExecutionId` char(36) NOT NULL,
    `ResultJson` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`Scope`, `IdempotencyKeyHash`),
    KEY `IX_SkillIdempotency_Execution` (`SkillExecutionId`, `CompletedAtUtc`),
    CONSTRAINT `FK_SkillIdempotency_Execution`
        FOREIGN KEY (`SkillExecutionId`) REFERENCES `skill_executions` (`SkillExecutionId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `skill_audit` (
    `AuditId` char(36) NOT NULL,
    `SkillExecutionId` char(36) NOT NULL,
    `BattleInstanceId` char(36) NOT NULL,
    `RoundNumber` int NOT NULL,
    `EffectExecutionId` char(36) NULL,
    `ParticipantId` char(36) NOT NULL,
    `TargetParticipantId` char(36) NULL,
    `SkillDefinitionId` int NOT NULL,
    `IdempotencySafeId` varchar(32) NOT NULL,
    `Result` varchar(64) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `ResolutionCursor` int NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `AuditJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`AuditId`),
    KEY `IX_SkillAudit_Execution` (`SkillExecutionId`, `CompletedAtUtc`),
    KEY `IX_SkillAudit_Battle` (`BattleInstanceId`, `RoundNumber`, `CompletedAtUtc`),
    CONSTRAINT `FK_SkillAudit_Execution`
        FOREIGN KEY (`SkillExecutionId`) REFERENCES `skill_executions` (`SkillExecutionId`) ON DELETE CASCADE,
    CONSTRAINT `CK_SkillAudit_Cursor` CHECK (`ResolutionCursor` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
