-- 265_translate_player_runtime_audit_readable_labels.sql
-- Game functions: portal interaction handler audit, character lifecycle replay result,
-- and formal validation character notes.

UPDATE `god2_player`.`world_interaction_audit`
SET `Handler` = CASE `Handler`
        WHEN 'Portal' THEN '傳送門處理器'
        WHEN 'Merchant' THEN '商店處理器'
        WHEN 'Dialog' THEN '對話處理器'
        WHEN 'None' THEN '無'
        ELSE `Handler`
    END;

UPDATE `god2_player`.`character_lifecycle_idempotency`
SET `result_code` = CASE `result_code`
        WHEN 'Success' THEN '成功'
        ELSE `result_code`
    END;

UPDATE `god2_player`.`characters`
SET `admin_note` = CASE `admin_note`
        WHEN 'Retired legacy stitched validation character; only characters created on 2026-08-12 or later are valid for formal client validation.'
            THEN '已退役的舊版拼接驗證角色；只有 2026-08-12 或之後建立的角色可用於正式客戶端驗證。'
        WHEN 'Formal schema119 Merchant 3954 external validation fixture; previous location 557790525/(18,10); audit baseline inventory/currency empty.'
            THEN '正式 schema119 商人 3954 外部驗證角色；前一位置 557790525/(18,10)；稽核基準背包與金幣為空。'
        WHEN 'Formal schema119 Merchant 3954 external validation fixture; previous location 557790525/(20,16).'
            THEN '正式 schema119 商人 3954 外部驗證角色；前一位置 557790525/(20,16)。'
        ELSE `admin_note`
    END;

UPDATE `god2_player`.`character_immortals`
SET `admin_note` = CASE `admin_note`
        WHEN 'Formal external-client Lv1 immortal projection acceptance; verified migration 106 baseline and migration 118 wire identity.'
            THEN '正式外部客戶端 1 級神仙投影驗收；已驗證 migration 106 基準與 migration 118 封包身分。'
        WHEN 'Formal external-client validation fixture; verified migration 106 baseline and migration 118 wire identity.'
            THEN '正式外部客戶端驗證資料；已驗證 migration 106 基準與 migration 118 封包身分。'
        ELSE `admin_note`
    END;
