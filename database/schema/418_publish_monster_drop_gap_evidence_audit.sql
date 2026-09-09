CREATE TABLE IF NOT EXISTS god2_research.monster_drop_gap_evidence_audit (
    audit_id int unsigned NOT NULL AUTO_INCREMENT,
    legacy_relationship_id char(64) NOT NULL,
    legacy_monster_id bigint NOT NULL,
    formal_monster_id bigint NULL,
    monster_name_zh_tw varchar(200) NULL,
    legacy_item_id bigint NOT NULL,
    formal_item_id bigint NULL,
    item_name_zh_tw varchar(200) NULL,
    legacy_drop_table_id bigint NULL,
    minimum_quantity int NULL,
    maximum_quantity int NULL,
    effective_drop_chance decimal(18,9) NOT NULL,
    legacy_relationship_status varchar(64) NOT NULL,
    legacy_production_enabled tinyint(1) NOT NULL,
    formal_drop_id bigint NULL,
    formal_drop_enabled tinyint(1) NULL,
    readiness_status_zh_tw varchar(128) NOT NULL,
    game_function_zh_tw varchar(256) NOT NULL,
    missing_evidence_zh_tw varchar(512) NOT NULL,
    next_action_zh_tw varchar(512) NOT NULL,
    safe_to_promote_now tinyint(1) NOT NULL DEFAULT 0,
    reviewed_at_utc timestamp NOT NULL DEFAULT utc_timestamp(),
    PRIMARY KEY (audit_id),
    UNIQUE KEY ux_monster_drop_gap_relationship (legacy_relationship_id),
    KEY ix_monster_drop_gap_readiness (readiness_status_zh_tw),
    KEY ix_monster_drop_gap_monster (legacy_monster_id),
    KEY ix_monster_drop_gap_item (legacy_item_id),
    KEY ix_monster_drop_gap_formal_drop (formal_drop_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM god2_research.monster_drop_gap_evidence_audit;

INSERT INTO god2_research.monster_drop_gap_evidence_audit (
    legacy_relationship_id,
    legacy_monster_id,
    formal_monster_id,
    monster_name_zh_tw,
    legacy_item_id,
    formal_item_id,
    item_name_zh_tw,
    legacy_drop_table_id,
    minimum_quantity,
    maximum_quantity,
    effective_drop_chance,
    legacy_relationship_status,
    legacy_production_enabled,
    formal_drop_id,
    formal_drop_enabled,
    readiness_status_zh_tw,
    game_function_zh_tw,
    missing_evidence_zh_tw,
    next_action_zh_tw,
    safe_to_promote_now
)
SELECT
    rel.RelationshipId AS legacy_relationship_id,
    rel.MonsterId AS legacy_monster_id,
    monster_row.monster_id AS formal_monster_id,
    monster_row.name_zh_tw AS monster_name_zh_tw,
    rel.ItemId AS legacy_item_id,
    item_row.item_id AS formal_item_id,
    item_row.name_zh_tw AS item_name_zh_tw,
    rel.DropTableId AS legacy_drop_table_id,
    rel.MinimumQuantity AS minimum_quantity,
    rel.MaximumQuantity AS maximum_quantity,
    rel.EffectiveDropChance AS effective_drop_chance,
    rel.DropRelationshipStatus AS legacy_relationship_status,
    rel.ProductionDropEnabled AS legacy_production_enabled,
    formal_drop.drop_id AS formal_drop_id,
    formal_drop.enabled AS formal_drop_enabled,
    CASE
        WHEN monster_row.monster_id IS NULL AND item_row.item_id IS NULL THEN '缺正式怪物與正式道具對應'
        WHEN monster_row.monster_id IS NULL THEN '缺正式怪物對應'
        WHEN item_row.item_id IS NULL THEN '缺正式道具對應'
        WHEN formal_drop.drop_id IS NOT NULL AND formal_drop.enabled = 1 THEN '正式啟用掉落已存在'
        WHEN formal_drop.drop_id IS NOT NULL THEN '正式掉落已存在但未啟用'
        WHEN rel.ProductionDropEnabled <> 1 THEN '舊掉落未達正式啟用條件'
        ELSE '可人工審核後升級'
    END AS readiness_status_zh_tw,
    '怪物死亡掉落道具功能' AS game_function_zh_tw,
    CASE
        WHEN monster_row.monster_id IS NULL AND item_row.item_id IS NULL THEN '舊掉落關係的 MonsterId 與 ItemId 都無法對到正式表；不能建立正式掉落。'
        WHEN monster_row.monster_id IS NULL THEN '舊掉落關係的 MonsterId 無法對到 god2_game.monsters；不能把未知怪物 ID 當正式怪物。'
        WHEN item_row.item_id IS NULL THEN '舊掉落關係的 ItemId 無法對到 god2_game.items；不能把未知道具 ID 當正式掉落物。'
        WHEN formal_drop.drop_id IS NOT NULL AND formal_drop.enabled = 1 THEN '正式啟用掉落已存在；後續只需驗證死亡獎勵流程。'
        WHEN formal_drop.drop_id IS NOT NULL THEN '正式掉落列已存在但未啟用，通常代表來源仍不完整或機率/數量未證實。'
        WHEN rel.ProductionDropEnabled <> 1 THEN '舊候選掉落尚未通過正式啟用 Gate；不能自動升級。'
        ELSE '怪物、道具與啟用條件具備，但仍需人工確認機率、數量與掉落群組。'
    END AS missing_evidence_zh_tw,
    CASE
        WHEN monster_row.monster_id IS NULL THEN '下一批先比對舊 monster ID、客戶端怪物 ID、正式 monster code/resource_id，建立安全怪物 ID 對照後再處理掉落。'
        WHEN item_row.item_id IS NULL THEN '下一批先補正式道具本體或確認舊道具已淘汰，再處理掉落。'
        WHEN formal_drop.drop_id IS NOT NULL AND formal_drop.enabled = 1 THEN '下一批可跑怪物死亡掉落流程測試，確認掉落會發到玩家或地面。'
        WHEN formal_drop.drop_id IS NOT NULL THEN '下一批補齊機率、數量與來源證據後，再決定是否啟用正式掉落。'
        WHEN rel.ProductionDropEnabled <> 1 THEN '下一批補齊 ProductionDropEnabled 的證據條件，不足者維持候選。'
        ELSE '下一批建立正式 monster_drops 補入 migration，並同步死亡獎勵測試。'
    END AS next_action_zh_tw,
    CASE
        WHEN monster_row.monster_id IS NOT NULL
         AND item_row.item_id IS NOT NULL
         AND rel.ProductionDropEnabled = 1
         AND formal_drop.drop_id IS NULL
            THEN 1
        ELSE 0
    END AS safe_to_promote_now
FROM god2.monster_drop_relationships rel
LEFT JOIN god2_game.monsters monster_row ON monster_row.monster_id = rel.MonsterId
LEFT JOIN god2_game.items item_row ON item_row.item_id = rel.ItemId
LEFT JOIN god2_game.monster_drops formal_drop
    ON formal_drop.monster_id = rel.MonsterId
   AND formal_drop.item_id = rel.ItemId
ORDER BY rel.MonsterId, rel.ItemId, rel.RelationshipId;

UPDATE god2_research.static_gap_remediation_queue
SET
    evidence_policy_zh_tw = '舊 monster_drop_relationships 有 47 筆候選關係，但目前 MonsterId 無法直接對到正式 monsters，禁止自動啟用或新增正式掉落。',
    remediation_action_zh_tw = '先使用 monster_drop_gap_evidence_audit 追蹤每筆掉落缺少的正式怪物對應、正式道具對應與啟用證據；不得把舊怪物 ID 當正式怪物 ID。',
    dependency_check_zh_tw = '需檢查 god2_game.monsters、items、monster_drops、死亡獎勵、背包或地面掉落接收流程；缺怪物對應或機率證據者不得啟用。',
    current_status_zh_tw = '缺正式怪物對應，暫停自動補',
    safe_to_apply_automatically = 0,
    safe_to_drop_legacy_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE legacy_table_name = 'drop_tables'
  AND formal_table_name = 'monster_drops';

UPDATE god2_research.legacy_god2_static_table_coverage
SET
    coverage_status_zh_tw = '正式掉落部分存在，但舊候選缺正式怪物對應與啟用證據',
    cleanup_decision_zh_tw = '保留舊 drop_tables 與 monster_drop_relationships；建立安全怪物 ID 對照並驗證掉落條件前，不得封存舊表。',
    next_verification_step_zh_tw = '依 monster_drop_gap_evidence_audit 先補正式怪物對應，再比對道具、數量、機率與 ProductionDropEnabled。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE legacy_table_name = 'drop_tables'
  AND formal_table_name = 'monster_drops';

UPDATE god2_research.database_schema_consolidation_status
SET
    consolidation_decision_zh_tw = '怪物掉落缺口已完成證據稽核；舊掉落候選缺正式怪物對應，禁止自動升級或啟用。',
    next_cleanup_step_zh_tw = '下一批先處理舊怪物 ID 與正式 monster 的安全對照，或改處理道具缺口中可讀且可對應的資料。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE schema_name = 'god2';
