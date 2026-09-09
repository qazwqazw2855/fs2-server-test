CREATE OR REPLACE VIEW god2.vw_blackbox_combat_observations_sanitized_readable AS
SELECT
    CONCAT('戰鬥觀測#', ROW_NUMBER() OVER (ORDER BY `觀測時間UTC`, `角色ID`, `怪物模板ID`)) AS `觀測紀錄`,
    `觀測時間UTC`,
    CASE WHEN `戰鬥意圖ID` IS NULL OR TRIM(CAST(`戰鬥意圖ID` AS CHAR)) = '' THEN '戰鬥意圖待建立' ELSE '戰鬥意圖已建立' END AS `戰鬥意圖狀態`,
    CASE WHEN `連線ID` IS NULL OR TRIM(CAST(`連線ID` AS CHAR)) = '' THEN '連線待建立' ELSE '連線已建立' END AS `連線狀態`,
    `角色ID`,
    `地圖ID`,
    `地圖名稱`,
    `動作類型`,
    CASE WHEN `怪物RuntimeID` IS NULL OR TRIM(CAST(`怪物RuntimeID` AS CHAR)) = '' THEN '怪物Runtime待建立' ELSE '怪物Runtime已建立' END AS `怪物Runtime狀態`,
    `怪物模板ID`,
    `怪物名稱`,
    `怪物等級`,
    `資料庫HP`,
    `資料庫MP`,
    `資料庫物攻`,
    `資料庫物防`,
    `資料庫魔攻`,
    `資料庫魔防`,
    `攻擊前HP`,
    `本次傷害`,
    `攻擊後HP`,
    `是否死亡`,
    `是否已發獎勵`,
    `是否排程重生`,
    `服務端結果`,
    CASE WHEN `失敗原因` IS NULL OR TRIM(`失敗原因`) = '' THEN '無失敗' ELSE `失敗原因` END AS `失敗狀態`,
    `政策狀態`,
    `完成時間UTC`
FROM god2.vw_blackbox_combat_observations_readable;

CREATE OR REPLACE VIEW god2.vw_blackbox_monster_runtime_observations_sanitized_readable AS
SELECT
    CONCAT('怪物死亡觀測#', ROW_NUMBER() OVER (ORDER BY `死亡時間UTC`, `怪物模板ID`, `擊殺者角色ID`)) AS `觀測紀錄`,
    CASE WHEN `死亡ID` IS NULL OR TRIM(CAST(`死亡ID` AS CHAR)) = '' THEN '死亡紀錄待建立' ELSE '死亡紀錄已建立' END AS `死亡紀錄狀態`,
    CASE WHEN `怪物RuntimeID` IS NULL OR TRIM(CAST(`怪物RuntimeID` AS CHAR)) = '' THEN '怪物Runtime待建立' ELSE '怪物Runtime已建立' END AS `怪物Runtime狀態`,
    `怪物模板ID`,
    `怪物名稱`,
    `擊殺者角色ID`,
    `擊殺者名稱`,
    `地圖ID`,
    `地圖名稱`,
    `死亡前HP`,
    `最後傷害`,
    `獎勵證據政策`,
    `掉落證據政策`,
    `重生證據政策`,
    CASE WHEN `重生ID` IS NULL OR TRIM(CAST(`重生ID` AS CHAR)) = '' THEN '重生紀錄待建立' ELSE '重生紀錄已建立' END AS `重生紀錄狀態`,
    `重生排程狀態`,
    `預計重生時間UTC`,
    CASE WHEN `新RuntimeID` IS NULL OR TRIM(CAST(`新RuntimeID` AS CHAR)) = '' THEN '新Runtime待建立' ELSE '新Runtime已建立' END AS `新Runtime狀態`,
    `黑箱測試優先級`,
    `測試重點`,
    `死亡時間UTC`
FROM god2.vw_blackbox_monster_runtime_observations_readable;

CREATE OR REPLACE VIEW god2.vw_monster_combat_runtime_state_sanitized_readable AS
SELECT
    CONCAT('怪物Runtime#', ROW_NUMBER() OVER (ORDER BY `出生時間UTC`, `怪物模板ID`, `出生點ID`)) AS `Runtime紀錄`,
    CASE WHEN `怪物RuntimeID` IS NULL OR TRIM(CAST(`怪物RuntimeID` AS CHAR)) = '' THEN '怪物Runtime待建立' ELSE '怪物Runtime已建立' END AS `怪物Runtime狀態`,
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
    `生命週期狀態`,
    `戰鬥狀態`,
    `等級`,
    `目前HP`,
    `最大HP`,
    `攻擊力`,
    `防禦力`,
    `仇恨狀態`,
    CASE WHEN `目前目標RuntimeID` IS NULL OR TRIM(CAST(`目前目標RuntimeID` AS CHAR)) = '' THEN '目前無Runtime目標' ELSE '目標Runtime已鎖定' END AS `目前目標狀態`,
    `Runtime版本`,
    `待同步標記`,
    `數值證據政策`,
    `內容版本`,
    `功能對照`,
    `出生時間UTC`,
    `最後戰鬥時間UTC`,
    `死亡時間UTC`,
    `預計重生時間UTC`,
    `更新時間UTC`
FROM god2.vw_monster_combat_runtime_state_readable;

CREATE OR REPLACE VIEW god2.vw_monster_death_records_sanitized_readable AS
SELECT
    CONCAT('怪物死亡#', ROW_NUMBER() OVER (ORDER BY `死亡時間UTC`, `怪物模板ID`, `擊殺者角色ID`)) AS `死亡紀錄`,
    CASE WHEN `戰鬥意圖ID` IS NULL OR TRIM(CAST(`戰鬥意圖ID` AS CHAR)) = '' THEN '戰鬥意圖待建立' ELSE '戰鬥意圖已建立' END AS `戰鬥意圖狀態`,
    CASE WHEN `怪物RuntimeID` IS NULL OR TRIM(CAST(`怪物RuntimeID` AS CHAR)) = '' THEN '怪物Runtime待建立' ELSE '怪物Runtime已建立' END AS `怪物Runtime狀態`,
    `怪物模板ID`,
    `怪物名稱`,
    CASE WHEN `擊殺者RuntimeID` IS NULL OR TRIM(CAST(`擊殺者RuntimeID` AS CHAR)) = '' THEN '擊殺者Runtime待建立' ELSE '擊殺者Runtime已建立' END AS `擊殺者Runtime狀態`,
    `擊殺者角色ID`,
    `擊殺者名稱`,
    `地圖ID`,
    `地圖名稱`,
    `出生點ID`,
    `死亡前HP`,
    `最後傷害`,
    `獎勵證據政策`,
    `掉落證據政策`,
    `重生證據政策`,
    `Runtime版本前`,
    `Runtime版本後`,
    CASE WHEN `關聯ID` IS NULL OR TRIM(CAST(`關聯ID` AS CHAR)) = '' THEN '關聯待建立' ELSE '關聯已建立' END AS `關聯狀態`,
    `功能對照`,
    `死亡時間UTC`
FROM god2.vw_monster_death_records_readable;

CREATE OR REPLACE VIEW god2.vw_blackbox_monster_runtime_sanitized_readable AS
SELECT
    CONVERT('怪物戰鬥觀測' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`觀測紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`怪物名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`動作類型`, '；HP=', COALESCE(CAST(`攻擊前HP` AS CHAR), '?'), '->', COALESCE(CAST(`攻擊後HP` AS CHAR), '?'), '；傷害=', COALESCE(CAST(`本次傷害` AS CHAR), '?'), '；MP=', COALESCE(CAST(`資料庫MP` AS CHAR), '待確認')) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT('怪物HP/MP、傷害、死亡、獎勵、重生排程與服務端結果' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('攻擊怪物並記錄傷害、HP前後、怪物MP、死亡、獎勵與重生是否正確' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_blackbox_combat_observations_sanitized_readable
UNION ALL
SELECT
    CONVERT('怪物Runtime狀態' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`Runtime紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`怪物名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`生命週期狀態`, '；', `戰鬥狀態`, '；HP=', COALESCE(CAST(`目前HP` AS CHAR), '?'), '/', COALESCE(CAST(`最大HP` AS CHAR), '?'), '；', `目前目標狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('觀察怪物出生、仇恨、目標鎖定、死亡與重生前後Runtime狀態' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_monster_combat_runtime_state_sanitized_readable
UNION ALL
SELECT
    CONVERT('怪物死亡紀錄' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`死亡紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`怪物名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT('擊殺者=', COALESCE(`擊殺者名稱`, '待確認'), '；死亡前HP=', COALESCE(CAST(`死亡前HP` AS CHAR), '?'), '；最後傷害=', COALESCE(CAST(`最後傷害` AS CHAR), '?')) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('擊殺怪物後核對死亡紀錄、獎勵、掉落、重生排程與關聯狀態' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_monster_death_records_sanitized_readable;
