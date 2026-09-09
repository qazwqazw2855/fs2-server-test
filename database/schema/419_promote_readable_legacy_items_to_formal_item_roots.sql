CREATE TABLE IF NOT EXISTS god2_research.item_root_promotion_audit (
    audit_id bigint unsigned NOT NULL AUTO_INCREMENT,
    legacy_item_id bigint NOT NULL,
    formal_item_id bigint NULL,
    item_code varchar(128) NULL,
    name_zh_tw varchar(200) NOT NULL,
    item_family_zh_tw varchar(128) NULL,
    item_type varchar(64) NULL,
    has_formal_equipment tinyint(1) NOT NULL,
    has_formal_weapon tinyint(1) NOT NULL,
    has_formal_magic_treasure tinyint(1) NOT NULL,
    promotion_status_zh_tw varchar(128) NOT NULL,
    game_function_zh_tw varchar(256) NOT NULL,
    data_policy_zh_tw varchar(512) NOT NULL,
    next_action_zh_tw varchar(512) NOT NULL,
    safe_to_drop_legacy_now tinyint(1) NOT NULL DEFAULT 0,
    reviewed_at_utc timestamp NOT NULL DEFAULT utc_timestamp(),
    PRIMARY KEY (audit_id),
    UNIQUE KEY ux_item_root_promotion_legacy_item (legacy_item_id),
    KEY ix_item_root_promotion_status (promotion_status_zh_tw),
    KEY ix_item_root_promotion_formal_item (formal_item_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO god2_game.items (
    item_id,
    client_item_id,
    code,
    name_zh_tw,
    description_zh_tw,
    item_category,
    item_family,
    maximum_stack,
    buy_price,
    sell_price,
    tradable,
    stackable,
    usable,
    equippable,
    enabled,
    created_at_utc,
    updated_at_utc
)
SELECT
    legacy_item.Id AS item_id,
    legacy_item.Id AS client_item_id,
    legacy_item.Code AS code,
    COALESCE(NULLIF(TRIM(legacy_item.NameZhTw), ''), legacy_item.DisplayName, legacy_item.Name) AS name_zh_tw,
    NULLIF(TRIM(legacy_item.DescriptionZhTw), '') AS description_zh_tw,
    COALESCE(NULLIF(TRIM(legacy_item.ItemFamily), ''), NULLIF(TRIM(legacy_item.ItemCategory), ''), 'Unknown') AS item_category,
    NULLIF(TRIM(legacy_item.ItemFamily), '') AS item_family,
    legacy_item.MaxStack AS maximum_stack,
    NULLIF(legacy_item.BaseBuyPrice, 0) AS buy_price,
    COALESCE(legacy_item.SellPrice, NULLIF(legacy_item.BaseSellPrice, 0)) AS sell_price,
    CASE WHEN legacy_item.TradePolicy = 'Allowed' THEN 1 WHEN legacy_item.TradePolicy = 'Disallowed' THEN 0 ELSE NULL END AS tradable,
    CASE WHEN legacy_item.StackPolicy = 'ExplicitStackable' THEN 1 WHEN legacy_item.StackPolicy = 'ExplicitNonStackable' THEN 0 ELSE NULL END AS stackable,
    CASE WHEN legacy_item.ConsumableCategory <> 'Unknown' THEN 1 ELSE NULL END AS usable,
    CASE WHEN legacy_item.ItemFamily IN ('Armor','Weapon','Accessory','Helmet','Equipment') THEN 1 ELSE NULL END AS equippable,
    legacy_item.Enabled AS enabled,
    utc_timestamp(6) AS created_at_utc,
    utc_timestamp(6) AS updated_at_utc
FROM god2.items legacy_item
LEFT JOIN god2_game.items formal_item ON formal_item.item_id = legacy_item.Id
WHERE formal_item.item_id IS NULL
  AND NULLIF(TRIM(COALESCE(legacy_item.NameZhTw, legacy_item.DisplayName, legacy_item.Name)), '') IS NOT NULL;

DELETE FROM god2_research.item_root_promotion_audit;

INSERT INTO god2_research.item_root_promotion_audit (
    legacy_item_id,
    formal_item_id,
    item_code,
    name_zh_tw,
    item_family_zh_tw,
    item_type,
    has_formal_equipment,
    has_formal_weapon,
    has_formal_magic_treasure,
    promotion_status_zh_tw,
    game_function_zh_tw,
    data_policy_zh_tw,
    next_action_zh_tw,
    safe_to_drop_legacy_now
)
SELECT
    legacy_item.Id AS legacy_item_id,
    formal_item.item_id AS formal_item_id,
    legacy_item.Code AS item_code,
    COALESCE(NULLIF(TRIM(legacy_item.NameZhTw), ''), legacy_item.DisplayName, legacy_item.Name) AS name_zh_tw,
    legacy_item.ItemFamily AS item_family_zh_tw,
    legacy_item.ItemType AS item_type,
    CASE WHEN equipment_row.item_id IS NULL THEN 0 ELSE 1 END AS has_formal_equipment,
    CASE WHEN weapon_row.item_id IS NULL THEN 0 ELSE 1 END AS has_formal_weapon,
    CASE WHEN magic_row.item_id IS NULL THEN 0 ELSE 1 END AS has_formal_magic_treasure,
    CASE
        WHEN formal_item.item_id IS NULL THEN '缺正式道具主表'
        WHEN equipment_row.item_id IS NOT NULL THEN '正式道具主表與裝備拆表已對齊'
        WHEN weapon_row.item_id IS NOT NULL THEN '正式道具主表與武器拆表已對齊'
        WHEN magic_row.item_id IS NOT NULL THEN '正式道具主表與法寶拆表已對齊'
        ELSE '正式道具主表已存在，待功能拆表確認'
    END AS promotion_status_zh_tw,
    CASE
        WHEN equipment_row.item_id IS NOT NULL THEN '裝備穿戴與角色屬性加成資料本體'
        WHEN weapon_row.item_id IS NOT NULL THEN '武器穿戴、攻擊距離與攻擊加成資料本體'
        WHEN magic_row.item_id IS NOT NULL THEN '法寶裝備與特殊效果資料本體'
        WHEN legacy_item.ItemFamily = 'QuestItem' THEN '任務道具與任務收集條件資料本體'
        WHEN legacy_item.ItemFamily = 'Consumable' THEN '消耗品使用與補給資料本體'
        WHEN legacy_item.ItemFamily = 'Material' THEN '材料、合成或強化素材資料本體'
        WHEN legacy_item.ItemFamily = 'PetItem' THEN '寵物道具、戰寵、餵食或寵物成長資料本體'
        ELSE '一般道具、活動道具或特殊物品資料本體'
    END AS game_function_zh_tw,
    '只從舊 items 的可讀繁中欄位補正式道具主表；不解析 DescriptionZhTw 內的攻防文字、不猜裝備數值、不搬 SourceHash/RunId/payload。' AS data_policy_zh_tw,
    CASE
        WHEN formal_item.item_id IS NULL THEN '需補繁中名稱或確認舊道具是否淘汰。'
        WHEN equipment_row.item_id IS NOT NULL OR weapon_row.item_id IS NOT NULL OR magic_row.item_id IS NOT NULL THEN '下一批驗證拆表欄位與角色裝備、背包、容器、掉落引用是否完整。'
        ELSE '下一批依 item_family 與 item_usage_rules 判斷是否需要補消耗、任務、材料或寵物專用正式表。'
    END AS next_action_zh_tw,
    0 AS safe_to_drop_legacy_now
FROM god2.items legacy_item
LEFT JOIN god2_game.items formal_item ON formal_item.item_id = legacy_item.Id
LEFT JOIN god2_game.equipment equipment_row ON equipment_row.item_id = legacy_item.Id
LEFT JOIN god2_game.weapons weapon_row ON weapon_row.item_id = legacy_item.Id
LEFT JOIN god2_game.magic_treasures magic_row ON magic_row.item_id = legacy_item.Id
ORDER BY legacy_item.Id;

UPDATE god2_research.static_gap_remediation_queue
SET
    evidence_policy_zh_tw = '已用舊 items 的可讀繁中欄位補正式道具主表；裝備、武器、法寶數值仍只接受既有拆表或明確證據，不從描述文字猜值。',
    remediation_action_zh_tw = '使用 item_root_promotion_audit 追蹤道具主表與 equipment/weapons/magic_treasures 拆表對齊狀態；下一批檢查拆表缺欄與 item_usage_rules。',
    dependency_check_zh_tw = '需檢查 items、equipment、weapons、magic_treasures、item_usage_rules、容器、掉落、商店、背包與角色裝備屬性計算。',
    current_status_zh_tw = '已補正式道具主表，待拆表與使用規則稽核',
    safe_to_apply_automatically = 0,
    safe_to_drop_legacy_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE legacy_table_name = 'items'
  AND formal_table_name IN ('items', 'equipment', 'weapons', 'magic_treasures');

UPDATE god2_research.legacy_god2_static_table_coverage
SET
    coverage_status_zh_tw = '正式道具主表已由可讀舊 items 補齊，仍需欄位語意與功能拆表稽核',
    cleanup_decision_zh_tw = '舊 items 仍保留作來源證據；確認所有拆表、使用規則與 runtime 引用前不得封存。',
    next_verification_step_zh_tw = '依 item_root_promotion_audit 檢查道具主表、裝備、武器、法寶、使用規則、容器與掉落引用是否完整。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE legacy_table_name = 'items'
  AND formal_table_name IN ('items', 'equipment', 'weapons', 'magic_treasures');

UPDATE god2_research.database_schema_consolidation_status
SET
    consolidation_decision_zh_tw = '道具主表缺口已用繁中可讀舊 items 補入正式 items；裝備、武器、法寶數值仍需依拆表與證據稽核。',
    next_cleanup_step_zh_tw = '下一批檢查 item_usage_rules 與拆表欄位完整度，再回補容器與掉落被道具本體卡住的缺口。',
    safe_to_drop_now = 0,
    reviewed_at_utc = utc_timestamp()
WHERE schema_name = 'god2';
