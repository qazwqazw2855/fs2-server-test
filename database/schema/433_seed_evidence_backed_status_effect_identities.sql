USE god2_game;

INSERT INTO god2_game.status_effects (
    status_effect_id,
    name_zh_tw,
    effect_type,
    element,
    duration_rounds,
    maximum_stacks,
    stack_mode,
    apply_phase,
    tick_phase,
    expire_phase,
    value_type,
    base_value,
    can_dispel,
    is_buff,
    is_debuff,
    enabled
) VALUES
(1, '神仙封', 'Seal', NULL, NULL, 1, 'RefreshDuration', 'BeforeAction', NULL, 'RoundEnd', NULL, NULL, 1, 0, 1, 1),
(2, '睡眠', 'Sleep', NULL, NULL, 1, 'RefreshDuration', 'BeforeAction', NULL, 'RoundEnd', NULL, NULL, 1, 0, 1, 1),
(3, '石化', 'Petrify', NULL, NULL, 1, 'RefreshDuration', 'BeforeAction', NULL, 'RoundEnd', NULL, NULL, 1, 0, 1, 1),
(4, '混亂', 'Confuse', NULL, NULL, 1, 'RefreshDuration', 'BeforeAction', NULL, 'RoundEnd', NULL, NULL, 1, 0, 1, 1),
(5, '中毒', 'Poison', NULL, NULL, 1, 'RefreshDuration', 'AfterAction', 'RoundStart', 'RoundEnd', NULL, NULL, 1, 0, 1, 1),
(6, '能力提升', 'Buff', NULL, NULL, 1, 'RefreshDuration', 'BeforeAction', NULL, 'RoundEnd', NULL, NULL, 1, 1, 0, 1),
(7, '能力降低', 'Debuff', NULL, NULL, 1, 'RefreshDuration', 'BeforeAction', NULL, 'RoundEnd', NULL, NULL, 1, 0, 1, 1),
(8, '金屬性狀態', 'ElementGold', '金', NULL, 1, 'RefreshDuration', 'BeforeAction', NULL, 'RoundEnd', NULL, NULL, 1, 1, 0, 1),
(9, '木屬性狀態', 'ElementWood', '木', NULL, 1, 'RefreshDuration', 'BeforeAction', NULL, 'RoundEnd', NULL, NULL, 1, 1, 0, 1),
(10, '水屬性狀態', 'ElementWater', '水', NULL, 1, 'RefreshDuration', 'BeforeAction', NULL, 'RoundEnd', NULL, NULL, 1, 1, 0, 1),
(11, '火屬性狀態', 'ElementFire', '火', NULL, 1, 'RefreshDuration', 'BeforeAction', NULL, 'RoundEnd', NULL, NULL, 1, 1, 0, 1),
(12, '土屬性狀態', 'ElementEarth', '土', NULL, 1, 'RefreshDuration', 'BeforeAction', NULL, 'RoundEnd', NULL, NULL, 1, 1, 0, 1)
ON DUPLICATE KEY UPDATE
    name_zh_tw = VALUES(name_zh_tw),
    effect_type = VALUES(effect_type),
    element = VALUES(element),
    maximum_stacks = VALUES(maximum_stacks),
    stack_mode = VALUES(stack_mode),
    apply_phase = VALUES(apply_phase),
    tick_phase = VALUES(tick_phase),
    expire_phase = VALUES(expire_phase),
    can_dispel = VALUES(can_dispel),
    is_buff = VALUES(is_buff),
    is_debuff = VALUES(is_debuff),
    enabled = VALUES(enabled);

UPDATE god2_research.static_gap_remediation_queue
SET
    current_status_zh_tw = '已補狀態身份，公式待證據',
    remediation_action_zh_tw = '已依 C# 戰鬥契約與道具解除狀態證據補入正式 status_effects 身份；持續回合、數值與公式仍需官方封包或黑箱測試。',
    safe_to_apply_automatically = 0,
    safe_to_drop_legacy_now = 0,
    reviewed_at_utc = UTC_TIMESTAMP()
WHERE formal_table_name = 'status_effects';

UPDATE god2_research.formal_table_server_correspondence
SET
    server_correspondence_status_zh_tw = '服務端直接對應，已補基礎資料',
    runtime_usage_policy_zh_tw = '服務端程式碼已有直接表名參照；已補入狀態身份，公式與數值繼續證據優先。',
    cleanup_decision_zh_tw = '保留，正式服務端對應中；不得刪除。',
    reviewed_at_utc = UTC_TIMESTAMP(6)
WHERE schema_name = 'god2_game'
  AND table_name = 'status_effects';

INSERT INTO god2_research.database_cleanup_audit (
    cleanup_key,
    cleanup_action_zh_tw,
    affected_schema,
    affected_table,
    reason_zh_tw,
    evidence_zh_tw
) VALUES
('seed_evidence_backed_status_effect_identities:status_effects',
 '補入正式狀態效果身份資料',
 'god2_game',
 'status_effects',
 'status_effects 是正式服務端直接對應表，不能保持空表；先補 C# 契約與道具解除效果已證明的狀態身份，不猜持續回合與公式數值。',
 '來源：XJZ C# CombatRuntimeContracts StatusEffectKind、ItemEffect runtime cleanse statuses、正式道具效果對帳。')
ON DUPLICATE KEY UPDATE
    cleanup_action_zh_tw = VALUES(cleanup_action_zh_tw),
    reason_zh_tw = VALUES(reason_zh_tw),
    evidence_zh_tw = VALUES(evidence_zh_tw),
    applied_at_utc = UTC_TIMESTAMP(6);
