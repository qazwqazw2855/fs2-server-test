USE god2_game;

UPDATE god2_research.xjz_executable_item_effect_candidates c
LEFT JOIN god2_research.xjz_item_effect_runtime_reconciliation r
    ON r.client_item_id = c.item_id
   AND r.handler_name = c.handler_name
   AND r.source_pack_id = c.source_pack_id
SET c.handler_parameters_zh_tw = CASE c.handler_name
    WHEN 'restore_hp' THEN CONCAT(
        '恢復生命值；候選數值：',
        COALESCE(CAST(r.expected_numeric_value AS CHAR), '待補證')
    )
    WHEN 'restore_mp' THEN CONCAT(
        '恢復法力值；候選數值：',
        COALESCE(CAST(r.expected_numeric_value AS CHAR), '待補證')
    )
    WHEN 'restore_hp_mp' THEN '同時恢復生命與法力；個別數值待補證'
    WHEN 'clear_status' THEN CONCAT(
        '解除異常狀態；候選狀態：',
        CASE r.expected_effect_type_zh_tw
            WHEN '解除中毒' THEN '中毒'
            WHEN '解除睡眠' THEN '睡眠'
            WHEN '解除神仙封' THEN '神仙封'
            WHEN '解除石化' THEN '石化'
            WHEN '解除混亂' THEN '混亂'
            ELSE '待補證'
        END
    )
    WHEN 'apply_stat_buff' THEN CONCAT(
        '套用能力提升；能力：',
        CASE r.expected_effect_type_zh_tw
            WHEN '提升腕力' THEN '腕力'
            WHEN '提升體力' THEN '體力'
            WHEN '提升智力' THEN '智力'
            WHEN '提升速度' THEN '速度'
            ELSE '待補證'
        END,
        '；數值：',
        COALESCE(CAST(r.expected_numeric_value AS CHAR), '待補證'),
        '；分鐘：',
        COALESCE(CAST(r.expected_duration_seconds DIV 60 AS CHAR), '待補證')
    )
    WHEN 'learn_skill' THEN '學習技能；需由技能書與技能 catalog 對照'
    WHEN 'teleport' THEN '傳送道具；目的地需由傳送封包或黑箱測試確認'
    WHEN 'feed_pet_or_mount' THEN '餵食寵物或坐騎；實際成長或飽食效果需黑箱測試'
    ELSE '候選參數已整理為 handler 類型；細節需後續對照'
END
WHERE c.source_pack_id = 'XJZ-csharp-evidence-capture-pack-20260818';

UPDATE god2_research.xjz_executable_item_effect_candidates
SET handler_parameters_zh_tw = REPLACE(handler_parameters_zh_tw, 'null', '待補證')
WHERE source_pack_id = 'XJZ-csharp-evidence-capture-pack-20260818'
  AND handler_parameters_zh_tw LIKE '%null%';

INSERT INTO god2_research.database_cleanup_audit (
    cleanup_key,
    cleanup_action_zh_tw,
    affected_schema,
    affected_table,
    reason_zh_tw,
    evidence_zh_tw
) VALUES
('refine_xjz_item_effect_candidate_parameter_labels:handler_parameters_zh_tw',
 '修正 XJZ 道具效果候選參數繁中顯示',
 'god2_research',
 'xjz_executable_item_effect_candidates',
 '將英文 stat 代碼與 null 字樣改成繁中能力名稱與待補證文字，避免資料庫可見面混入看不懂的工程值。',
 '僅更新研究候選說明欄；正式 item_effects 與 runtime 對帳結果不變。')
ON DUPLICATE KEY UPDATE
    cleanup_action_zh_tw = VALUES(cleanup_action_zh_tw),
    reason_zh_tw = VALUES(reason_zh_tw),
    evidence_zh_tw = VALUES(evidence_zh_tw),
    applied_at_utc = UTC_TIMESTAMP(6);
