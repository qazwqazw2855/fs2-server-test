CREATE TABLE IF NOT EXISTS god2_research.container_gap_evidence_audit (
    audit_id int unsigned NOT NULL AUTO_INCREMENT,
    legacy_container_item_id bigint NOT NULL,
    formal_item_id bigint NULL,
    formal_container_id bigint NULL,
    container_name_zh_tw varchar(200) NULL,
    legacy_reward_count int unsigned NOT NULL,
    formal_reward_count int unsigned NOT NULL,
    readiness_status_zh_tw varchar(128) NOT NULL,
    game_function_zh_tw varchar(256) NOT NULL,
    missing_evidence_zh_tw varchar(512) NOT NULL,
    next_action_zh_tw varchar(512) NOT NULL,
    safe_to_promote_now tinyint(1) NOT NULL DEFAULT 0,
    reviewed_at_utc timestamp NOT NULL DEFAULT utc_timestamp(),
    PRIMARY KEY (audit_id),
    UNIQUE KEY ux_container_gap_legacy_container (legacy_container_item_id),
    KEY ix_container_gap_readiness (readiness_status_zh_tw),
    KEY ix_container_gap_formal_item (formal_item_id),
    KEY ix_container_gap_formal_container (formal_container_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM god2_research.container_gap_evidence_audit;

INSERT INTO god2_research.container_gap_evidence_audit (
    legacy_container_item_id,
    formal_item_id,
    formal_container_id,
    container_name_zh_tw,
    legacy_reward_count,
    formal_reward_count,
    readiness_status_zh_tw,
    game_function_zh_tw,
    missing_evidence_zh_tw,
    next_action_zh_tw,
    safe_to_promote_now
)
SELECT
    source_rows.ContainerItemId AS legacy_container_item_id,
    item_row.item_id AS formal_item_id,
    container_row.container_id AS formal_container_id,
    item_row.name_zh_tw AS container_name_zh_tw,
    source_rows.legacy_reward_count,
    COUNT(reward_row.reward_order) AS formal_reward_count,
    CASE
        WHEN item_row.item_id IS NULL THEN '缺正式道具本體，不能建立容器'
        WHEN container_row.container_id IS NULL THEN '缺正式容器主表'
        WHEN COUNT(reward_row.reward_order) = 0 THEN '缺正式容器獎勵'
        ELSE '正式容器與獎勵已存在'
    END AS readiness_status_zh_tw,
    '道具箱、禮包、容器開啟獎勵功能' AS game_function_zh_tw,
    CASE
        WHEN item_row.item_id IS NULL THEN '舊容器 ID 在 god2_game.items 找不到可讀正式道具；不能只用數字 ID 建立正式容器。'
        WHEN container_row.container_id IS NULL THEN '正式 items 已有容器道具，但 god2_game.containers 缺主表資料。'
        WHEN COUNT(reward_row.reward_order) = 0 THEN '正式容器主表存在，但 container_rewards 沒有開啟後獎勵。'
        ELSE '容器道具、正式容器主表與獎勵明細都已存在；後續只需驗證使用流程。'
    END AS missing_evidence_zh_tw,
    CASE
        WHEN item_row.item_id IS NULL THEN '下一批先從舊 items、正式 items 缺口或客戶端道具證據補回這些容器道具本體，再建立容器主表。'
        WHEN container_row.container_id IS NULL THEN '下一批可依正式 items.name_zh_tw 建立 containers，roll_count 先保守留空，避免猜開啟次數。'
        WHEN COUNT(reward_row.reward_order) = 0 THEN '下一批比對 container_item_relationships，補 container_rewards 的道具與數量。'
        ELSE '下一批可轉為道具使用流程測試，確認開啟容器會消耗道具並發獎勵。'
    END AS next_action_zh_tw,
    CASE
        WHEN item_row.item_id IS NOT NULL AND container_row.container_id IS NULL THEN 1
        ELSE 0
    END AS safe_to_promote_now
FROM (
    SELECT ContainerItemId, COUNT(*) AS legacy_reward_count
    FROM god2.container_item_relationships
    GROUP BY ContainerItemId
) source_rows
LEFT JOIN god2_game.items item_row ON item_row.item_id = source_rows.ContainerItemId
LEFT JOIN god2_game.containers container_row ON container_row.item_id = item_row.item_id
LEFT JOIN god2_game.container_rewards reward_row ON reward_row.container_id = container_row.container_id
GROUP BY
    source_rows.ContainerItemId,
    item_row.item_id,
    container_row.container_id,
    item_row.name_zh_tw,
    source_rows.legacy_reward_count
ORDER BY source_rows.ContainerItemId;

UPDATE god2_research.static_gap_remediation_queue
SET
    evidence_policy_zh_tw = '容器獎勵明細已有 470 筆；目前真正缺口是部分舊容器 ID 找不到正式 items 道具本體，禁止用純數字 ID 硬建正式容器。',
    remediation_action_zh_tw = '依 container_gap_evidence_audit 先補缺失的正式容器道具本體；已有正式 item 與 containers 的容器不重複補。',
    dependency_check_zh_tw = '需檢查 god2_game.items、containers、container_rewards、item_usage_rules、背包消耗與獎勵發放流程；缺正式 item 的容器不得升級。',
    current_status_zh_tw = '部分完成，缺正式容器道具本體',
    safe_to_apply_automatically = 0,
    safe_to_drop_legacy_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE legacy_table_name = 'container_item_relationships'
  AND formal_table_name = 'containers';

UPDATE god2_research.legacy_god2_static_table_coverage
SET
    coverage_status_zh_tw = '正式容器主表部分覆蓋，部分舊容器缺正式道具本體',
    cleanup_decision_zh_tw = '保留舊 container_item_relationships；補齊缺失容器道具本體並驗證開啟流程前，不得封存舊表。',
    next_verification_step_zh_tw = '依 container_gap_evidence_audit 先處理缺正式 items 的容器，再驗證 containers 與 container_rewards 是否完整。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE legacy_table_name = 'container_item_relationships'
  AND formal_table_name = 'containers';

UPDATE god2_research.database_schema_consolidation_status
SET
    consolidation_decision_zh_tw = '容器/禮包缺口已完成證據稽核；正式獎勵明細已存在，缺口集中在部分容器道具本體未進正式 items。',
    next_cleanup_step_zh_tw = '下一批優先處理 safe_to_promote_now=1 的容器主表缺口，缺正式 item 的容器需先回到道具整合流程。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE schema_name = 'god2';
