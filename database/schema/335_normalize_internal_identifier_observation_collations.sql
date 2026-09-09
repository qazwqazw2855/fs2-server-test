CREATE OR REPLACE VIEW god2.vw_blackbox_internal_identifier_cleanup_observations_readable AS
SELECT
    CONVERT('管理欄位鎖定' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CONCAT(`資料庫`, '.', `資料表`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`欄位名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能欄位`,
    CONVERT(CONCAT(`鎖定狀態`, '；筆數=', COUNT(*)) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('抽樣確認正式值保護是否仍能防止重建覆蓋，不需要暴露Hash' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `檢查方式`
FROM god2_game_meta.vw_admin_field_locks_sanitized_readable
GROUP BY `資料庫`, `資料表`, `欄位名稱`, `鎖定狀態`, `功能對照`
UNION ALL
SELECT
    CONVERT('Runtime目錄發佈' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`目錄版本` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`建置來源` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能欄位`,
    CONVERT(`版本狀態` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('確認正式服務端啟用的是目前正式版本，不需要暴露目錄指紋或驗證UUID' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `檢查方式`
FROM god2_game_meta.vw_runtime_catalog_releases_sanitized_readable
UNION ALL
SELECT
    CONVERT('世界互動重放保護' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`互動紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`互動類型` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能欄位`,
    CONVERT(CONCAT(`重放保護狀態`, '；', `互動指紋狀態`, '；', `重放保護鍵狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT('防止傳送門、NPC與世界互動被重複提交或重放' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('重複觸發同一世界互動，確認服務端回傳快取結果而不是重複執行' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `檢查方式`
FROM god2_player.vw_world_interaction_idempotency_sanitized_readable
UNION ALL
SELECT
    CONVERT('角色資產' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`角色ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`角色名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能欄位`,
    CONVERT(CONCAT(`背包狀態`, '；', `錢包狀態`, '；', `貨幣`, '=', `餘額`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT('角色背包、錢包、裝備與資產狀態' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('登入角色後比對背包容量、金錢餘額、裝備格與物品數量' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `檢查方式`
FROM god2_game.vw_player_assets_sanitized_readable
UNION ALL
SELECT
    CONVERT('裝備套裝成員' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`套裝ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`道具名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能欄位`,
    CONVERT(CONCAT(`裝備部位`, '；', `套裝效果狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT('裝備套裝部位、成員與套裝效果啟用狀態' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('穿戴套裝成員後確認部位、件數條件與套裝效果是否觸發' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `檢查方式`
FROM god2_game.vw_equipment_set_members_sanitized_readable
UNION ALL
SELECT
    CONVERT('資料恢復批次' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`恢復批次` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`階段` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能欄位`,
    CONVERT(CONCAT(`狀態`, '；', `來源狀態`, '；', `轉換器`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('確認資料來源是官方證據且轉換為繁體中文後才提升到正式資料' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `檢查方式`
FROM god2.vw_content_recovery_runs_sanitized_readable;
