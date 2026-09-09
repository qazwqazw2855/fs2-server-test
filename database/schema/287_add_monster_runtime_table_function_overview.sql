CREATE OR REPLACE VIEW `god2_game`.`vw_monster_runtime_table_function_overview_readable` AS
SELECT
    table_map.`schema_name` AS `資料庫`,
    table_map.`table_name` AS `資料表`,
    table_map.`game_function_zh_tw` AS `遊戲功能`,
    table_map.`runtime_role_zh_tw` AS `服務端用途`,
    table_map.`operator_hint_zh_tw` AS `查看重點`,
    COALESCE(table_stats.`TABLE_ROWS`, 0) AS `目前估計筆數`
FROM (
    SELECT 'god2_game' AS `schema_name`, 'monsters' AS `table_name`, '正式怪物主資料' AS `game_function_zh_tw`, '保存服務端可用的怪物名稱、等級、HP、MP、屬性與戰鬥基礎資料' AS `runtime_role_zh_tw`, '查怪物是否已進正式服務端，以及 HP/MP 是否已有證據' AS `operator_hint_zh_tw`
    UNION ALL SELECT 'god2_game', 'monster_drops', '正式怪物掉落', '保存正式服務端可用的怪物掉落規則', '查打怪掉落是否已正式啟用'
    UNION ALL SELECT 'god2_game', 'monster_spawns', '正式怪物出生點', '保存正式服務端可用的地圖怪物出生點', '查怪物是否會在地圖上出現'
    UNION ALL SELECT 'god2_game', 'monster_skills', '正式怪物技能', '保存怪物可使用的技能或技能候選', '查怪物是否會施放技能'
    UNION ALL SELECT 'god2_game', 'monster_ai_profiles', '正式怪物AI設定', '保存怪物戰鬥判斷與行為分類', '查怪物是普攻型、技能型或特殊行為'
    UNION ALL SELECT 'god2_game', 'monster_ai_rules', '正式怪物AI規則', '保存 AI profile 下的具體觸發條件與動作', '查怪物什麼條件下放技能或換目標'
    UNION ALL SELECT 'god2', 'monsters', '舊版怪物證據表', '保存早期恢復到的怪物名稱與基礎資料；不等於正式 runtime 主表', '查舊證據是否可與正式怪物安全對上'
    UNION ALL SELECT 'god2', 'monster_semantic_profiles', '怪物語意證據', '保存怪物名稱、分類或來源語意候選', '查怪物資料語意是否已解碼'
    UNION ALL SELECT 'god2', 'monster_spawn_semantics', '怪物出生語意證據', '保存怪物與地圖出生關係的語意候選', '查出生點證據是否足夠正式啟用'
    UNION ALL SELECT 'god2', 'monster_drop_relationships', '怪物掉落關係證據', '保存怪物與掉落物之間的候選關係', '查掉落候選是否有足夠機率與來源證據'
    UNION ALL SELECT 'god2', 'monster_combat_runtime_state', '怪物戰鬥即時狀態', '保存怪物進戰鬥後的 HP/MP 與狀態快照', '查實戰中怪物目前 HP/MP、狀態與版本'
    UNION ALL SELECT 'god2', 'monster_death_records', '怪物死亡紀錄', '保存怪物被擊殺後的戰鬥與掉落觸發紀錄', '查怪物何時死亡、由誰擊殺、是否觸發掉落'
    UNION ALL SELECT 'god2', 'monster_respawn_schedules', '怪物重生排程', '保存怪物死亡後下一次出生時間', '查怪物是否卡重生或重生時間是否正確'
) table_map
LEFT JOIN `information_schema`.`TABLES` table_stats
    ON table_stats.`TABLE_SCHEMA` = table_map.`schema_name`
   AND table_stats.`TABLE_NAME` = table_map.`table_name`;
