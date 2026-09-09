CREATE OR REPLACE VIEW god2.vw_merchants_sanitized_readable AS
SELECT
    `商人ID`,
    `NPC_ID`,
    `商人名稱`,
    `NPC名稱`,
    `地圖ID`,
    COALESCE(NULLIF(`地圖名稱`, ''), '地圖待確認') AS `地圖名稱`,
    `座標X`,
    `座標Y`,
    CASE WHEN `恢復狀態` = 'Recovered' THEN '已恢復' ELSE COALESCE(NULLIF(`恢復狀態`, ''), '待確認') END AS `恢復狀態`,
    CASE WHEN `在地化狀態` = 'TraditionalVerified' THEN '繁體已驗證' WHEN `在地化狀態` = 'ConvertedToTraditional' THEN '已轉繁體' ELSE COALESCE(NULLIF(`在地化狀態`, ''), '待確認') END AS `在地化狀態`,
    CASE WHEN `恢復批次` IS NULL OR TRIM(`恢復批次`) = '' THEN '恢復批次待確認' ELSE '恢復批次已記錄' END AS `恢復來源狀態`,
    `功能對照`
FROM god2.vw_merchants_readable;

CREATE OR REPLACE VIEW god2.vw_monster_drop_relationships_sanitized_readable AS
SELECT
    CONCAT('怪物掉落#', ROW_NUMBER() OVER (ORDER BY `怪物ID`, `物品ID`, `建立時間UTC`)) AS `掉落紀錄`,
    CASE WHEN `匯入批次ID` IS NULL OR TRIM(`匯入批次ID`) = '' THEN '匯入批次待確認' ELSE '匯入批次已記錄' END AS `匯入來源狀態`,
    `怪物ID`,
    COALESCE(NULLIF(`怪物名稱`, ''), '怪物名稱待確認') AS `怪物名稱`,
    `掉落表ID`,
    CASE WHEN `掉落群組ID` IS NULL THEN '掉落群組待確認' ELSE '掉落群組已記錄' END AS `掉落群組狀態`,
    CASE WHEN `掉落項目ID` IS NULL OR TRIM(CAST(`掉落項目ID` AS CHAR)) = '' THEN '掉落項目待確認' ELSE '掉落項目已記錄' END AS `掉落項目狀態`,
    `物品ID`,
    `物品名稱`,
    `最小數量`,
    `最大數量`,
    `宣告掉落率`,
    `實際掉落率`,
    `權重`,
    COALESCE(NULLIF(`擲骰類型`, ''), '擲骰類型待確認') AS `擲骰類型`,
    COALESCE(NULLIF(`互斥群組`, ''), '無互斥群組') AS `互斥群組`,
    `是否必掉`,
    COALESCE(NULLIF(`任務條件`, ''), '無任務條件') AS `任務條件`,
    COALESCE(NULLIF(`地圖條件`, ''), '無地圖條件') AS `地圖條件`,
    COALESCE(NULLIF(`等級條件`, ''), '無等級條件') AS `等級條件`,
    COALESCE(NULLIF(`活動條件`, ''), '無活動條件') AS `活動條件`,
    CASE WHEN `關聯狀態` = 'Candidate' THEN '候選' WHEN `關聯狀態` = 'Derived' THEN '推導' ELSE COALESCE(NULLIF(`關聯狀態`, ''), '待確認') END AS `關聯狀態`,
    `掉落狀態`,
    `正式狀態`,
    `功能對照`,
    `建立時間UTC`,
    `更新時間UTC`
FROM god2.vw_monster_drop_relationships_readable;

CREATE OR REPLACE VIEW god2.vw_monster_respawn_schedules_sanitized_readable AS
SELECT
    CONCAT('怪物重生#', ROW_NUMBER() OVER (ORDER BY `建立時間UTC`, `怪物模板ID`, `出生點ID`)) AS `重生紀錄`,
    CASE WHEN `死亡ID` IS NULL OR TRIM(CAST(`死亡ID` AS CHAR)) = '' THEN '死亡紀錄待建立' ELSE '死亡紀錄已建立' END AS `死亡紀錄狀態`,
    `怪物模板ID`,
    `怪物名稱`,
    `出生點ID`,
    `出生群組`,
    CASE WHEN `世界實例ID` IS NULL OR TRIM(CAST(`世界實例ID` AS CHAR)) = '' THEN '世界實例待建立' ELSE '世界實例已建立' END AS `世界實例狀態`,
    `地圖ID`,
    `地圖名稱`,
    `座標X`,
    `座標Y`,
    `座標Z`,
    `方向`,
    `預計重生時間UTC`,
    `證據政策`,
    `排程狀態`,
    CASE WHEN `前RuntimeID` IS NULL OR TRIM(CAST(`前RuntimeID` AS CHAR)) = '' THEN '前Runtime未記錄' ELSE '前Runtime已記錄' END AS `前Runtime狀態`,
    CASE WHEN `新RuntimeID` IS NULL OR TRIM(CAST(`新RuntimeID` AS CHAR)) = '' THEN '新Runtime待建立' ELSE '新Runtime已建立' END AS `新Runtime狀態`,
    CASE WHEN `失敗代碼` IS NULL OR TRIM(`失敗代碼`) = '' THEN '無失敗' ELSE `失敗代碼` END AS `失敗狀態`,
    `功能對照`,
    `建立時間UTC`,
    `完成時間UTC`
FROM god2.vw_monster_respawn_schedules_readable;

CREATE OR REPLACE VIEW god2_game.vw_world_client_resource_status_sanitized_readable AS
SELECT
    `地圖ID`,
    `地圖名稱`,
    CASE
        WHEN `區域代碼` IS NULL OR TRIM(`區域代碼`) = '' THEN '區域待確認'
        WHEN `區域代碼` = 'array' THEN '陣法區域'
        ELSE '區域已對應'
    END AS `區域狀態`,
    `格寬`,
    `格高`,
    `最小X`,
    `最大X`,
    `最小Y`,
    `最大Y`,
    `資源狀態`,
    `客戶端身分狀態`,
    `功能對照`,
    `更新時間UTC`
FROM god2_game.vw_world_client_resource_status_readable;

CREATE OR REPLACE VIEW god2_game_meta.vw_admin_change_audit_sanitized_readable AS
SELECT
    CONCAT('管理異動#', ROW_NUMBER() OVER (ORDER BY `異動時間UTC`, `資料庫`, `資料表`, `資料ID`, `欄位名稱`)) AS `異動紀錄`,
    `資料庫`,
    `資料表`,
    `資料ID`,
    `欄位名稱`,
    `舊值摘要`,
    `新值摘要`,
    CASE WHEN `異動者` LIKE '%catalog_builder%' THEN '正式目錄建置器' WHEN `異動者` LIKE '%localhost%' THEN '本機正式工具' ELSE COALESCE(NULLIF(`異動者`, ''), '異動者待確認') END AS `異動者`,
    CASE WHEN `異動來源` = 'AdminDirectSql' THEN '管理工具直接修正' ELSE COALESCE(NULLIF(`異動來源`, ''), '異動來源待確認') END AS `異動來源`,
    COALESCE(NULLIF(`備註`, ''), '無備註') AS `備註`,
    `功能對照`,
    `異動時間UTC`
FROM god2_game_meta.vw_admin_change_audit_readable;

CREATE OR REPLACE VIEW god2_game_meta.vw_admin_change_audit_summary_sanitized_readable AS
SELECT
    `資料庫`,
    `資料表`,
    `欄位名稱`,
    `異動次數`,
    `第一次異動UTC`,
    `最後異動UTC`,
    CASE
        WHEN `異動來源摘要` LIKE '%AdminDirectSql%' THEN '管理工具直接修正'
        ELSE COALESCE(NULLIF(`異動來源摘要`, ''), '異動來源待確認')
    END AS `異動來源摘要`,
    `功能對照`
FROM god2_game_meta.vw_admin_change_audit_summary_readable;

CREATE OR REPLACE VIEW god2_game_meta.vw_catalog_validation_sanitized_readable AS
SELECT
    CONCAT('目錄驗證#', ROW_NUMBER() OVER (ORDER BY `驗證時間UTC`, `嚴重程度`, `資料庫`, `資料表`, `欄位名稱`)) AS `驗證紀錄`,
    CASE WHEN `驗證批次ID` IS NULL OR TRIM(CAST(`驗證批次ID` AS CHAR)) = '' THEN '驗證批次待建立' ELSE '驗證批次已記錄' END AS `驗證批次狀態`,
    `驗證時間UTC`,
    CASE WHEN `嚴重程度` = 'Error' THEN '錯誤' WHEN `嚴重程度` = 'Warning' THEN '警告' ELSE COALESCE(NULLIF(`嚴重程度`, ''), '待確認') END AS `嚴重程度`,
    COALESCE(NULLIF(`資料庫`, ''), '全資料庫') AS `資料庫`,
    COALESCE(NULLIF(`資料表`, ''), '全資料表') AS `資料表`,
    `資料ID`,
    COALESCE(NULLIF(`欄位名稱`, ''), '全欄位') AS `欄位名稱`,
    CASE
        WHEN `規則代碼` = 'canonical.column_comments' THEN '正式欄位繁體中文註解檢查'
        WHEN `規則代碼` = 'canonical.admin_trigger_coverage' THEN '管理異動觸發器覆蓋檢查'
        ELSE '驗證規則已記錄'
    END AS `驗證規則`,
    `繁中訊息`,
    `處理建議`
FROM god2_game_meta.vw_catalog_validation_readable;

CREATE OR REPLACE VIEW god2.vw_blackbox_drop_resource_meta_sanitized_readable AS
SELECT
    CONVERT('商人功能' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`商人ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`商人名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`地圖名稱`, '(', `座標X`, ',', `座標Y`, ')；', `恢復狀態`, '；', `在地化狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('點擊NPC商人，確認商店開啟、買賣、修理、分類與價格' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_merchants_sanitized_readable
UNION ALL
SELECT
    CONVERT('怪物掉落' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`掉落紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(CONCAT(`怪物名稱`, ' -> ', `物品名稱`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`掉落狀態`, '；', `正式狀態`, '；數量=', COALESCE(CAST(`最小數量` AS CHAR), '?'), '-', COALESCE(CAST(`最大數量` AS CHAR), '?'), '；機率=', COALESCE(CAST(`實際掉落率` AS CHAR), '待確認')) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('連續擊殺怪物並記錄掉落物、數量、任務/地圖/等級條件與掉落率' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_monster_drop_relationships_sanitized_readable
UNION ALL
SELECT
    CONVERT('世界資源' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`地圖ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`地圖名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`區域狀態`, '；', `資源狀態`, '；', `客戶端身分狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('進入地圖確認載入、座標、碰撞範圍、傳送目的地與客戶端顯示' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game.vw_world_client_resource_status_sanitized_readable
UNION ALL
SELECT
    CONVERT('目錄驗證' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`驗證紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(CONCAT(`資料庫`, '.', `資料表`, '.', `欄位名稱`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`嚴重程度`, '；', `驗證規則`, '；', `繁中訊息`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT('正式目錄與資料庫治理驗證' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('依處理建議修正欄位註解、觸發器覆蓋或正式資料治理問題' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game_meta.vw_catalog_validation_sanitized_readable;
