-- Migration 439: reconcile quest reward remediation status with XJZ capture targets.
-- Function: quest completion rewards. Capture targets exist, but reward rows remain unpromoted
-- until official reward-claim packets or black-box claim samples prove item/exp/currency values.

UPDATE god2_research.static_gap_remediation_queue
SET priority_rank = 35,
    remediation_category_zh_tw = '補證清單',
    game_function_zh_tw = '任務完成獎勵',
    evidence_policy_zh_tw = '任務流程與目標可由客戶端資料確認，但獎勵物品、經驗與貨幣需完成回報封包或黑箱樣本證明。',
    remediation_action_zh_tw = '已建立 xjz_quest_reward_capture_targets 共 418 筆任務獎勵抓包目標；正式 quest_rewards 需等獎勵領取樣本後再寫入。',
    dependency_check_zh_tw = 'quest_rewards 需要 quest_id、reward_order、reward_type 與可能的 item_id；目前不建立假獎勵，避免任務完成後發錯物品或金錢。',
    current_status_zh_tw = '已建立獎勵抓包目標，正式獎勵待領取樣本',
    safe_to_apply_automatically = 0,
    safe_to_drop_legacy_now = 0,
    reviewed_at_utc = UTC_TIMESTAMP()
WHERE formal_table_name = 'quest_rewards';

UPDATE god2_research.formal_table_server_correspondence
SET server_correspondence_status_zh_tw = '服務端表存在；已補獎勵抓包目標，正式獎勵待領取樣本',
    runtime_usage_policy_zh_tw = '只有任務獎勵領取封包或黑箱樣本確認 item、exp、currency 後才寫入 quest_rewards。',
    cleanup_decision_zh_tw = '保留正式空表；用 xjz_quest_reward_capture_targets 作為補證來源。',
    reviewed_at_utc = UTC_TIMESTAMP(6)
WHERE schema_name = 'god2_game' AND table_name = 'quest_rewards';
