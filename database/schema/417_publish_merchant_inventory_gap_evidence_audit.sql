CREATE TABLE IF NOT EXISTS god2_research.merchant_inventory_gap_evidence_audit (
    audit_id int unsigned NOT NULL AUTO_INCREMENT,
    legacy_merchant_id bigint NOT NULL,
    legacy_npc_id bigint NULL,
    formal_merchant_id bigint NULL,
    formal_npc_id bigint NULL,
    merchant_name_zh_tw varchar(200) NOT NULL,
    candidate_item_count int unsigned NOT NULL,
    production_sale_candidate_count int unsigned NOT NULL,
    formal_inventory_count int unsigned NOT NULL,
    readiness_status_zh_tw varchar(128) NOT NULL,
    game_function_zh_tw varchar(256) NOT NULL,
    missing_evidence_zh_tw varchar(512) NOT NULL,
    next_action_zh_tw varchar(512) NOT NULL,
    safe_to_promote_now tinyint(1) NOT NULL DEFAULT 0,
    reviewed_at_utc timestamp NOT NULL DEFAULT utc_timestamp(),
    PRIMARY KEY (audit_id),
    UNIQUE KEY ux_merchant_inventory_gap_legacy_merchant (legacy_merchant_id),
    KEY ix_merchant_inventory_gap_readiness (readiness_status_zh_tw),
    KEY ix_merchant_inventory_gap_formal_merchant (formal_merchant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM god2_research.merchant_inventory_gap_evidence_audit;

INSERT INTO god2_research.merchant_inventory_gap_evidence_audit (
    legacy_merchant_id,
    legacy_npc_id,
    formal_merchant_id,
    formal_npc_id,
    merchant_name_zh_tw,
    candidate_item_count,
    production_sale_candidate_count,
    formal_inventory_count,
    readiness_status_zh_tw,
    game_function_zh_tw,
    missing_evidence_zh_tw,
    next_action_zh_tw,
    safe_to_promote_now
)
SELECT
    legacy_merchant.Id AS legacy_merchant_id,
    legacy_merchant.NpcId AS legacy_npc_id,
    formal_merchant.merchant_id AS formal_merchant_id,
    formal_merchant.npc_id AS formal_npc_id,
    COALESCE(NULLIF(TRIM(legacy_merchant.NameZhTw), ''), legacy_merchant.Name) AS merchant_name_zh_tw,
    COUNT(candidate.CandidateId) AS candidate_item_count,
    SUM(CASE WHEN candidate.ProductionSaleEnabled = 1 THEN 1 ELSE 0 END) AS production_sale_candidate_count,
    COUNT(formal_inventory.merchant_inventory_id) AS formal_inventory_count,
    CASE
        WHEN formal_merchant.merchant_id IS NULL THEN '缺正式商店本體'
        WHEN COUNT(candidate.CandidateId) = 0 AND COUNT(formal_inventory.merchant_inventory_id) = 0 THEN '缺商店販售商品證據'
        WHEN SUM(CASE WHEN candidate.ProductionSaleEnabled = 1 THEN 1 ELSE 0 END) = 0 AND COUNT(formal_inventory.merchant_inventory_id) = 0 THEN '候選商品未達正式販售條件'
        WHEN COUNT(formal_inventory.merchant_inventory_id) > 0 THEN '已有正式販售資料'
        ELSE '可人工審核後升級'
    END AS readiness_status_zh_tw,
    'NPC 商店販售清單功能' AS game_function_zh_tw,
    CASE
        WHEN formal_merchant.merchant_id IS NULL THEN '舊 merchants 無法對到 god2_game.merchants；不能建立沒有商店主體的販售清單。'
        WHEN COUNT(candidate.CandidateId) = 0 AND COUNT(formal_inventory.merchant_inventory_id) = 0 THEN '目前沒有 merchant_inventory_candidates 或可讀候選列；不能猜商店賣哪些道具、價格或數量。'
        WHEN SUM(CASE WHEN candidate.ProductionSaleEnabled = 1 THEN 1 ELSE 0 END) = 0 AND COUNT(formal_inventory.merchant_inventory_id) = 0 THEN '有候選商品但尚未通過正式販售條件；不能升級為正式商品。'
        WHEN COUNT(formal_inventory.merchant_inventory_id) > 0 THEN '已有正式商店販售資料；後續需驗證購買封包、價格、背包容量與貨幣扣除。'
        ELSE '具備候選商品，但仍需人工確認商品、價格與正式 item 對應。'
    END AS missing_evidence_zh_tw,
    CASE
        WHEN formal_merchant.merchant_id IS NULL THEN '下一批先修正 merchants 與 NPC 對應，再處理販售清單。'
        WHEN COUNT(candidate.CandidateId) = 0 AND COUNT(formal_inventory.merchant_inventory_id) = 0 THEN '下一批需從客戶端商店資料、封包證據或實機黑箱測試取得商品、價格、貨幣與限制。'
        WHEN SUM(CASE WHEN candidate.ProductionSaleEnabled = 1 THEN 1 ELSE 0 END) = 0 AND COUNT(formal_inventory.merchant_inventory_id) = 0 THEN '下一批需補齊候選商品的關係證據與價格證據，再允許升級。'
        WHEN COUNT(formal_inventory.merchant_inventory_id) > 0 THEN '下一批可做正式購買流程測試，確認商品可買且價格正確。'
        ELSE '下一批建立正式 merchant_inventory 補入 migration，並同步 runtime catalog 驗證。'
    END AS next_action_zh_tw,
    CASE
        WHEN formal_merchant.merchant_id IS NOT NULL
         AND SUM(CASE WHEN candidate.ProductionSaleEnabled = 1 THEN 1 ELSE 0 END) > 0
            THEN 1
        ELSE 0
    END AS safe_to_promote_now
FROM god2.merchants legacy_merchant
LEFT JOIN god2_game.merchants formal_merchant
    ON formal_merchant.merchant_id = legacy_merchant.Id
LEFT JOIN god2_research.merchant_inventory_candidates candidate
    ON candidate.MerchantId = legacy_merchant.Id
LEFT JOIN god2_game.merchant_inventory formal_inventory
    ON formal_inventory.merchant_id = formal_merchant.merchant_id
GROUP BY
    legacy_merchant.Id,
    legacy_merchant.NpcId,
    formal_merchant.merchant_id,
    formal_merchant.npc_id,
    legacy_merchant.NameZhTw,
    legacy_merchant.Name
ORDER BY legacy_merchant.Id;

UPDATE god2_research.static_gap_remediation_queue
SET
    evidence_policy_zh_tw = '舊 merchants 只有商店本體，沒有商品清單；merchant_inventory_candidates 目前無候選資料，禁止猜商品、價格、貨幣或數量。',
    remediation_action_zh_tw = '先保留正式 merchant_inventory 現有實測資料，使用 merchant_inventory_gap_evidence_audit 追蹤每個舊商店缺少的販售商品證據。',
    dependency_check_zh_tw = '需檢查 god2_game.merchants、god2_game.items、merchant_inventory、購買封包、背包容量、貨幣扣除與價格；不得用商店名稱推測商品。',
    current_status_zh_tw = '缺商品候選證據，暫停自動補',
    safe_to_apply_automatically = 0,
    safe_to_drop_legacy_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE legacy_table_name = 'merchants'
  AND formal_table_name = 'merchant_inventory';

UPDATE god2_research.legacy_god2_static_table_coverage
SET
    coverage_status_zh_tw = '正式商店販售清單缺口，舊商店表缺商品與價格證據',
    cleanup_decision_zh_tw = '保留舊 merchants 作商店本體來源；取得商品清單、價格與正式 item 對應前，不得補入正式 merchant_inventory。',
    next_verification_step_zh_tw = '依 merchant_inventory_gap_evidence_audit 補商店商品、價格、貨幣與限制證據，再建立正式 merchant_inventory 補入 migration。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE legacy_table_name = 'merchants'
  AND formal_table_name = 'merchant_inventory';

UPDATE god2_research.database_schema_consolidation_status
SET
    consolidation_decision_zh_tw = '商店販售清單缺口已完成證據稽核；舊 merchants 只有商店本體，沒有足夠商品與價格證據自動升級。',
    next_cleanup_step_zh_tw = '下一批改處理掉落缺口，或回到客戶端/封包/黑箱測試補商店商品證據。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE schema_name = 'god2';
