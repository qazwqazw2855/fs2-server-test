-- 262_translate_runtime_ledger_state_labels.sql
-- Game functions: inventory merchant/item/equipment transaction ledger, currency wallet state,
-- portal interaction replay/audit, and character lifecycle replay labels.

ALTER TABLE `god2_player`.`character_lifecycle_idempotency`
    DROP CONSTRAINT IF EXISTS `ck_character_lifecycle_operation`;

UPDATE `god2_player`.`inventory_audit_ledger`
SET `OperationType` = CASE `OperationType`
        WHEN 'MerchantBuy' THEN '商店購買'
        WHEN 'MerchantSell' THEN '商店販售'
        WHEN 'EnhanceEquipment' THEN '裝備強化'
        WHEN 'UseItem' THEN '使用物品'
        ELSE `OperationType`
    END,
    `Source` = CASE `Source`
        WHEN 'Merchant' THEN '商店'
        WHEN 'EquipmentEnhancement' THEN '裝備強化'
        WHEN 'OfficialItemUse' THEN '官方物品使用'
        ELSE `Source`
    END,
    `CurrencyType` = CASE `CurrencyType`
        WHEN 'Gold' THEN '金幣'
        WHEN 'None' THEN '無'
        ELSE `CurrencyType`
    END,
    `Result` = CASE `Result`
        WHEN 'Success' THEN '成功'
        ELSE `Result`
    END;

UPDATE `god2_player`.`inventory_transaction_idempotency`
SET `OperationType` = CASE `OperationType`
        WHEN 'MerchantBuy' THEN '商店購買'
        WHEN 'MerchantSell' THEN '商店販售'
        WHEN 'EnhanceEquipment' THEN '裝備強化'
        WHEN 'UseItem' THEN '使用物品'
        ELSE `OperationType`
    END,
    `Result` = CASE `Result`
        WHEN 'Success' THEN '成功'
        ELSE `Result`
    END;

UPDATE `god2_player`.`player_currency_balances`
SET `DirtyState` = CASE `DirtyState`
        WHEN 'Clean' THEN '乾淨'
        WHEN 'Dirty' THEN '已變更'
        ELSE `DirtyState`
    END;

UPDATE `god2_player`.`player_inventory_state`
SET `DirtyState` = CASE `DirtyState`
        WHEN 'Clean' THEN '乾淨'
        WHEN 'Dirty' THEN '已變更'
        ELSE `DirtyState`
    END;

UPDATE `god2_player`.`world_interaction_audit`
SET `InteractionType` = CASE `InteractionType`
        WHEN 'Portal' THEN '傳送門'
        ELSE `InteractionType`
    END,
    `Result` = CASE `Result`
        WHEN 'Success' THEN '成功'
        ELSE `Result`
    END,
    `RollbackStatus` = CASE `RollbackStatus`
        WHEN 'NotRequired' THEN '不需回復'
        ELSE `RollbackStatus`
    END;

UPDATE `god2_player`.`world_interaction_idempotency`
SET `InteractionType` = CASE `InteractionType`
        WHEN 'Portal' THEN '傳送門'
        ELSE `InteractionType`
    END;

UPDATE `god2_player`.`character_lifecycle_idempotency`
SET `operation` = CASE `operation`
        WHEN 'Create' THEN '建立'
        WHEN 'Rename' THEN '改名'
        WHEN 'Delete' THEN '刪除'
        ELSE `operation`
    END;

ALTER TABLE `god2_player`.`character_lifecycle_idempotency`
    ADD CONSTRAINT `ck_character_lifecycle_operation`
        CHECK (`operation` IN ('Create','Rename','Delete','建立','改名','刪除'));
