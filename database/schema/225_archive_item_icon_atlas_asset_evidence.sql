CREATE TABLE IF NOT EXISTS `god2_research`.`item_icon_atlas_asset_evidence` (
    `atlas_id` int NOT NULL,
    `resource_path_resolved` varchar(500) NULL,
    `file_size_bytes` bigint NULL,
    `file_sha256` char(64) NULL,
    `validation_status` varchar(30) NOT NULL,
    `validated_at_utc` datetime(6) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`atlas_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @has_item_icon_resource_path_resolved := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='item_icon_atlases'
      AND `COLUMN_NAME`='resource_path_resolved'
);
SET @has_item_icon_file_size_bytes := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='item_icon_atlases'
      AND `COLUMN_NAME`='file_size_bytes'
);
SET @has_item_icon_file_sha256 := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='item_icon_atlases'
      AND `COLUMN_NAME`='file_sha256'
);
SET @has_item_icon_validated_at_utc := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='item_icon_atlases'
      AND `COLUMN_NAME`='validated_at_utc'
);

SET @archive_item_icon_asset_evidence_sql := IF(
    @has_item_icon_resource_path_resolved > 0
    AND @has_item_icon_file_size_bytes > 0
    AND @has_item_icon_file_sha256 > 0
    AND @has_item_icon_validated_at_utc > 0,
    'INSERT INTO `god2_research`.`item_icon_atlas_asset_evidence`
         (`atlas_id`,`resource_path_resolved`,`file_size_bytes`,`file_sha256`,`validation_status`,`validated_at_utc`,`moved_at_utc`)
     SELECT `atlas_id`,`resource_path_resolved`,`file_size_bytes`,`file_sha256`,`validation_status`,`validated_at_utc`,UTC_TIMESTAMP(6)
     FROM `god2_game`.`item_icon_atlases`
     ON DUPLICATE KEY UPDATE
         `resource_path_resolved`=VALUES(`resource_path_resolved`),
         `file_size_bytes`=VALUES(`file_size_bytes`),
         `file_sha256`=VALUES(`file_sha256`),
         `validation_status`=VALUES(`validation_status`),
         `validated_at_utc`=VALUES(`validated_at_utc`),
         `moved_at_utc`=UTC_TIMESTAMP(6)',
    'SELECT 1'
);
PREPARE archive_item_icon_asset_evidence_stmt FROM @archive_item_icon_asset_evidence_sql;
EXECUTE archive_item_icon_asset_evidence_stmt;
DEALLOCATE PREPARE archive_item_icon_asset_evidence_stmt;

DROP VIEW IF EXISTS `god2_game`.`vw_item_asset_validation`;

ALTER TABLE `god2_game`.`item_icon_atlases`
    DROP COLUMN IF EXISTS `resource_path_resolved`,
    DROP COLUMN IF EXISTS `file_size_bytes`,
    DROP COLUMN IF EXISTS `file_sha256`,
    DROP COLUMN IF EXISTS `validated_at_utc`;

CREATE OR REPLACE VIEW `god2_game`.`vw_item_asset_validation` AS
SELECT
    mapping_row.`client_item_id` AS `客戶端道具ID`,
    registry_row.`name_zh_tw` AS `繁體名稱`,
    mapping_row.`catalog_type` AS `資料表分類`,
    mapping_row.`icon_code` AS `圖碼`,
    mapping_row.`icon_sprite_index` AS `圖檔內索引`,
    mapping_row.`model_key` AS `模型鍵`,
    mapping_row.`icon_validation_status` AS `圖檔驗證`,
    mapping_row.`model_validation_status` AS `模型驗證`,
    mapping_row.`overall_validation_status` AS `整體驗證`,
    mapping_row.`validation_note_zh_tw` AS `驗證備註`
FROM `god2_game`.`item_asset_mappings` mapping_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=mapping_row.`item_id`;
