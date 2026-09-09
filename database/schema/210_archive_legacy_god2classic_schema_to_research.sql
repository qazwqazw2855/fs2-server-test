CREATE SCHEMA IF NOT EXISTS `god2_research`;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_schema_archive` (
    `source_schema` varchar(64) NOT NULL,
    `source_table` varchar(128) NOT NULL,
    `archived_table` varchar(191) NOT NULL,
    `estimated_archived_rows` bigint NOT NULL DEFAULT 0,
    `archive_reason_zh_tw` varchar(255) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`source_schema`, `source_table`)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`legacy_god2classic_schema_archive`
    (`source_schema`,`source_table`,`archived_table`,`estimated_archived_rows`,`archive_reason_zh_tw`,`archived_at_utc`)
SELECT `TABLE_SCHEMA`,
       `TABLE_NAME`,
       CONCAT('legacy_god2classic_', `TABLE_NAME`),
       COALESCE(`TABLE_ROWS`, 0),
       '舊 god2classic schema 為早期匯入/證據殘留；正式服務端改用 god2/god2_game/god2_player/god2_game_meta，舊表移入 research 封存，避免正式資料庫再混入原始 payload 或舊文字欄。',
       UTC_TIMESTAMP(6)
FROM information_schema.TABLES
WHERE `TABLE_SCHEMA`='god2classic' AND `TABLE_TYPE`='BASE TABLE'
ON DUPLICATE KEY UPDATE
    `archived_table`=VALUES(`archived_table`),
    `estimated_archived_rows`=VALUES(`estimated_archived_rows`),
    `archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),
    `archived_at_utc`=VALUES(`archived_at_utc`);

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='accounts' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_accounts` LIKE `god2classic`.`accounts`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_accounts` SELECT * FROM `god2classic`.`accounts`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='battle_pets' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_battle_pets` LIKE `god2classic`.`battle_pets`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_battle_pets` SELECT * FROM `god2classic`.`battle_pets`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='characters' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_characters` LIKE `god2classic`.`characters`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_characters` SELECT * FROM `god2classic`.`characters`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='dialogs' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_dialogs` LIKE `god2classic`.`dialogs`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_dialogs` SELECT * FROM `god2classic`.`dialogs`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='drop_tables' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_drop_tables` LIKE `god2classic`.`drop_tables`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_drop_tables` SELECT * FROM `god2classic`.`drop_tables`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='drop_table_items' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_drop_table_items` LIKE `god2classic`.`drop_table_items`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_drop_table_items` SELECT * FROM `god2classic`.`drop_table_items`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='equipment_slots' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_equipment_slots` LIKE `god2classic`.`equipment_slots`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_equipment_slots` SELECT * FROM `god2classic`.`equipment_slots`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='immortals' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_immortals` LIKE `god2classic`.`immortals`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_immortals` SELECT * FROM `god2classic`.`immortals`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='inventory_slots' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_inventory_slots` LIKE `god2classic`.`inventory_slots`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_inventory_slots` SELECT * FROM `god2classic`.`inventory_slots`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='items' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_items` LIKE `god2classic`.`items`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_items` SELECT * FROM `god2classic`.`items`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='localization_entries' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_localization_entries` LIKE `god2classic`.`localization_entries`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_localization_entries` SELECT * FROM `god2classic`.`localization_entries`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='maps' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_maps` LIKE `god2classic`.`maps`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_maps` SELECT * FROM `god2classic`.`maps`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='merchants' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_merchants` LIKE `god2classic`.`merchants`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_merchants` SELECT * FROM `god2classic`.`merchants`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='merchant_items' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_merchant_items` LIKE `god2classic`.`merchant_items`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_merchant_items` SELECT * FROM `god2classic`.`merchant_items`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='monsters' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_monsters` LIKE `god2classic`.`monsters`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_monsters` SELECT * FROM `god2classic`.`monsters`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='npcs' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_npcs` LIKE `god2classic`.`npcs`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_npcs` SELECT * FROM `god2classic`.`npcs`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='official_import_categories' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_official_import_categories` LIKE `god2classic`.`official_import_categories`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_official_import_categories` SELECT * FROM `god2classic`.`official_import_categories`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='official_import_records' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_official_import_records` LIKE `god2classic`.`official_import_records`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_official_import_records` SELECT * FROM `god2classic`.`official_import_records`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='official_import_reference_issues' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_official_import_reference_issues` LIKE `god2classic`.`official_import_reference_issues`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_official_import_reference_issues` SELECT * FROM `god2classic`.`official_import_reference_issues`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='portals' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_portals` LIKE `god2classic`.`portals`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_portals` SELECT * FROM `god2classic`.`portals`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='quests' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_quests` LIKE `god2classic`.`quests`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_quests` SELECT * FROM `god2classic`.`quests`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='rewards' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_rewards` LIKE `god2classic`.`rewards`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_rewards` SELECT * FROM `god2classic`.`rewards`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='reward_items' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_reward_items` LIKE `god2classic`.`reward_items`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_reward_items` SELECT * FROM `god2classic`.`reward_items`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='skills' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_skills` LIKE `god2classic`.`skills`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_skills` SELECT * FROM `god2classic`.`skills`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='spawns' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_spawns` LIKE `god2classic`.`spawns`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_spawns` SELECT * FROM `god2classic`.`spawns`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='status_effects' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic_status_effects` LIKE `god2classic`.`status_effects`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic_status_effects` SELECT * FROM `god2classic`.`status_effects`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @table_exists := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='god2classic' AND TABLE_NAME='__schemaversion' AND TABLE_TYPE='BASE TABLE');
SET @sql := IF(@table_exists > 0, 'CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2classic___schemaversion` LIKE `god2classic`.`__schemaversion`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
SET @sql := IF(@table_exists > 0, 'INSERT INTO `god2_research`.`legacy_god2classic___schemaversion` SELECT * FROM `god2classic`.`__schemaversion`', 'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

DROP DATABASE IF EXISTS `god2classic`;

