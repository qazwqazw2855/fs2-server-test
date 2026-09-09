CREATE TABLE IF NOT EXISTS `god2_research`.`formal_database_schema_consolidation` (
    `schema_name` varchar(64) NOT NULL,
    `schema_role_zh_tw` varchar(255) NOT NULL,
    `game_function_zh_tw` varchar(255) NOT NULL,
    `runtime_reference_zh_tw` text NOT NULL,
    `cleanup_decision_zh_tw` varchar(255) NOT NULL,
    `safe_to_drop_now` tinyint(1) NOT NULL DEFAULT 0,
    `next_consolidation_step_zh_tw` text NOT NULL,
    `reviewed_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`schema_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`formal_database_schema_consolidation`
    (`schema_name`, `schema_role_zh_tw`, `game_function_zh_tw`, `runtime_reference_zh_tw`, `cleanup_decision_zh_tw`, `safe_to_drop_now`, `next_consolidation_step_zh_tw`, `reviewed_at_utc`)
VALUES
    ('god2', '正式服務端連線入口與仍在使用的 runtime/legacy 狀態 schema', '帳號入口、戰鬥狀態、技能/任務/戰鬥 runtime 過渡表、schema migration 版本記錄', 'config/database.json 的 databaseName=god2；MariaDbItemUseRepository 仍讀寫 god2.battle_status_instances、god2.status_effects、god2.battle_status_removals；__schemaversion 也在此 schema。', '目前不可刪；需先把仍被 runtime 直接引用的戰鬥狀態表遷移到 god2_player 或 god2_game 後才能收斂。', 0, '下一步應建立 runtime schema migration 計畫：先列出所有 god2.* runtime 讀寫點，再逐表搬遷或建立相容 view，最後才移除舊表。', UTC_TIMESTAMP(6)),
    ('god2_game', '正式靜態遊戲資料 schema', '道具、裝備、技能、怪物、地圖、傳送點、NPC、任務、掉落、製作、陣型、神仙、戰寵等正式遊戲內容', 'MariaDbCanonicalCatalogRepositories、MariaDbCanonicalGameplayContentRuntime、MariaDbGameplayInventoryRepository 等正式 runtime 主要讀取來源。', '保留；這是正式服務端靜態資料核心。', 0, '持續把有證據的候選資料提升進 god2_game；缺證據的只保留補證目標，不硬塞正式值。', UTC_TIMESTAMP(6)),
    ('god2_player', '正式玩家狀態 schema', '帳號、角色、背包、裝備實例、寵物、神仙、社交、道具使用 idempotency、玩家世界互動狀態', 'ConsoleHost 與 Persistence runtime 持續讀寫 god2_player.accounts、characters、character_inventory、character_pets、character_immortals、player_social_* 等表。', '保留；這是正式服務端玩家資料核心。', 0, '保留 runtime 必需 JSON idempotency 欄位，但用繁中 summary/view 提供可讀檢查面。', UTC_TIMESTAMP(6)),
    ('god2_game_meta', '正式管理與驗證 metadata schema', '欄位鎖、管理稽核、catalog release、外部數值證據、正式資料驗證狀態', 'MariaDbCanonicalGameplayContentRuntime 與 MariaDbPromotedGameplayContentRuntime 讀取 runtime_catalog_releases；工具與管理流程使用 audit/lock 表。', '保留；屬於正式資料治理，不是玩家遊戲內容，但仍是正式服務端資料的一部分。', 0, '保留 metadata；若未來要更單一化，可只把 readable view 暴露給管理者，不混入 god2_game。', UTC_TIMESTAMP(6)),
    ('god2_research', '研究證據與補證追蹤 schema', '封包證據、客戶端還原候選、黑箱測試目標、資料提升審核、清理稽核', 'OfficialDataImport、PromotedGameplayContentRuntime 與多個 migration 使用；正式 runtime 不應直接以未提升候選值作為權威 gameplay 數值。', '保留但隔離；不能併入正式遊戲表，避免未證實資料污染正式服。', 0, '持續把已證實且相容的資料提升到 god2_game/god2_player；未證實資料只留在 research 供補證。', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    `schema_role_zh_tw`=VALUES(`schema_role_zh_tw`),
    `game_function_zh_tw`=VALUES(`game_function_zh_tw`),
    `runtime_reference_zh_tw`=VALUES(`runtime_reference_zh_tw`),
    `cleanup_decision_zh_tw`=VALUES(`cleanup_decision_zh_tw`),
    `safe_to_drop_now`=VALUES(`safe_to_drop_now`),
    `next_consolidation_step_zh_tw`=VALUES(`next_consolidation_step_zh_tw`),
    `reviewed_at_utc`=VALUES(`reviewed_at_utc`);

CREATE OR REPLACE VIEW `god2_research`.`vw_formal_database_schema_consolidation_zh_tw` AS
SELECT
    s.`schema_name` AS `資料庫Schema`,
    s.`schema_role_zh_tw` AS `角色`,
    s.`game_function_zh_tw` AS `遊戲功能對照`,
    s.`runtime_reference_zh_tw` AS `服務端對應證據`,
    s.`cleanup_decision_zh_tw` AS `清理決策`,
    CASE WHEN s.`safe_to_drop_now`=1 THEN '可刪除' ELSE '不可直接刪除' END AS `目前是否可刪`,
    s.`next_consolidation_step_zh_tw` AS `下一步`,
    s.`reviewed_at_utc` AS `檢查時間UTC`,
    COALESCE(t.`base_table_count`, 0) AS `正式表數`,
    COALESCE(t.`view_count`, 0) AS `檢視表數`
FROM `god2_research`.`formal_database_schema_consolidation` s
LEFT JOIN (
    SELECT
        `table_schema`,
        SUM(CASE WHEN `table_type`='BASE TABLE' THEN 1 ELSE 0 END) AS `base_table_count`,
        SUM(CASE WHEN `table_type`='VIEW' THEN 1 ELSE 0 END) AS `view_count`
    FROM `information_schema`.`tables`
    WHERE `table_schema` IN ('god2','god2_game','god2_player','god2_game_meta','god2_research')
    GROUP BY `table_schema`
) t ON t.`table_schema`=s.`schema_name`;