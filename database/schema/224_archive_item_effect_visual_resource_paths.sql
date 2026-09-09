ALTER TABLE `god2_research`.`client_item_effect_visual_evidence`
    ADD COLUMN IF NOT EXISTS `rom_resource_path` varchar(260) NULL AFTER `source_table`,
    ADD COLUMN IF NOT EXISTS `rtg_resource_path` varchar(260) NULL AFTER `rom_resource_path`;

SET @has_visual_rom_resource_path := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='client_item_effect_visuals'
      AND `COLUMN_NAME`='rom_resource_path'
);
SET @has_visual_rtg_resource_path := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='client_item_effect_visuals'
      AND `COLUMN_NAME`='rtg_resource_path'
);
SET @archive_visual_resource_paths_sql := IF(
    @has_visual_rom_resource_path > 0 AND @has_visual_rtg_resource_path > 0,
    'UPDATE `god2_research`.`client_item_effect_visual_evidence` evidence_row
     JOIN `god2_game`.`client_item_effect_visuals` visual_row ON visual_row.`effect_id`=evidence_row.`effect_id`
     SET evidence_row.`rom_resource_path`=visual_row.`rom_resource_path`,
         evidence_row.`rtg_resource_path`=visual_row.`rtg_resource_path`,
         evidence_row.`moved_at_utc`=UTC_TIMESTAMP(6)',
    'SELECT 1'
);
PREPARE archive_visual_resource_paths_stmt FROM @archive_visual_resource_paths_sql;
EXECUTE archive_visual_resource_paths_stmt;
DEALLOCATE PREPARE archive_visual_resource_paths_stmt;

DROP VIEW IF EXISTS `god2_game`.`vw_client_item_effect_visuals_readable`;

ALTER TABLE `god2_game`.`client_item_effect_visuals`
    DROP COLUMN IF EXISTS `rom_resource_path`,
    DROP COLUMN IF EXISTS `rtg_resource_path`;

CREATE OR REPLACE VIEW `god2_game`.`vw_client_item_effect_visuals_readable` AS
SELECT
    `effect_id` AS `特效編號`,
    `sound_index` AS `音效索引`,
    `visual_description_zh_tw` AS `視覺描述`,
    `authored_mechanical_description_zh_tw` AS `機械提示繁中`,
    `mechanical_semantics_status` AS `服務端語意狀態`,
    `catalog_enabled` AS `目錄啟用`,
    `runtime_eligible` AS `可執行`
FROM `god2_game`.`client_item_effect_visuals`
WHERE `catalog_enabled`=1;
