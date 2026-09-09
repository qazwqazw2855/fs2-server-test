UPDATE god2_research.legacy_god2_table_consolidation_inventory
SET
    current_role_zh_tw = '舊戰鬥 runtime 資料',
    game_function_zh_tw = '戰鬥操作防重送、結果快取與重試保護功能，避免同一招或同一次攻擊被服務端重複結算。',
    suggested_target_schema = 'god2_player',
    consolidation_decision_zh_tw = '仍被戰鬥持久化程式引用；先保留在舊 god2，後續需設計正式 god2_player 對應表再遷移。',
    next_cleanup_step_zh_tw = '下一批比對 MariaDbCombatMutationStore 查詢與正式玩家 schema，建立正式戰鬥防重送表後再切換引用。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE table_name = 'combat_idempotency';

UPDATE god2_research.legacy_god2_table_consolidation_inventory
SET
    current_role_zh_tw = '舊戰鬥 runtime 資料',
    game_function_zh_tw = '戰鬥審計紀錄功能，用來追蹤攻擊者、目標、傷害、死亡、獎勵與重生是否完成。',
    suggested_target_schema = 'god2_player',
    consolidation_decision_zh_tw = '仍被戰鬥持久化程式引用；先保留在舊 god2，後續需設計正式 god2_player 對應表再遷移。',
    next_cleanup_step_zh_tw = '下一批比對 MariaDbCombatMutationStore 寫入欄位與正式玩家 schema，建立正式戰鬥審計表後再切換引用。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE table_name = 'combat_audit';

UPDATE god2_research.legacy_god2_table_consolidation_inventory
SET
    current_role_zh_tw = '舊靜態遊戲資料',
    game_function_zh_tw = 'NPC 對話來源資料功能，已用來建立正式 god2_game.npc_dialogs 與 NPC 對話顯示。',
    suggested_target_schema = 'god2_game',
    consolidation_decision_zh_tw = '正式 runtime 已讀 god2_game.npc_dialogs；舊表保留作來源比對，確認正式對話覆蓋完整後再封存。',
    next_cleanup_step_zh_tw = '下一批比對 god2.dialogs 與 god2_game.npc_dialogs 的 NPC、代碼與文字鍵覆蓋率，缺口補進正式表。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE table_name = 'dialogs';

UPDATE god2_research.legacy_god2_table_consolidation_inventory
SET
    current_role_zh_tw = '舊研究候選或證據資料',
    game_function_zh_tw = '遊戲顯示文字與繁體中文內容來源功能，提供道具、任務、NPC 對話等文字比對與匯入。',
    suggested_target_schema = 'god2_research',
    consolidation_decision_zh_tw = '正式 runtime 不應直接依賴舊文字來源表；先保留給研究與匯入工具，比對完成後遷入 research 封存表。',
    next_cleanup_step_zh_tw = '下一批比對正式 god2_game 顯示欄位與 localization_entries 文字覆蓋率，只把缺少的繁中文字補進正式表。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE table_name = 'localization_entries';

UPDATE god2_research.database_schema_consolidation_status
SET
    consolidation_decision_zh_tw = '舊 god2 的 manual_review base table 已歸類清零；仍禁止整庫刪除，改採逐表遷移或封存。',
    next_cleanup_step_zh_tw = '下一批依 legacy_god2_table_consolidation_inventory 從 god2_game 靜態資料與 god2_player runtime 資料開始逐表比對覆蓋率。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE schema_name = 'god2';

UPDATE god2_research.database_traditional_chinese_surface_audit
SET
    issue_count = 1,
    status_zh_tw = '有待遷移項目',
    cleanup_policy_zh_tw = '舊 god2 已無未歸類 base table，但仍有 runtime 與證據來源依賴，逐表遷移完成前禁止刪除。'
WHERE audit_name = 'schema_consolidation_status';
