-- Canonical operational player state used by the normal Server runtime.
-- Evidence tables in `god2` are read only during this one-time state copy.

ALTER TABLE `god2_player`.`characters`
    ADD COLUMN IF NOT EXISTS `current_direction` VARCHAR(32) NOT NULL DEFAULT 'Unknown' COMMENT '目前面向方向' AFTER `position_y`,
    ADD COLUMN IF NOT EXISTS `runtime_version` BIGINT NOT NULL DEFAULT 0 COMMENT '位置與世界狀態的樂觀鎖定版本' AFTER `current_direction`,
    ADD COLUMN IF NOT EXISTS `last_portal_template_id` BIGINT NULL COMMENT '最近使用的傳送點 ID' AFTER `runtime_version`,
    ADD COLUMN IF NOT EXISTS `last_portal_transition_id` CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '最近傳送交易 GUID' AFTER `last_portal_template_id`;

ALTER TABLE `god2_player`.`character_inventory`
    ADD COLUMN IF NOT EXISTS `protocol_visible_item_id` BIGINT NOT NULL DEFAULT 0 COMMENT '正式客戶端可見的物品實例 ID' AFTER `inventory_id`,
    ADD COLUMN IF NOT EXISTS `inventory_version` BIGINT NOT NULL DEFAULT 0 COMMENT '背包整體版本' AFTER `quantity`,
    ADD COLUMN IF NOT EXISTS `slot_version` BIGINT NOT NULL DEFAULT 1 COMMENT '欄位版本' AFTER `inventory_version`,
    ADD COLUMN IF NOT EXISTS `bind_state` VARCHAR(32) NOT NULL DEFAULT 'Unknown' COMMENT '綁定狀態' AFTER `bound`,
    ADD COLUMN IF NOT EXISTS `item_instance_metadata` JSON NULL COMMENT 'Runtime 必要的物品實例 metadata；不存放封包原文' AFTER `bind_state`,
    ADD COLUMN IF NOT EXISTS `deleted_at_utc` DATETIME(6) NULL COMMENT '軟刪除時間' AFTER `updated_at_utc`;

CREATE TABLE IF NOT EXISTS `god2_player`.`player_inventory_state` (
    `CharacterId` BIGINT NOT NULL COMMENT '角色 ID',
    `InventoryId` CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '背包 GUID',
    `Capacity` INT NOT NULL COMMENT '背包容量',
    `InventoryVersion` BIGINT NOT NULL COMMENT '背包版本',
    `MutationSequence` BIGINT NOT NULL COMMENT '變更序號',
    `DirtyState` VARCHAR(32) NOT NULL COMMENT '持久化狀態',
    `UpdatedAtUtc` DATETIME(6) NOT NULL COMMENT '更新時間 UTC',
    PRIMARY KEY (`CharacterId`),
    CONSTRAINT `fk_player_inventory_state_character` FOREIGN KEY (`CharacterId`)
        REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Runtime 背包版本與容量狀態';

CREATE TABLE IF NOT EXISTS `god2_player`.`player_currency_balances` (
    `CharacterId` BIGINT NOT NULL COMMENT '角色 ID',
    `CurrencyType` VARCHAR(32) NOT NULL COMMENT '貨幣類型',
    `Balance` BIGINT NOT NULL COMMENT '目前餘額',
    `Version` BIGINT NOT NULL COMMENT '貨幣版本',
    `DirtyState` VARCHAR(32) NOT NULL COMMENT '持久化狀態',
    `UpdatedAtUtc` DATETIME(6) NOT NULL COMMENT '更新時間 UTC',
    PRIMARY KEY (`CharacterId`,`CurrencyType`),
    CONSTRAINT `fk_player_currency_character` FOREIGN KEY (`CharacterId`)
        REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_player_currency_nonnegative` CHECK (`Balance` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色貨幣餘額';

CREATE TABLE IF NOT EXISTS `god2_player`.`inventory_item_identity_sequence` (
    `PersistentInventoryItemId` BIGINT NOT NULL AUTO_INCREMENT COMMENT '全域持久化物品實例 ID',
    `ReservedAtUtc` DATETIME(6) NOT NULL COMMENT '保留時間 UTC',
    PRIMARY KEY (`PersistentInventoryItemId`)
) ENGINE=InnoDB AUTO_INCREMENT=8000000000000000000 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='背包物品實例 ID 配號器';

CREATE TABLE IF NOT EXISTS `god2_player`.`inventory_transaction_idempotency` (
    `IdempotencyKeyHash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '冪等鍵 SHA-256',
    `PayloadHash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '請求內容 SHA-256',
    `TransactionId` CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '交易 GUID',
    `CharacterId` BIGINT NOT NULL COMMENT '角色 ID',
    `OperationType` VARCHAR(32) NOT NULL COMMENT '背包操作類型',
    `Result` VARCHAR(32) NOT NULL COMMENT '操作結果',
    `FailureCode` VARCHAR(128) NOT NULL COMMENT '失敗碼；成功時為空字串',
    `InventoryVersionBefore` BIGINT NOT NULL COMMENT '操作前背包版本',
    `InventoryVersionAfter` BIGINT NOT NULL COMMENT '操作後背包版本',
    `CurrencyBefore` BIGINT NOT NULL COMMENT '操作前貨幣',
    `CurrencyAfter` BIGINT NOT NULL COMMENT '操作後貨幣',
    `CreatedAtUtc` DATETIME(6) NOT NULL COMMENT '建立時間 UTC',
    `CompletedAtUtc` DATETIME(6) NOT NULL COMMENT '完成時間 UTC',
    PRIMARY KEY (`IdempotencyKeyHash`),
    KEY `ix_inventory_idempotency_character` (`CharacterId`),
    CONSTRAINT `fk_inventory_idempotency_character` FOREIGN KEY (`CharacterId`)
        REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='背包交易冪等結果';

CREATE TABLE IF NOT EXISTS `god2_player`.`inventory_audit_ledger` (
    `AuditId` CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '稽核 GUID',
    `TransactionId` CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '交易 GUID',
    `IdempotencySafeId` VARCHAR(32) NOT NULL COMMENT '不含敏感資料的冪等顯示碼',
    `CharacterId` BIGINT NOT NULL COMMENT '角色 ID',
    `SessionId` VARCHAR(64) NOT NULL COMMENT 'Server session ID',
    `OperationType` VARCHAR(32) NOT NULL COMMENT '背包操作類型',
    `Source` VARCHAR(128) NOT NULL COMMENT '操作來源',
    `MerchantTemplateId` INT NULL COMMENT '商人 ID',
    `ItemTemplateId` INT NULL COMMENT '物品模板 ID',
    `InventoryItemId` BIGINT NULL COMMENT '物品實例 ID',
    `QuantityBefore` INT NOT NULL COMMENT '操作前數量',
    `QuantityAfter` INT NOT NULL COMMENT '操作後數量',
    `CurrencyType` VARCHAR(32) NOT NULL COMMENT '貨幣類型',
    `CurrencyBefore` BIGINT NOT NULL COMMENT '操作前貨幣',
    `CurrencyAfter` BIGINT NOT NULL COMMENT '操作後貨幣',
    `InventoryVersionBefore` BIGINT NOT NULL COMMENT '操作前背包版本',
    `InventoryVersionAfter` BIGINT NOT NULL COMMENT '操作後背包版本',
    `Result` VARCHAR(32) NOT NULL COMMENT '操作結果',
    `FailureCode` VARCHAR(128) NOT NULL COMMENT '失敗碼',
    `CreatedAtUtc` DATETIME(6) NOT NULL COMMENT '建立時間 UTC',
    `CompletedAtUtc` DATETIME(6) NOT NULL COMMENT '完成時間 UTC',
    `CorrelationId` VARCHAR(64) NOT NULL COMMENT '追蹤 ID',
    PRIMARY KEY (`AuditId`),
    KEY `ix_inventory_audit_character` (`CharacterId`),
    KEY `ix_inventory_audit_transaction` (`TransactionId`),
    CONSTRAINT `fk_inventory_audit_character` FOREIGN KEY (`CharacterId`)
        REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='背包交易稽核帳本';

CREATE TABLE IF NOT EXISTS `god2_player`.`world_interaction_idempotency` (
    `IdempotencyKeyHash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '冪等鍵 SHA-256',
    `CharacterId` BIGINT NOT NULL COMMENT '角色 ID',
    `InteractionType` VARCHAR(32) NOT NULL COMMENT '世界互動類型',
    `PayloadHash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '請求內容 SHA-256',
    `ResultJson` JSON NOT NULL COMMENT '可重播的結果 JSON',
    `CreatedAtUtc` DATETIME(6) NOT NULL COMMENT '建立時間 UTC',
    `CompletedAtUtc` DATETIME(6) NOT NULL COMMENT '完成時間 UTC',
    PRIMARY KEY (`IdempotencyKeyHash`),
    KEY `ix_world_idempotency_character` (`CharacterId`,`CompletedAtUtc`),
    CONSTRAINT `fk_world_idempotency_character` FOREIGN KEY (`CharacterId`)
        REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='世界互動冪等結果';

CREATE TABLE IF NOT EXISTS `god2_player`.`world_interaction_audit` (
    `AuditId` CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '稽核 GUID',
    `InteractionId` CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '互動 GUID',
    `TransitionId` CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '傳送 GUID',
    `IdempotencySafeId` VARCHAR(32) NOT NULL COMMENT '不含敏感資料的冪等顯示碼',
    `CorrelationId` VARCHAR(128) NOT NULL COMMENT '追蹤 ID',
    `SessionId` VARCHAR(64) NOT NULL COMMENT 'Server session ID',
    `CharacterId` BIGINT NOT NULL COMMENT '角色 ID',
    `PlayerRuntimeEntityId` BIGINT NOT NULL COMMENT '玩家 Runtime entity ID',
    `TargetRuntimeEntityId` BIGINT NOT NULL COMMENT '目標 Runtime entity ID',
    `TargetTemplateId` INT NOT NULL COMMENT '目標模板 ID',
    `InteractionType` VARCHAR(32) NOT NULL COMMENT '互動類型',
    `Handler` VARCHAR(64) NOT NULL COMMENT '處理器名稱',
    `SourceMapId` INT NOT NULL COMMENT '來源地圖 ID',
    `TargetMapId` INT NULL COMMENT '目標地圖 ID',
    `SourcePosition` VARCHAR(128) NOT NULL COMMENT '來源座標',
    `TargetPosition` VARCHAR(128) NULL COMMENT '目標座標',
    `RuntimeVersionBefore` BIGINT NOT NULL COMMENT '操作前 Runtime 版本',
    `RuntimeVersionAfter` BIGINT NOT NULL COMMENT '操作後 Runtime 版本',
    `Result` VARCHAR(64) NOT NULL COMMENT '互動結果',
    `FailureCode` VARCHAR(128) NOT NULL COMMENT '失敗碼',
    `RollbackStatus` VARCHAR(64) NOT NULL COMMENT '回復狀態',
    `SessionRebound` TINYINT(1) NOT NULL COMMENT 'Session 是否重新繫結',
    `ReplicationCleared` TINYINT(1) NOT NULL COMMENT '舊複寫狀態是否清除',
    `ReplicationRebuilt` TINYINT(1) NOT NULL COMMENT '新複寫狀態是否建立',
    `CreatedAtUtc` DATETIME(6) NOT NULL COMMENT '建立時間 UTC',
    `CompletedAtUtc` DATETIME(6) NOT NULL COMMENT '完成時間 UTC',
    PRIMARY KEY (`AuditId`),
    KEY `ix_world_audit_character` (`CharacterId`,`CompletedAtUtc`),
    KEY `ix_world_audit_interaction` (`InteractionId`),
    CONSTRAINT `fk_world_audit_character` FOREIGN KEY (`CharacterId`)
        REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='世界互動稽核帳本';

SET @god2_sync_mode := 1;

UPDATE `god2_player`.`characters` canonical
JOIN `god2`.`characters` previous_state ON previous_state.`Id`=canonical.`character_id`
SET canonical.`current_direction`=previous_state.`CurrentDirection`,
    canonical.`runtime_version`=previous_state.`RuntimeVersion`,
    canonical.`last_portal_template_id`=previous_state.`LastPortalTemplateId`,
    canonical.`last_portal_transition_id`=previous_state.`LastPortalTransitionId`;

INSERT IGNORE INTO `god2_player`.`player_inventory_state`
SELECT old_state.`CharacterId`,old_state.`InventoryId`,old_state.`Capacity`,old_state.`InventoryVersion`,
       old_state.`MutationSequence`,old_state.`DirtyState`,old_state.`UpdatedAtUtc`
FROM `god2`.`player_inventory_state` old_state
JOIN `god2_player`.`characters` character_row ON character_row.`character_id`=old_state.`CharacterId`;

INSERT IGNORE INTO `god2_player`.`player_currency_balances`
SELECT old_value.`CharacterId`,old_value.`CurrencyType`,old_value.`Balance`,old_value.`Version`,old_value.`DirtyState`,old_value.`UpdatedAtUtc`
FROM `god2`.`player_currency_balances` old_value
JOIN `god2_player`.`characters` character_row ON character_row.`character_id`=old_value.`CharacterId`;

INSERT IGNORE INTO `god2_player`.`character_inventory`
    (`inventory_id`,`protocol_visible_item_id`,`character_id`,`slot_index`,`item_id`,`item_name_cache`,`quantity`,
     `inventory_version`,`slot_version`,`bound`,`bind_state`,`item_instance_metadata`,`enabled`,`created_at_utc`,`updated_at_utc`,`deleted_at_utc`)
SELECT old_value.`PersistentInventoryItemId`,old_value.`ProtocolVisibleItemId`,old_value.`CharacterId`,old_value.`SlotIndex`,
       old_value.`ItemId`,item_row.`name_zh_tw`,old_value.`Quantity`,old_value.`InventoryVersion`,old_value.`SlotVersion`,
       CASE WHEN old_value.`BindState` IN ('Bound','CharacterBound','AccountBound') THEN 1 ELSE 0 END,
       old_value.`BindState`,old_value.`ItemInstanceMetadata`,CASE WHEN old_value.`DeletedAtUtc` IS NULL THEN 1 ELSE 0 END,
       old_value.`CreatedAtUtc`,old_value.`UpdatedAtUtc`,old_value.`DeletedAtUtc`
FROM `god2`.`inventory_slots` old_value
JOIN `god2_player`.`characters` character_row ON character_row.`character_id`=old_value.`CharacterId`
JOIN `god2_game`.`items` item_row ON item_row.`item_id`=old_value.`ItemId`;

INSERT IGNORE INTO `god2_player`.`inventory_transaction_idempotency`
SELECT old_value.* FROM `god2`.`inventory_transaction_idempotency` old_value
JOIN `god2_player`.`characters` character_row ON character_row.`character_id`=old_value.`CharacterId`;

INSERT IGNORE INTO `god2_player`.`inventory_audit_ledger`
SELECT old_value.* FROM `god2`.`inventory_audit_ledger` old_value
JOIN `god2_player`.`characters` character_row ON character_row.`character_id`=old_value.`CharacterId`;

INSERT IGNORE INTO `god2_player`.`world_interaction_idempotency`
SELECT old_value.* FROM `god2`.`world_interaction_idempotency` old_value
JOIN `god2_player`.`characters` character_row ON character_row.`character_id`=old_value.`CharacterId`;

INSERT IGNORE INTO `god2_player`.`world_interaction_audit`
SELECT old_value.* FROM `god2`.`world_interaction_audit` old_value
JOIN `god2_player`.`characters` character_row ON character_row.`character_id`=old_value.`CharacterId`;

SET @god2_sync_mode := 0;

GRANT SELECT ON `god2`.`__SchemaVersion` TO `god2_runtime_role`;
