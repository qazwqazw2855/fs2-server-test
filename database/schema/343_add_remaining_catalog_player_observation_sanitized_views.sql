CREATE OR REPLACE VIEW god2_game.vw_monsters_sanitized_readable AS
SELECT
    `怪物編號`,
    CASE WHEN `服務端代碼` IS NULL OR TRIM(`服務端代碼`) = '' THEN '服務端代碼待建立' ELSE '服務端代碼已建立' END AS `服務端對應狀態`,
    `怪物名稱`,
    `怪物類型`,
    `等級`,
    `最大HP`,
    `最大MP`,
    `力量`,
    `體力`,
    `智力`,
    `速度`,
    `金屬性`,
    `木屬性`,
    `水屬性`,
    `火屬性`,
    `土屬性`,
    `物理攻擊`,
    `物理防禦`,
    `法術攻擊`,
    `法術防禦`,
    `經驗獎勵`,
    `金錢獎勵`,
    `是否首領`,
    `是否菁英`,
    `攻擊模式`,
    `服務端狀態`,
    '怪物目錄：HP/MP、攻防、五行、獎勵、首領/菁英與攻擊模式' AS `功能對照`
FROM god2_game.vw_monsters_readable;

CREATE OR REPLACE VIEW god2_game.vw_pet_templates_sanitized_readable AS
SELECT
    `戰寵模板ID`,
    `戰寵名稱`,
    `戰寵族系`,
    `戰寵族群`,
    `五行`,
    `玩家初始等級`,
    CASE WHEN `野生怪物來源` IS NULL OR TRIM(CAST(`野生怪物來源` AS CHAR)) = '' THEN '未綁定野生怪物' ELSE '已綁定野生怪物' END AS `野生怪物來源狀態`,
    `野生遭遇等級`,
    `20級技能`,
    `40級技能`,
    `60級技能`,
    `捕捉轉換規則`,
    `基礎最大HP`,
    `基礎最大MP`,
    `基礎力量`,
    `基礎體力`,
    `基礎智力`,
    `基礎速度`,
    `最大技能格`,
    `正式狀態`,
    '戰寵模板：捕捉來源、五行、等級技能、HP/MP與基礎能力' AS `功能對照`
FROM god2_game.vw_pet_templates_readable;

CREATE OR REPLACE VIEW god2_game.vw_quests_sanitized_readable AS
SELECT
    `任務編號`,
    CASE WHEN `服務端代碼` IS NULL OR TRIM(`服務端代碼`) = '' THEN '服務端代碼待建立' ELSE '服務端代碼已建立' END AS `服務端對應狀態`,
    `任務名稱`,
    COALESCE(NULLIF(`任務類型`, ''), '任務類型待確認') AS `任務類型`,
    `起始NPC`,
    COALESCE(NULLIF(`起始NPC名稱`, ''), '起始NPC待確認') AS `起始NPC名稱`,
    `結束NPC`,
    COALESCE(NULLIF(`結束NPC名稱`, ''), '結束NPC待確認') AS `結束NPC名稱`,
    `最低等級`,
    `最高等級`,
    CASE WHEN `可重複` = 1 THEN '可重複' WHEN `可重複` = 0 THEN '不可重複' ELSE '重複規則待確認' END AS `重複規則`,
    `重複間隔秒數`,
    TRIM(REGEXP_REPLACE(REGEXP_REPLACE(COALESCE(`任務描述`, ''), '/c[0-9]+', ''), '[[:space:]]+', ' ')) AS `任務描述`,
    TRIM(REGEXP_REPLACE(REGEXP_REPLACE(COALESCE(`完成文字`, ''), '/c[0-9]+', ''), '[[:space:]]+', ' ')) AS `完成文字`,
    `服務端狀態`,
    '任務目錄：接取NPC、回報NPC、等級限制、重複規則、描述與完成文字' AS `功能對照`
FROM god2_game.vw_quests_readable;

CREATE OR REPLACE VIEW god2_game.vw_blackbox_combat_formula_targets_sanitized_readable AS
SELECT
    `怪物ID`,
    CASE WHEN `服務端代碼` IS NULL OR TRIM(`服務端代碼`) = '' THEN '服務端代碼待建立' ELSE '服務端代碼已建立' END AS `服務端對應狀態`,
    `怪物名稱`,
    `等級`,
    `最大HP`,
    `最大MP`,
    `物理攻擊`,
    `物理防禦`,
    `法術攻擊`,
    `法術防禦`,
    `金`,
    `木`,
    `水`,
    `火`,
    `土`,
    `經驗`,
    `金錢`,
    CASE WHEN `數值來源` IS NULL OR TRIM(`數值來源`) = '' THEN '數值來源待確認' WHEN `數值來源` LIKE '%official%' THEN '官方證據' ELSE '服務端設計或推導' END AS `數值來源狀態`,
    `數值政策`,
    `出生點數`,
    `已啟用出生點數`,
    `第一個地圖ID`,
    `第一個地圖名稱`,
    `第一個X`,
    `第一個Y`,
    `掉落資料數`,
    `已啟用掉落數`,
    `怪物技能數`,
    `怪物技能摘要`,
    `MP測試重點`,
    `黑箱測試優先級`,
    `測試重點`,
    `服務端狀態`,
    '戰鬥公式黑箱：怪物HP/MP、物攻物防、魔攻魔防、五行、防禦1.5倍與獎勵' AS `功能對照`
FROM god2_game.vw_blackbox_combat_formula_test_targets_readable;

CREATE OR REPLACE VIEW god2_game.vw_blackbox_monster_test_targets_sanitized_readable AS
SELECT
    `怪物ID`,
    `怪物名稱`,
    `等級`,
    `HP`,
    `MP`,
    `物攻`,
    `物防`,
    `魔攻`,
    `魔防`,
    `經驗`,
    `金錢`,
    CASE WHEN `數值來源` IS NULL OR TRIM(`數值來源`) = '' THEN '數值來源待確認' WHEN `數值來源` LIKE '%official%' THEN '官方證據' ELSE '服務端設計或推導' END AS `數值來源狀態`,
    `數值政策`,
    `怪物啟用`,
    `出生點數`,
    `已啟用出生點數`,
    `第一個地圖ID`,
    `第一個地圖名稱`,
    `第一個X`,
    `第一個Y`,
    `掉落資料數`,
    `已啟用掉落數`,
    '怪物黑箱：出生、HP/MP、攻防、獎勵、掉落與服務端啟用狀態' AS `功能對照`
FROM god2_game.vw_blackbox_monster_test_targets_readable;

CREATE OR REPLACE VIEW god2_game.vw_blackbox_portal_test_targets_sanitized_readable AS
SELECT
    `傳送點ID`,
    `傳送點名稱`,
    `來源地圖ID`,
    `來源地圖`,
    `來源X`,
    `來源Y`,
    `觸發半徑`,
    `目標地圖ID`,
    `目標地圖`,
    `目標X`,
    `目標Y`,
    COALESCE(NULLIF(`目標方向`, ''), '目標方向待確認') AS `目標方向`,
    `需求等級`,
    CASE WHEN `需求任務ID` IS NULL THEN '無任務需求' ELSE '有任務需求' END AS `任務需求狀態`,
    CASE WHEN `消耗物品ID` IS NULL THEN '無消耗物品' ELSE '需要消耗物品' END AS `消耗物品狀態`,
    `消耗數量`,
    IF(`已啟用` = 1, '已啟用', '未啟用') AS `啟用狀態`,
    `黑箱優先級`,
    '傳送門黑箱：來源座標、觸發半徑、目標座標、等級/任務/物品需求與啟用狀態' AS `功能對照`
FROM god2_game.vw_blackbox_portal_test_targets_readable;

CREATE OR REPLACE VIEW god2_player.vw_blackbox_inventory_operation_observations_sanitized_readable AS
SELECT
    `角色ID`,
    `操作類型`,
    CASE WHEN `來源` LIKE '%官方實機%' THEN '官方實機證據' ELSE COALESCE(NULLIF(`來源`, ''), '來源待確認') END AS `來源`,
    `物品模板ID`,
    `物品名稱`,
    `操作前數量`,
    `操作後數量`,
    `貨幣類型`,
    `操作前貨幣`,
    `操作後貨幣`,
    `操作前背包版本`,
    `操作後背包版本`,
    `結果`,
    CASE WHEN `失敗代碼` IS NULL OR TRIM(`失敗代碼`) = '' THEN '無失敗' ELSE `失敗代碼` END AS `失敗狀態`,
    `功能對照`,
    `完成時間UTC`
FROM god2_player.vw_blackbox_inventory_operation_observations_readable;

CREATE OR REPLACE VIEW god2_player.vw_blackbox_social_operation_observations_sanitized_readable AS
SELECT
    `操作者角色ID`,
    `操作者名稱`,
    `操作類型`,
    CASE WHEN `結果代碼` IS NULL OR TRIM(`結果代碼`) = '' THEN '結果待確認' ELSE `結果代碼` END AS `結果`,
    `相關角色ID`,
    `相關角色名稱`,
    `是否異動`,
    `封鎖判定`,
    `功能對照`,
    `黑箱測試優先級`,
    `測試重點`,
    `建立時間UTC`
FROM god2_player.vw_blackbox_social_operation_observations_readable;

CREATE OR REPLACE VIEW god2_player.vw_blackbox_world_interaction_observations_sanitized_readable AS
SELECT
    `角色ID`,
    `角色名稱`,
    `互動類型`,
    `服務端處理器`,
    `功能對照`,
    `來源地圖ID`,
    `來源地圖名稱`,
    `來源座標`,
    `目標地圖ID`,
    `目標地圖名稱`,
    `目標座標`,
    `操作前角色版本`,
    `操作後角色版本`,
    `結果`,
    CASE WHEN `失敗代碼` IS NULL OR TRIM(`失敗代碼`) = '' THEN '無失敗' ELSE `失敗代碼` END AS `失敗狀態`,
    `回滾狀態`,
    `連線重綁`,
    `同步清除`,
    `同步重建`,
    `黑箱測試優先級`,
    `測試重點`,
    `完成時間UTC`
FROM god2_player.vw_blackbox_world_interaction_observations_readable;

CREATE OR REPLACE VIEW god2_game.vw_blackbox_remaining_targets_sanitized_readable AS
SELECT
    CONVERT('戰鬥公式測試' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`怪物ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`怪物名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT('HP=', `最大HP`, '；MP=', `最大MP`, '；物攻/防=', `物理攻擊`, '/', `物理防禦`, '；魔攻/防=', `法術攻擊`, '/', `法術防禦`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('實機打同一怪，測物攻物防、魔攻魔防、五行、防禦1.5倍、怪物MP技能施放' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game.vw_blackbox_combat_formula_targets_sanitized_readable
UNION ALL
SELECT
    CONVERT('傳送門測試' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`傳送點ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`傳送點名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`來源地圖`, '(', `來源X`, ',', `來源Y`, ') -> ', `目標地圖`, '(', `目標X`, ',', `目標Y`, ')；', `啟用狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('走到傳送點確認觸發半徑、需求條件、目標地圖、落點與同步重建' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game.vw_blackbox_portal_test_targets_sanitized_readable
UNION ALL
SELECT
    CONVERT('戰寵模板測試' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`戰寵模板ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`戰寵名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`戰寵族系`, '；五行=', `五行`, '；HP=', COALESCE(CAST(`基礎最大HP` AS CHAR), '待確認'), '；MP=', COALESCE(CAST(`基礎最大MP` AS CHAR), '待確認'), '；', `野生怪物來源狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('捕捉/配置戰寵後確認五行、HP/MP、基礎能力、等級技能與來源怪物' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game.vw_pet_templates_sanitized_readable;
