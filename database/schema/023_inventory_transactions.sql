ALTER TABLE `items`
    ADD COLUMN IF NOT EXISTS `DisplayName` varchar(128) NULL AFTER `Name`,
    ADD COLUMN IF NOT EXISTS `LocalizationKey` varchar(128) NULL AFTER `DisplayName`,
    ADD COLUMN IF NOT EXISTS `ItemCategory` varchar(32) NOT NULL DEFAULT 'Unknown' AFTER `ItemType`,
    ADD COLUMN IF NOT EXISTS `StackPolicy` varchar(32) NOT NULL DEFAULT 'Unknown' AFTER `ItemCategory`,
    ADD COLUMN IF NOT EXISTS `BindPolicy` varchar(32) NOT NULL DEFAULT 'Unknown' AFTER `MaxStack`,
    ADD COLUMN IF NOT EXISTS `TradePolicy` varchar(32) NOT NULL DEFAULT 'Unknown' AFTER `BindPolicy`,
    ADD COLUMN IF NOT EXISTS `SellPolicy` varchar(32) NOT NULL DEFAULT 'Unknown' AFTER `TradePolicy`,
    ADD COLUMN IF NOT EXISTS `BaseBuyPrice` bigint NOT NULL DEFAULT 0 AFTER `SellPolicy`,
    ADD COLUMN IF NOT EXISTS `BaseSellPrice` bigint NOT NULL DEFAULT 0 AFTER `BaseBuyPrice`,
    ADD COLUMN IF NOT EXISTS `CurrencyType` varchar(32) NOT NULL DEFAULT 'Gold' AFTER `BaseSellPrice`,
    ADD COLUMN IF NOT EXISTS `EquipmentCategory` varchar(64) NOT NULL DEFAULT 'Unknown' AFTER `CurrencyType`,
    ADD COLUMN IF NOT EXISTS `ConsumableCategory` varchar(64) NOT NULL DEFAULT 'Unknown' AFTER `EquipmentCategory`,
    ADD COLUMN IF NOT EXISTS `QuestItemFlag` tinyint(1) NOT NULL DEFAULT 0 AFTER `ConsumableCategory`,
    ADD COLUMN IF NOT EXISTS `Enabled` tinyint(1) NOT NULL DEFAULT 1 AFTER `QuestItemFlag`,
    ADD COLUMN IF NOT EXISTS `ContentVersion` varchar(64) NOT NULL DEFAULT 'runtime-item-content-v1' AFTER `Enabled`,
    ADD COLUMN IF NOT EXISTS `RawMetadata` json NULL AFTER `ContentVersion`,
    ADD COLUMN IF NOT EXISTS `UpdatedAtUtc` datetime(6) NULL AFTER `RawMetadata`;

ALTER TABLE `inventory_slots`
    ADD COLUMN IF NOT EXISTS `PersistentInventoryItemId` bigint NULL AFTER `CharacterId`,
    ADD COLUMN IF NOT EXISTS `ProtocolVisibleItemId` bigint NOT NULL DEFAULT 0 AFTER `PersistentInventoryItemId`,
    ADD COLUMN IF NOT EXISTS `InventoryVersion` bigint NOT NULL DEFAULT 0 AFTER `Quantity`,
    ADD COLUMN IF NOT EXISTS `SlotVersion` bigint NOT NULL DEFAULT 1 AFTER `InventoryVersion`,
    ADD COLUMN IF NOT EXISTS `BindState` varchar(32) NOT NULL DEFAULT 'Unknown' AFTER `SlotVersion`,
    ADD COLUMN IF NOT EXISTS `ItemInstanceMetadata` json NULL AFTER `BindState`,
    ADD COLUMN IF NOT EXISTS `DeletedAtUtc` datetime(6) NULL AFTER `UpdatedAtUtc`;

UPDATE `inventory_slots`
SET `PersistentInventoryItemId` = (`CharacterId` * 1000000) + `SlotIndex` + 1
WHERE `PersistentInventoryItemId` IS NULL;

CREATE TABLE IF NOT EXISTS `player_inventory_state` (
    `CharacterId` bigint NOT NULL,
    `InventoryId` char(36) NOT NULL,
    `Capacity` int NOT NULL,
    `InventoryVersion` bigint NOT NULL,
    `MutationSequence` bigint NOT NULL,
    `DirtyState` varchar(32) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`CharacterId`),
    CONSTRAINT `FK_PlayerInventoryState_Characters_CharacterId`
        FOREIGN KEY (`CharacterId`) REFERENCES `characters` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `player_currency_balances` (
    `CharacterId` bigint NOT NULL,
    `CurrencyType` varchar(32) NOT NULL,
    `Balance` bigint NOT NULL,
    `Version` bigint NOT NULL,
    `DirtyState` varchar(32) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`CharacterId`, `CurrencyType`),
    CONSTRAINT `FK_PlayerCurrencyBalances_Characters_CharacterId`
        FOREIGN KEY (`CharacterId`) REFERENCES `characters` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `inventory_transaction_idempotency` (
    `IdempotencyKeyHash` char(64) NOT NULL,
    `PayloadHash` char(64) NOT NULL,
    `TransactionId` char(36) NOT NULL,
    `CharacterId` bigint NOT NULL,
    `OperationType` varchar(32) NOT NULL,
    `Result` varchar(32) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `InventoryVersionBefore` bigint NOT NULL,
    `InventoryVersionAfter` bigint NOT NULL,
    `CurrencyBefore` bigint NOT NULL,
    `CurrencyAfter` bigint NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`IdempotencyKeyHash`),
    KEY `IX_InventoryIdempotency_CharacterId` (`CharacterId`),
    CONSTRAINT `FK_InventoryIdempotency_Characters_CharacterId`
        FOREIGN KEY (`CharacterId`) REFERENCES `characters` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `inventory_audit_ledger` (
    `AuditId` char(36) NOT NULL,
    `TransactionId` char(36) NOT NULL,
    `IdempotencySafeId` varchar(32) NOT NULL,
    `CharacterId` bigint NOT NULL,
    `SessionId` varchar(64) NOT NULL,
    `OperationType` varchar(32) NOT NULL,
    `Source` varchar(128) NOT NULL,
    `MerchantTemplateId` int NULL,
    `ItemTemplateId` int NULL,
    `InventoryItemId` bigint NULL,
    `QuantityBefore` int NOT NULL,
    `QuantityAfter` int NOT NULL,
    `CurrencyType` varchar(32) NOT NULL,
    `CurrencyBefore` bigint NOT NULL,
    `CurrencyAfter` bigint NOT NULL,
    `InventoryVersionBefore` bigint NOT NULL,
    `InventoryVersionAfter` bigint NOT NULL,
    `Result` varchar(32) NOT NULL,
    `FailureCode` varchar(128) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    `CorrelationId` varchar(64) NOT NULL,
    PRIMARY KEY (`AuditId`),
    KEY `IX_InventoryAudit_CharacterId` (`CharacterId`),
    KEY `IX_InventoryAudit_TransactionId` (`TransactionId`),
    CONSTRAINT `FK_InventoryAudit_Characters_CharacterId`
        FOREIGN KEY (`CharacterId`) REFERENCES `characters` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
