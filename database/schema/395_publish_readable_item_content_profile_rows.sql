CREATE DATABASE IF NOT EXISTS `god2_research`
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`item_content_profile_candidate_rows` (
    `item_id` bigint NOT NULL COMMENT '正式道具 ID',
    `client_item_id` int NOT NULL COMMENT '客戶端道具 ID',
    `item_name_zh_tw` varchar(200) NULL COMMENT '正式道具繁體中文名稱',
    `item_family_zh_tw` varchar(80) NOT NULL COMMENT '道具家族',
    `official_category_zh_tw` varchar(80) NOT NULL COMMENT '官方分類繁體中文',
    `stackable` tinyint(1) NULL COMMENT '是否可堆疊',
    `maximum_stack` int NULL COMMENT '最大堆疊數',
    `trade_policy_zh_tw` varchar(40) NOT NULL COMMENT '交易政策',
    `warehouse_policy_zh_tw` varchar(40) NOT NULL COMMENT '倉庫政策',
    `equipment_slot_zh_tw` varchar(80) NULL COMMENT '裝備欄位',
    `required_level` int NULL COMMENT '需求等級',
    `physical_attack_bonus` int NULL COMMENT '物理攻擊加成',
    `magic_attack_bonus` int NULL COMMENT '法術攻擊加成',
    `physical_defense_bonus` int NULL COMMENT '物理防禦加成',
    `magic_defense_bonus` int NULL COMMENT '法術防禦加成',
    `hp_bonus` int NULL COMMENT 'HP 加成',
    `mp_bonus` int NULL COMMENT 'MP 加成',
    `speed_bonus` int NULL COMMENT '速度加成',
    `icon_key` varchar(64) NULL COMMENT '圖示 key',
    `model_key` varchar(128) NULL COMMENT '模型 key',
    `sync_status_zh_tw` varchar(64) NOT NULL COMMENT '同步判定',
    `sync_policy_zh_tw` varchar(256) NOT NULL COMMENT '同步政策與原因',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`item_id`),
    KEY `ix_item_content_profile_candidate_rows_client_item` (`client_item_id`),
    KEY `ix_item_content_profile_candidate_rows_family` (`item_family_zh_tw`),
    KEY `ix_item_content_profile_candidate_rows_category` (`official_category_zh_tw`),
    KEY `ix_item_content_profile_candidate_rows_slot` (`equipment_slot_zh_tw`),
    KEY `ix_item_content_profile_candidate_rows_status` (`sync_status_zh_tw`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='道具 profile 可讀候選列；保留道具屬性、裝備加成、圖示與模型，不包含 hash 或原始 payload';

INSERT INTO `god2_research`.`item_content_profile_candidate_rows`
    (`item_id`,`client_item_id`,`item_name_zh_tw`,`item_family_zh_tw`,`official_category_zh_tw`,`stackable`,`maximum_stack`,
     `trade_policy_zh_tw`,`warehouse_policy_zh_tw`,`equipment_slot_zh_tw`,`required_level`,`physical_attack_bonus`,`magic_attack_bonus`,
     `physical_defense_bonus`,`magic_defense_bonus`,`hp_bonus`,`mp_bonus`,`speed_bonus`,`icon_key`,`model_key`,`sync_status_zh_tw`,`sync_policy_zh_tw`)
SELECT
    profile_row.`ItemId`,
    profile_row.`ClientItemId`,
    item_row.`name_zh_tw`,
    profile_row.`ItemFamily`,
    profile_row.`OfficialCategoryZhTw`,
    profile_row.`Stackable`,
    profile_row.`MaximumStack`,
    CASE profile_row.`TradePolicy`
        WHEN 'Allowed' THEN '可交易'
        WHEN 'Disallowed' THEN '不可交易'
        ELSE '未確認'
    END,
    CASE profile_row.`WarehousePolicy`
        WHEN 'Allowed' THEN '可放倉庫'
        WHEN 'Disallowed' THEN '不可放倉庫'
        ELSE '未確認'
    END,
    profile_row.`EquipmentSlot`,
    profile_row.`RequiredLevel`,
    profile_row.`PhysicalAttackBonus`,
    profile_row.`MagicAttackBonus`,
    profile_row.`PhysicalDefenseBonus`,
    profile_row.`MagicDefenseBonus`,
    profile_row.`HpBonus`,
    profile_row.`MpBonus`,
    profile_row.`SpeedBonus`,
    profile_row.`IconKey`,
    profile_row.`ModelKey`,
    CASE
        WHEN item_row.`item_id` IS NULL THEN '缺正式道具'
        WHEN profile_row.`EquipmentSlot` IS NOT NULL AND profile_row.`EquipmentSlot` <> '' THEN '裝備屬性候選'
        ELSE '道具屬性候選'
    END,
    CASE
        WHEN item_row.`item_id` IS NULL THEN '找不到正式道具對應，保留為研究候選。'
        WHEN profile_row.`EquipmentSlot` IS NOT NULL AND profile_row.`EquipmentSlot` <> ''
            THEN '已對應正式道具；可用於核對裝備欄位與攻防 HP/MP 加成。'
        ELSE '已對應正式道具；可用於核對堆疊、交易、倉庫、圖示與模型。'
    END
FROM `god2`.`item_content_profiles` profile_row
LEFT JOIN `god2_game`.`item_registry` item_row
  ON item_row.`item_id` = profile_row.`ItemId`
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `item_name_zh_tw`=VALUES(`item_name_zh_tw`),
    `item_family_zh_tw`=VALUES(`item_family_zh_tw`),
    `official_category_zh_tw`=VALUES(`official_category_zh_tw`),
    `stackable`=VALUES(`stackable`),
    `maximum_stack`=VALUES(`maximum_stack`),
    `trade_policy_zh_tw`=VALUES(`trade_policy_zh_tw`),
    `warehouse_policy_zh_tw`=VALUES(`warehouse_policy_zh_tw`),
    `equipment_slot_zh_tw`=VALUES(`equipment_slot_zh_tw`),
    `required_level`=VALUES(`required_level`),
    `physical_attack_bonus`=VALUES(`physical_attack_bonus`),
    `magic_attack_bonus`=VALUES(`magic_attack_bonus`),
    `physical_defense_bonus`=VALUES(`physical_defense_bonus`),
    `magic_defense_bonus`=VALUES(`magic_defense_bonus`),
    `hp_bonus`=VALUES(`hp_bonus`),
    `mp_bonus`=VALUES(`mp_bonus`),
    `speed_bonus`=VALUES(`speed_bonus`),
    `icon_key`=VALUES(`icon_key`),
    `model_key`=VALUES(`model_key`),
    `sync_status_zh_tw`=VALUES(`sync_status_zh_tw`),
    `sync_policy_zh_tw`=VALUES(`sync_policy_zh_tw`),
    `updated_at_utc`=UTC_TIMESTAMP(6);

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`item_content_profile_candidate_rows` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`item_content_profile_candidate_rows` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`item_content_profile_candidate_rows` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`item_content_profile_candidate_rows` TO 'god2_catalog_builder'@'127.0.0.1';
