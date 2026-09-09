-- 263_translate_inventory_audit_source_labels.sql
-- Game function: inventory audit provenance for official merchant buy/sell packet evidence.

UPDATE `god2_player`.`inventory_audit_ledger`
SET `Source` = CASE `Source`
        WHEN 'LiveRecovery/Stages4-7-attempt-759-trace' THEN '官方實機商店買賣封包證據'
        WHEN 'WorldInteraction:Merchant' THEN '世界互動：商店'
        ELSE `Source`
    END;
