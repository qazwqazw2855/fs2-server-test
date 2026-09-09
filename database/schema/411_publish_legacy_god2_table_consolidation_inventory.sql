CREATE TABLE IF NOT EXISTS god2_research.legacy_god2_table_consolidation_inventory (
    table_name varchar(128) NOT NULL,
    table_kind_zh_tw varchar(32) NOT NULL,
    row_count_estimate bigint unsigned NULL,
    current_role_zh_tw varchar(128) NOT NULL,
    game_function_zh_tw varchar(256) NOT NULL,
    suggested_target_schema varchar(64) NOT NULL,
    consolidation_decision_zh_tw varchar(256) NOT NULL,
    next_cleanup_step_zh_tw varchar(512) NOT NULL,
    safe_to_drop_now tinyint(1) NOT NULL DEFAULT 0,
    reviewed_at_utc timestamp NOT NULL DEFAULT utc_timestamp(),
    PRIMARY KEY (table_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

TRUNCATE TABLE god2_research.legacy_god2_table_consolidation_inventory;

INSERT INTO god2_research.legacy_god2_table_consolidation_inventory (
    table_name,
    table_kind_zh_tw,
    row_count_estimate,
    current_role_zh_tw,
    game_function_zh_tw,
    suggested_target_schema,
    consolidation_decision_zh_tw,
    next_cleanup_step_zh_tw,
    safe_to_drop_now
)
SELECT
    t.table_name,
    '舊 god2 base table' AS table_kind_zh_tw,
    t.table_rows AS row_count_estimate,
    CASE
        WHEN t.table_name IN ('accounts', 'characters')
             OR t.table_name LIKE 'character\_%'
             OR t.table_name LIKE 'battle\_%'
             OR t.table_name LIKE 'quest\_%state%'
             OR t.table_name LIKE 'quest\_%progress%'
             OR t.table_name LIKE 'skill\_%state%'
             OR t.table_name LIKE 'player\_%'
            THEN '舊玩家或戰鬥 runtime 資料'
        WHEN t.table_name LIKE '%profile%'
             OR t.table_name LIKE '%semantic%'
             OR t.table_name LIKE '%identity%'
             OR t.table_name LIKE '%evidence%'
             OR t.table_name LIKE '%recovery%'
            THEN '舊研究候選或證據資料'
        WHEN t.table_name = '__schemaversion'
            THEN '舊 schema 版本紀錄'
        WHEN t.table_name LIKE '%map%'
             OR t.table_name LIKE '%npc%'
             OR t.table_name LIKE '%monster%'
             OR t.table_name LIKE '%item%'
             OR t.table_name LIKE '%equipment%'
             OR t.table_name LIKE '%skill%'
             OR t.table_name LIKE '%quest%'
             OR t.table_name LIKE '%merchant%'
             OR t.table_name LIKE '%drop%'
             OR t.table_name LIKE '%pet%'
             OR t.table_name LIKE '%portal%'
             OR t.table_name LIKE '%status%'
            THEN '舊靜態遊戲資料'
        ELSE '舊資料，待人工判讀'
    END AS current_role_zh_tw,
    CASE
        WHEN t.table_name IN ('accounts', 'characters')
             OR t.table_name LIKE 'character\_%'
             OR t.table_name LIKE 'player\_%'
            THEN '帳號、角色、背包、裝備、人物狀態等玩家持久化功能'
        WHEN t.table_name LIKE 'battle\_%'
            THEN '戰鬥中的回合、傷害、狀態或臨時運算資料'
        WHEN t.table_name LIKE '%map%' OR t.table_name LIKE '%portal%'
            THEN '地圖、傳送點、場景進出功能'
        WHEN t.table_name LIKE '%npc%' OR t.table_name LIKE '%merchant%'
            THEN 'NPC 對話、商店、服務與出生配置功能'
        WHEN t.table_name LIKE '%monster%' OR t.table_name LIKE '%drop%'
            THEN '怪物、出生點、掉落、戰鬥目標資料功能'
        WHEN t.table_name LIKE '%item%' OR t.table_name LIKE '%equipment%'
            THEN '道具、裝備、套裝、容器關係與使用條件功能'
        WHEN t.table_name LIKE '%skill%' OR t.table_name LIKE '%status%'
            THEN '技能、被動、狀態效果與戰鬥附加效果功能'
        WHEN t.table_name LIKE '%quest%'
            THEN '任務接取、進度、獎勵與任務目標功能'
        WHEN t.table_name LIKE '%pet%'
            THEN '寵物、蛋、成長、先天能力與關聯資料功能'
        WHEN t.table_name LIKE '%profile%'
             OR t.table_name LIKE '%semantic%'
             OR t.table_name LIKE '%identity%'
             OR t.table_name LIKE '%evidence%'
             OR t.table_name LIKE '%recovery%'
            THEN '證據整理、語意候選、客戶端資料復原與人工審核功能'
        WHEN t.table_name = '__schemaversion'
            THEN '舊 schema migration 版本紀錄功能'
        ELSE '尚未歸類的舊資料功能，需人工確認'
    END AS game_function_zh_tw,
    CASE
        WHEN t.table_name IN ('accounts', 'characters')
             OR t.table_name LIKE 'character\_%'
             OR t.table_name LIKE 'battle\_%'
             OR t.table_name LIKE 'quest\_%state%'
             OR t.table_name LIKE 'quest\_%progress%'
             OR t.table_name LIKE 'skill\_%state%'
             OR t.table_name LIKE 'player\_%'
            THEN 'god2_player'
        WHEN t.table_name LIKE '%profile%'
             OR t.table_name LIKE '%semantic%'
             OR t.table_name LIKE '%identity%'
             OR t.table_name LIKE '%evidence%'
             OR t.table_name LIKE '%recovery%'
            THEN 'god2_research'
        WHEN t.table_name = '__schemaversion'
            THEN 'god2_game_meta'
        WHEN t.table_name LIKE '%map%'
             OR t.table_name LIKE '%npc%'
             OR t.table_name LIKE '%monster%'
             OR t.table_name LIKE '%item%'
             OR t.table_name LIKE '%equipment%'
             OR t.table_name LIKE '%skill%'
             OR t.table_name LIKE '%quest%'
             OR t.table_name LIKE '%merchant%'
             OR t.table_name LIKE '%drop%'
             OR t.table_name LIKE '%pet%'
             OR t.table_name LIKE '%portal%'
             OR t.table_name LIKE '%status%'
            THEN 'god2_game'
        ELSE 'manual_review'
    END AS suggested_target_schema,
    CASE
        WHEN t.table_name = '__schemaversion'
            THEN '保留到舊 schema 完全退場，再改由正式 meta 紀錄取代'
        WHEN t.table_name LIKE '%profile%'
             OR t.table_name LIKE '%semantic%'
             OR t.table_name LIKE '%identity%'
             OR t.table_name LIKE '%evidence%'
             OR t.table_name LIKE '%recovery%'
            THEN '先視為研究歸檔來源，不進正式 runtime；確認已搬移後再封存'
        ELSE '先保留並標記目標 schema；必須確認程式碼引用與正式資料已同步後才能遷移或封存'
    END AS consolidation_decision_zh_tw,
    CASE
        WHEN t.table_name = '__schemaversion'
            THEN '下一批比對正式 migration 紀錄，確認沒有工具仍依賴舊版本表'
        WHEN t.table_name LIKE '%profile%'
             OR t.table_name LIKE '%semantic%'
             OR t.table_name LIKE '%identity%'
             OR t.table_name LIKE '%evidence%'
             OR t.table_name LIKE '%recovery%'
            THEN '下一批比對 god2_research 是否已有可讀候選表或歸檔表，重複者只保留研究 schema 版本'
        ELSE '下一批逐表比對正式 schema、runtime catalog、repository 查詢與測試覆蓋，確認後再做遷移'
    END AS next_cleanup_step_zh_tw,
    0 AS safe_to_drop_now
FROM information_schema.tables t
WHERE t.table_schema = 'god2'
  AND t.table_type = 'BASE TABLE'
ORDER BY t.table_name;

UPDATE god2_research.database_schema_consolidation_status
SET
    consolidation_decision_zh_tw = '已建立逐表盤點清單；舊 god2 仍禁止刪除，改採逐表遷移或封存',
    next_cleanup_step_zh_tw = '下一批依 legacy_god2_table_consolidation_inventory 逐表比對程式碼引用與正式 schema 覆蓋率。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE schema_name = 'god2';

UPDATE god2_research.database_traditional_chinese_surface_audit
SET
    issue_count = 1,
    status_zh_tw = '有待遷移項目',
    cleanup_policy_zh_tw = '舊 god2 已完成逐表盤點，但尚未確認所有 runtime 引用與正式 schema 覆蓋前禁止刪除。'
WHERE audit_name = 'schema_consolidation_status';
