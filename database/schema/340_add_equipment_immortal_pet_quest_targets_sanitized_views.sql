CREATE OR REPLACE VIEW god2_game.vw_blackbox_equipment_test_targets_sanitized_readable AS
SELECT
    `物品ID`,
    `客戶端物品ID`,
    CASE WHEN `服務端代碼` IS NULL OR TRIM(`服務端代碼`) = '' THEN '服務端代碼待建立' ELSE '服務端代碼已建立' END AS `服務端對應狀態`,
    `裝備名稱`,
    `物品分類`,
    `物品家族`,
    `裝備類型`,
    `裝備欄位`,
    `需求等級`,
    `需求腕力`,
    `需求智力`,
    `HP加成`,
    `MP加成`,
    `腕力加成`,
    `體力加成`,
    `智力加成`,
    `速度加成`,
    `物攻加成`,
    `物防加成`,
    `魔攻加成`,
    `魔防加成`,
    `金加成`,
    `木加成`,
    `水加成`,
    `火加成`,
    `土加成`,
    `耐久`,
    `最高強化`,
    `孔數`,
    `黑箱測試優先級`,
    `測試重點`,
    `服務端狀態`,
    '裝備穿脫：需求、能力加成、五行、耐久、強化、孔數與角色面板變化' AS `功能對照`
FROM god2_game.vw_blackbox_equipment_test_targets_readable;

CREATE OR REPLACE VIEW god2_game.vw_blackbox_immortal_test_targets_sanitized_readable AS
SELECT
    `神仙模板ID`,
    CASE WHEN `服務端代碼` IS NULL OR TRIM(`服務端代碼`) = '' THEN '服務端代碼待建立' ELSE '服務端代碼已建立' END AS `服務端對應狀態`,
    `神仙名稱`,
    `神仙系別`,
    `對應職業`,
    `神仙類型`,
    `初始等級`,
    `初始品階`,
    `基礎最大HP`,
    `基礎最大MP`,
    `腕力`,
    `體力`,
    `智力`,
    `速度`,
    `金`,
    `木`,
    `水`,
    `火`,
    `土`,
    `物理攻擊`,
    `物理防禦`,
    `法術攻擊`,
    `法術防禦`,
    `黑箱測試優先級`,
    `測試重點`,
    `服務端狀態`,
    '神仙系統：取得、職業對應、品階、屬性、五行與戰鬥能力' AS `功能對照`
FROM god2_game.vw_blackbox_immortal_test_targets_readable;

CREATE OR REPLACE VIEW god2_game.vw_blackbox_pet_test_targets_sanitized_readable AS
SELECT
    `戰寵模板ID`,
    CASE WHEN `服務端代碼` IS NULL OR TRIM(`服務端代碼`) = '' THEN '服務端代碼待建立' ELSE '服務端代碼已建立' END AS `服務端對應狀態`,
    `戰寵名稱`,
    `戰寵族系`,
    `戰寵族群`,
    `五行`,
    `野生怪物ID`,
    `野生怪物名稱`,
    `捕捉轉換規則`,
    `玩家取得等級`,
    `基礎最大HP`,
    `基礎最大MP`,
    `腕力`,
    `體力`,
    `智力`,
    `速度`,
    `最大技能格`,
    `已登錄技能數量`,
    `技能摘要`,
    `20級技能`,
    `40級技能`,
    `60級技能`,
    `黑箱測試優先級`,
    `測試重點`,
    `服務端狀態`,
    '戰寵系統：捕捉、轉換、等級、HP/MP、能力、技能格與等級技能' AS `功能對照`
FROM god2_game.vw_blackbox_pet_test_targets_readable;

CREATE OR REPLACE VIEW god2_game.vw_blackbox_quest_flow_test_targets_sanitized_readable AS
SELECT
    `任務編號`,
    CASE WHEN `服務端代碼` IS NULL OR TRIM(`服務端代碼`) = '' THEN '服務端代碼待建立' ELSE '服務端代碼已建立' END AS `服務端對應狀態`,
    `任務名稱`,
    `任務類型`,
    `最低等級`,
    `最高等級`,
    `接任務NPC編號`,
    `接任務NPC名稱`,
    `回報NPC編號`,
    `回報NPC名稱`,
    `目標數量`,
    `目標摘要`,
    `獎勵數量`,
    `獎勵摘要`,
    `重複設定`,
    `重複間隔秒數`,
    `黑箱測試優先級`,
    `測試重點`,
    `服務端狀態`,
    '任務流程：接取、目標進度、回報、獎勵、重複設定與等級限制' AS `功能對照`
FROM god2_game.vw_blackbox_quest_flow_test_targets_readable;

CREATE OR REPLACE VIEW god2_game.vw_blackbox_growth_targets_sanitized_readable AS
SELECT
    CONVERT('裝備測試' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`物品ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`裝備名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`裝備欄位`, '；需求等級=', COALESCE(CAST(`需求等級` AS CHAR), '0'), '；物攻+', COALESCE(CAST(`物攻加成` AS CHAR), '0'), '；物防+', COALESCE(CAST(`物防加成` AS CHAR), '0'), '；魔攻+', COALESCE(CAST(`魔攻加成` AS CHAR), '0'), '；魔防+', COALESCE(CAST(`魔防加成` AS CHAR), '0')) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('穿脫裝備後核對角色面板、戰鬥傷害、防禦1.5倍、耐久與強化孔數' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game.vw_blackbox_equipment_test_targets_sanitized_readable
UNION ALL
SELECT
    CONVERT('神仙測試' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`神仙模板ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`神仙名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`神仙系別`, '；', `對應職業`, '；HP=', `基礎最大HP`, '；MP=', `基礎最大MP`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('取得或配置神仙後核對職業限制、品階、屬性、五行與戰鬥能力' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game.vw_blackbox_immortal_test_targets_sanitized_readable
UNION ALL
SELECT
    CONVERT('戰寵測試' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`戰寵模板ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`戰寵名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`戰寵族系`, '；五行=', `五行`, '；HP=', `基礎最大HP`, '；MP=', `基礎最大MP`, '；技能數=', `已登錄技能數量`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('捕捉或配置戰寵後核對能力、MP、技能格、20/40/60級技能與戰鬥表現' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game.vw_blackbox_pet_test_targets_sanitized_readable
UNION ALL
SELECT
    CONVERT('任務流程測試' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`任務編號` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`任務名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`任務類型`, '；NPC=', COALESCE(`接任務NPC名稱`, '待確認'), '->', COALESCE(`回報NPC名稱`, '待確認'), '；目標=', `目標數量`, '；獎勵=', `獎勵數量`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('接任務、推進目標、回報任務，核對NPC、對話、進度、防重放與獎勵' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game.vw_blackbox_quest_flow_test_targets_sanitized_readable;
