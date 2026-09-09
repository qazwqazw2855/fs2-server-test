-- 267_translate_observed_world_placeholder_names.sql
-- Game functions: readable observed merchant/dialog NPC names, client-derived portal names,
-- and placeholder map names used by formal world interaction data.

UPDATE `god2_game`.`merchants`
SET `name_zh_tw` = CASE `name_zh_tw`
        WHEN 'Observed Merchant 3954' THEN '實測商人 3954'
        ELSE `name_zh_tw`
    END;

UPDATE `god2_game`.`merchant_inventory`
SET `merchant_name_cache` = CASE `merchant_name_cache`
        WHEN 'Observed Merchant 3954' THEN '實測商人 3954'
        ELSE `merchant_name_cache`
    END;

UPDATE `god2_game`.`npcs`
SET `name_zh_tw` = CASE
        WHEN `name_zh_tw` = 'Observed Merchant 3954' THEN '實測商人 3954'
        WHEN `name_zh_tw` = 'Observed Dialog NPC 3793' THEN '實測對話 NPC 3793'
        WHEN `name_zh_tw` LIKE 'client:npc-template/data2/rom/npc/npc%-rom'
            THEN CONCAT('客戶端 NPC 模板 ', REPLACE(SUBSTRING_INDEX(SUBSTRING_INDEX(`name_zh_tw`, '/', -1), '-', 1), 'npc', ''))
        ELSE `name_zh_tw`
    END;

UPDATE `god2_game`.`npc_spawns`
SET `npc_name_cache` = CASE `npc_name_cache`
        WHEN 'Observed Merchant 3954' THEN '實測商人 3954'
        WHEN 'Observed Dialog NPC 3793' THEN '實測對話 NPC 3793'
        ELSE `npc_name_cache`
    END,
    `map_name_cache` = CASE `map_name_cache`
        WHEN 'Island01/indoor/groceryL' THEN '島嶼01室內雜貨左側'
        ELSE `map_name_cache`
    END;

UPDATE `god2_game`.`maps`
SET `name_zh_tw` = CASE `name_zh_tw`
        WHEN 'Tong map group' THEN '潼關地圖群'
        WHEN 'Island01/indoor/groceryL' THEN '島嶼01室內雜貨左側'
        WHEN '打寶區Island' THEN '打寶區島嶼'
        WHEN '練功房 / battle training rooms' THEN '練功房'
        ELSE `name_zh_tw`
    END;

UPDATE `god2_game`.`portals`
SET `name_zh_tw` = CASE
        WHEN `name_zh_tw` LIKE 'client:can-link/array/%'
            THEN CONCAT('客戶端傳送點：陣列區第', SUBSTRING_INDEX(`name_zh_tw`, '/', -1), '號')
        WHEN `name_zh_tw` LIKE 'client:can-link/island03/%'
            THEN CONCAT('客戶端傳送點：島嶼03第', SUBSTRING_INDEX(`name_zh_tw`, '/', -1), '號')
        WHEN `name_zh_tw` LIKE 'client:can-link/south003/%'
            THEN CONCAT('客戶端傳送點：南方003第', SUBSTRING_INDEX(`name_zh_tw`, '/', -1), '號')
        WHEN `name_zh_tw` LIKE 'client:can-link/tong/%'
            THEN CONCAT('客戶端傳送點：潼關第', SUBSTRING_INDEX(`name_zh_tw`, '/', -1), '號')
        WHEN `name_zh_tw` = 'Observed Stage 3 Route' THEN '實測第 3 階段路線'
        WHEN `name_zh_tw` = 'official-observed/kunlun-portal-to-secret-room' THEN '官方實測：崑崙傳送至密室'
        WHEN `name_zh_tw` = 'official-observed/training-room-entry' THEN '官方實測：練功房入口'
        WHEN `name_zh_tw` = 'official-observed/yuanshi-transfer-to-fenghua' THEN '官方實測：元始傳送至楓華'
        ELSE `name_zh_tw`
    END;
