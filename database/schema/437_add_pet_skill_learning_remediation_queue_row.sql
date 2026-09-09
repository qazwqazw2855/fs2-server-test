-- Migration 437: add missing remediation queue row for battle-pet feed skill learning.
-- Function: make pet_skill_learning_items visible in the formal feature/gap tracker.

INSERT INTO god2_research.static_gap_remediation_queue
  (legacy_table_name, formal_table_name, priority_rank, remediation_category_zh_tw,
   game_function_zh_tw, evidence_policy_zh_tw, remediation_action_zh_tw,
   dependency_check_zh_tw, current_status_zh_tw, safe_to_apply_automatically,
   safe_to_drop_legacy_now, reviewed_at_utc)
SELECT
  'xjz_pet_skill_learning_item_candidates', 'pet_skill_learning_items', 45, '候選對照',
  '戰寵餵食學技能',
  '正式道具描述可證明餵食學技能語意，但正式 runtime 需要唯一 skill_id；重名或缺名不可自動啟用。',
  '已建立 xjz_pet_skill_learning_item_candidates 共 239 筆候選；先修正式技能名稱/分類重複後，再將唯一對應寫入 pet_skill_learning_items。',
  'pet_skill_learning_items 需要 item_registry.item_id 與 skills.skill_id 外鍵；目前未自動插入正式表，避免技能重名造成錯誤學習。',
  '已建立候選清單，正式對應待技能去重',
  0, 0, UTC_TIMESTAMP()
WHERE NOT EXISTS (
  SELECT 1 FROM god2_research.static_gap_remediation_queue WHERE formal_table_name = 'pet_skill_learning_items'
);

UPDATE god2_research.static_gap_remediation_queue
SET priority_rank = 45,
    remediation_category_zh_tw = '候選對照',
    game_function_zh_tw = '戰寵餵食學技能',
    evidence_policy_zh_tw = '正式道具描述可證明餵食學技能語意，但正式 runtime 需要唯一 skill_id；重名或缺名不可自動啟用。',
    remediation_action_zh_tw = '已建立 xjz_pet_skill_learning_item_candidates 共 239 筆候選；先修正式技能名稱/分類重複後，再將唯一對應寫入 pet_skill_learning_items。',
    dependency_check_zh_tw = 'pet_skill_learning_items 需要 item_registry.item_id 與 skills.skill_id 外鍵；目前未自動插入正式表，避免技能重名造成錯誤學習。',
    current_status_zh_tw = '已建立候選清單，正式對應待技能去重',
    safe_to_apply_automatically = 0,
    safe_to_drop_legacy_now = 0,
    reviewed_at_utc = UTC_TIMESTAMP()
WHERE formal_table_name = 'pet_skill_learning_items';

UPDATE god2_research.formal_table_server_correspondence
SET server_correspondence_status_zh_tw = '服務端表存在；已補候選對照，正式資料待技能去重',
    runtime_usage_policy_zh_tw = '只有道具與技能可唯一對應時才寫入 pet_skill_learning_items；避免戰寵餵食後學錯技能。',
    cleanup_decision_zh_tw = '保留正式空表；用 xjz_pet_skill_learning_item_candidates 作為補齊來源。',
    reviewed_at_utc = UTC_TIMESTAMP(6)
WHERE schema_name = 'god2_game' AND table_name = 'pet_skill_learning_items';
