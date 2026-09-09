CREATE TABLE IF NOT EXISTS `god2_research`.`merchant_inventory_candidate_rows`
(
    `candidate_row_id` bigint NOT NULL AUTO_INCREMENT,
    `merchant_id` int NULL,
    `merchant_name_zh_tw` varchar(160) NULL,
    `client_inventory_group_id` int NOT NULL,
    `item_id` int NOT NULL,
    `item_name_zh_tw` varchar(160) NULL,
    `buy_price` bigint NULL,
    `sell_price` bigint NULL,
    `quantity_limit` int NULL,
    `refresh_policy_zh_tw` varchar(96) NOT NULL,
    `evidence_status_zh_tw` varchar(96) NOT NULL,
    `relationship_status_zh_tw` varchar(96) NOT NULL,
    `price_status_zh_tw` varchar(96) NOT NULL,
    `production_sale_enabled` tinyint(1) NOT NULL DEFAULT 0,
    `sync_status_zh_tw` varchar(96) NOT NULL,
    `sync_policy_zh_tw` varchar(220) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`candidate_row_id`),
    KEY `IX_merchant_inventory_candidate_rows_merchant` (`merchant_id`),
    KEY `IX_merchant_inventory_candidate_rows_item` (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`quest_objective_candidate_rows`
(
    `candidate_row_id` bigint NOT NULL AUTO_INCREMENT,
    `client_quest_id` int NOT NULL,
    `formal_quest_id` bigint NULL,
    `formal_quest_name_zh_tw` varchar(180) NULL,
    `objective_type_zh_tw` varchar(96) NOT NULL,
    `item_id` int NULL,
    `item_name_zh_tw` varchar(160) NULL,
    `monster_id` int NULL,
    `monster_name_zh_tw` varchar(160) NULL,
    `required_quantity` int NULL,
    `objective_text_zh_tw` text NULL,
    `evidence_status_zh_tw` varchar(96) NOT NULL,
    `relationship_status_zh_tw` varchar(96) NOT NULL,
    `quantity_status_zh_tw` varchar(96) NOT NULL,
    `production_objective_enabled` tinyint(1) NOT NULL DEFAULT 0,
    `sync_status_zh_tw` varchar(96) NOT NULL,
    `sync_policy_zh_tw` varchar(220) NOT NULL,
    PRIMARY KEY (`candidate_row_id`),
    KEY `IX_quest_objective_candidate_rows_quest` (`client_quest_id`,`formal_quest_id`),
    KEY `IX_quest_objective_candidate_rows_item` (`item_id`),
    KEY `IX_quest_objective_candidate_rows_monster` (`monster_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @rename_quest_steps_column := (
    SELECT IF(
        EXISTS (
            SELECT 1
            FROM information_schema.`COLUMNS`
            WHERE `TABLE_SCHEMA` = 'god2_research'
              AND `TABLE_NAME` = 'quest_content_profile_candidate_rows'
              AND `COLUMN_NAME` = 'has_steps_payload'
        ),
        'ALTER TABLE `god2_research`.`quest_content_profile_candidate_rows` CHANGE COLUMN `has_steps_payload` `has_unreviewed_steps` tinyint(1) NOT NULL DEFAULT 0 COMMENT ''來源曾有步驟資料但尚未整理成正式任務目標''',
        'SELECT 1'
    )
);
PREPARE rename_quest_steps_column_stmt FROM @rename_quest_steps_column;
EXECUTE rename_quest_steps_column_stmt;
DEALLOCATE PREPARE rename_quest_steps_column_stmt;

TRUNCATE TABLE `god2_research`.`merchant_inventory_candidate_rows`;
TRUNCATE TABLE `god2_research`.`quest_objective_candidate_rows`;

INSERT INTO `god2_research`.`merchant_inventory_candidate_rows`
    (`merchant_id`,`merchant_name_zh_tw`,`client_inventory_group_id`,`item_id`,`item_name_zh_tw`,
     `buy_price`,`sell_price`,`quantity_limit`,`refresh_policy_zh_tw`,`evidence_status_zh_tw`,
     `relationship_status_zh_tw`,`price_status_zh_tw`,`production_sale_enabled`,`sync_status_zh_tw`,`sync_policy_zh_tw`)
SELECT
    candidate.`MerchantId`,
    npc_row.`name_zh_tw`,
    candidate.`ClientInventoryGroupId`,
    candidate.`ItemId`,
    item_row.`name_zh_tw`,
    candidate.`BuyPrice`,
    candidate.`SellPrice`,
    candidate.`QuantityLimit`,
    candidate.`RefreshPolicy`,
    candidate.`EvidenceStatus`,
    candidate.`RelationshipEvidenceStatus`,
    candidate.`PriceEvidenceStatus`,
    candidate.`ProductionSaleEnabled`,
    CASE
        WHEN candidate.`ProductionSaleEnabled` = 1 THEN '可同步正式商店'
        ELSE '尚未啟用正式販售'
    END,
    CASE
        WHEN candidate.`ProductionSaleEnabled` = 1 THEN '商店庫存證據已通過 gate，可供正式商店同步。'
        ELSE '缺價格或關係證據時只作人工檢查，不自動加入正式商店。'
    END
FROM `god2_research`.`merchant_inventory_candidates` candidate
LEFT JOIN `god2_game`.`npcs` npc_row
  ON npc_row.`npc_id` = candidate.`MerchantId`
LEFT JOIN `god2_game`.`item_registry` item_row
  ON item_row.`item_id` = candidate.`ItemId`;

INSERT INTO `god2_research`.`quest_objective_candidate_rows`
    (`client_quest_id`,`formal_quest_id`,`formal_quest_name_zh_tw`,`objective_type_zh_tw`,
     `item_id`,`item_name_zh_tw`,`monster_id`,`monster_name_zh_tw`,`required_quantity`,`objective_text_zh_tw`,
     `evidence_status_zh_tw`,`relationship_status_zh_tw`,`quantity_status_zh_tw`,`production_objective_enabled`,
     `sync_status_zh_tw`,`sync_policy_zh_tw`)
SELECT
    candidate.`ClientQuestId`,
    formal_quest.`quest_id`,
    formal_quest.`name_zh_tw`,
    candidate.`ObjectiveType`,
    candidate.`ItemId`,
    item_row.`name_zh_tw`,
    candidate.`MonsterId`,
    monster_row.`name_zh_tw`,
    candidate.`RequiredQuantity`,
    candidate.`ObjectiveTextZhTw`,
    candidate.`EvidenceStatus`,
    candidate.`RelationshipEvidenceStatus`,
    candidate.`QuantityEvidenceStatus`,
    candidate.`ProductionObjectiveEnabled`,
    CASE
        WHEN candidate.`ProductionObjectiveEnabled` = 1 THEN '可同步正式任務目標'
        ELSE '尚未啟用正式任務目標'
    END,
    CASE
        WHEN candidate.`ProductionObjectiveEnabled` = 1 THEN '任務目標證據已通過 gate，可供正式任務流程使用。'
        ELSE '缺數量、道具、怪物或任務關係證據時只作人工檢查。'
    END
FROM `god2_research`.`quest_objective_candidates` candidate
LEFT JOIN `god2_game`.`quests` formal_quest
  ON formal_quest.`code` = CONCAT('quest_', candidate.`ClientQuestId`)
LEFT JOIN `god2_game`.`item_registry` item_row
  ON item_row.`item_id` = candidate.`ItemId`
LEFT JOIN `god2_game`.`monsters` monster_row
  ON monster_row.`monster_id` = candidate.`MonsterId`;

UPDATE `god2_research`.`database_readability_surface`
SET `readability_tier_zh_tw` = '工具證據歸檔',
    `intended_reader_zh_tw` = '工具管線，不建議人工直接編輯',
    `safe_to_browse` = 0,
    `contains_tool_only_fields` = 1,
    `cleanup_policy_zh_tw` = '保留給商店與任務目標匯入工具；人工檢查請看對應 candidate_rows 可讀表。',
    `notes_zh_tw` = '此表含工具追蹤欄位，不當作日常檢查入口。'
WHERE `schema_name` = 'god2_research'
  AND `table_name` IN ('merchant_inventory_candidates','quest_objective_candidates');

REPLACE INTO `god2_research`.`database_readability_surface`
    (`schema_name`,`table_name`,`readability_tier_zh_tw`,`game_function_zh_tw`,`intended_reader_zh_tw`,
     `safe_to_browse`,`contains_tool_only_fields`,`cleanup_policy_zh_tw`,`notes_zh_tw`)
VALUES
    ('god2_research','merchant_inventory_candidate_rows','可讀候選整理','商店販售清單資料','人工確認與資料補齊',1,0,
     '可人工瀏覽；不保存工具追蹤欄位，只保留商店、道具、價格、數量與同步狀態。',
     '這是商店庫存證據的可讀檢查面。'),
    ('god2_research','quest_objective_candidate_rows','可讀候選整理','任務、目標、NPC 對話與獎勵資料','人工確認與資料補齊',1,0,
     '可人工瀏覽；不保存工具追蹤欄位，只保留任務目標、道具、怪物、數量與同步狀態。',
     '這是任務目標證據的可讀檢查面。');

TRUNCATE TABLE `god2_research`.`database_field_readability_audit`;

INSERT INTO `god2_research`.`database_field_readability_audit`
    (`schema_name`,`table_name`,`field_name`,`field_type`,`readability_status_zh_tw`,`cleanup_policy_zh_tw`,`game_function_zh_tw`)
SELECT
    column_row.`TABLE_SCHEMA`,
    column_row.`TABLE_NAME`,
    column_row.`COLUMN_NAME`,
    column_row.`DATA_TYPE`,
    CASE
        WHEN surface.`safe_to_browse` = 1 AND surface.`contains_tool_only_fields` = 0 THEN '可人工瀏覽'
        WHEN surface.`safe_to_browse` = 1 THEN '可人工瀏覽但需注意欄位'
        ELSE '工具欄位'
    END,
    CASE
        WHEN surface.`safe_to_browse` = 1 AND surface.`contains_tool_only_fields` = 0 THEN '保留為可讀欄位。'
        WHEN surface.`safe_to_browse` = 1 THEN '保留欄位但需在候選表中提供繁中語意，不直接暴露原始內容。'
        ELSE '保留給工具追證據；若要給人工看，必須另建可讀候選表。'
    END,
    surface.`game_function_zh_tw`
FROM information_schema.`COLUMNS` column_row
JOIN `god2_research`.`database_readability_surface` surface
  ON surface.`schema_name` = column_row.`TABLE_SCHEMA`
 AND surface.`table_name` = column_row.`TABLE_NAME`
WHERE column_row.`TABLE_SCHEMA` IN ('god2_game','god2_research')
  AND (
        column_row.`COLUMN_NAME` LIKE '%Hash%'
     OR column_row.`COLUMN_NAME` LIKE '%Payload%'
     OR column_row.`COLUMN_NAME` LIKE '%Raw%'
     OR column_row.`COLUMN_NAME` LIKE '%Json%'
     OR column_row.`COLUMN_NAME` LIKE '%Binary%'
     OR column_row.`COLUMN_NAME` LIKE '%Blob%'
     OR column_row.`COLUMN_NAME` LIKE '%Hex%'
     OR column_row.`COLUMN_NAME` LIKE '%ProfileId%'
     OR column_row.`COLUMN_NAME` LIKE '%RelationshipId%'
     OR column_row.`COLUMN_NAME` = 'RunId'
  );
