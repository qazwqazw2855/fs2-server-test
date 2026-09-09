ALTER TABLE `characters`
    ADD COLUMN IF NOT EXISTS `CurrentDirection` varchar(32) NOT NULL DEFAULT 'Unknown' AFTER `PositionY`,
    ADD COLUMN IF NOT EXISTS `RuntimeVersion` bigint NOT NULL DEFAULT 0 AFTER `CurrentDirection`,
    ADD COLUMN IF NOT EXISTS `LastPortalTemplateId` int NULL AFTER `RuntimeVersion`,
    ADD COLUMN IF NOT EXISTS `LastPortalTransitionId` char(36) NULL AFTER `LastPortalTemplateId`;

CREATE TABLE IF NOT EXISTS `world_interaction_idempotency` (
    `IdempotencyKeyHash` char(64) NOT NULL,
    `CharacterId` bigint NOT NULL,
    `InteractionType` varchar(32) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `ResultJson` json NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`IdempotencyKeyHash`),
    KEY `IX_WorldInteractionIdempotency_Character` (`CharacterId`, `CompletedAtUtc`),
    CONSTRAINT `FK_WorldInteractionIdempotency_Character`
        FOREIGN KEY (`CharacterId`) REFERENCES `characters` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `world_interaction_audit` (
    `AuditId` char(36) NOT NULL,
    `InteractionId` char(36) NOT NULL,
    `TransitionId` char(36) NULL,
    `IdempotencySafeId` varchar(32) NOT NULL,
    `CorrelationId` varchar(128) NOT NULL,
    `SessionId` varchar(64) NOT NULL,
    `CharacterId` bigint NOT NULL,
    `PlayerRuntimeEntityId` bigint NOT NULL,
    `TargetRuntimeEntityId` bigint NOT NULL,
    `TargetTemplateId` int NOT NULL,
    `InteractionType` varchar(32) NOT NULL,
    `Handler` varchar(64) NOT NULL,
    `SourceMapId` int NOT NULL,
    `TargetMapId` int NULL,
    `SourcePosition` varchar(128) NOT NULL,
    `TargetPosition` varchar(128) NULL,
    `RuntimeVersionBefore` bigint NOT NULL,
    `RuntimeVersionAfter` bigint NOT NULL,
    `Result` varchar(64) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `RollbackStatus` varchar(64) NOT NULL,
    `SessionRebound` tinyint(1) NOT NULL,
    `ReplicationCleared` tinyint(1) NOT NULL,
    `ReplicationRebuilt` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`AuditId`),
    KEY `IX_WorldInteractionAudit_Character` (`CharacterId`, `CompletedAtUtc`),
    KEY `IX_WorldInteractionAudit_Interaction` (`InteractionId`),
    CONSTRAINT `FK_WorldInteractionAudit_Character`
        FOREIGN KEY (`CharacterId`) REFERENCES `characters` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
