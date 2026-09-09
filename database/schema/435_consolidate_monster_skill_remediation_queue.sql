-- Migration 435: consolidate duplicate monster skill remediation queue rows.
-- Function: keep the monster battle skill worklist readable after XJZ probe intake.

UPDATE god2_research.static_gap_remediation_queue
SET priority_rank = 40,
    remediation_category_zh_tw = '補證清單',
    game_function_zh_tw = '怪物戰鬥技能',
    evidence_policy_zh_tw = '客戶端還原只能證明需補測的怪物身份，不能直接證明技能 ID、觸發條件或使用機率。',
    remediation_action_zh_tw = '使用 xjz_monster_skill_probe_targets 對 215 隻怪物補抓技能欄位、技能效果、使用機率與觸發條件；未確認前不啟用正式 monster_skills。',
    dependency_check_zh_tw = '正式 monster_skills 需要 monster_id 與 skill_id 外鍵；目前只建立補證清單，不寫入正式技能表。',
    current_status_zh_tw = '已建立怪物技能抓包目標，正式技能仍待證據',
    safe_to_apply_automatically = 0,
    safe_to_drop_legacy_now = 0,
    reviewed_at_utc = UTC_TIMESTAMP()
WHERE formal_table_name = 'monster_skills';

DELETE newer
FROM god2_research.static_gap_remediation_queue newer
JOIN god2_research.static_gap_remediation_queue keeper
  ON keeper.formal_table_name = newer.formal_table_name
 AND keeper.formal_table_name = 'monster_skills'
 AND keeper.queue_id < newer.queue_id;
