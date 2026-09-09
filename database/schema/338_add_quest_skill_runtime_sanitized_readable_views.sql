CREATE OR REPLACE VIEW god2.vw_quest_operation_idempotency_sanitized_readable AS
SELECT
    CONCAT('任務操作#', ROW_NUMBER() OVER (ORDER BY `建立時間UTC`, `角色ID`, `操作`)) AS `操作紀錄`,
    `操作`,
    CASE
        WHEN `任務意圖ID` IS NULL OR TRIM(CAST(`任務意圖ID` AS CHAR)) = '' THEN '任務意圖待建立'
        ELSE '任務意圖已建立'
    END AS `任務意圖狀態`,
    CASE
        WHEN `任務實例ID` IS NULL OR TRIM(CAST(`任務實例ID` AS CHAR)) = '' THEN '任務實例待建立'
        ELSE '任務實例已建立'
    END AS `任務實例狀態`,
    `角色ID`,
    `角色名稱`,
    CASE
        WHEN `結果代碼` IS NULL OR TRIM(`結果代碼`) = '' THEN '結果待確認'
        WHEN `結果代碼` IN ('Success', 'Succeeded', 'OK') THEN '成功'
        WHEN `結果代碼` IN ('Failed', 'Failure') THEN '失敗'
        ELSE `結果代碼`
    END AS `結果`,
    `重放保護狀態`,
    `功能對照`,
    `建立時間UTC`,
    `完成時間UTC`
FROM god2.vw_quest_operation_idempotency_readable;

CREATE OR REPLACE VIEW god2.vw_quest_progress_mutations_sanitized_readable AS
SELECT
    CONCAT('任務進度異動#', ROW_NUMBER() OVER (ORDER BY `建立時間UTC`, `角色ID`, `任務實例ID`, `目標定義ID`)) AS `進度異動紀錄`,
    CASE
        WHEN `語意事件ID` IS NULL OR TRIM(CAST(`語意事件ID` AS CHAR)) = '' THEN '語意事件待建立'
        ELSE '語意事件已建立'
    END AS `語意事件狀態`,
    `任務實例ID`,
    `目標定義ID`,
    `角色ID`,
    `角色名稱`,
    `異動前進度`,
    `進度變化`,
    `異動後進度`,
    `預期任務版本`,
    `預期目標版本`,
    CASE
        WHEN `結果代碼` IS NULL OR TRIM(`結果代碼`) = '' THEN '結果待確認'
        WHEN `結果代碼` IN ('Success', 'Succeeded', 'OK') THEN '成功'
        WHEN `結果代碼` IN ('Failed', 'Failure') THEN '失敗'
        ELSE `結果代碼`
    END AS `結果`,
    CASE
        WHEN `失敗代碼` IS NULL OR TRIM(`失敗代碼`) = '' THEN '無失敗'
        ELSE `失敗代碼`
    END AS `失敗狀態`,
    `功能對照`,
    `建立時間UTC`
FROM god2.vw_quest_progress_mutations_readable;

CREATE OR REPLACE VIEW god2.vw_quest_reward_finalization_sanitized_readable AS
SELECT
    CONCAT('任務獎勵#', ROW_NUMBER() OVER (ORDER BY `建立時間UTC`, `角色ID`, `任務實例ID`)) AS `獎勵紀錄`,
    CASE
        WHEN `獎勵計畫ID` IS NULL OR TRIM(CAST(`獎勵計畫ID` AS CHAR)) = '' THEN '獎勵計畫待建立'
        ELSE '獎勵計畫已建立'
    END AS `獎勵計畫狀態`,
    CASE
        WHEN `完成計畫ID` IS NULL OR TRIM(CAST(`完成計畫ID` AS CHAR)) = '' THEN '完成計畫待建立'
        ELSE '完成計畫已建立'
    END AS `完成計畫狀態`,
    `任務實例ID`,
    `角色ID`,
    `角色名稱`,
    `獎勵狀態`,
    CASE
        WHEN `結果代碼` IS NULL OR TRIM(`結果代碼`) = '' THEN '結果待確認'
        WHEN `結果代碼` IN ('Success', 'Succeeded', 'OK') THEN '成功'
        WHEN `結果代碼` IN ('Failed', 'Failure') THEN '失敗'
        ELSE `結果代碼`
    END AS `結果`,
    CASE
        WHEN `失敗代碼` IS NULL OR TRIM(`失敗代碼`) = '' THEN '無失敗'
        ELSE `失敗代碼`
    END AS `失敗狀態`,
    `功能對照`,
    `建立時間UTC`,
    `完成時間UTC`
FROM god2.vw_quest_reward_finalization_readable;

CREATE OR REPLACE VIEW god2.vw_skill_cost_reservations_sanitized_readable AS
SELECT
    CONCAT('技能資源預扣#', ROW_NUMBER() OVER (ORDER BY `建立時間UTC`, `參戰者ID`, `技能ID`)) AS `預扣紀錄`,
    CASE
        WHEN `技能執行ID` IS NULL OR TRIM(CAST(`技能執行ID` AS CHAR)) = '' THEN '技能執行待建立'
        ELSE '技能執行已建立'
    END AS `技能執行狀態`,
    `參戰者ID`,
    `技能ID`,
    `技能名稱`,
    CASE
        WHEN `資源類型` = 'MP' THEN '魔力'
        WHEN `資源類型` = 'HP' THEN '生命'
        WHEN `資源類型` = 'SP' THEN '特殊資源'
        ELSE COALESCE(NULLIF(`資源類型`, ''), '資源類型待確認')
    END AS `資源類型`,
    `數量`,
    `預扣狀態`,
    `Runtime版本前`,
    `功能對照`,
    `建立時間UTC`,
    `提交時間UTC`,
    `釋放時間UTC`
FROM god2.vw_skill_cost_reservations_readable;

CREATE OR REPLACE VIEW god2.vw_skill_effect_executions_sanitized_readable AS
SELECT
    CONCAT('技能效果#', ROW_NUMBER() OVER (ORDER BY `完成時間UTC`, `戰鬥場次ID`, `回合`, `技能ID`, `效果順序`, `目標順序`)) AS `效果紀錄`,
    CASE
        WHEN `技能執行ID` IS NULL OR TRIM(CAST(`技能執行ID` AS CHAR)) = '' THEN '技能執行待建立'
        ELSE '技能執行已建立'
    END AS `技能執行狀態`,
    `戰鬥場次ID`,
    `回合`,
    `技能ID`,
    `技能名稱`,
    `效果定義ID`,
    `效果順序`,
    `目標順序`,
    `目標參戰ID`,
    `效果類型`,
    `效果狀態`,
    `HP前`,
    `傷害`,
    `治療`,
    `HP後`,
    `Runtime版本前`,
    `Runtime版本後`,
    CASE
        WHEN `失敗代碼` IS NULL OR TRIM(`失敗代碼`) = '' THEN '無失敗'
        ELSE `失敗代碼`
    END AS `失敗狀態`,
    `功能對照`,
    `完成時間UTC`
FROM god2.vw_skill_effect_executions_readable;

CREATE OR REPLACE VIEW god2.vw_blackbox_quest_skill_runtime_sanitized_observations_readable AS
SELECT
    CONVERT('任務操作防重放' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`操作紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`角色名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`操作`, '；', `結果`, '；', `重放保護狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('重複提交接任務/交任務/更新目標，確認服務端不重複套用' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_quest_operation_idempotency_sanitized_readable
UNION ALL
SELECT
    CONVERT('任務進度異動' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`進度異動紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`角色名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT('進度=', `異動前進度`, '+', `進度變化`, '->', `異動後進度`, '；', `結果`, '；', `失敗狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('打怪、收集、對話或抵達地點後確認任務目標進度是否正確更新' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_quest_progress_mutations_sanitized_readable
UNION ALL
SELECT
    CONVERT('任務獎勵結算' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`獎勵紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`角色名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`獎勵狀態`, '；', `結果`, '；', `失敗狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('交任務後核對經驗、金錢、道具、狀態是否只發一次' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_quest_reward_finalization_sanitized_readable
UNION ALL
SELECT
    CONVERT('技能資源預扣' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`預扣紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`技能名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`資源類型`, '-', `數量`, '；', `預扣狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('施放技能時核對MP/HP消耗、提交與失敗釋放是否正確' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_skill_cost_reservations_sanitized_readable
UNION ALL
SELECT
    CONVERT('技能效果執行' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`效果紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`技能名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`效果類型`, '；', `效果狀態`, '；傷害=', COALESCE(CAST(`傷害` AS CHAR), '0'), '；治療=', COALESCE(CAST(`治療` AS CHAR), '0'), '；HP=', COALESCE(CAST(`HP前` AS CHAR), '?'), '->', COALESCE(CAST(`HP後` AS CHAR), '?')) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('測物攻、魔攻、五行加減傷、怪物MP技能施放與防禦1.5倍防禦後的HP前後值' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2.vw_skill_effect_executions_sanitized_readable;
