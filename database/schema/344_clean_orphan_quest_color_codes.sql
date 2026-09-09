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
    TRIM(REGEXP_REPLACE(REGEXP_REPLACE(REGEXP_REPLACE(COALESCE(`任務描述`, ''), '/c[0-9]+', ''), '/c', ''), '[[:space:]]+', ' ')) AS `任務描述`,
    TRIM(REGEXP_REPLACE(REGEXP_REPLACE(REGEXP_REPLACE(COALESCE(`完成文字`, ''), '/c[0-9]+', ''), '/c', ''), '[[:space:]]+', ' ')) AS `完成文字`,
    `服務端狀態`,
    '任務目錄：接取NPC、回報NPC、等級限制、重複規則、描述與完成文字' AS `功能對照`
FROM god2_game.vw_quests_readable;
