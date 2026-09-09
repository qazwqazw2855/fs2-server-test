CREATE TABLE IF NOT EXISTS `battle_actor_instances` (
    `BattleInstanceId` char(36) NOT NULL,
    `EngineMode` varchar(48) NOT NULL,
    `ActorState` varchar(48) NOT NULL,
    `LifecycleState` varchar(32) NOT NULL,
    `BattleVersion` bigint NOT NULL,
    `RoundNumber` int NOT NULL,
    `RoundVersion` bigint NOT NULL,
    `CommandWindowVersion` bigint NOT NULL,
    `JournalSequence` bigint NOT NULL,
    `OutboxSequence` bigint NOT NULL,
    `FinalizationState` varchar(32) NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `SnapshotJson` longtext NOT NULL,
    `SnapshotHash` char(64) NOT NULL,
    `SchemaVersion` int NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`BattleInstanceId`),
    KEY `IX_BattleActor_State` (`ActorState`, `UpdatedAtUtc`),
    KEY `IX_BattleActor_Recovery` (`RecoveryState`, `UpdatedAtUtc`),
    CONSTRAINT `FK_BattleActor_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_BattleActor_Versions`
        CHECK (`BattleVersion` >= 0 AND `RoundNumber` >= 1 AND `RoundVersion` >= 0
               AND `CommandWindowVersion` >= 0 AND `JournalSequence` >= 0 AND `OutboxSequence` >= 0),
    CONSTRAINT `CK_BattleActor_Schema` CHECK (`SchemaVersion` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_command_journal` (
    `BattleInstanceId` char(36) NOT NULL,
    `JournalSequence` bigint NOT NULL,
    `EntryType` varchar(48) NOT NULL,
    `BattleVersion` bigint NOT NULL,
    `RoundNumber` int NOT NULL,
    `CommandWindowVersion` bigint NOT NULL,
    `PlanId` char(36) NULL,
    `ActionExecutionId` char(36) NULL,
    `ResultReference` varchar(128) NOT NULL,
    `SafeIdempotencyHash` varchar(32) NOT NULL,
    `PayloadVersion` int NOT NULL,
    `CanonicalPayload` longtext NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `CorrelationSafeId` varchar(64) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`BattleInstanceId`, `JournalSequence`),
    KEY `IX_BattleJournal_Round` (`BattleInstanceId`, `RoundNumber`, `JournalSequence`),
    KEY `IX_BattleJournal_Action` (`BattleInstanceId`, `ActionExecutionId`),
    CONSTRAINT `FK_BattleJournal_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_BattleJournal_Sequence`
        CHECK (`JournalSequence` >= 1 AND `BattleVersion` >= 0 AND `RoundNumber` >= 1
               AND `CommandWindowVersion` >= 0 AND `PayloadVersion` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_round_plans` (
    `BattleInstanceId` char(36) NOT NULL,
    `RoundNumber` int NOT NULL,
    `CommandWindowVersion` bigint NOT NULL,
    `CommandLockId` char(36) NOT NULL,
    `CommandLockHash` char(64) NOT NULL,
    `CommandLockJson` longtext NOT NULL,
    `RoundResolutionPlanId` char(36) NULL,
    `PlanHash` char(64) NULL,
    `PlanJson` longtext NULL,
    `RngAlgorithmVersion` varchar(64) NOT NULL,
    `RngStateJson` longtext NOT NULL,
    `SchemaVersion` int NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`BattleInstanceId`, `RoundNumber`),
    UNIQUE KEY `UX_BattleRoundPlan_Window`
        (`BattleInstanceId`, `RoundNumber`, `CommandWindowVersion`),
    UNIQUE KEY `UX_BattleRoundPlan_Id`
        (`BattleInstanceId`, `RoundResolutionPlanId`),
    CONSTRAINT `FK_BattleRoundPlan_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_BattleRoundPlan_Version`
        CHECK (`RoundNumber` >= 1 AND `CommandWindowVersion` >= 0 AND `SchemaVersion` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_action_results` (
    `BattleInstanceId` char(36) NOT NULL,
    `ActionExecutionId` char(36) NOT NULL,
    `RoundNumber` int NOT NULL,
    `ExecutionOrder` int NOT NULL,
    `ParticipantId` char(36) NOT NULL,
    `ResultCode` varchar(48) NOT NULL,
    `ResultHash` char(64) NOT NULL,
    `ResultJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`BattleInstanceId`, `ActionExecutionId`),
    UNIQUE KEY `UX_BattleActionResult_Order`
        (`BattleInstanceId`, `RoundNumber`, `ExecutionOrder`),
    CONSTRAINT `FK_BattleActionResult_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_BattleActionResult_Order`
        CHECK (`RoundNumber` >= 1 AND `ExecutionOrder` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_round_checkpoints` (
    `CheckpointId` char(36) NOT NULL,
    `BattleInstanceId` char(36) NOT NULL,
    `CheckpointVersion` bigint NOT NULL,
    `PayloadVersion` int NOT NULL,
    `SnapshotHash` char(64) NOT NULL,
    `SnapshotJson` longtext NOT NULL,
    `IsValid` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`CheckpointId`),
    UNIQUE KEY `UX_BattleCheckpoint_Version`
        (`BattleInstanceId`, `CheckpointVersion`),
    KEY `IX_BattleCheckpoint_Valid`
        (`BattleInstanceId`, `IsValid`, `CheckpointVersion`),
    CONSTRAINT `FK_BattleCheckpoint_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_BattleCheckpoint_Version`
        CHECK (`CheckpointVersion` >= 1 AND `PayloadVersion` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_event_outbox` (
    `BattleInstanceId` char(36) NOT NULL,
    `EventSequence` bigint NOT NULL,
    `EventId` char(36) NOT NULL,
    `EventType` varchar(64) NOT NULL,
    `RoundNumber` int NOT NULL,
    `ActionExecutionId` char(36) NULL,
    `PayloadVersion` int NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `EventJson` longtext NOT NULL,
    `DeliveryStateJson` longtext NOT NULL,
    `DispatchState` varchar(32) NOT NULL,
    `RetryCount` int NOT NULL,
    `LastFailureCode` varchar(128) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`BattleInstanceId`, `EventSequence`),
    UNIQUE KEY `UX_BattleOutbox_Event` (`EventId`),
    KEY `IX_BattleOutbox_Pending` (`DispatchState`, `UpdatedAtUtc`),
    CONSTRAINT `FK_BattleOutbox_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_BattleOutbox_Sequence`
        CHECK (`EventSequence` >= 1 AND `RoundNumber` >= 1 AND `PayloadVersion` >= 1
               AND `RetryCount` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_finalizations` (
    `BattleInstanceId` char(36) NOT NULL,
    `FinalizationPlanId` char(36) NOT NULL,
    `SafeIdempotencyHash` varchar(32) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `State` varchar(32) NOT NULL,
    `RewardCommitted` tinyint(1) NOT NULL,
    `QuestEventsCommitted` tinyint(1) NOT NULL,
    `WorldResumeCommitted` tinyint(1) NOT NULL,
    `PlanJson` longtext NOT NULL,
    `ResultJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`BattleInstanceId`),
    UNIQUE KEY `UX_BattleFinalization_Plan` (`FinalizationPlanId`),
    UNIQUE KEY `UX_BattleFinalization_Idempotency` (`SafeIdempotencyHash`),
    CONSTRAINT `FK_BattleFinalization_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_actor_recovery` (
    `RecoveryPlanId` char(36) NOT NULL,
    `BattleInstanceId` char(36) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `State` varchar(32) NOT NULL,
    `ExpectedCheckpointVersion` bigint NOT NULL,
    `ExpectedJournalSequence` bigint NOT NULL,
    `ResumeState` varchar(48) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `RecoveryJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`RecoveryPlanId`),
    UNIQUE KEY `UX_BattleRecovery_InstancePayload` (`BattleInstanceId`, `PayloadHash`),
    CONSTRAINT `FK_BattleRecovery_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_BattleRecovery_Sequences`
        CHECK (`ExpectedCheckpointVersion` >= 0 AND `ExpectedJournalSequence` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
