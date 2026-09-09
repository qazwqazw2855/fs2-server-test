USE god2_game;

ALTER TABLE god2_player.character_lifecycle_idempotency
    ADD COLUMN IF NOT EXISTS result_summary_zh_tw VARCHAR(512) NOT NULL DEFAULT '服務端內部重播結果，供防止重複創角/重複角色生命週期操作使用。' AFTER result_json;

UPDATE god2_player.character_lifecycle_idempotency
SET result_summary_zh_tw = CONCAT(
    '角色生命週期操作：',
    operation,
    '；結果：',
    result_code,
    '；用途：防止同一請求重複建立或重複套用。'
);

ALTER TABLE god2_player.pet_operation_idempotency
    ADD COLUMN IF NOT EXISTS result_summary_zh_tw VARCHAR(512) NOT NULL DEFAULT '服務端內部重播結果，供防止重複寵物操作使用。' AFTER result_json;

UPDATE god2_player.pet_operation_idempotency
SET result_summary_zh_tw = CONCAT(
    '寵物操作防重：',
    CONVERT(operation_type USING utf8mb4) COLLATE utf8mb4_unicode_ci,
    '；角色：',
    character_id,
    '；寵物：',
    COALESCE(CAST(pet_instance_id AS CHAR), '未指定'),
    '；用途：避免同一寵物操作重複套用。'
);

ALTER TABLE god2_player.pet_operation_audit
    ADD COLUMN IF NOT EXISTS detail_summary_zh_tw VARCHAR(512) NOT NULL DEFAULT '服務端內部操作稽核摘要，詳細重播資料僅供服務端追蹤。' AFTER detail_json;

UPDATE god2_player.pet_operation_audit
SET detail_summary_zh_tw = CONCAT(
    '寵物操作稽核：',
    CONVERT(operation_type USING utf8mb4) COLLATE utf8mb4_unicode_ci,
    '；角色：',
    character_id,
    '；寵物：',
    COALESCE(CAST(pet_instance_id AS CHAR), '未指定'),
    '；交易：',
    transaction_id
);

ALTER TABLE god2_player.world_interaction_idempotency
    ADD COLUMN IF NOT EXISTS ResultSummaryZhTw VARCHAR(512) NOT NULL DEFAULT '服務端內部重播結果，供防止重複世界互動使用。' AFTER ResultJson;

UPDATE god2_player.world_interaction_idempotency
SET ResultSummaryZhTw = CONCAT(
    '世界互動防重：',
    InteractionType,
    '；角色：',
    CharacterId,
    '；用途：避免同一世界互動重複套用。'
);

CREATE OR REPLACE VIEW god2_player.vw_character_lifecycle_idempotency_readable AS
SELECT
    account_id AS `帳號編號`,
    operation AS `操作`,
    result_code AS `結果代碼`,
    result_summary_zh_tw AS `結果摘要`,
    created_at_utc AS `建立時間`
FROM god2_player.character_lifecycle_idempotency;

CREATE OR REPLACE VIEW god2_player.vw_pet_operation_idempotency_readable AS
SELECT
    character_id AS `角色編號`,
    operation_type AS `寵物操作`,
    pet_instance_id AS `寵物實例編號`,
    result_summary_zh_tw AS `結果摘要`,
    completed_at_utc AS `完成時間`
FROM god2_player.pet_operation_idempotency;

CREATE OR REPLACE VIEW god2_player.vw_pet_operation_audit_readable AS
SELECT
    audit_id AS `稽核編號`,
    transaction_id AS `交易編號`,
    character_id AS `角色編號`,
    operation_type AS `寵物操作`,
    pet_instance_id AS `寵物實例編號`,
    detail_summary_zh_tw AS `操作摘要`,
    completed_at_utc AS `完成時間`
FROM god2_player.pet_operation_audit;

CREATE OR REPLACE VIEW god2_player.vw_world_interaction_idempotency_runtime_readable AS
SELECT
    CharacterId AS `角色編號`,
    InteractionType AS `互動類型`,
    ResultSummaryZhTw AS `結果摘要`,
    CreatedAtUtc AS `建立時間`,
    CompletedAtUtc AS `完成時間`
FROM god2_player.world_interaction_idempotency;

INSERT INTO god2_research.database_cleanup_audit (
    cleanup_key,
    cleanup_action_zh_tw,
    affected_schema,
    affected_table,
    reason_zh_tw,
    evidence_zh_tw
) VALUES
('add_player_idempotency_readable_summaries:all',
 '新增玩家內部防重表繁中摘要與可讀視圖',
 'god2_player',
 'character_lifecycle_idempotency,pet_operation_idempotency,pet_operation_audit,world_interaction_idempotency',
 '這些表的 JSON 欄位被服務端 persistence 讀寫用於 idempotency replay，不能刪除；新增繁中摘要欄與 readable view，讓人工查表時不用直接看內部 JSON。',
 'MariaDbRuntimeRepositories、MariaDbWorldInteractionRepository、MariaDbPetLifecycleRepository 仍直接讀寫這些 JSON 欄位。')
ON DUPLICATE KEY UPDATE
    cleanup_action_zh_tw = VALUES(cleanup_action_zh_tw),
    reason_zh_tw = VALUES(reason_zh_tw),
    evidence_zh_tw = VALUES(evidence_zh_tw),
    applied_at_utc = UTC_TIMESTAMP(6);
