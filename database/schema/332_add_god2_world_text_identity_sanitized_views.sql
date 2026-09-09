CREATE OR REPLACE VIEW god2.vw_dialogs_sanitized_readable AS
SELECT
    `對話ID`,
    `NPC_ID`,
    COALESCE(NULLIF(`NPC名稱`, ''), '未綁定NPC') AS `NPC名稱`,
    CASE
        WHEN `對話文字` IS NULL OR TRIM(`對話文字`) = '' THEN '對話文字待恢復或待黑箱確認'
        WHEN LOCATE('/', `對話文字`) > 0
          OR LOCATE(CHAR(92), `對話文字`) > 0
          OR LOWER(`對話文字`) LIKE '%.csv%'
          OR LOWER(`對話文字`) LIKE '%.csvz%' THEN '客戶端來源路徑已隱藏'
        ELSE TRIM(REGEXP_REPLACE(REGEXP_REPLACE(`對話文字`, '/c[0-9]+', ''), '[[:space:]]+', ' '))
    END AS `對話文字`,
    CASE
        WHEN `恢復狀態` = 'Recovered' THEN '已恢復'
        WHEN `恢復狀態` = 'Candidate' THEN '候選'
        WHEN `恢復狀態` = 'Unknown' THEN '待確認'
        ELSE COALESCE(NULLIF(`恢復狀態`, ''), '待確認')
    END AS `恢復狀態`,
    `功能對照`
FROM god2.vw_dialogs_readable;

CREATE OR REPLACE VIEW god2.vw_localization_entries_sanitized_readable AS
SELECT
    CASE
        WHEN LOWER(`文字鍵`) LIKE '%skill%' THEN '技能文字'
        WHEN LOWER(`文字鍵`) LIKE '%item%' THEN '道具文字'
        WHEN LOWER(`文字鍵`) LIKE '%npc%' THEN 'NPC文字'
        WHEN LOWER(`文字鍵`) LIKE '%map%' THEN '地圖文字'
        WHEN LOWER(`文字鍵`) LIKE '%quest%' THEN '任務文字'
        WHEN LOWER(`文字鍵`) LIKE '%dialog%' THEN '對話文字'
        ELSE '通用文字'
    END AS `文字分類`,
    CASE
        WHEN `文字內容` IS NULL OR TRIM(`文字內容`) = '' THEN ''
        WHEN LOCATE('/', `文字內容`) > 0
          OR LOCATE(CHAR(92), `文字內容`) > 0
          OR LOWER(`文字內容`) LIKE '%.csv%'
          OR LOWER(`文字內容`) LIKE '%.csvz%'
          OR LOWER(`文字內容`) LIKE 'original/%'
          OR LOWER(`文字內容`) LIKE 'data/%' THEN ''
        ELSE TRIM(REGEXP_REPLACE(REGEXP_REPLACE(`文字內容`, '/c[0-9]+', ''), '[[:space:]]+', ' '))
    END AS `文字內容`,
    CASE
        WHEN `文字內容` IS NULL OR TRIM(`文字內容`) = '' THEN '文字內容待確認'
        WHEN LOCATE('/', `文字內容`) > 0
          OR LOCATE(CHAR(92), `文字內容`) > 0
          OR LOWER(`文字內容`) LIKE '%.csv%'
          OR LOWER(`文字內容`) LIKE '%.csvz%'
          OR LOWER(`文字內容`) LIKE 'original/%'
          OR LOWER(`文字內容`) LIKE 'data/%' THEN '客戶端來源路徑已隱藏'
        ELSE '可讀文字'
    END AS `文字內容狀態`,
    CASE
        WHEN `語言狀態` = 'Recovered' THEN '已恢復'
        WHEN `語言狀態` = 'Candidate' THEN '候選'
        WHEN `語言狀態` = 'Unknown' THEN '待確認'
        ELSE COALESCE(NULLIF(`語言狀態`, ''), '待確認')
    END AS `語言狀態`,
    `功能對照`,
    `更新時間UTC`
FROM god2.vw_localization_entries_readable;

CREATE OR REPLACE VIEW god2.vw_maps_sanitized_readable AS
SELECT
    `地圖ID`,
    CASE
        WHEN `地圖名稱` IS NULL OR TRIM(`地圖名稱`) = '' THEN '地圖名稱待確認'
        WHEN LOWER(`地圖名稱`) LIKE '%candidate%' THEN TRIM(REPLACE(REPLACE(`地圖名稱`, 'candidate', ''), 'Candidate', ''))
        ELSE `地圖名稱`
    END AS `地圖名稱`,
    CASE
        WHEN `地圖名稱` IS NULL OR TRIM(`地圖名稱`) = '' THEN '地圖名稱待黑箱確認'
        WHEN LOWER(`地圖名稱`) LIKE '%candidate%' THEN '候選名稱，需黑箱確認'
        ELSE '已命名'
    END AS `地圖名稱狀態`,
    `寬度`,
    `高度`,
    CASE
        WHEN `恢復狀態` = 'Recovered' THEN '已恢復'
        WHEN `恢復狀態` = 'Candidate' THEN '候選'
        WHEN `恢復狀態` = 'Unknown' THEN '待確認'
        ELSE COALESCE(NULLIF(`恢復狀態`, ''), '待確認')
    END AS `恢復狀態`,
    CASE
        WHEN `在地化狀態` = 'Recovered' THEN '已恢復'
        WHEN `在地化狀態` = 'Candidate' THEN '候選'
        WHEN `在地化狀態` = 'Unknown' THEN '待確認'
        ELSE COALESCE(NULLIF(`在地化狀態`, ''), '待確認')
    END AS `在地化狀態`,
    CASE
        WHEN `客戶端地圖ID` IS NOT NULL OR `客戶端區域ID` IS NOT NULL OR `資源識別` IS NOT NULL THEN '客戶端地圖已對應'
        ELSE '客戶端地圖待對應'
    END AS `客戶端對應狀態`,
    CASE
        WHEN `正式狀態` = 'Formal' THEN '正式'
        WHEN `正式狀態` = 'Candidate' THEN '候選'
        WHEN `正式狀態` = 'Unknown' THEN '待確認'
        ELSE COALESCE(NULLIF(`正式狀態`, ''), '待確認')
    END AS `正式狀態`,
    `功能對照`,
    `建立時間UTC`,
    `更新時間UTC`
FROM god2.vw_maps_readable;

CREATE OR REPLACE VIEW god2.vw_npcs_sanitized_readable AS
SELECT
    `NPC_ID`,
    COALESCE(NULLIF(`NPC名稱`, ''), 'NPC名稱待確認') AS `NPC名稱`,
    COALESCE(NULLIF(`NPC類型`, ''), 'NPC類型待確認') AS `NPC類型`,
    COALESCE(NULLIF(`互動家族`, ''), '互動功能待確認') AS `互動家族`,
    `地圖ID`,
    COALESCE(NULLIF(`地圖名稱`, ''), '地圖名稱待確認') AS `地圖名稱`,
    `座標X`,
    `座標Y`,
    `方向`,
    CASE
        WHEN `恢復狀態` = 'Recovered' THEN '已恢復'
        WHEN `恢復狀態` = 'Candidate' THEN '候選'
        WHEN `恢復狀態` = 'Unknown' THEN '待確認'
        ELSE COALESCE(NULLIF(`恢復狀態`, ''), '待確認')
    END AS `恢復狀態`,
    CASE
        WHEN `在地化狀態` = 'Recovered' THEN '已恢復'
        WHEN `在地化狀態` = 'Candidate' THEN '候選'
        WHEN `在地化狀態` = 'Unknown' THEN '待確認'
        ELSE COALESCE(NULLIF(`在地化狀態`, ''), '待確認')
    END AS `在地化狀態`,
    CASE
        WHEN `官方身分狀態` = 'Recovered' THEN '已恢復'
        WHEN `官方身分狀態` = 'Candidate' THEN '候選'
        WHEN `官方身分狀態` = 'Unknown' THEN '待確認'
        ELSE COALESCE(NULLIF(`官方身分狀態`, ''), '待確認')
    END AS `官方身分狀態`,
    CASE
        WHEN `出生狀態` = 'Spawned' THEN '已配置出生點'
        WHEN `出生狀態` = 'Missing' THEN '缺少出生點'
        WHEN `出生狀態` = 'Unknown' THEN '待確認'
        ELSE COALESCE(NULLIF(`出生狀態`, ''), '待確認')
    END AS `出生狀態`,
    CASE
        WHEN `客戶端資源類型` IS NOT NULL OR `客戶端資源序號` IS NOT NULL THEN '客戶端外觀已對應'
        ELSE '客戶端外觀待對應'
    END AS `客戶端外觀狀態`,
    `功能對照`
FROM god2.vw_npcs_readable;

CREATE OR REPLACE VIEW god2.vw_portals_sanitized_readable AS
SELECT
    `傳送門ID`,
    CASE
        WHEN `傳送門名稱` IS NULL OR TRIM(`傳送門名稱`) = '' THEN CONCAT('傳送門#', `傳送門ID`)
        WHEN LOWER(`傳送門名稱`) LIKE 'client:%' THEN CONCAT('傳送門#', `傳送門ID`)
        WHEN LOCATE('/', `傳送門名稱`) > 0 OR LOCATE(CHAR(92), `傳送門名稱`) > 0 THEN CONCAT('傳送門#', `傳送門ID`)
        ELSE `傳送門名稱`
    END AS `傳送門名稱`,
    `來源地圖ID`,
    COALESCE(NULLIF(`來源地圖名稱`, ''), '來源地圖待確認') AS `來源地圖名稱`,
    `來源X`,
    `來源Y`,
    `目標地圖ID`,
    COALESCE(NULLIF(`目標地圖名稱`, ''), '目標地圖待確認') AS `目標地圖名稱`,
    `目標X`,
    `目標Y`,
    CASE
        WHEN `恢復狀態` = 'Recovered' THEN '已恢復'
        WHEN `恢復狀態` = 'Candidate' THEN '候選'
        WHEN `恢復狀態` = 'Unknown' THEN '待確認'
        ELSE COALESCE(NULLIF(`恢復狀態`, ''), '待確認')
    END AS `恢復狀態`,
    `功能對照`
FROM god2.vw_portals_readable;

CREATE OR REPLACE VIEW god2.vw_blackbox_world_text_identity_observations_readable AS
SELECT
    'NPC對話' AS `觀察類型`,
    CAST(`對話ID` AS CHAR) AS `目標ID`,
    COALESCE(`NPC名稱`, '未綁定NPC') AS `目標名稱`,
    `對話文字` AS `目前狀態`,
    `功能對照`,
    '進遊戲點擊NPC並記錄實際對話文字、選項與任務觸發' AS `黑箱測試方式`
FROM god2.vw_dialogs_sanitized_readable
WHERE `對話文字` IN ('對話文字待恢復或待黑箱確認', '客戶端來源路徑已隱藏')
UNION ALL
SELECT
    '在地化文字' AS `觀察類型`,
    `文字分類` AS `目標ID`,
    `文字分類` AS `目標名稱`,
    `文字內容狀態` AS `目前狀態`,
    `功能對照`,
    '在客戶端對應畫面讀取實際繁體文字，回填可讀文字而不是來源檔名' AS `黑箱測試方式`
FROM god2.vw_localization_entries_sanitized_readable
WHERE `文字內容狀態` <> '可讀文字'
UNION ALL
SELECT
    '地圖名稱' AS `觀察類型`,
    CAST(`地圖ID` AS CHAR) AS `目標ID`,
    `地圖名稱` AS `目標名稱`,
    `地圖名稱狀態` AS `目前狀態`,
    `功能對照`,
    '進入地圖後比對小地圖、場景名稱、傳送來源與目標是否一致' AS `黑箱測試方式`
FROM god2.vw_maps_sanitized_readable
WHERE `地圖名稱狀態` <> '已命名' OR `客戶端對應狀態` <> '客戶端地圖已對應'
UNION ALL
SELECT
    'NPC外觀與出生點' AS `觀察類型`,
    CAST(`NPC_ID` AS CHAR) AS `目標ID`,
    `NPC名稱` AS `目標名稱`,
    CONCAT(`出生狀態`, '；', `客戶端外觀狀態`) AS `目前狀態`,
    `功能對照`,
    '進遊戲到指定地圖座標確認NPC是否存在、外觀是否正確、點擊後功能是否吻合' AS `黑箱測試方式`
FROM god2.vw_npcs_sanitized_readable
WHERE `出生狀態` <> '已配置出生點' OR `客戶端外觀狀態` <> '客戶端外觀已對應'
UNION ALL
SELECT
    '傳送門路線' AS `觀察類型`,
    CAST(`傳送門ID` AS CHAR) AS `目標ID`,
    `傳送門名稱` AS `目標名稱`,
    CONCAT(`來源地圖名稱`, ' -> ', `目標地圖名稱`) AS `目前狀態`,
    `功能對照`,
    '踩傳送點或點擊入口，確認來源座標、目標地圖與落點是否一致' AS `黑箱測試方式`
FROM god2.vw_portals_sanitized_readable;
