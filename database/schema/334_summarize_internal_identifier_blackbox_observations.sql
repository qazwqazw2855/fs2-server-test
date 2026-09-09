CREATE OR REPLACE VIEW god2.vw_blackbox_internal_identifier_cleanup_observations_readable AS
SELECT
    '管理欄位鎖定' AS `觀察類型`,
    CONCAT(`資料庫`, '.', `資料表`) AS `目標`,
    `欄位名稱` AS `功能欄位`,
    CONCAT(`鎖定狀態`, '；筆數=', COUNT(*)) AS `目前狀態`,
    `功能對照`,
    '抽樣確認正式值保護是否仍能防止重建覆蓋，不需要暴露Hash' AS `檢查方式`
FROM god2_game_meta.vw_admin_field_locks_sanitized_readable
GROUP BY `資料庫`, `資料表`, `欄位名稱`, `鎖定狀態`, `功能對照`
UNION ALL
SELECT
    'Runtime目錄發佈' AS `觀察類型`,
    `目錄版本` AS `目標`,
    `建置來源` AS `功能欄位`,
    `版本狀態` AS `目前狀態`,
    `功能對照`,
    '確認正式服務端啟用的是目前正式版本，不需要暴露目錄指紋或驗證UUID' AS `檢查方式`
FROM god2_game_meta.vw_runtime_catalog_releases_sanitized_readable
UNION ALL
SELECT
    '世界互動重放保護' AS `觀察類型`,
    `互動紀錄` AS `目標`,
    `互動類型` AS `功能欄位`,
    CONCAT(`重放保護狀態`, '；', `互動指紋狀態`, '；', `重放保護鍵狀態`) AS `目前狀態`,
    '防止傳送門、NPC與世界互動被重複提交或重放' AS `功能對照`,
    '重複觸發同一世界互動，確認服務端回傳快取結果而不是重複執行' AS `檢查方式`
FROM god2_player.vw_world_interaction_idempotency_sanitized_readable
UNION ALL
SELECT
    '角色資產' AS `觀察類型`,
    CAST(`角色ID` AS CHAR) AS `目標`,
    `角色名稱` AS `功能欄位`,
    CONCAT(`背包狀態`, '；', `錢包狀態`, '；', `貨幣`, '=', `餘額`) AS `目前狀態`,
    '角色背包、錢包、裝備與資產狀態' AS `功能對照`,
    '登入角色後比對背包容量、金錢餘額、裝備格與物品數量' AS `檢查方式`
FROM god2_game.vw_player_assets_sanitized_readable
UNION ALL
SELECT
    '裝備套裝成員' AS `觀察類型`,
    CAST(`套裝ID` AS CHAR) AS `目標`,
    `道具名稱` AS `功能欄位`,
    CONCAT(`裝備部位`, '；', `套裝效果狀態`) AS `目前狀態`,
    '裝備套裝部位、成員與套裝效果啟用狀態' AS `功能對照`,
    '穿戴套裝成員後確認部位、件數條件與套裝效果是否觸發' AS `檢查方式`
FROM god2_game.vw_equipment_set_members_sanitized_readable
UNION ALL
SELECT
    '資料恢復批次' AS `觀察類型`,
    `恢復批次` AS `目標`,
    `階段` AS `功能欄位`,
    CONCAT(`狀態`, '；', `來源狀態`, '；', `轉換器`) AS `目前狀態`,
    `功能對照`,
    '確認資料來源是官方證據且轉換為繁體中文後才提升到正式資料' AS `檢查方式`
FROM god2.vw_content_recovery_runs_sanitized_readable;
