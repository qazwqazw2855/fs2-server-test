CREATE TABLE IF NOT EXISTS `god2_research`.`database_schema_consolidation_status`
(
    `schema_name` varchar(64) NOT NULL,
    `schema_role_zh_tw` varchar(96) NOT NULL,
    `base_table_count` int NOT NULL DEFAULT 0,
    `view_count` int NOT NULL DEFAULT 0,
    `runtime_safety_zh_tw` varchar(96) NOT NULL,
    `consolidation_decision_zh_tw` varchar(220) NOT NULL,
    `next_cleanup_step_zh_tw` varchar(220) NOT NULL,
    `safe_to_drop_now` tinyint(1) NOT NULL DEFAULT 0,
    `reviewed_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`schema_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

TRUNCATE TABLE `god2_research`.`database_schema_consolidation_status`;

INSERT INTO `god2_research`.`database_schema_consolidation_status`
    (`schema_name`,`schema_role_zh_tw`,`base_table_count`,`view_count`,`runtime_safety_zh_tw`,
     `consolidation_decision_zh_tw`,`next_cleanup_step_zh_tw`,`safe_to_drop_now`)
SELECT
    schema_row.`schema_name`,
    CASE schema_row.`schema_name`
        WHEN 'god2_game' THEN '正式遊戲靜態資料'
        WHEN 'god2_player' THEN '正式玩家角色與帳號資料'
        WHEN 'god2_game_meta' THEN '正式管理、鎖欄與發佈 manifest'
        WHEN 'god2_research' THEN '證據整理、可讀候選與工具歸檔'
        WHEN 'god2' THEN '舊版混合 runtime 與來源資料'
        ELSE '其他資料庫'
    END,
    COALESCE(table_counts.`base_table_count`, 0),
    COALESCE(table_counts.`view_count`, 0),
    CASE schema_row.`schema_name`
        WHEN 'god2_game' THEN '正式服必需'
        WHEN 'god2_player' THEN '正式服必需'
        WHEN 'god2_game_meta' THEN '正式服必需'
        WHEN 'god2_research' THEN '工具必需，人工不直接編輯歸檔表'
        WHEN 'god2' THEN '仍有 legacy/runtime 引用，禁止直接刪除'
        ELSE '未納入 God2 正式整理範圍'
    END,
    CASE schema_row.`schema_name`
        WHEN 'god2_game' THEN '保留為正式遊戲資料面；所有正式表已納入服務端對應或工具同步盤點。'
        WHEN 'god2_player' THEN '保留為正式玩家狀態資料面；不得合併進靜態資料庫以免污染玩家資料。'
        WHEN 'god2_game_meta' THEN '保留為正式管理資料面；保存發佈、欄位鎖與稽核資料。'
        WHEN 'god2_research' THEN '保留為證據與候選整理面；人工看 candidate/readability 表，不直接看 source/archive 表。'
        WHEN 'god2' THEN '暫時保留；先把仍在使用的 runtime 表與 legacy 來源表逐項遷到 god2_player、god2_game 或 god2_research。'
        ELSE '未決策。'
    END,
    CASE schema_row.`schema_name`
        WHEN 'god2_game' THEN '繼續做欄位語意與資料完整性稽核。'
        WHEN 'god2_player' THEN '逐步確認玩家 runtime 表都有繁中可讀 view 與正式服務端引用。'
        WHEN 'god2_game_meta' THEN '保留 manifest、admin audit、field lock；清掉已失效 meta 表前先查引用。'
        WHEN 'god2_research' THEN '把人工要看的資料維持在可讀候選表；source/archive 只給工具。'
        WHEN 'god2' THEN '下一批先盤點舊 god2 內哪些 base table 已被正式 schema 取代、哪些仍被 runtime 直接使用。'
        ELSE '不處理。'
    END,
    0
FROM (
    SELECT 'god2_game' AS `schema_name`
    UNION ALL SELECT 'god2_player'
    UNION ALL SELECT 'god2_game_meta'
    UNION ALL SELECT 'god2_research'
    UNION ALL SELECT 'god2'
) schema_row
LEFT JOIN (
    SELECT
        table_row.`TABLE_SCHEMA`,
        SUM(CASE WHEN table_row.`TABLE_TYPE` = 'BASE TABLE' THEN 1 ELSE 0 END) AS `base_table_count`,
        SUM(CASE WHEN table_row.`TABLE_TYPE` = 'VIEW' THEN 1 ELSE 0 END) AS `view_count`
    FROM information_schema.`TABLES` table_row
    WHERE table_row.`TABLE_SCHEMA` IN ('god2_game','god2_player','god2_game_meta','god2_research','god2')
    GROUP BY table_row.`TABLE_SCHEMA`
) table_counts
  ON table_counts.`TABLE_SCHEMA` = schema_row.`schema_name`;

REPLACE INTO `god2_research`.`database_traditional_chinese_surface_audit`
    (`audit_name`,`checked_scope_zh_tw`,`issue_count`,`status_zh_tw`,`cleanup_policy_zh_tw`)
SELECT
    'schema_consolidation_status',
    'God2 正式、玩家、管理、研究與舊版 schema 分層狀態',
    SUM(CASE WHEN `schema_name` = 'god2' AND `safe_to_drop_now` = 0 THEN 1 ELSE 0 END),
    '有待遷移項目',
    '舊 god2 schema 尚未證明可刪；先保留並逐表遷移或合併，避免破壞仍在使用的戰鬥、任務、技能與回收流程。'
FROM `god2_research`.`database_schema_consolidation_status`;
