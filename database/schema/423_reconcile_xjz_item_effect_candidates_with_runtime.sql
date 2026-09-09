-- 423_reconcile_xjz_item_effect_candidates_with_runtime.sql
-- Purpose: reconcile XJZ client-recovered item effect candidates with formal runtime item_effects.
-- Boundary: do not invent missing numeric values. Null amount/hp/mp candidates remain blocked for packet or black-box testing.

CREATE TABLE IF NOT EXISTS god2_research.xjz_item_effect_runtime_reconciliation (
  client_item_id INT NOT NULL,
  formal_item_id INT NULL,
  item_name_zh_tw VARCHAR(128) NOT NULL,
  handler_name VARCHAR(64) NOT NULL,
  expected_effect_type_zh_tw VARCHAR(64) NULL,
  expected_numeric_value BIGINT NULL,
  expected_duration_seconds INT NULL,
  runtime_effect_index INT NULL,
  runtime_numeric_value BIGINT NULL,
  runtime_duration_seconds INT NULL,
  runtime_match TINYINT(1) NOT NULL,
  promotion_status_zh_tw VARCHAR(128) NOT NULL,
  action_zh_tw VARCHAR(256) NOT NULL,
  source_pack_id VARCHAR(96) NOT NULL,
  updated_at_utc TIMESTAMP NOT NULL DEFAULT UTC_TIMESTAMP(),
  PRIMARY KEY (client_item_id, handler_name, source_pack_id),
  KEY idx_xjz_item_effect_reconcile_formal_item (formal_item_id),
  KEY idx_xjz_item_effect_reconcile_status (promotion_status_zh_tw)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM god2_research.xjz_item_effect_runtime_reconciliation
WHERE source_pack_id='XJZ-csharp-evidence-capture-pack-20260818';

INSERT INTO god2_research.xjz_item_effect_runtime_reconciliation
(client_item_id,formal_item_id,item_name_zh_tw,handler_name,expected_effect_type_zh_tw,expected_numeric_value,
 expected_duration_seconds,runtime_effect_index,runtime_numeric_value,runtime_duration_seconds,runtime_match,
 promotion_status_zh_tw,action_zh_tw,source_pack_id)
WITH expected AS (
  SELECT candidate.item_id AS client_item_id,
         candidate.formal_item_id,
         candidate.name_zh_tw AS item_name_zh_tw,
         candidate.handler_name,
         CASE
             WHEN candidate.handler_name='restore_hp'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.amount')) REGEXP '^[0-9]+$' THEN '生命恢復'
             WHEN candidate.handler_name='restore_mp'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.amount')) REGEXP '^[0-9]+$' THEN '法力恢復'
             WHEN candidate.handler_name='clear_status'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.status'))='poison' THEN '解除中毒'
             WHEN candidate.handler_name='clear_status'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.status'))='sleep' THEN '解除睡眠'
             WHEN candidate.handler_name='clear_status'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.status'))='seal' THEN '解除神仙封'
             WHEN candidate.handler_name='clear_status'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.status'))='petrify' THEN '解除石化'
             WHEN candidate.handler_name='clear_status'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.status'))='confuse' THEN '解除混亂'
             WHEN candidate.handler_name='apply_stat_buff'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.stat'))='strength'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.amount')) REGEXP '^[0-9]+$' THEN '提升腕力'
             WHEN candidate.handler_name='apply_stat_buff'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.stat'))='vitality'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.amount')) REGEXP '^[0-9]+$' THEN '提升體力'
             WHEN candidate.handler_name='apply_stat_buff'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.stat'))='intelligence'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.amount')) REGEXP '^[0-9]+$' THEN '提升智力'
             WHEN candidate.handler_name='apply_stat_buff'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.stat'))='speed'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.amount')) REGEXP '^[0-9]+$' THEN '提升速度'
             ELSE NULL
         END AS expected_effect_type_zh_tw,
         CASE
             WHEN candidate.handler_name='clear_status'
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.status')) IN ('poison','sleep','seal','petrify','confuse') THEN 1
             WHEN candidate.handler_name IN ('restore_hp','restore_mp','apply_stat_buff')
                  AND JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.amount')) REGEXP '^[0-9]+$'
                  THEN CAST(JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.amount')) AS UNSIGNED)
             ELSE NULL
         END AS expected_numeric_value,
         CASE
             WHEN JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.duration_minutes')) REGEXP '^[0-9]+$'
                  THEN CAST(JSON_UNQUOTE(JSON_EXTRACT(candidate.handler_param_json,'$.duration_minutes')) AS UNSIGNED) * 60
             ELSE NULL
         END AS expected_duration_seconds
  FROM god2_research.xjz_executable_item_effect_candidates candidate
  WHERE candidate.source_pack_id='XJZ-csharp-evidence-capture-pack-20260818'
    AND candidate.handler_name IN ('restore_hp','restore_mp','clear_status','apply_stat_buff','restore_hp_mp')
)
SELECT expected.client_item_id,
       expected.formal_item_id,
       expected.item_name_zh_tw,
       expected.handler_name,
       expected.expected_effect_type_zh_tw,
       expected.expected_numeric_value,
       expected.expected_duration_seconds,
       runtime_effect.effect_index,
       runtime_effect.numeric_value,
       runtime_effect.duration_seconds,
       CASE WHEN runtime_effect.item_id IS NULL THEN 0 ELSE 1 END AS runtime_match,
       CASE
           WHEN expected.formal_item_id IS NULL THEN '正式道具未連結'
           WHEN expected.expected_effect_type_zh_tw IS NULL OR expected.expected_numeric_value IS NULL THEN '候選缺少可執行數值'
           WHEN runtime_effect.item_id IS NULL THEN '正式服務端缺效果'
           ELSE '已實裝且數值一致'
       END AS promotion_status_zh_tw,
       CASE
           WHEN expected.formal_item_id IS NULL THEN '先補正式道具主表連結。'
           WHEN expected.expected_effect_type_zh_tw IS NULL OR expected.expected_numeric_value IS NULL THEN '不能猜數值，留待官方封包或黑箱測試。'
           WHEN runtime_effect.item_id IS NULL THEN '可由候選轉入 god2_game.item_effects。'
           ELSE '不需重複寫入，正式 runtime 已可使用。'
       END AS action_zh_tw,
       'XJZ-csharp-evidence-capture-pack-20260818'
FROM expected
LEFT JOIN god2_game.item_effects runtime_effect
  ON runtime_effect.item_id=expected.formal_item_id
 AND runtime_effect.effect_type=expected.expected_effect_type_zh_tw
 AND runtime_effect.numeric_value=expected.expected_numeric_value
 AND (runtime_effect.duration_seconds <=> expected.expected_duration_seconds)
 AND runtime_effect.enabled=1
 AND runtime_effect.runtime_eligible=1;

CREATE OR REPLACE VIEW god2_research.vw_xjz_item_effect_runtime_reconciliation_readable AS
SELECT item_name_zh_tw AS `道具名稱`,
       client_item_id AS `客戶端道具ID`,
       formal_item_id AS `正式服務端道具ID`,
       handler_name AS `客戶端還原處理器`,
       expected_effect_type_zh_tw AS `預期效果`,
       expected_numeric_value AS `預期數值`,
       expected_duration_seconds AS `預期持續秒數`,
       promotion_status_zh_tw AS `套用狀態`,
       action_zh_tw AS `下一步`
FROM god2_research.xjz_item_effect_runtime_reconciliation
ORDER BY promotion_status_zh_tw, handler_name, client_item_id;

UPDATE god2_research.xjz_csharp_apply_queue
SET safe_apply_level_zh_tw=(
        SELECT CONCAT(
            '已對照正式 runtime：',
            SUM(CASE WHEN promotion_status_zh_tw='已實裝且數值一致' THEN 1 ELSE 0 END),
            ' 筆已實裝一致，',
            SUM(CASE WHEN promotion_status_zh_tw='正式服務端缺效果' THEN 1 ELSE 0 END),
            ' 筆可補，',
            SUM(CASE WHEN promotion_status_zh_tw='候選缺少可執行數值' THEN 1 ELSE 0 END),
            ' 筆待補證'
        )
        FROM god2_research.xjz_item_effect_runtime_reconciliation
        WHERE source_pack_id='XJZ-csharp-evidence-capture-pack-20260818'
    ),
    next_server_action_zh_tw='先處理套用狀態為正式服務端缺效果的項目；候選缺少可執行數值者不得猜值。'
WHERE queue_key='item_effect_rules'
  AND source_pack_id='XJZ-csharp-evidence-capture-pack-20260818';
