CREATE OR REPLACE VIEW god2_game_meta.vw_admin_field_locks_sanitized_readable AS
SELECT
    `資料庫`,
    `資料表`,
    `資料ID`,
    `欄位名稱`,
    CASE
        WHEN `鎖定值Hash` IS NULL OR TRIM(`鎖定值Hash`) = '' THEN '尚未鎖定'
        ELSE '已鎖定正式值'
    END AS `鎖定狀態`,
    CASE
        WHEN `鎖定者` LIKE '%catalog_builder%' THEN '正式目錄建置器'
        WHEN `鎖定者` LIKE '%localhost%' THEN '本機正式工具'
        ELSE COALESCE(NULLIF(`鎖定者`, ''), '未記錄')
    END AS `鎖定來源`,
    COALESCE(NULLIF(`備註`, ''), '無備註') AS `備註`,
    `功能對照`,
    `鎖定時間UTC`
FROM god2_game_meta.vw_admin_field_locks_readable;

CREATE OR REPLACE VIEW god2_game_meta.vw_runtime_catalog_releases_sanitized_readable AS
SELECT
    CONCAT('正式目錄#', ROW_NUMBER() OVER (ORDER BY `建置時間UTC`, `目錄版本ID`)) AS `目錄版本`,
    `目錄資料筆數`,
    CASE
        WHEN `版本狀態` = 'Active' THEN '目前正式版本'
        WHEN `版本狀態` = 'Superseded' THEN '已被新版取代'
        WHEN `版本狀態` = 'Failed' THEN '建置失敗'
        ELSE COALESCE(NULLIF(`版本狀態`, ''), '待確認')
    END AS `版本狀態`,
    `啟用狀態`,
    CASE
        WHEN `建置器版本` LIKE 'God2.GameCatalogBuilder%' THEN '遊戲目錄建置器'
        ELSE COALESCE(NULLIF(`建置器版本`, ''), '建置器待確認')
    END AS `建置來源`,
    COALESCE(NULLIF(`失敗訊息`, ''), '無失敗訊息') AS `失敗訊息`,
    `功能對照`,
    `建置時間UTC`,
    `啟用時間UTC`
FROM god2_game_meta.vw_runtime_catalog_releases_readable;

CREATE OR REPLACE VIEW god2_player.vw_world_interaction_idempotency_sanitized_readable AS
SELECT
    CONCAT('互動紀錄#', ROW_NUMBER() OVER (ORDER BY `建立時間UTC`, `角色ID`, `互動類型`)) AS `互動紀錄`,
    `角色ID`,
    `角色名稱`,
    `互動類型`,
    `重放保護狀態`,
    CASE
        WHEN `互動指紋SHA256` IS NULL OR TRIM(`互動指紋SHA256`) = '' THEN '互動指紋待建立'
        ELSE '互動指紋已建立'
    END AS `互動指紋狀態`,
    CASE
        WHEN `重放保護KeyHash` IS NULL OR TRIM(`重放保護KeyHash`) = '' THEN '重放保護待建立'
        ELSE '重放保護已建立'
    END AS `重放保護鍵狀態`,
    `建立時間UTC`,
    `完成時間UTC`
FROM god2_player.vw_world_interaction_idempotency_readable;

CREATE OR REPLACE VIEW god2_game.vw_player_assets_sanitized_readable AS
SELECT
    `角色ID`,
    `角色名稱`,
    `帳號ID`,
    `背包ID`,
    `背包容量`,
    `背包版本`,
    `背包異動序號`,
    `背包狀態`,
    `背包物品格數`,
    `背包物品總數量`,
    `貨幣`,
    `餘額`,
    `錢包版本`,
    `錢包狀態`,
    `已裝備格數`,
    `已裝備道具`,
    `角色更新時間UTC`,
    `背包更新時間UTC`,
    `錢包更新時間UTC`
FROM god2_game.vw_player_assets_readable;

CREATE OR REPLACE VIEW god2_game.vw_equipment_set_members_sanitized_readable AS
SELECT
    `套裝ID`,
    `套裝名稱`,
    `道具ID`,
    `道具名稱`,
    CASE
        WHEN `官方部位Key` = 'Weapon' THEN '武器'
        WHEN `官方部位Key` = 'Armor' THEN '身體'
        WHEN `官方部位Key` = 'Helmet' THEN '頭部'
        WHEN `官方部位Key` = 'Gloves' THEN '護手'
        WHEN `官方部位Key` = 'Shoes' THEN '鞋子'
        WHEN `官方部位Key` = 'Accessory' THEN '飾品'
        ELSE COALESCE(NULLIF(`部位功能`, ''), '部位待確認')
    END AS `裝備部位`,
    `套裝關係狀態`,
    `套裝效果狀態`
FROM god2_game.vw_equipment_set_members_readable;

CREATE OR REPLACE VIEW god2.vw_content_recovery_runs_sanitized_readable AS
SELECT
    CONCAT('恢復批次#', ROW_NUMBER() OVER (ORDER BY `開始時間UTC`, `原始恢復批次`)) AS `恢復批次`,
    CASE
        WHEN `階段` LIKE '%Phase1%' THEN '第一階段資料恢復'
        WHEN `階段` LIKE '%Phase2%' THEN '第二階段資料提升'
        WHEN `階段` LIKE '%Phase3%' THEN '第三階段正式提升'
        ELSE COALESCE(NULLIF(`階段`, ''), '恢復階段待確認')
    END AS `階段`,
    CASE
        WHEN `狀態` = 'COMPLETED' THEN '已完成'
        WHEN `狀態` = 'FAILED_VALIDATION_GATE' THEN '驗證未通過'
        WHEN `狀態` = 'FAILED' THEN '失敗'
        ELSE COALESCE(NULLIF(`狀態`, ''), '待確認')
    END AS `狀態`,
    CASE
        WHEN `客戶端來源識別` LIKE 'OfficialClient:%' THEN '官方客戶端證據'
        ELSE '來源待確認'
    END AS `來源狀態`,
    CASE
        WHEN `轉換器名稱` LIKE '%Opencc%' THEN '繁體中文轉換器'
        ELSE COALESCE(NULLIF(`轉換器名稱`, ''), '轉換器待確認')
    END AS `轉換器`,
    `開始時間UTC`,
    `完成時間UTC`,
    `功能對照`
FROM (
    SELECT
        `恢復批次` AS `原始恢復批次`,
        `階段`,
        `狀態`,
        `客戶端來源識別`,
        `轉換器名稱`,
        `開始時間UTC`,
        `完成時間UTC`,
        `功能對照`
    FROM god2.vw_content_recovery_runs_readable
) AS source_runs;

CREATE OR REPLACE VIEW god2.vw_blackbox_internal_identifier_cleanup_observations_readable AS
SELECT
    '管理欄位鎖定' AS `觀察類型`,
    CONCAT(`資料庫`, '.', `資料表`) AS `目標`,
    `欄位名稱` AS `功能欄位`,
    `鎖定狀態` AS `目前狀態`,
    `功能對照`,
    '確認正式值保護是否仍能防止重建覆蓋，不需要暴露Hash' AS `檢查方式`
FROM god2_game_meta.vw_admin_field_locks_sanitized_readable
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
