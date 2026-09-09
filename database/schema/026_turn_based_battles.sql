CREATE TABLE IF NOT EXISTS `battle_instances` (
    `BattleInstanceId` char(36) NOT NULL,
    `BattleRequestId` char(36) NOT NULL,
    `WorldInstanceId` varchar(128) NOT NULL,
    `SourceMapId` int NOT NULL,
    `EncounterDefinitionId` varchar(128) NOT NULL,
    `State` varchar(32) NOT NULL,
    `Phase` varchar(32) NOT NULL,
    `CurrentRoundNumber` int NOT NULL,
    `CurrentResolutionIndex` int NOT NULL,
    `BattleVersion` bigint NOT NULL,
    `WinnerSide` varchar(32) NOT NULL,
    `CompletionReason` varchar(64) NOT NULL,
    `RewardState` varchar(32) NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `AggregateJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`BattleInstanceId`),
    UNIQUE KEY `UX_BattleInstance_Request` (`BattleRequestId`),
    KEY `IX_BattleInstance_Active` (`State`, `UpdatedAtUtc`),
    KEY `IX_BattleInstance_Map` (`SourceMapId`, `State`),
    CONSTRAINT `CK_BattleInstance_Round` CHECK (`CurrentRoundNumber` >= 1),
    CONSTRAINT `CK_BattleInstance_Resolution` CHECK (`CurrentResolutionIndex` >= 0),
    CONSTRAINT `CK_BattleInstance_Version` CHECK (`BattleVersion` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_participants` (
    `BattleInstanceId` char(36) NOT NULL,
    `ParticipantId` char(36) NOT NULL,
    `ParticipantType` varchar(32) NOT NULL,
    `Side` varchar(32) NOT NULL,
    `FormationSlot` int NOT NULL,
    `CharacterId` bigint NULL,
    `MonsterTemplateId` int NULL,
    `SourceRuntimeEntityId` bigint NOT NULL,
    `BattleRuntimeEntityId` bigint NOT NULL,
    `SessionSafeId` varchar(32) NOT NULL,
    `CurrentHp` bigint NOT NULL,
    `IsAlive` tinyint(1) NOT NULL,
    `IsConnected` tinyint(1) NOT NULL,
    `RuntimeVersion` bigint NOT NULL,
    `ParticipantJson` longtext NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`BattleInstanceId`, `ParticipantId`),
    UNIQUE KEY `UX_BattleParticipant_Runtime` (`BattleInstanceId`, `BattleRuntimeEntityId`),
    UNIQUE KEY `UX_BattleParticipant_Formation` (`BattleInstanceId`, `Side`, `FormationSlot`),
    KEY `IX_BattleParticipant_Character` (`CharacterId`, `IsAlive`),
    CONSTRAINT `FK_BattleParticipant_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_BattleParticipant_Hp` CHECK (`CurrentHp` >= 0),
    CONSTRAINT `CK_BattleParticipant_Version` CHECK (`RuntimeVersion` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_rounds` (
    `BattleInstanceId` char(36) NOT NULL,
    `RoundNumber` int NOT NULL,
    `State` varchar(32) NOT NULL,
    `CurrentResolutionIndex` int NOT NULL,
    `RoundVersion` bigint NOT NULL,
    `RoundJson` longtext NOT NULL,
    `StartedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`BattleInstanceId`, `RoundNumber`),
    CONSTRAINT `FK_BattleRound_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_BattleRound_Number` CHECK (`RoundNumber` >= 1),
    CONSTRAINT `CK_BattleRound_Resolution` CHECK (`CurrentResolutionIndex` >= 0),
    CONSTRAINT `CK_BattleRound_Version` CHECK (`RoundVersion` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_actions` (
    `BattleInstanceId` char(36) NOT NULL,
    `RoundNumber` int NOT NULL,
    `ActionId` char(36) NOT NULL,
    `ParticipantId` char(36) NOT NULL,
    `ActionType` varchar(32) NOT NULL,
    `State` varchar(32) NOT NULL,
    `ResolutionIndex` int NULL,
    `PayloadHash` char(64) NOT NULL,
    `ActionJson` longtext NOT NULL,
    `ResultJson` longtext NULL,
    `SubmittedAtUtc` datetime(6) NOT NULL,
    `ResolvedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`BattleInstanceId`, `RoundNumber`, `ActionId`),
    UNIQUE KEY `UX_BattleAction_ParticipantRound` (`BattleInstanceId`, `RoundNumber`, `ParticipantId`),
    KEY `IX_BattleAction_Resolution` (`BattleInstanceId`, `RoundNumber`, `ResolutionIndex`),
    CONSTRAINT `FK_BattleAction_Round`
        FOREIGN KEY (`BattleInstanceId`, `RoundNumber`)
        REFERENCES `battle_rounds` (`BattleInstanceId`, `RoundNumber`) ON DELETE CASCADE,
    CONSTRAINT `CK_BattleAction_Resolution` CHECK (`ResolutionIndex` IS NULL OR `ResolutionIndex` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_idempotency` (
    `Scope` varchar(32) NOT NULL,
    `IdempotencyKeyHash` char(64) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `BattleInstanceId` char(36) NULL,
    `ResultJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Scope`, `IdempotencyKeyHash`),
    KEY `IX_BattleIdempotency_Instance` (`BattleInstanceId`, `CompletedAtUtc`),
    CONSTRAINT `FK_BattleIdempotency_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_completion` (
    `BattleInstanceId` char(36) NOT NULL,
    `WinnerSide` varchar(32) NOT NULL,
    `CompletionReason` varchar(64) NOT NULL,
    `RewardState` varchar(32) NOT NULL,
    `RecoveryState` varchar(32) NOT NULL,
    `BattleVersion` bigint NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`BattleInstanceId`),
    CONSTRAINT `FK_BattleCompletion_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `battle_audit` (
    `AuditId` char(36) NOT NULL,
    `BattleInstanceId` char(36) NOT NULL,
    `RoundNumber` int NULL,
    `ActionId` char(36) NULL,
    `IdempotencySafeId` varchar(32) NOT NULL,
    `SessionSafeId` varchar(32) NOT NULL,
    `CharacterId` bigint NULL,
    `ParticipantId` char(36) NULL,
    `StateBefore` varchar(32) NOT NULL,
    `StateAfter` varchar(32) NOT NULL,
    `PhaseBefore` varchar(32) NOT NULL,
    `PhaseAfter` varchar(32) NOT NULL,
    `BattleVersionBefore` bigint NOT NULL,
    `BattleVersionAfter` bigint NOT NULL,
    `Result` varchar(64) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `AuditJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`AuditId`),
    KEY `IX_BattleAudit_Instance` (`BattleInstanceId`, `CompletedAtUtc`),
    KEY `IX_BattleAudit_Character` (`CharacterId`, `CompletedAtUtc`),
    CONSTRAINT `FK_BattleAudit_Battle`
        FOREIGN KEY (`BattleInstanceId`) REFERENCES `battle_instances` (`BattleInstanceId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
