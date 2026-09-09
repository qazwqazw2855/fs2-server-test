CREATE OR REPLACE VIEW god2.vw_battle_action_results_sanitized_readable AS
SELECT
    CONCAT('戰鬥行動#', ROW_NUMBER() OVER (ORDER BY `建立時間UTC`, `戰鬥場次ID`, `回合`, `執行順序`)) AS `行動紀錄`,
    `戰鬥場次ID`,
    `回合`,
    `執行順序`,
    `參戰者ID`,
    CASE
        WHEN `結果代碼` IS NULL OR TRIM(`結果代碼`) = '' THEN '結果待確認'
        WHEN `結果代碼` IN ('Success', 'Succeeded', 'OK') THEN '成功'
        WHEN `結果代碼` IN ('Failed', 'Failure') THEN '失敗'
        ELSE `結果代碼`
    END AS `結果`,
    `功能對照`,
    `建立時間UTC`
FROM god2.vw_battle_action_results_readable;

CREATE OR REPLACE VIEW god2.vw_battle_event_outbox_sanitized_readable AS
SELECT
    CONCAT('戰鬥事件#', ROW_NUMBER() OVER (ORDER BY `建立時間UTC`, `戰鬥場次ID`, `事件序號`)) AS `事件紀錄`,
    `戰鬥場次ID`,
    `事件序號`,
    `事件類型`,
    `回合`,
    CASE
        WHEN `行動執行ID` IS NULL OR TRIM(CAST(`行動執行ID` AS CHAR)) = '' THEN '未綁定行動'
        ELSE '已綁定行動'
    END AS `行動綁定狀態`,
    `派發狀態`,
    `重試次數`,
    CASE
        WHEN `最後失敗代碼` IS NULL OR TRIM(`最後失敗代碼`) = '' THEN '無失敗'
        ELSE `最後失敗代碼`
    END AS `最後失敗狀態`,
    `功能對照`,
    `建立時間UTC`,
    `更新時間UTC`
FROM god2.vw_battle_event_outbox_readable;

CREATE OR REPLACE VIEW god2.vw_battle_instances_sanitized_readable AS
SELECT
    CONCAT('戰鬥場次#', ROW_NUMBER() OVER (ORDER BY `建立時間UTC`, `戰鬥場次ID`)) AS `戰鬥紀錄`,
    `戰鬥場次ID`,
    CASE
        WHEN `戰鬥請求ID` IS NULL OR TRIM(CAST(`戰鬥請求ID` AS CHAR)) = '' THEN '戰鬥請求待建立'
        ELSE '戰鬥請求已建立'
    END AS `戰鬥請求狀態`,
    CASE
        WHEN `世界實例ID` IS NULL OR TRIM(CAST(`世界實例ID` AS CHAR)) = '' THEN '世界實例待建立'
        ELSE '世界實例已建立'
    END AS `世界實例狀態`,
    `來源地圖ID`,
    COALESCE(NULLIF(`來源地圖名稱`, ''), '來源地圖待確認') AS `來源地圖名稱`,
    `遭遇定義ID`,
    `戰鬥狀態`,
    `戰鬥階段`,
    `目前回合`,
    `目前解析序號`,
    `戰鬥版本`,
    COALESCE(NULLIF(`勝利方`, ''), '尚未決定') AS `勝利方`,
    COALESCE(NULLIF(`結束原因`, ''), '尚未結束') AS `結束原因`,
    `獎勵狀態`,
    `恢復狀態`,
    `功能對照`,
    `建立時間UTC`,
    `更新時間UTC`,
    `完成時間UTC`
FROM god2.vw_battle_instances_readable;

CREATE OR REPLACE VIEW god2.vw_battle_participants_sanitized_readable AS
SELECT
    `戰鬥場次ID`,
    `參戰者ID`,
    `參戰者類型`,
    `陣營`,
    `站位`,
    `角色ID`,
    `角色名稱`,
    `怪物模板ID`,
    `怪物名稱`,
    CASE
        WHEN `來源RuntimeID` IS NULL OR TRIM(CAST(`來源RuntimeID` AS CHAR)) = '' THEN '來源Runtime待建立'
        ELSE '來源Runtime已建立'
    END AS `來源Runtime狀態`,
    CASE
        WHEN `戰鬥RuntimeID` IS NULL OR TRIM(CAST(`戰鬥RuntimeID` AS CHAR)) = '' THEN '戰鬥Runtime待建立'
        ELSE '戰鬥Runtime已建立'
    END AS `戰鬥Runtime狀態`,
    `目前HP`,
    `存活狀態`,
    `連線狀態`,
    `Runtime版本`,
    `功能對照`,
    `更新時間UTC`
FROM god2.vw_battle_participants_readable;

CREATE OR REPLACE VIEW god2.vw_battle_status_instances_sanitized_readable AS
SELECT
    CONCAT('戰鬥狀態#', ROW_NUMBER() OVER (ORDER BY `套用時間UTC`, `戰鬥場次ID`, `目標參戰ID`, `狀態ID`)) AS `狀態紀錄`,
    `狀態ID`,
    `狀態名稱`,
    `戰鬥場次ID`,
    `目標參戰ID`,
    CASE
        WHEN `來源參戰ID` IS NULL THEN '來源參戰者待確認'
        ELSE '來源參戰者已記錄'
    END AS `來源參戰狀態`,
    CASE
        WHEN `來源技能執行ID` IS NULL OR TRIM(CAST(`來源技能執行ID` AS CHAR)) = '' THEN '非技能來源或待確認'
        ELSE '技能來源已記錄'
    END AS `來源技能狀態`,
    `層數`,
    `最大層數`,
    `套用回合`,
    `最後刷新回合`,
    `到期回合後`,
    `剩餘回合`,
    `生命週期狀態`,
    `觸發游標`,
    `Runtime版本`,
    `證據政策`,
    `功能對照`,
    `套用時間UTC`,
    `更新時間UTC`,
    `移除時間UTC`,
    COALESCE(NULLIF(`移除原因`, ''), '尚未移除') AS `移除原因`
FROM god2.vw_battle_status_instances_readable;

CREATE OR REPLACE VIEW god2.vw_battle_status_trigger_results_sanitized_readable AS
SELECT
    CONCAT('狀態觸發#', ROW_NUMBER() OVER (ORDER BY `完成時間UTC`, `戰鬥場次ID`, `觸發順序`, `執行順序`)) AS `觸發紀錄`,
    `狀態ID`,
    `狀態名稱`,
    `戰鬥場次ID`,
    `觸發順序`,
    `執行順序`,
    `觸發狀態`,
    `結果`,
    CASE
        WHEN `失敗代碼` IS NULL OR TRIM(`失敗代碼`) = '' THEN '無失敗'
        ELSE `失敗代碼`
    END AS `失敗狀態`,
    `傷害`,
    `治療`,
    `HP前`,
    `HP後`,
    `恢復狀態`,
    `功能對照`,
    `完成時間UTC`
FROM god2.vw_battle_status_trigger_results_readable;

CREATE OR REPLACE VIEW god2.vw_blackbox_battle_runtime_sanitized_observations_readable AS
SELECT
    CONVERT('戰鬥場次' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`戰鬥紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`來源地圖名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`戰鬥狀態`, '；', `戰鬥階段`, '；回合=', `目前回合`, '；勝利方=', `勝利方`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('進入戰鬥後確認場次建立、回合推進、結束原因、獎勵與恢復狀態' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_battle_instances_sanitized_readable
UNION ALL
SELECT
    CONVERT('參戰者' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`參戰者ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(COALESCE(`角色名稱`, `怪物名稱`, '參戰者待確認') USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`參戰者類型`, '；', `陣營`, '；HP=', COALESCE(CAST(`目前HP` AS CHAR), '待確認'), '；', `存活狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('戰鬥中核對玩家、怪物、站位、HP、死亡與連線狀態' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_battle_participants_sanitized_readable
UNION ALL
SELECT
    CONVERT('戰鬥事件派發' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`事件紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`事件類型` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`派發狀態`, '；重試=', `重試次數`, '；', `最後失敗狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('確認戰鬥事件有送到 client，失敗時記錄重試與最後失敗狀態' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_battle_event_outbox_sanitized_readable
UNION ALL
SELECT
    CONVERT('戰鬥狀態效果' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`狀態紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`狀態名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT('層數=', `層數`, '/', `最大層數`, '；剩餘回合=', `剩餘回合`, '；', `生命週期狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('施放技能或被動狀態後核對層數、回合、到期與移除原因' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_battle_status_instances_sanitized_readable
UNION ALL
SELECT
    CONVERT('狀態觸發結果' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`觸發紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`狀態名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`結果`, '；傷害=', COALESCE(CAST(`傷害` AS CHAR), '0'), '；治療=', COALESCE(CAST(`治療` AS CHAR), '0'), '；HP=', COALESCE(CAST(`HP前` AS CHAR), '?'), '->', COALESCE(CAST(`HP後` AS CHAR), '?')) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('測毒、回血、持續傷害或被動觸發，核對傷害/治療與HP前後值' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_battle_status_trigger_results_sanitized_readable;
