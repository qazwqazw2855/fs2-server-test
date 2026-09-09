CREATE TABLE IF NOT EXISTS god2_research.npc_dialog_gap_evidence_audit (
    audit_id int unsigned NOT NULL AUTO_INCREMENT,
    legacy_dialog_id bigint NOT NULL,
    legacy_code varchar(128) NOT NULL,
    legacy_npc_id bigint NULL,
    text_key varchar(256) NULL,
    has_formal_npc tinyint(1) NOT NULL,
    has_zh_tw_text tinyint(1) NOT NULL,
    readiness_status_zh_tw varchar(128) NOT NULL,
    game_function_zh_tw varchar(256) NOT NULL,
    missing_evidence_zh_tw varchar(512) NOT NULL,
    next_action_zh_tw varchar(512) NOT NULL,
    safe_to_promote_now tinyint(1) NOT NULL DEFAULT 0,
    reviewed_at_utc timestamp NOT NULL DEFAULT utc_timestamp(),
    PRIMARY KEY (audit_id),
    UNIQUE KEY ux_npc_dialog_gap_legacy_dialog (legacy_dialog_id),
    KEY ix_npc_dialog_gap_readiness (readiness_status_zh_tw),
    KEY ix_npc_dialog_gap_code (legacy_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM god2_research.npc_dialog_gap_evidence_audit;

INSERT INTO god2_research.npc_dialog_gap_evidence_audit (
    legacy_dialog_id,
    legacy_code,
    legacy_npc_id,
    text_key,
    has_formal_npc,
    has_zh_tw_text,
    readiness_status_zh_tw,
    game_function_zh_tw,
    missing_evidence_zh_tw,
    next_action_zh_tw,
    safe_to_promote_now
)
SELECT
    dialog_row.Id AS legacy_dialog_id,
    dialog_row.Code AS legacy_code,
    dialog_row.NpcId AS legacy_npc_id,
    dialog_row.TextKey AS text_key,
    CASE WHEN npc_row.npc_id IS NULL THEN 0 ELSE 1 END AS has_formal_npc,
    CASE WHEN NULLIF(TRIM(localization_row.TextValue), '') IS NULL THEN 0 ELSE 1 END AS has_zh_tw_text,
    CASE
        WHEN npc_row.npc_id IS NULL AND NULLIF(TRIM(localization_row.TextValue), '') IS NULL THEN '缺 NPC 對應與繁中對話文字'
        WHEN npc_row.npc_id IS NULL THEN '缺 NPC 對應'
        WHEN NULLIF(TRIM(localization_row.TextValue), '') IS NULL THEN '缺繁中對話文字'
        ELSE '可人工審核後升級'
    END AS readiness_status_zh_tw,
    'NPC 對話顯示與對話流程功能' AS game_function_zh_tw,
    CASE
        WHEN npc_row.npc_id IS NULL AND NULLIF(TRIM(localization_row.TextValue), '') IS NULL THEN '舊 dialogs 沒有 NpcId，localization_entries 也沒有對應繁中文字；不能產生正式可顯示 NPC 對話。'
        WHEN npc_row.npc_id IS NULL THEN '舊 dialogs 沒有可對到 god2_game.npcs 的 NpcId；不能確定這段對話屬於哪個 NPC。'
        WHEN NULLIF(TRIM(localization_row.TextValue), '') IS NULL THEN 'TextKey 找不到可讀繁中文字；不能把空白或代碼當成正式對話內容。'
        ELSE '具備 NPC 與繁中文字，但仍需人工確認對話流程與下一段對話。'
    END AS missing_evidence_zh_tw,
    CASE
        WHEN npc_row.npc_id IS NULL AND NULLIF(TRIM(localization_row.TextValue), '') IS NULL THEN '下一批需從客戶端對話資料、封包證據或實機對話黑箱測試取得 NPC 對應與繁中內容。'
        WHEN npc_row.npc_id IS NULL THEN '下一批需從 NPC 對話封包或客戶端對話索引補 NPC 對應。'
        WHEN NULLIF(TRIM(localization_row.TextValue), '') IS NULL THEN '下一批需從 localization、客戶端資源或實機對話截圖補繁中內容。'
        ELSE '下一批可建立人工審核後的正式 npc_dialogs 補入 migration。'
    END AS next_action_zh_tw,
    0 AS safe_to_promote_now
FROM god2.dialogs dialog_row
LEFT JOIN god2_game.npcs npc_row ON npc_row.npc_id = dialog_row.NpcId
LEFT JOIN god2.localization_entries localization_row
    ON localization_row.Language = 'zh-TW'
   AND localization_row.TextKey = dialog_row.TextKey
ORDER BY dialog_row.Id;

UPDATE god2_research.static_gap_remediation_queue
SET
    evidence_policy_zh_tw = '舊 dialogs 目前缺 NPC 對應與繁中對話文字，禁止自動補入正式 npc_dialogs；需客戶端證據、封包證據或黑箱測試補齊。',
    remediation_action_zh_tw = '先保留正式 npc_dialogs 空表狀態，使用 npc_dialog_gap_evidence_audit 追蹤每筆缺少的 NPC 對應與繁中文字。',
    dependency_check_zh_tw = '需同時檢查 god2_game.npcs、npc_dialog_options、runtime catalog Dialog 載入與 NPC 互動流程；不得以 code 或 TextKey 當正式對話內容。',
    current_status_zh_tw = '缺證據，暫停自動補',
    safe_to_apply_automatically = 0,
    safe_to_drop_legacy_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE legacy_table_name = 'dialogs'
  AND formal_table_name = 'npc_dialogs';

UPDATE god2_research.legacy_god2_static_table_coverage
SET
    coverage_status_zh_tw = '正式表空表缺口，舊表缺 NPC 對應與繁中內容，禁止自動升級',
    cleanup_decision_zh_tw = '保留舊 dialogs 作來源證據；取得 NPC 對應與繁中對話內容前，不得補入正式 npc_dialogs，也不得封存舊表。',
    next_verification_step_zh_tw = '依 npc_dialog_gap_evidence_audit 逐筆補 NPC 對應與繁中內容，再建立正式 npc_dialogs 補入 migration。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE legacy_table_name = 'dialogs'
  AND formal_table_name = 'npc_dialogs';

UPDATE god2_research.database_schema_consolidation_status
SET
    consolidation_decision_zh_tw = 'NPC 對話缺口已完成證據稽核；舊 dialogs 缺 NPC 對應與繁中內容，禁止自動升級為正式 npc_dialogs。',
    next_cleanup_step_zh_tw = '下一批改處理可自動補的容器、商店或掉落；NPC 對話等取得客戶端/封包/黑箱證據後再補。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE schema_name = 'god2';
