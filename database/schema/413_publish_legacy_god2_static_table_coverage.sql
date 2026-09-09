UPDATE god2_research.legacy_god2_table_consolidation_inventory
SET
    current_role_zh_tw = '舊戰鬥 runtime 資料',
    suggested_target_schema = 'god2_player',
    consolidation_decision_zh_tw = '這是怪物戰鬥執行期間的 runtime 狀態，不是靜態怪物設定；先保留到正式玩家/runtime schema 有對應表。',
    next_cleanup_step_zh_tw = '下一批比對正式戰鬥 runtime 持久化設計後再遷移，不可併入 god2_game 靜態資料。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE table_name IN ('monster_combat_runtime_state', 'monster_death_records', 'monster_respawn_schedules');

UPDATE god2_research.legacy_god2_table_consolidation_inventory
SET
    current_role_zh_tw = '舊任務 runtime 資料',
    suggested_target_schema = 'god2_player',
    consolidation_decision_zh_tw = '這是角色任務接取、進度、防重送或獎勵結算狀態，不是正式任務靜態設定；先保留到正式玩家 schema 有對應表。',
    next_cleanup_step_zh_tw = '下一批比對正式角色任務狀態表與任務獎勵結算流程後再遷移，不可併入 god2_game 靜態資料。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE table_name IN ('quest_instances', 'quest_operation_idempotency', 'quest_reward_finalization');

UPDATE god2_research.legacy_god2_table_consolidation_inventory
SET
    current_role_zh_tw = '舊技能 runtime 資料',
    suggested_target_schema = 'god2_player',
    consolidation_decision_zh_tw = '這是技能施放、防重送、消耗保留、效果執行與審計狀態，不是正式技能靜態設定；先保留到正式玩家/runtime schema 有對應表。',
    next_cleanup_step_zh_tw = '下一批比對正式技能施放狀態與戰鬥流程後再遷移，不可併入 god2_game 靜態資料。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE table_name IN ('skill_audit', 'skill_cost_reservations', 'skill_effect_executions', 'skill_executions', 'skill_idempotency');

CREATE TABLE IF NOT EXISTS god2_research.legacy_god2_static_table_coverage (
    legacy_table_name varchar(128) NOT NULL,
    formal_table_name varchar(128) NOT NULL,
    game_function_zh_tw varchar(256) NOT NULL,
    legacy_row_count_estimate bigint unsigned NULL,
    formal_row_count_estimate bigint unsigned NULL,
    coverage_status_zh_tw varchar(128) NOT NULL,
    cleanup_decision_zh_tw varchar(256) NOT NULL,
    next_verification_step_zh_tw varchar(512) NOT NULL,
    safe_to_drop_now tinyint(1) NOT NULL DEFAULT 0,
    reviewed_at_utc timestamp NOT NULL DEFAULT utc_timestamp(),
    PRIMARY KEY (legacy_table_name, formal_table_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM god2_research.legacy_god2_static_table_coverage;

INSERT INTO god2_research.legacy_god2_static_table_coverage (
    legacy_table_name,
    formal_table_name,
    game_function_zh_tw,
    legacy_row_count_estimate,
    formal_row_count_estimate,
    coverage_status_zh_tw,
    cleanup_decision_zh_tw,
    next_verification_step_zh_tw,
    safe_to_drop_now
)
SELECT
    mapping.legacy_table_name,
    mapping.formal_table_name,
    mapping.game_function_zh_tw,
    legacy_tables.table_rows AS legacy_row_count_estimate,
    formal_tables.table_rows AS formal_row_count_estimate,
    CASE
        WHEN formal_tables.table_name IS NULL THEN '正式表不存在，禁止封存'
        WHEN COALESCE(legacy_tables.table_rows, 0) = 0 THEN '舊表目前無資料，仍需確認程式引用'
        WHEN COALESCE(formal_tables.table_rows, 0) = 0 THEN '正式表無資料，禁止封存'
        WHEN COALESCE(formal_tables.table_rows, 0) >= COALESCE(legacy_tables.table_rows, 0) THEN '正式表筆數已覆蓋或超過舊表，仍需欄位語意比對'
        ELSE '正式表筆數少於舊表，需要補資料或確認舊資料是否已淘汰'
    END AS coverage_status_zh_tw,
    CASE
        WHEN formal_tables.table_name IS NULL THEN '保留舊表，先建立或確認正式表設計'
        ELSE '保留舊表作來源證據；完成欄位語意與服務端引用比對後才可封存'
    END AS cleanup_decision_zh_tw,
    mapping.next_verification_step_zh_tw,
    0 AS safe_to_drop_now
FROM (
    SELECT 'client_map_identities' AS legacy_table_name, 'client_map_resource_identities' AS formal_table_name, '客戶端地圖編號與正式地圖資源對照功能' AS game_function_zh_tw, '比對客戶端地圖代號、正式 map_id 與傳送點引用是否一致。' AS next_verification_step_zh_tw
    UNION ALL SELECT 'container_item_relationships', 'containers', '道具容器開啟後可取得內容物的關係功能', '比對容器代碼、內容物道具、數量與正式 container_rewards 是否完整。'
    UNION ALL SELECT 'container_item_relationships', 'container_rewards', '道具容器獎勵清單功能', '比對容器獎勵是否都能由正式服務端查到並套用。'
    UNION ALL SELECT 'dialogs', 'npc_dialogs', 'NPC 對話顯示與對話流程功能', '比對 NPC、對話代碼、文字鍵與正式 body_zh_tw 覆蓋率。'
    UNION ALL SELECT 'drop_tables', 'monster_drops', '怪物死亡後掉落物品功能', '比對舊掉落表與正式 monster_drops 的怪物、道具、機率與啟用狀態。'
    UNION ALL SELECT 'equipment_set_definitions', 'item_sets', '裝備套裝定義功能', '比對套裝代碼、名稱與正式 item_sets 是否完整。'
    UNION ALL SELECT 'equipment_set_members', 'item_set_members', '裝備套裝成員功能', '比對每套裝的裝備成員是否已全部進正式表。'
    UNION ALL SELECT 'items', 'items', '一般道具基礎資料功能', '比對道具代碼、繁中名稱、類型、堆疊與使用規則。'
    UNION ALL SELECT 'items', 'equipment', '裝備屬性功能', '比對舊道具內裝備類資料是否已拆入正式 equipment。'
    UNION ALL SELECT 'items', 'weapons', '武器攻擊與裝備功能', '比對舊道具內武器資料是否已拆入正式 weapons。'
    UNION ALL SELECT 'items', 'magic_treasures', '法寶裝備功能', '比對舊道具內法寶資料是否已拆入正式 magic_treasures。'
    UNION ALL SELECT 'maps', 'maps', '地圖基礎資料與場景功能', '比對地圖代碼、名稱、資源與傳送引用。'
    UNION ALL SELECT 'merchants', 'merchants', 'NPC 商店基礎資料功能', '比對商店 NPC、商店代碼與啟用狀態。'
    UNION ALL SELECT 'merchants', 'merchant_inventory', 'NPC 商店販售清單功能', '比對商店販售道具、價格、貨幣與數量限制。'
    UNION ALL SELECT 'monster_drop_relationships', 'monster_drops', '怪物與掉落物關係功能', '比對正式 monster_drops 是否包含舊關係表每個怪物掉落。'
    UNION ALL SELECT 'monsters', 'monsters', '怪物模板與戰鬥目標功能', '比對怪物代碼、名稱、等級、HP/MP 與五行屬性來源。'
    UNION ALL SELECT 'monsters', 'monster_skills', '怪物可用技能功能', '比對怪物技能是否已有正式關聯，缺少者標記待黑箱或證據補齊。'
    UNION ALL SELECT 'npc_client_identities', 'npc_appearance_identities', 'NPC 客戶端外觀與身份對照功能', '比對 NPC 客戶端編號、外觀資源與正式 NPC 是否一致。'
    UNION ALL SELECT 'npc_spawns', 'npc_spawns', 'NPC 出生點與地圖站位功能', '比對 NPC 所在地圖、座標與服務端出生配置。'
    UNION ALL SELECT 'npcs', 'npcs', 'NPC 模板與服務功能', '比對 NPC 代碼、名稱、類型、預設對話與服務端 catalog。'
    UNION ALL SELECT 'pet_egg_relationships', 'pet_templates', '寵物蛋孵化或取得寵物功能', '比對寵物蛋道具與正式寵物模板的取得關係。'
    UNION ALL SELECT 'pet_innate_definitions', 'pet_innate_definitions', '寵物先天能力功能', '比對先天能力代碼、名稱、效果說明與正式寵物資料。'
    UNION ALL SELECT 'portals', 'portals', '地圖傳送門與場景切換功能', '比對傳送點來源地圖、目的地圖、座標與條件。'
    UNION ALL SELECT 'quests', 'quests', '任務基礎資料功能', '比對任務代碼、名稱、接取條件與啟用狀態。'
    UNION ALL SELECT 'quests', 'quest_objectives', '任務目標功能', '比對任務需求的打怪、收集、對話或到達目標。'
    UNION ALL SELECT 'quests', 'quest_rewards', '任務獎勵功能', '比對經驗、金錢、道具、寵物或其他獎勵是否進正式表。'
    UNION ALL SELECT 'skills', 'skills', '技能基礎資料與被動技能功能', '比對技能代碼、名稱、職業、類型、等級與啟用狀態。'
    UNION ALL SELECT 'skills', 'skill_client_metadata', '技能客戶端顯示與描述功能', '比對技能圖示、描述、施放顯示與客戶端 metadata。'
    UNION ALL SELECT 'skills', 'physical_skill_damage_coefficients', '物理技能傷害係數功能', '比對物理技能公式係數是否已拆入正式係數表。'
    UNION ALL SELECT 'skills', 'magic_skill_damage_coefficients', '法術技能傷害係數功能', '比對法術技能公式係數是否已拆入正式係數表。'
    UNION ALL SELECT 'status_effects', 'status_effects', 'Buff、Debuff 與狀態效果功能', '比對狀態代碼、名稱、持續時間與戰鬥效果。'
) mapping
LEFT JOIN information_schema.tables legacy_tables
    ON legacy_tables.table_schema = 'god2'
   AND legacy_tables.table_name = mapping.legacy_table_name
   AND legacy_tables.table_type = 'BASE TABLE'
LEFT JOIN information_schema.tables formal_tables
    ON formal_tables.table_schema = 'god2_game'
   AND formal_tables.table_name = mapping.formal_table_name
   AND formal_tables.table_type = 'BASE TABLE';

UPDATE god2_research.database_schema_consolidation_status
SET
    consolidation_decision_zh_tw = '舊 god2 靜態資料已建立正式表覆蓋率盤點；runtime 狀態表已改歸 god2_player，仍禁止直接刪除。',
    next_cleanup_step_zh_tw = '下一批依 legacy_god2_static_table_coverage 先處理正式表不存在、正式筆數不足或語意缺口的項目。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE schema_name = 'god2';
