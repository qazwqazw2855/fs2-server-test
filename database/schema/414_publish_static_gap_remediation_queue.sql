CREATE TABLE IF NOT EXISTS god2_research.static_gap_remediation_queue (
    queue_id int unsigned NOT NULL AUTO_INCREMENT,
    legacy_table_name varchar(128) NOT NULL,
    formal_table_name varchar(128) NOT NULL,
    priority_rank int unsigned NOT NULL,
    remediation_category_zh_tw varchar(64) NOT NULL,
    game_function_zh_tw varchar(256) NOT NULL,
    evidence_policy_zh_tw varchar(256) NOT NULL,
    remediation_action_zh_tw varchar(512) NOT NULL,
    dependency_check_zh_tw varchar(512) NOT NULL,
    current_status_zh_tw varchar(128) NOT NULL,
    safe_to_apply_automatically tinyint(1) NOT NULL DEFAULT 0,
    safe_to_drop_legacy_now tinyint(1) NOT NULL DEFAULT 0,
    reviewed_at_utc timestamp NOT NULL DEFAULT utc_timestamp(),
    PRIMARY KEY (queue_id),
    UNIQUE KEY ux_static_gap_remediation_pair (legacy_table_name, formal_table_name),
    KEY ix_static_gap_remediation_priority (priority_rank),
    KEY ix_static_gap_remediation_formal_table (formal_table_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM god2_research.static_gap_remediation_queue;

INSERT INTO god2_research.static_gap_remediation_queue (
    legacy_table_name,
    formal_table_name,
    priority_rank,
    remediation_category_zh_tw,
    game_function_zh_tw,
    evidence_policy_zh_tw,
    remediation_action_zh_tw,
    dependency_check_zh_tw,
    current_status_zh_tw,
    safe_to_apply_automatically,
    safe_to_drop_legacy_now
)
SELECT
    coverage.legacy_table_name,
    coverage.formal_table_name,
    CASE
        WHEN coverage.formal_table_name = 'npc_dialogs' THEN 10
        WHEN coverage.formal_table_name = 'status_effects' THEN 20
        WHEN coverage.formal_table_name = 'quest_rewards' THEN 30
        WHEN coverage.formal_table_name = 'monster_skills' THEN 40
        WHEN coverage.formal_table_name IN ('containers', 'container_rewards') THEN 50
        WHEN coverage.formal_table_name = 'merchant_inventory' THEN 60
        WHEN coverage.formal_table_name = 'monster_drops' THEN 70
        WHEN coverage.formal_table_name IN ('items', 'equipment', 'weapons', 'magic_treasures') THEN 80
        WHEN coverage.formal_table_name IN ('skill_client_metadata', 'physical_skill_damage_coefficients', 'magic_skill_damage_coefficients') THEN 90
        ELSE 100
    END AS priority_rank,
    CASE
        WHEN coverage.coverage_status_zh_tw = '正式表無資料，禁止封存' THEN '正式表空表缺口'
        ELSE '正式表筆數不足缺口'
    END AS remediation_category_zh_tw,
    coverage.game_function_zh_tw,
    CASE
        WHEN coverage.formal_table_name IN ('npc_dialogs', 'containers', 'container_rewards', 'merchant_inventory', 'monster_drops')
            THEN '可先用舊 god2 來源表與既有證據比對補齊；欄位語意不明者不得猜值。'
        WHEN coverage.formal_table_name IN ('quest_rewards', 'monster_skills', 'status_effects')
            THEN '正式表目前為空，必須先找既有客戶端證據、封包證據或黑箱測試證據；不得直接自創效果。'
        WHEN coverage.formal_table_name IN ('items', 'equipment', 'weapons', 'magic_treasures')
            THEN '只能補舊表已有且語意可讀的道具拆表資料；未知機器碼、payload、hash 不可進正式表。'
        WHEN coverage.formal_table_name IN ('skill_client_metadata', 'physical_skill_damage_coefficients', 'magic_skill_damage_coefficients')
            THEN '技能顯示與係數需優先使用公測技能證據；缺少傷害公式證據者列入黑箱測試。'
        ELSE '需要人工確認證據來源後再補。'
    END AS evidence_policy_zh_tw,
    CASE
        WHEN coverage.formal_table_name = 'npc_dialogs'
            THEN '比對 god2.dialogs、god2.localization_entries 與 god2_game.npcs，把有 NPC 對應且有繁中文字的對話補入正式 npc_dialogs。'
        WHEN coverage.formal_table_name = 'status_effects'
            THEN '比對舊 status_effects 與技能 buff 候選證據，先補狀態代碼、繁中名稱、持續時間與可讀效果說明。'
        WHEN coverage.formal_table_name = 'quest_rewards'
            THEN '比對 quests 與任務候選資料，補正式任務獎勵；無證據的獎勵先列缺口，不猜。'
        WHEN coverage.formal_table_name = 'monster_skills'
            THEN '比對怪物語意候選與戰鬥封包證據，補怪物技能關係；無證據者待黑箱測試。'
        WHEN coverage.formal_table_name IN ('containers', 'container_rewards')
            THEN '比對 container_item_relationships 與正式 items，補容器與開啟獎勵對照。'
        WHEN coverage.formal_table_name = 'merchant_inventory'
            THEN '比對 merchants、items 與商店證據，補 NPC 商店販售品項、價格與貨幣。'
        WHEN coverage.formal_table_name = 'monster_drops'
            THEN '比對 drop_tables、monster_drop_relationships、formal monster_drops，補缺少的怪物掉落關係。'
        WHEN coverage.formal_table_name IN ('items', 'equipment', 'weapons', 'magic_treasures')
            THEN '比對舊 items 與正式道具拆表，補缺少的可讀欄位與拆表資料。'
        WHEN coverage.formal_table_name IN ('skill_client_metadata', 'physical_skill_damage_coefficients', 'magic_skill_damage_coefficients')
            THEN '比對技能候選證據與正式 skills，補技能顯示資料與已證實係數；未知公式先進黑箱測試清單。'
        ELSE coverage.next_verification_step_zh_tw
    END AS remediation_action_zh_tw,
    CASE
        WHEN coverage.formal_table_name = 'npc_dialogs'
            THEN '需檢查 god2_game.npcs.default_dialog_id、npc_dialog_options、runtime catalog Dialog 載入是否仍通過。'
        WHEN coverage.formal_table_name = 'status_effects'
            THEN '需檢查技能效果、戰鬥狀態、Buff/Debuff 顯示與持續時間是否互相一致。'
        WHEN coverage.formal_table_name = 'quest_rewards'
            THEN '需檢查 quest_objectives、quest_rewards、玩家背包、經驗金錢結算與任務完成流程。'
        WHEN coverage.formal_table_name = 'monster_skills'
            THEN '需檢查 monsters、skills、戰鬥 AI 呼叫與技能傷害公式，不可破壞基本普攻。'
        WHEN coverage.formal_table_name IN ('containers', 'container_rewards')
            THEN '需檢查 item_usage_rules、container_rewards、背包新增物品與道具消耗流程。'
        WHEN coverage.formal_table_name = 'merchant_inventory'
            THEN '需檢查 merchants、items、價格、貨幣、購買封包與背包容量。'
        WHEN coverage.formal_table_name = 'monster_drops'
            THEN '需檢查 monsters、items、monster_drops、死亡獎勵、背包掉落接收與掉落機率。'
        WHEN coverage.formal_table_name IN ('items', 'equipment', 'weapons', 'magic_treasures')
            THEN '需檢查 items、item_usage_rules、equipment、weapons、magic_treasures、角色裝備與屬性計算。'
        WHEN coverage.formal_table_name IN ('skill_client_metadata', 'physical_skill_damage_coefficients', 'magic_skill_damage_coefficients')
            THEN '需檢查 skills、職業限制、MP 消耗、冷卻、傷害公式與技能顯示 metadata。'
        ELSE '需檢查正式表、runtime catalog 與服務端 repository 是否一致。'
    END AS dependency_check_zh_tw,
    '待修復' AS current_status_zh_tw,
    CASE
        WHEN coverage.formal_table_name IN ('npc_dialogs', 'containers', 'container_rewards', 'merchant_inventory', 'monster_drops')
            THEN 1
        ELSE 0
    END AS safe_to_apply_automatically,
    0 AS safe_to_drop_legacy_now
FROM god2_research.legacy_god2_static_table_coverage coverage
WHERE coverage.coverage_status_zh_tw IN ('正式表筆數少於舊表，需要補資料或確認舊資料是否已淘汰', '正式表無資料，禁止封存');

UPDATE god2_research.database_schema_consolidation_status
SET
    consolidation_decision_zh_tw = '靜態資料缺口已建立修復優先清單；可由舊表自動補的項目與需要證據/黑箱測試的項目已分開。',
    next_cleanup_step_zh_tw = '下一批先處理 safe_to_apply_automatically=1 的 NPC 對話、容器、商店、掉落，再處理需證據補齊的任務、狀態、怪物技能與技能係數。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE schema_name = 'god2';
