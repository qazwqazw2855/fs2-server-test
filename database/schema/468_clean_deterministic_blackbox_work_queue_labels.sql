-- 468_clean_deterministic_blackbox_work_queue_labels.sql
-- Purpose: normalize deterministic black-box work queue labels to readable Traditional Chinese categories.

UPDATE god2_research.xjz_deterministic_blackbox_work_queue
SET priority_zh_tw = CASE priority_zh_tw
  WHEN 'highest' THEN '最高'
  WHEN 'high' THEN '高'
  WHEN 'medium' THEN '中'
  WHEN 'normal' THEN '中'
  WHEN 'low' THEN '低'
  ELSE priority_zh_tw
END;

UPDATE god2_research.xjz_deterministic_blackbox_work_queue
SET game_function_zh_tw = '怪物可直接觀測規則'
WHERE source_table_name = 'xjz_monster_observation_capture_targets'
  AND blackbox_goal_zh_tw LIKE '%preempt_trigger%';

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
