-- Remove legacy equipment overlap and create strict item-to-client-asset mappings.

DELETE equipment_row
FROM `god2_game`.`equipment` equipment_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id` = equipment_row.`item_id`
WHERE COALESCE(registry_row.`equippable`,0) = 0
   OR registry_row.`source_item_type` IN ('WPN','TWP','GWP','SSW','TLI');

CREATE TABLE IF NOT EXISTS `god2_game`.`item_icon_atlases` (
    `atlas_id` int NOT NULL COMMENT 'RomMaps row ID',
    `minimum_icon_code` int NOT NULL COMMENT 'Inclusive global icon code lower bound',
    `maximum_icon_code` int NOT NULL COMMENT 'Inclusive global icon code upper bound',
    `resource_path_original` varchar(500) NOT NULL COMMENT 'Original path declared by official client gamedata',
    `resource_path_resolved` varchar(500) NULL COMMENT 'Resolved installed-client ROMZ path',
    `file_size_bytes` bigint NULL COMMENT 'Installed asset size',
    `file_sha256` char(64) NULL COMMENT 'Installed asset SHA-256',
    `validation_status` varchar(30) NOT NULL COMMENT 'Verified or MissingAsset',
    `validated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`atlas_id`),
    UNIQUE KEY `ux_item_icon_atlas_range` (`minimum_icon_code`,`maximum_icon_code`),
    KEY `ix_item_icon_atlas_status` (`validation_status`),
    CONSTRAINT `ck_item_icon_atlas_range` CHECK (`minimum_icon_code` <= `maximum_icon_code`),
    CONSTRAINT `ck_item_icon_atlas_status` CHECK (`validation_status` IN ('Verified','MissingAsset'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Official item icon atlas ranges and installed files';

CREATE TABLE IF NOT EXISTS `god2_game`.`item_asset_mappings` (
    `item_id` bigint NOT NULL COMMENT 'Shared item registry ID',
    `client_item_id` int NOT NULL COMMENT 'Official client item ID',
    `catalog_type` varchar(30) NOT NULL COMMENT 'Item, Weapon, Equipment or MagicTreasure',
    `icon_code` int NOT NULL COMMENT 'Official global icon code',
    `icon_atlas_id` int NULL COMMENT 'Matched official RomMaps atlas',
    `icon_sprite_index` int NULL COMMENT 'Zero-based sprite index inside matched atlas range',
    `model_key` varchar(256) NOT NULL COMMENT 'Official client presentation/model key',
    `icon_validation_status` varchar(30) NOT NULL COMMENT 'Verified, RangeMissing or AssetMissing',
    `model_validation_status` varchar(30) NOT NULL COMMENT 'ReferenceVerified or AssetPathUnresolved',
    `overall_validation_status` varchar(30) NOT NULL COMMENT 'Verified or EvidenceBlocked',
    `validation_note_zh_tw` varchar(500) NULL COMMENT 'Readable validation note',
    `validated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`item_id`),
    UNIQUE KEY `ux_item_asset_client_item_id` (`client_item_id`),
    KEY `ix_item_asset_icon_atlas` (`icon_atlas_id`),
    KEY `ix_item_asset_catalog_type` (`catalog_type`),
    KEY `ix_item_asset_overall_status` (`overall_validation_status`),
    CONSTRAINT `fk_item_asset_registry` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `fk_item_asset_atlas` FOREIGN KEY (`icon_atlas_id`) REFERENCES `god2_game`.`item_icon_atlases` (`atlas_id`),
    CONSTRAINT `ck_item_asset_catalog_type` CHECK (`catalog_type` IN ('Item','Weapon','Equipment','MagicTreasure')),
    CONSTRAINT `ck_item_asset_icon_status` CHECK (`icon_validation_status` IN ('Verified','RangeMissing','AssetMissing')),
    CONSTRAINT `ck_item_asset_model_status` CHECK (`model_validation_status` IN ('ReferenceVerified','AssetPathUnresolved')),
    CONSTRAINT `ck_item_asset_overall_status` CHECK (`overall_validation_status` IN ('Verified','EvidenceBlocked'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Strict item icon and presentation resource mapping';

CREATE OR REPLACE VIEW `god2_game`.`vw_item_asset_validation` AS
SELECT mapping_row.`client_item_id` AS `客戶端道具ID`,registry_row.`name_zh_tw` AS `繁體名稱`,
       mapping_row.`catalog_type` AS `資料表分類`,mapping_row.`icon_code` AS `圖碼`,
       atlas_row.`resource_path_original` AS `官方圖檔`,atlas_row.`resource_path_resolved` AS `本機圖檔`,
       mapping_row.`icon_sprite_index` AS `圖檔內索引`,mapping_row.`model_key` AS `模型鍵`,
       mapping_row.`icon_validation_status` AS `圖檔驗證`,mapping_row.`model_validation_status` AS `模型驗證`,
       mapping_row.`overall_validation_status` AS `整體驗證`,mapping_row.`validation_note_zh_tw` AS `驗證備註`
FROM `god2_game`.`item_asset_mappings` mapping_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id` = mapping_row.`item_id`
LEFT JOIN `god2_game`.`item_icon_atlases` atlas_row ON atlas_row.`atlas_id` = mapping_row.`icon_atlas_id`;
