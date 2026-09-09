-- 467_create_deterministic_blackbox_work_queue.sql
-- Purpose: collect deterministic black-box capture targets and explicitly exclude long-run statistical systems.

DROP TABLE IF EXISTS god2_research.xjz_blackbox_excluded_deferred_systems;
CREATE TABLE god2_research.xjz_blackbox_excluded_deferred_systems (
  excluded_key VARCHAR(96) NOT NULL PRIMARY KEY,
  game_function_zh_tw VARCHAR(160) NOT NULL,
  excluded_reason_zh_tw VARCHAR(512) NOT NULL,
  resume_condition_zh_tw VARCHAR(512) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO god2_research.xjz_blackbox_excluded_deferred_systems VALUES
('drop_rate_probability', '掉落機率', '需要大量樣本才有可信機率，不能用單次黑箱當真值。', '等確定性封包與功能補完後，再設計長時間樣本統計。'),
('reward_pool_probability', '抽獎/福袋機率', '獎池結果需要大量抽樣與來源條件，單次封包只能證明結果不能證明機率。', '等獎池來源、觸發條件與樣本策略確定後再做。'),
('shop_refresh_rules', '商店刷新規則', '刷新週期與隨機規則需要時間序列樣本，單次黑箱不足。', '等商店靜態販售與交易流程先補完後再做。'),
('monster_ai_full_decision', '怪物 AI 完整決策', '完整 AI 需要多回合、多狀態、多隊伍配置樣本，先不阻塞技能/MP/封包補齊。', '等怪物技能、MP、基礎戰鬥封包補完後再設計。'),
('combat_formula_full_coefficients', '傷害公式完整係數', '完整係數需要控制變因與大量黑箱校準，先不阻塞可直接觀測的傷害/MP/狀態封包。', '等技能施放、角色/怪物屬性與防禦倍率樣本補齊後再做。');

DROP TABLE IF EXISTS god2_research.xjz_deterministic_blackbox_work_queue;
CREATE TABLE god2_research.xjz_deterministic_blackbox_work_queue (
  work_item_id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  source_table_name VARCHAR(160) NOT NULL,
  source_identifier VARCHAR(160) NOT NULL,
  game_function_zh_tw VARCHAR(160) NOT NULL,
  blackbox_goal_zh_tw VARCHAR(512) NOT NULL,
  required_action_zh_tw TEXT NOT NULL,
  expected_observation_zh_tw TEXT NOT NULL,
  target_sample_table VARCHAR(160) NOT NULL,
  priority_zh_tw VARCHAR(32) NOT NULL,
  evidence_status_zh_tw VARCHAR(128) NOT NULL,
  server_apply_after_capture_zh_tw VARCHAR(512) NOT NULL,
  deferred_probability_or_ai_flag TINYINT(1) NOT NULL DEFAULT 0,
  created_from_zh_tw VARCHAR(160) NOT NULL,
  UNIQUE KEY uq_xjz_blackbox_work_queue_source (source_table_name, source_identifier),
  KEY idx_xjz_blackbox_work_queue_priority (priority_zh_tw),
  KEY idx_xjz_blackbox_work_queue_function (game_function_zh_tw)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO god2_research.xjz_deterministic_blackbox_work_queue (
  source_table_name, source_identifier, game_function_zh_tw, blackbox_goal_zh_tw,
  required_action_zh_tw, expected_observation_zh_tw, target_sample_table, priority_zh_tw,
  evidence_status_zh_tw, server_apply_after_capture_zh_tw, created_from_zh_tw
)
SELECT
  'xjz_mount_formal_item_links_csharp',
  CAST(l.mount_template_id AS CHAR),
  '坐騎/傳送獸正式道具對照',
  CONCAT('確認坐騎模板「', l.name_zh_tw, '」應連到哪一個正式道具。'),
  CONCAT('在客戶端取得或查看同名坐騎/傳送獸道具，記錄道具代碼、背包道具 ID、使用後騎乘/傳送狀態與封包。'),
  '確認正式 item_id/client_item_id、是否可騎乘、是否可傳送、使用成功/失敗封包。',
  'teleport_flow_sample',
  CASE WHEN m.can_teleport = 1 THEN '高' ELSE '中' END,
  l.link_status_zh_tw,
  '確認後回填坐騎模板 formal_item_id，並讓道具使用、坐騎服務、傳送服務可以直接查到正式道具。',
  'C# 坐騎模板與正式道具表'
FROM god2_research.xjz_mount_formal_item_links_csharp l
JOIN god2_research.xjz_mount_templates_csharp m ON m.mount_template_id = l.mount_template_id
WHERE l.formal_item_id IS NULL;

INSERT IGNORE INTO god2_research.xjz_deterministic_blackbox_work_queue (
  source_table_name, source_identifier, game_function_zh_tw, blackbox_goal_zh_tw,
  required_action_zh_tw, expected_observation_zh_tw, target_sample_table, priority_zh_tw,
  evidence_status_zh_tw, server_apply_after_capture_zh_tw, created_from_zh_tw
)
SELECT
  'xjz_transport_flow_capture_targets_csharp',
  CAST(target_id AS CHAR),
  '坐騎/交通/傳送封包',
  CONCAT('確認交通來源「', source_ref, '」的騎乘、移動或傳送封包欄位。'),
  request_hint_zh_tw,
  wanted_fields_zh_tw,
  sample_table,
  CASE WHEN capture_priority = 'high' THEN '高' WHEN capture_priority = 'medium' THEN '中' ELSE '低' END,
  evidence_status,
  '確認後回填騎乘狀態、速度、地圖座標、目標地圖與原始封包欄位。',
  'C# 交通抓包目標'
FROM god2_research.xjz_transport_flow_capture_targets_csharp
WHERE packet_evidence_boundary = 1;

INSERT IGNORE INTO god2_research.xjz_deterministic_blackbox_work_queue (
  source_table_name, source_identifier, game_function_zh_tw, blackbox_goal_zh_tw,
  required_action_zh_tw, expected_observation_zh_tw, target_sample_table, priority_zh_tw,
  evidence_status_zh_tw, server_apply_after_capture_zh_tw, created_from_zh_tw
)
SELECT
  'xjz_skill_runtime_capture_targets_csharp',
  CAST(target_id AS CHAR),
  CASE actor_skill_kind
    WHEN 'monster_skill' THEN '怪物技能/怪物 MP'
    WHEN 'combat_pet_skill' THEN '戰鬥寵技能施放'
    ELSE '角色/神仙技能施放'
  END,
  CONCAT('確認技能「', skill_name_zh_tw, '」的實際施放封包、MP、命中、傷害/補血與狀態結果。'),
  request_hint_zh_tw,
  wanted_fields_zh_tw,
  target_sample_table,
  CASE WHEN capture_priority = 'high' THEN '高' WHEN capture_priority = 'medium' THEN '中' ELSE '低' END,
  evidence_status,
  '確認後回填技能 runtime、MP 消耗、目標限制、效果碼、狀態結果與怪物 MP 證據。',
  'C# 技能施放抓包目標'
FROM god2_research.xjz_skill_runtime_capture_targets_csharp
WHERE packet_evidence_boundary = 1;

INSERT IGNORE INTO god2_research.xjz_deterministic_blackbox_work_queue (
  source_table_name, source_identifier, game_function_zh_tw, blackbox_goal_zh_tw,
  required_action_zh_tw, expected_observation_zh_tw, target_sample_table, priority_zh_tw,
  evidence_status_zh_tw, server_apply_after_capture_zh_tw, created_from_zh_tw
)
SELECT
  'xjz_monster_skill_probe_templates_csharp',
  CAST(probe_id AS CHAR),
  '怪物技能/怪物 MP',
  CONCAT('確認怪物「', monster_name_zh_tw, '」的技能槽、技能施放與 MP 變化。'),
  '進入該怪物戰鬥，記錄怪物施放技能前後封包、MP、技能 ID、目標與結果。',
  'monster_code, monster_name_zh_tw, skill_slots, skill_id, mp_before, mp_after, raw_packet',
  capture_target_table,
  '高',
  evidence_status,
  '確認後回填 monster_skills、怪物 MP 欄位與怪物技能封包證據；不推導完整 AI 決策。',
  'C# 怪物技能探針'
FROM god2_research.xjz_monster_skill_probe_templates_csharp
WHERE packet_evidence_boundary = 1;

INSERT IGNORE INTO god2_research.xjz_deterministic_blackbox_work_queue (
  source_table_name, source_identifier, game_function_zh_tw, blackbox_goal_zh_tw,
  required_action_zh_tw, expected_observation_zh_tw, target_sample_table, priority_zh_tw,
  evidence_status_zh_tw, server_apply_after_capture_zh_tw, created_from_zh_tw
)
SELECT
  'xjz_monster_observation_capture_targets',
  CAST(target_id AS CHAR),
  CASE
    WHEN observed_monster_field_zh_tw LIKE '%HP%' OR observed_monster_field_zh_tw LIKE '%生命%' THEN '怪物 HP/MP 可視化'
    WHEN observed_monster_field_zh_tw LIKE '%MP%' OR observed_monster_field_zh_tw LIKE '%法力%' THEN '怪物 HP/MP 可視化'
    ELSE '怪物可直接觀測規則'
  END,
  CONCAT('確認「', item_name_zh_tw, '」能觀測到的怪物欄位：', observed_monster_field_zh_tw),
  required_player_action_zh_tw,
  wanted_fields_zh_tw,
  target_sample_table_zh_tw,
  capture_priority_zh_tw,
  evidence_status_zh_tw,
  '確認後回填怪物可視欄位、HP/MP、封印規則或其他直接觀測結果；不推導掉落機率與完整 AI。',
  'C# 怪物觀測抓包目標'
FROM god2_research.xjz_monster_observation_capture_targets
WHERE needs_packet_evidence = 1
  AND observation_function_zh_tw NOT LIKE '%機率%';

INSERT IGNORE INTO god2_research.xjz_deterministic_blackbox_work_queue (
  source_table_name, source_identifier, game_function_zh_tw, blackbox_goal_zh_tw,
  required_action_zh_tw, expected_observation_zh_tw, target_sample_table, priority_zh_tw,
  evidence_status_zh_tw, server_apply_after_capture_zh_tw, created_from_zh_tw
)
SELECT
  'xjz_rebirth_capture_targets_csharp',
  CAST(target_id AS CHAR),
  '轉生任務/轉生條件',
  CONCAT('確認轉生任務「', name_zh_tw, '」的需求、結果與封包。'),
  request_hint_zh_tw,
  wanted_fields_zh_tw,
  sample_table,
  CASE WHEN capture_priority = 'high' THEN '高' WHEN capture_priority = 'medium' THEN '中' ELSE '低' END,
  evidence_status,
  '確認後回填轉生階段、需求等級、神仙需求、道具需求、成功/失敗結果與轉生封包。',
  'C# 轉生抓包目標'
FROM god2_research.xjz_rebirth_capture_targets_csharp
WHERE packet_evidence_boundary = 1;

INSERT IGNORE INTO god2_research.xjz_deterministic_blackbox_work_queue (
  source_table_name, source_identifier, game_function_zh_tw, blackbox_goal_zh_tw,
  required_action_zh_tw, expected_observation_zh_tw, target_sample_table, priority_zh_tw,
  evidence_status_zh_tw, server_apply_after_capture_zh_tw, created_from_zh_tw
)
SELECT
  'xjz_item_effect_runtime_reconciliation',
  CONCAT(client_item_id, ':', handler_name, ':', source_pack_id),
  '道具使用效果',
  CONCAT('確認道具「', item_name_zh_tw, '」的使用效果與正式 runtime 是否一致。'),
  CONCAT('在可用場景使用道具，記錄使用前後 HP、MP、BUFF、狀態、背包數量與封包。'),
  'client_item_id, formal_item_id, hp_before, hp_after, mp_before, mp_after, buff_state, inventory_delta, raw_packet',
  'item_use_transition_sample',
  CASE WHEN promotion_status_zh_tw = '正式道具未連結' THEN '高' ELSE '中' END,
  promotion_status_zh_tw,
  '確認後回填 item_effects、使用範圍、數值、持續時間與 runtime eligibility。',
  '道具效果 runtime 對照'
FROM god2_research.xjz_item_effect_runtime_reconciliation
WHERE runtime_match <> 1 OR runtime_match IS NULL;

DROP VIEW IF EXISTS god2_research.vw_xjz_deterministic_blackbox_work_queue_summary_zh_tw;
CREATE VIEW god2_research.vw_xjz_deterministic_blackbox_work_queue_summary_zh_tw AS
SELECT
  game_function_zh_tw,
  priority_zh_tw,
  COUNT(*) AS work_item_count,
  MIN(source_table_name) AS example_source_table,
  '確定性黑箱可先跑，不包含機率、刷新、完整 AI、完整公式係數' AS scope_note_zh_tw
FROM god2_research.xjz_deterministic_blackbox_work_queue
WHERE deferred_probability_or_ai_flag = 0
GROUP BY game_function_zh_tw, priority_zh_tw;

DROP VIEW IF EXISTS god2_research.vw_xjz_blackbox_excluded_deferred_systems_zh_tw;
CREATE VIEW god2_research.vw_xjz_blackbox_excluded_deferred_systems_zh_tw AS
SELECT
  excluded_key,
  game_function_zh_tw,
  excluded_reason_zh_tw,
  resume_condition_zh_tw
FROM god2_research.xjz_blackbox_excluded_deferred_systems;
