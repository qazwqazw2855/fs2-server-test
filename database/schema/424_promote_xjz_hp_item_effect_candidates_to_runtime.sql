USE god2_game;

START TRANSACTION;

CREATE TEMPORARY TABLE god2_research.tmp_xjz_hp_item_effect_promotions AS
SELECT
    r.client_item_id,
    r.formal_item_id AS item_id,
    r.expected_effect_type_zh_tw AS effect_type,
    r.expected_numeric_value AS numeric_value,
    r.source_pack_id
FROM god2_research.xjz_item_effect_runtime_reconciliation r
WHERE r.source_pack_id = 'XJZ-csharp-evidence-capture-pack-20260818'
  AND r.promotion_status_zh_tw = '正式服務端缺效果'
  AND r.formal_item_id IS NOT NULL
  AND r.expected_effect_type_zh_tw = '生命恢復'
  AND r.expected_numeric_value IS NOT NULL
  AND r.expected_numeric_value > 0;

INSERT INTO god2_game.item_effects (
    item_id,
    effect_index,
    effect_type,
    numeric_value,
    usage_scope,
    target_policy,
    effect_text_zh_tw,
    runtime_eligible,
    enabled,
    duration_seconds
)
SELECT
    s.item_id,
    COALESCE(existing_index.max_effect_index, 0) + 1 AS effect_index,
    s.effect_type,
    s.numeric_value,
    '世界與戰鬥' AS usage_scope,
    '可對他人' AS target_policy,
    CONCAT('恢復生命 ', s.numeric_value) AS effect_text_zh_tw,
    1 AS runtime_eligible,
    1 AS enabled,
    NULL AS duration_seconds
FROM god2_research.tmp_xjz_hp_item_effect_promotions s
LEFT JOIN (
    SELECT item_id, MAX(effect_index) AS max_effect_index
    FROM god2_game.item_effects
    GROUP BY item_id
) existing_index
    ON existing_index.item_id = s.item_id
LEFT JOIN god2_game.item_effects duplicate_effect
    ON duplicate_effect.item_id = s.item_id
   AND duplicate_effect.effect_type = s.effect_type
   AND duplicate_effect.numeric_value = s.numeric_value
   AND duplicate_effect.runtime_eligible = 1
   AND duplicate_effect.enabled = 1
WHERE duplicate_effect.item_id IS NULL;

UPDATE god2_research.xjz_item_effect_runtime_reconciliation r
JOIN god2_research.tmp_xjz_hp_item_effect_promotions s
    ON s.client_item_id = r.client_item_id
   AND s.source_pack_id = r.source_pack_id
JOIN god2_game.item_effects e
    ON e.item_id = s.item_id
   AND e.effect_type = s.effect_type
   AND e.numeric_value = s.numeric_value
   AND e.runtime_eligible = 1
   AND e.enabled = 1
SET
    r.runtime_effect_index = e.effect_index,
    r.runtime_numeric_value = e.numeric_value,
    r.runtime_duration_seconds = e.duration_seconds,
    r.runtime_match = 1,
    r.promotion_status_zh_tw = '已實裝且數值一致',
    r.action_zh_tw = '已依 C# 客戶端還原候選補入正式道具生命恢復效果。',
    r.updated_at_utc = UTC_TIMESTAMP()
WHERE r.promotion_status_zh_tw = '正式服務端缺效果';

UPDATE god2_research.xjz_csharp_apply_queue
SET
    safe_apply_level_zh_tw = CONCAT(
        '已對照正式 runtime：',
        (SELECT COUNT(*) FROM god2_research.xjz_item_effect_runtime_reconciliation WHERE promotion_status_zh_tw = '已實裝且數值一致'),
        ' 筆已實裝一致，',
        (SELECT COUNT(*) FROM god2_research.xjz_item_effect_runtime_reconciliation WHERE promotion_status_zh_tw = '正式服務端缺效果'),
        ' 筆可補，',
        (SELECT COUNT(*) FROM god2_research.xjz_item_effect_runtime_reconciliation WHERE promotion_status_zh_tw = '候選缺少可執行數值'),
        ' 筆待補證'
    ),
    next_server_action_zh_tw = '已先補入有明確生命恢復數值的安全項目；候選缺少可執行數值者不得猜值，仍待官方封包或黑箱測試。'
WHERE queue_key = 'item_effect_rules';

COMMIT;
