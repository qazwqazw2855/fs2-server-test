CREATE DATABASE IF NOT EXISTS `god2_research`
  DEFAULT CHARACTER SET utf8mb4
  DEFAULT COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`database_readability_surface`
(
    `schema_name` varchar(64) NOT NULL,
    `table_name` varchar(128) NOT NULL,
    `readability_tier_zh_tw` varchar(32) NOT NULL,
    `game_function_zh_tw` varchar(160) NOT NULL,
    `intended_reader_zh_tw` varchar(64) NOT NULL,
    `safe_to_browse` tinyint(1) NOT NULL DEFAULT 0,
    `contains_tool_only_fields` tinyint(1) NOT NULL DEFAULT 0,
    `cleanup_policy_zh_tw` varchar(220) NOT NULL,
    `notes_zh_tw` varchar(320) NOT NULL,
    `last_reviewed_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`schema_name`,`table_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`database_field_readability_audit`
(
    `schema_name` varchar(64) NOT NULL,
    `table_name` varchar(128) NOT NULL,
    `field_name` varchar(128) NOT NULL,
    `field_type` varchar(64) NOT NULL,
    `readability_status_zh_tw` varchar(48) NOT NULL,
    `cleanup_policy_zh_tw` varchar(220) NOT NULL,
    `game_function_zh_tw` varchar(160) NOT NULL,
    PRIMARY KEY (`schema_name`,`table_name`,`field_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

TRUNCATE TABLE `god2_research`.`database_readability_surface`;
TRUNCATE TABLE `god2_research`.`database_field_readability_audit`;

INSERT INTO `god2_research`.`database_readability_surface`
    (`schema_name`,`table_name`,`readability_tier_zh_tw`,`game_function_zh_tw`,`intended_reader_zh_tw`,
     `safe_to_browse`,`contains_tool_only_fields`,`cleanup_policy_zh_tw`,`notes_zh_tw`)
SELECT
    table_row.`TABLE_SCHEMA`,
    table_row.`TABLE_NAME`,
    CASE
        WHEN table_row.`TABLE_SCHEMA` = 'god2_game' THEN '正式遊戲資料'
        WHEN table_row.`TABLE_NAME` LIKE '%_candidate_rows' THEN '可讀候選整理'
        WHEN table_row.`TABLE_NAME` LIKE '%_candidates' THEN '可讀候選整理'
        WHEN table_row.`TABLE_NAME` LIKE '%_archive' THEN '工具證據歸檔'
        WHEN table_row.`TABLE_NAME` LIKE '%_evidence' THEN '工具證據歸檔'
        WHEN table_row.`TABLE_NAME` LIKE 'content_phase3_%' THEN '工具證據歸檔'
        WHEN table_row.`TABLE_NAME` LIKE '%_manifest' THEN '工具證據歸檔'
        ELSE '研究輔助資料'
    END,
    CASE
        WHEN table_row.`TABLE_NAME` LIKE '%item%' THEN '道具、裝備、獎勵與物品資料'
        WHEN table_row.`TABLE_NAME` LIKE '%skill%' THEN '技能、被動、消耗與效果資料'
        WHEN table_row.`TABLE_NAME` LIKE '%quest%' THEN '任務、目標、NPC 對話與獎勵資料'
        WHEN table_row.`TABLE_NAME` LIKE '%monster%' THEN '怪物、戰鬥能力、出生點與掉落資料'
        WHEN table_row.`TABLE_NAME` LIKE '%pet%' THEN '神仙、寵物成長、技能與天賦資料'
        WHEN table_row.`TABLE_NAME` LIKE '%npc%' THEN 'NPC 外觀、座標、商店與互動資料'
        WHEN table_row.`TABLE_NAME` LIKE '%map%' OR table_row.`TABLE_NAME` LIKE '%portal%' THEN '地圖、傳送點與場景資源資料'
        WHEN table_row.`TABLE_NAME` LIKE '%character%' OR table_row.`TABLE_NAME` LIKE '%class%' THEN '角色職業、屬性與角色建立資料'
        WHEN table_row.`TABLE_NAME` LIKE '%container%' OR table_row.`TABLE_NAME` LIKE '%reward%' THEN '箱子、禮包與獎勵內容資料'
        WHEN table_row.`TABLE_NAME` LIKE '%merchant%' THEN '商店販售清單資料'
        ELSE '服務端資料整理與證據追蹤'
    END,
    CASE
        WHEN table_row.`TABLE_SCHEMA` = 'god2_game' THEN '正式服務端與人工檢查'
        WHEN table_row.`TABLE_NAME` LIKE '%_candidate_rows' OR table_row.`TABLE_NAME` LIKE '%_candidates' THEN '人工確認與資料補齊'
        ELSE '工具管線，不建議人工直接編輯'
    END,
    CASE
        WHEN table_row.`TABLE_SCHEMA` = 'god2_game' THEN 1
        WHEN table_row.`TABLE_NAME` LIKE '%_candidate_rows' OR table_row.`TABLE_NAME` LIKE '%_candidates' THEN 1
        ELSE 0
    END,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM information_schema.`COLUMNS` column_row
            WHERE column_row.`TABLE_SCHEMA` = table_row.`TABLE_SCHEMA`
              AND column_row.`TABLE_NAME` = table_row.`TABLE_NAME`
              AND (
                    column_row.`COLUMN_NAME` LIKE '%Hash%'
                 OR column_row.`COLUMN_NAME` LIKE '%Payload%'
                 OR column_row.`COLUMN_NAME` LIKE '%Raw%'
                 OR column_row.`COLUMN_NAME` LIKE '%Json%'
                 OR column_row.`COLUMN_NAME` LIKE '%Binary%'
                 OR column_row.`COLUMN_NAME` LIKE '%Blob%'
                 OR column_row.`COLUMN_NAME` LIKE '%Hex%'
                 OR column_row.`COLUMN_NAME` LIKE '%ProfileId%'
                 OR column_row.`COLUMN_NAME` LIKE '%RelationshipId%'
                 OR column_row.`COLUMN_NAME` = 'RunId'
              )
        ) THEN 1
        ELSE 0
    END,
    CASE
        WHEN table_row.`TABLE_SCHEMA` = 'god2_game' THEN '正式遊戲表保留；若有欄位不清楚，優先補繁中說明與測試，不直接刪除。'
        WHEN table_row.`TABLE_NAME` LIKE '%_candidate_rows' OR table_row.`TABLE_NAME` LIKE '%_candidates' THEN '可人工瀏覽；只保留已整理語意與對應狀態，不保存原始封包或雜湊。'
        ELSE '工具歸檔表保留給證據回溯；不要當成服主日常檢查入口，需另產可讀候選表。'
    END,
    CASE
        WHEN table_row.`TABLE_SCHEMA` = 'god2_game' THEN '這是服務端實際讀取或同步後的正式資料面。'
        WHEN table_row.`TABLE_NAME` LIKE '%_candidate_rows' OR table_row.`TABLE_NAME` LIKE '%_candidates' THEN '這是把證據轉成人可讀欄位後的檢查面。'
        ELSE '這是匯入、比對、重建用的底層證據面。'
    END
FROM information_schema.`TABLES` table_row
WHERE table_row.`TABLE_SCHEMA` IN ('god2_game','god2_research')
  AND table_row.`TABLE_TYPE` = 'BASE TABLE';

INSERT INTO `god2_research`.`database_field_readability_audit`
    (`schema_name`,`table_name`,`field_name`,`field_type`,`readability_status_zh_tw`,`cleanup_policy_zh_tw`,`game_function_zh_tw`)
SELECT
    column_row.`TABLE_SCHEMA`,
    column_row.`TABLE_NAME`,
    column_row.`COLUMN_NAME`,
    column_row.`DATA_TYPE`,
    CASE
        WHEN surface.`safe_to_browse` = 1 AND surface.`contains_tool_only_fields` = 0 THEN '可人工瀏覽'
        WHEN surface.`safe_to_browse` = 1 THEN '可人工瀏覽但需注意欄位'
        ELSE '工具欄位'
    END,
    CASE
        WHEN surface.`safe_to_browse` = 1 AND surface.`contains_tool_only_fields` = 0 THEN '保留為可讀欄位。'
        WHEN surface.`safe_to_browse` = 1 THEN '保留欄位但需在候選表中提供繁中語意，不直接暴露原始內容。'
        ELSE '保留給工具追證據；若要給人工看，必須另建可讀候選表。'
    END,
    surface.`game_function_zh_tw`
FROM information_schema.`COLUMNS` column_row
JOIN `god2_research`.`database_readability_surface` surface
  ON surface.`schema_name` = column_row.`TABLE_SCHEMA`
 AND surface.`table_name` = column_row.`TABLE_NAME`
WHERE column_row.`TABLE_SCHEMA` IN ('god2_game','god2_research')
  AND (
        column_row.`COLUMN_NAME` LIKE '%Hash%'
     OR column_row.`COLUMN_NAME` LIKE '%Payload%'
     OR column_row.`COLUMN_NAME` LIKE '%Raw%'
     OR column_row.`COLUMN_NAME` LIKE '%Json%'
     OR column_row.`COLUMN_NAME` LIKE '%Binary%'
     OR column_row.`COLUMN_NAME` LIKE '%Blob%'
     OR column_row.`COLUMN_NAME` LIKE '%Hex%'
     OR column_row.`COLUMN_NAME` LIKE '%ProfileId%'
     OR column_row.`COLUMN_NAME` LIKE '%RelationshipId%'
     OR column_row.`COLUMN_NAME` = 'RunId'
  );
