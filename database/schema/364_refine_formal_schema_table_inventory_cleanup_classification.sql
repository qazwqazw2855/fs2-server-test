-- Schema 364
-- Game function: refine formal schema cleanup audit so empty legacy tables are not reported as immediately droppable while views or tools still reference them.
-- Purpose: continue database consolidation safely by separating guarded drop candidates from delayed cleanup candidates.

CREATE OR REPLACE VIEW `god2_game`.`vw_formal_schema_table_inventory_audit_readable` AS
SELECT table_row.`TABLE_SCHEMA` AS `資料庫`,
       table_row.`TABLE_NAME` AS `資料表`,
       table_row.`TABLE_TYPE` AS `表格類型`,
       COALESCE(table_row.`TABLE_ROWS`,0) AS `估計筆數`,
       CASE
           WHEN table_row.`TABLE_SCHEMA`='god2_player' THEN '正式玩家資料'
           WHEN table_row.`TABLE_SCHEMA`='god2_game' AND table_row.`TABLE_NAME` LIKE 'vw_%' THEN '正式可讀視圖'
           WHEN table_row.`TABLE_SCHEMA`='god2_game' AND table_row.`TABLE_NAME` LIKE 'blackbox_%' THEN '黑箱測試證據'
           WHEN table_row.`TABLE_SCHEMA`='god2_game' AND table_row.`TABLE_NAME` LIKE '%_design_rules' THEN '服務端設計規則'
           WHEN table_row.`TABLE_SCHEMA`='god2_game' THEN '正式靜態內容'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME`='__schemaversion' THEN '正式遷移紀錄'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME` LIKE 'battle_%' THEN '正式戰鬥Runtime'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME` LIKE 'combat_%' THEN '正式戰鬥Runtime'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME` LIKE 'skill_%' THEN '正式技能Runtime'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME` LIKE 'quest_%' THEN '正式任務Runtime'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME` IN ('status_effects') THEN '正式戰鬥狀態定義'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME` LIKE 'vw_%' THEN '正式可讀視圖'
           WHEN table_row.`TABLE_SCHEMA`='god2' THEN '舊資料鏡像或證據來源'
           ELSE '未分類'
       END AS `用途分類`,
       CASE
           WHEN table_row.`TABLE_TYPE`='VIEW' THEN '保留：可讀/稽核視圖'
           WHEN table_row.`TABLE_SCHEMA`='god2_player' THEN '保留：正式服務端玩家狀態'
           WHEN table_row.`TABLE_SCHEMA`='god2_game' THEN '保留：正式服務端靜態內容'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME`='__schemaversion' THEN '保留：正式遷移版本'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME` LIKE 'battle_%' THEN '保留：戰鬥流程使用'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME` LIKE 'combat_%' THEN '保留：戰鬥流程使用'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME` LIKE 'skill_%' THEN '保留：技能流程使用'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME` LIKE 'quest_%' THEN '保留：任務流程使用'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME`='status_effects' THEN '保留：道具解除狀態與戰鬥狀態使用'
           WHEN table_row.`TABLE_SCHEMA`='god2'
             AND table_row.`TABLE_NAME` IN (
                 'character_creation_profiles','equipment_slots','inventory_audit_ledger','inventory_slots',
                 'monster_combat_runtime_state','monster_death_records','monster_respawn_schedules',
                 'pet_egg_relationships','player_currency_balances','player_inventory_state','world_interaction_audit'
             )
             AND COALESCE(table_row.`TABLE_ROWS`,0)=0 THEN '延後清理：空表但仍有正式/稽核視圖引用'
           WHEN table_row.`TABLE_SCHEMA`='god2'
             AND table_row.`TABLE_NAME` IN ('content_runtime_releases','spawns')
             AND COALESCE(table_row.`TABLE_ROWS`,0)=0 THEN '延後清理：空表但仍有程式或工具引用'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND COALESCE(table_row.`TABLE_ROWS`,0)=0 THEN '候選清理：空的舊鏡像/尚未啟用表，刪除前需確認外鍵與程式引用'
           WHEN table_row.`TABLE_SCHEMA`='god2' THEN '候選整併：搬入 god2_game 或 god2_player 後再刪舊表'
           ELSE '需要人工確認'
       END AS `建議動作`,
       CASE
           WHEN table_row.`TABLE_SCHEMA`='god2' AND table_row.`TABLE_NAME` IN (
               'items','maps','npcs','monsters','quests','skills','merchants','portals',
               'equipment_set_definitions','equipment_set_members','container_item_relationships',
               'monster_semantic_profiles','monster_spawn_semantics','pet_content_profiles',
               'skill_content_profiles','skill_semantic_profiles','quest_content_profiles','item_content_profiles',
               'localization_entries'
           ) THEN '與 god2_game 正式內容高度重疊，列為後續合併與刪除優先候選'
           WHEN table_row.`TABLE_SCHEMA`='god2'
             AND table_row.`TABLE_NAME` IN (
                 'character_creation_profiles','equipment_slots','inventory_audit_ledger','inventory_slots',
                 'monster_combat_runtime_state','monster_death_records','monster_respawn_schedules',
                 'pet_egg_relationships','player_currency_balances','player_inventory_state','world_interaction_audit'
             )
             AND COALESCE(table_row.`TABLE_ROWS`,0)=0 THEN '先改寫或移除引用舊表的可讀/稽核視圖，再進行刪表'
           WHEN table_row.`TABLE_SCHEMA`='god2'
             AND table_row.`TABLE_NAME` IN ('content_runtime_releases','spawns')
             AND COALESCE(table_row.`TABLE_ROWS`,0)=0 THEN '先改寫舊工具/回收流程引用到 god2_game 正式表，再進行刪表'
           WHEN table_row.`TABLE_SCHEMA`='god2' AND COALESCE(table_row.`TABLE_ROWS`,0)=0 THEN '目前空表；若沒有服務端寫入路徑即可刪除或延後建立'
           WHEN table_row.`TABLE_SCHEMA`='god2_player' AND COALESCE(table_row.`TABLE_ROWS`,0)=0 THEN '正式玩家狀態空表；保留給功能啟用後寫入'
           ELSE ''
       END AS `整理備註`
FROM `information_schema`.`TABLES` table_row
WHERE table_row.`TABLE_SCHEMA` IN ('god2','god2_game','god2_player');
