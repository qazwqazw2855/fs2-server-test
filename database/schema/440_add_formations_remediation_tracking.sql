-- Migration 440: add remediation tracking for empty formal formations table.
-- Function: combat formation / battle positioning definitions.
-- No official formation layout evidence is promoted here.

INSERT INTO god2_research.static_gap_remediation_queue
  (legacy_table_name, formal_table_name, priority_rank, remediation_category_zh_tw,
   game_function_zh_tw, evidence_policy_zh_tw, remediation_action_zh_tw,
   dependency_check_zh_tw, current_status_zh_tw, safe_to_apply_automatically,
   safe_to_drop_legacy_now, reviewed_at_utc)
SELECT
  'xjz_combat_runtime_contract_feature_audit', 'formations', 70, '待補證',
  '戰鬥陣型與站位配置',
  '目前只有服務端表與戰鬥 runtime 需求，尚無官方陣型名稱、格位、加成或限制證據；不可自動產生正式陣型。',
  '後續需透過客戶端介面、官方抓包或黑箱測試確認陣型清單、站位格、開啟條件與戰鬥加成；確認前 formations 保持空表。',
  'formations 會被戰鬥站位與隊伍配置使用；若未確認陣型格位就啟用，可能造成戰鬥位置、範圍技能與 AI 選目標錯誤。',
  '缺官方陣型證據，待抓包或下次設計',
  0, 0, UTC_TIMESTAMP()
WHERE NOT EXISTS (
  SELECT 1 FROM god2_research.static_gap_remediation_queue WHERE formal_table_name = 'formations'
);

UPDATE god2_research.static_gap_remediation_queue
SET priority_rank = 70,
    remediation_category_zh_tw = '待補證',
    game_function_zh_tw = '戰鬥陣型與站位配置',
    evidence_policy_zh_tw = '目前只有服務端表與戰鬥 runtime 需求，尚無官方陣型名稱、格位、加成或限制證據；不可自動產生正式陣型。',
    remediation_action_zh_tw = '後續需透過客戶端介面、官方抓包或黑箱測試確認陣型清單、站位格、開啟條件與戰鬥加成；確認前 formations 保持空表。',
    dependency_check_zh_tw = 'formations 會被戰鬥站位與隊伍配置使用；若未確認陣型格位就啟用，可能造成戰鬥位置、範圍技能與 AI 選目標錯誤。',
    current_status_zh_tw = '缺官方陣型證據，待抓包或下次設計',
    safe_to_apply_automatically = 0,
    safe_to_drop_legacy_now = 0,
    reviewed_at_utc = UTC_TIMESTAMP()
WHERE formal_table_name = 'formations';

UPDATE god2_research.formal_table_server_correspondence
SET server_correspondence_status_zh_tw = '服務端表存在；缺官方陣型證據，暫不自動填入',
    runtime_usage_policy_zh_tw = '陣型名稱、站位格、開啟條件與戰鬥加成確認前不得啟用 formations。',
    cleanup_decision_zh_tw = '保留正式空表；後續由官方抓包、黑箱測試或服主設計補齊。',
    reviewed_at_utc = UTC_TIMESTAMP(6)
WHERE schema_name = 'god2_game' AND table_name = 'formations';
