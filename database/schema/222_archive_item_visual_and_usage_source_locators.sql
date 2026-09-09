ALTER TABLE `god2_research`.`client_item_effect_visual_evidence`
    ADD COLUMN IF NOT EXISTS `source_table` varchar(24) NULL AFTER `effect_id`;

ALTER TABLE `god2_research`.`client_item_usage_flag_addition_evidence`
    ADD COLUMN IF NOT EXISTS `resource_key` varchar(120) NULL AFTER `client_item_id`;

SET @has_effect_visual_source_table := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='client_item_effect_visuals'
      AND `COLUMN_NAME`='source_table'
);
SET @archive_effect_visual_source_table_sql := IF(
    @has_effect_visual_source_table > 0,
    'UPDATE `god2_research`.`client_item_effect_visual_evidence` evidence_row
     JOIN `god2_game`.`client_item_effect_visuals` visual_row ON visual_row.`effect_id`=evidence_row.`effect_id`
     SET evidence_row.`source_table`=visual_row.`source_table`,
         evidence_row.`moved_at_utc`=UTC_TIMESTAMP(6)',
    'SELECT 1'
);
PREPARE archive_effect_visual_source_table_stmt FROM @archive_effect_visual_source_table_sql;
EXECUTE archive_effect_visual_source_table_stmt;
DEALLOCATE PREPARE archive_effect_visual_source_table_stmt;

SET @has_usage_source_section := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='client_item_usage_flag_additions'
      AND `COLUMN_NAME`='source_section'
);
SET @has_usage_resource_key := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='client_item_usage_flag_additions'
      AND `COLUMN_NAME`='resource_key'
);
SET @archive_usage_resource_key_sql := IF(
    @has_usage_source_section > 0 AND @has_usage_resource_key > 0,
    'UPDATE `god2_research`.`client_item_usage_flag_addition_evidence` evidence_row
     JOIN `god2_game`.`client_item_usage_flag_additions` usage_row
       ON usage_row.`source_section`=evidence_row.`source_section`
      AND usage_row.`client_item_id`=evidence_row.`client_item_id`
     SET evidence_row.`resource_key`=usage_row.`resource_key`,
         evidence_row.`moved_at_utc`=UTC_TIMESTAMP(6)',
    'SELECT 1'
);
PREPARE archive_usage_resource_key_stmt FROM @archive_usage_resource_key_sql;
EXECUTE archive_usage_resource_key_stmt;
DEALLOCATE PREPARE archive_usage_resource_key_stmt;

DROP VIEW IF EXISTS `god2_game`.`vw_client_item_effect_visuals_readable`;

ALTER TABLE `god2_game`.`client_item_effect_visuals`
    DROP INDEX IF EXISTS `ix_client_item_effect_visual_source`,
    DROP COLUMN IF EXISTS `source_table`;

SET @usage_primary_has_source_section := (
    SELECT COUNT(*)
    FROM `information_schema`.`STATISTICS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='client_item_usage_flag_additions'
      AND `INDEX_NAME`='PRIMARY'
      AND `COLUMN_NAME`='source_section'
);
SET @drop_usage_source_primary_sql := IF(
    @usage_primary_has_source_section > 0,
    'ALTER TABLE `god2_game`.`client_item_usage_flag_additions` DROP PRIMARY KEY',
    'SELECT 1'
);
PREPARE drop_usage_source_primary_stmt FROM @drop_usage_source_primary_sql;
EXECUTE drop_usage_source_primary_stmt;
DEALLOCATE PREPARE drop_usage_source_primary_stmt;

ALTER TABLE `god2_game`.`client_item_usage_flag_additions`
    DROP COLUMN IF EXISTS `source_section`,
    DROP COLUMN IF EXISTS `resource_key`;

SET @usage_has_primary := (
    SELECT COUNT(*)
    FROM `information_schema`.`STATISTICS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='client_item_usage_flag_additions'
      AND `INDEX_NAME`='PRIMARY'
);
SET @add_usage_client_item_primary_sql := IF(
    @usage_has_primary = 0,
    'ALTER TABLE `god2_game`.`client_item_usage_flag_additions` ADD PRIMARY KEY (`client_item_id`)',
    'SELECT 1'
);
PREPARE add_usage_client_item_primary_stmt FROM @add_usage_client_item_primary_sql;
EXECUTE add_usage_client_item_primary_stmt;
DEALLOCATE PREPARE add_usage_client_item_primary_stmt;

CREATE OR REPLACE VIEW `god2_game`.`vw_client_item_effect_visuals_readable` AS
SELECT
    `effect_id` AS `特效編號`,
    `rom_resource_path` AS `ROM資源`,
    `rtg_resource_path` AS `RTG資源`,
    `sound_index` AS `音效索引`,
    `visual_description_zh_tw` AS `視覺描述`,
    `authored_mechanical_description_zh_tw` AS `機械提示繁中`,
    `mechanical_semantics_status` AS `服務端語意狀態`,
    `catalog_enabled` AS `目錄啟用`,
    `runtime_eligible` AS `可執行`
FROM `god2_game`.`client_item_effect_visuals`
WHERE `catalog_enabled`=1;
