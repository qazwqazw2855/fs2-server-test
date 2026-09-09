USE god2_game;

ALTER TABLE god2_research.xjz_executable_item_effect_candidates
    ADD COLUMN IF NOT EXISTS handler_parameters_zh_tw VARCHAR(512) NULL AFTER target_type;

UPDATE god2_research.xjz_executable_item_effect_candidates
SET handler_parameters_zh_tw = CASE handler_name
    WHEN 'restore_hp' THEN CONCAT('恢復生命值；候選數值：', COALESCE(CAST(JSON_UNQUOTE(JSON_EXTRACT(handler_param_json, '$.amount')) AS CHAR), '待補證'))
    WHEN 'restore_mp' THEN CONCAT('恢復法力值；候選數值：', COALESCE(CAST(JSON_UNQUOTE(JSON_EXTRACT(handler_param_json, '$.amount')) AS CHAR), '待補證'))
    WHEN 'restore_hp_mp' THEN '同時恢復生命與法力；個別數值待補證'
    WHEN 'clear_status' THEN CONCAT('解除異常狀態；候選狀態：', COALESCE(JSON_UNQUOTE(JSON_EXTRACT(handler_param_json, '$.status')), '待補證'))
    WHEN 'apply_stat_buff' THEN CONCAT(
        '套用能力提升；能力：',
        COALESCE(JSON_UNQUOTE(JSON_EXTRACT(handler_param_json, '$.stat')), '待補證'),
        '；數值：',
        COALESCE(CAST(JSON_UNQUOTE(JSON_EXTRACT(handler_param_json, '$.amount')) AS CHAR), '待補證'),
        '；分鐘：',
        COALESCE(CAST(JSON_UNQUOTE(JSON_EXTRACT(handler_param_json, '$.duration_minutes')) AS CHAR), '待補證')
    )
    WHEN 'learn_skill' THEN '學習技能；需由技能書與技能 catalog 對照'
    WHEN 'teleport' THEN '傳送道具；目的地需由傳送封包或黑箱測試確認'
    WHEN 'feed_pet_or_mount' THEN '餵食寵物或坐騎；實際成長/飽食效果需黑箱測試'
    ELSE '候選參數已整理為 handler 類型；細節需後續對照'
END
WHERE source_pack_id = 'XJZ-csharp-evidence-capture-pack-20260818';

ALTER TABLE god2_research.xjz_executable_item_effect_candidates
    DROP COLUMN IF EXISTS handler_param_json;

ALTER TABLE god2_research.xjz_executable_item_effect_candidates
    CHANGE COLUMN IF EXISTS packet_evidence_boundary needs_packet_evidence TINYINT(4) NOT NULL;

ALTER TABLE god2_research.xjz_executable_item_effect_candidate_summary
    CHANGE COLUMN IF EXISTS packet_boundary_count packet_capture_needed_count INT NOT NULL;

ALTER TABLE god2_research.xjz_monster_visual_candidates
    CHANGE COLUMN IF EXISTS raw_source source_file VARCHAR(256) NOT NULL,
    CHANGE COLUMN IF EXISTS raw_line source_line INT NULL,
    CHANGE COLUMN IF EXISTS packet_evidence_boundary needs_packet_evidence TINYINT(1) NOT NULL;

ALTER TABLE god2_research.xjz_npc_shop_slot_candidates
    CHANGE COLUMN IF EXISTS raw_source source_file VARCHAR(256) NOT NULL,
    CHANGE COLUMN IF EXISTS raw_line source_line INT NULL,
    CHANGE COLUMN IF EXISTS packet_evidence_boundary needs_packet_evidence TINYINT(1) NOT NULL;

ALTER TABLE god2_research.xjz_npc_shop_slot_candidate_summary
    CHANGE COLUMN IF EXISTS packet_evidence_boundary needs_packet_evidence TINYINT(1) NOT NULL;

ALTER TABLE god2_research.xjz_quest_reward_capture_targets
    ADD COLUMN IF NOT EXISTS wanted_fields_zh_tw VARCHAR(512) NULL AFTER capture_context;

UPDATE god2_research.xjz_quest_reward_capture_targets
SET wanted_fields_zh_tw = '需抓取：任務編號、獎勵類型、獎勵道具或資源、繁中獎勵名稱、數量、參數、經驗、仙幣、領取前後角色與背包狀態'
WHERE source_pack_id = 'XJZ-csharp-evidence-capture-pack-20260818';

ALTER TABLE god2_research.xjz_quest_reward_capture_targets
    DROP COLUMN IF EXISTS wanted_fields_json;

ALTER TABLE god2_research.xjz_quest_reward_capture_targets
    CHANGE COLUMN IF EXISTS raw_source source_file VARCHAR(256) NOT NULL,
    CHANGE COLUMN IF EXISTS raw_line source_line INT NULL,
    CHANGE COLUMN IF EXISTS packet_evidence_boundary needs_packet_evidence TINYINT(1) NOT NULL;

ALTER TABLE god2_research.xjz_combat_runtime_contract_feature_audit
    CHANGE COLUMN IF EXISTS packet_evidence_boundary needs_packet_evidence TINYINT(1) NOT NULL;

INSERT INTO god2_research.database_cleanup_audit (
    cleanup_key,
    cleanup_action_zh_tw,
    affected_schema,
    affected_table,
    reason_zh_tw,
    evidence_zh_tw
) VALUES
('clean_xjz_research_readable_surface:all',
 '清理 XJZ 研究表可讀欄位',
 'god2_research',
 'xjz_*',
 '將 raw_source/raw_line 改成 source_file/source_line，將 handler/wanted JSON 改成繁中說明或已抽出的正式欄位，降低資料庫可見面的未解碼雜訊。',
 'XJZ 來源 ZIP 與遷移檔仍保留原始證據；正式 DB 表面只保留可讀候選、來源位置與待補證狀態。')
ON DUPLICATE KEY UPDATE
    cleanup_action_zh_tw = VALUES(cleanup_action_zh_tw),
    reason_zh_tw = VALUES(reason_zh_tw),
    evidence_zh_tw = VALUES(evidence_zh_tw),
    applied_at_utc = UTC_TIMESTAMP(6);
