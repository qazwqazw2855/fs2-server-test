ALTER TABLE `god2_game`.`client_item_effect_visuals`
  MODIFY COLUMN `effect_id` int(11) NOT NULL COMMENT '客戶端物品效果 ID',
  MODIFY COLUMN `sound_index` int(11) NULL COMMENT '客戶端音效索引；未知時為 NULL',
  MODIFY COLUMN `visual_description_zh_tw` varchar(500) NOT NULL COMMENT '繁體中文視覺效果說明',
  MODIFY COLUMN `authored_mechanical_description_zh_tw` varchar(500) NULL COMMENT '人工整理的機制說明；未驗證時為 NULL',
  MODIFY COLUMN `mechanical_semantics_status` varchar(64) NOT NULL COMMENT '機制語意驗證狀態',
  MODIFY COLUMN `catalog_enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否保留在正式目錄',
  MODIFY COLUMN `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否允許正式服務端執行',
  MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) COMMENT '建立時間（UTC）',
  MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) ON UPDATE current_timestamp(6) COMMENT '更新時間（UTC）';

ALTER TABLE `god2_game`.`client_item_usage_flag_additions`
  MODIFY COLUMN `client_item_id` int(11) NOT NULL COMMENT '客戶端物品 ID',
  MODIFY COLUMN `display_name_zh_tw` varchar(300) NOT NULL COMMENT '繁體中文顯示名稱',
  MODIFY COLUMN `normal_use` tinyint(1) NULL COMMENT '是否可在一般場景使用',
  MODIFY COLUMN `battle_use` tinyint(1) NULL COMMENT '是否可在戰鬥中使用',
  MODIFY COLUMN `equippable` tinyint(1) NULL COMMENT '是否可裝備',
  MODIFY COLUMN `use_on_other` tinyint(1) NULL COMMENT '是否可指定其他目標',
  MODIFY COLUMN `hotkey_allowed` tinyint(1) NULL COMMENT '是否可放入快捷鍵',
  MODIFY COLUMN `tradable` tinyint(1) NULL COMMENT '是否可交易',
  MODIFY COLUMN `droppable` tinyint(1) NULL COMMENT '是否可丟棄或掉落',
  MODIFY COLUMN `storable` tinyint(1) NULL COMMENT '是否可存入倉庫',
  MODIFY COLUMN `stackable` tinyint(1) NULL COMMENT '是否可堆疊',
  MODIFY COLUMN `combine_up` tinyint(1) NULL COMMENT '是否支援向上合成',
  MODIFY COLUMN `combine_down` tinyint(1) NULL COMMENT '是否支援向下拆解',
  MODIFY COLUMN `binding_status` varchar(64) NOT NULL COMMENT '綁定狀態',
  MODIFY COLUMN `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否允許正式服務端執行',
  MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) COMMENT '建立時間（UTC）';

ALTER TABLE `god2_game`.`client_map_resource_identities`
  MODIFY COLUMN `map_identity_id` bigint(20) NOT NULL COMMENT '地圖資源識別 ID',
  MODIFY COLUMN `client_build_id` varchar(64) NOT NULL COMMENT '客戶端版本 ID',
  MODIFY COLUMN `client_area_id` int(11) NOT NULL COMMENT '客戶端區域 ID',
  MODIFY COLUMN `client_map_id` int(11) NOT NULL COMMENT '客戶端地圖 ID',
  MODIFY COLUMN `map_id` bigint(20) NOT NULL COMMENT '服務端地圖 ID',
  MODIFY COLUMN `resource_key` varchar(512) NOT NULL COMMENT '客戶端地圖資源鍵',
  MODIFY COLUMN `world_map_x` int(11) NOT NULL COMMENT '官方世界地圖 UI X 座標',
  MODIFY COLUMN `world_map_y` int(11) NOT NULL COMMENT '官方世界地圖 UI Y 座標',
  MODIFY COLUMN `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用此地圖資源識別';

ALTER TABLE `god2_game`.`equipment`
  MODIFY COLUMN `icon_id` int(11) NULL COMMENT '官方全域物品圖示代碼',
  MODIFY COLUMN `model_key` varchar(256) NULL COMMENT '官方客戶端外觀或模型鍵';

ALTER TABLE `god2_game`.`items`
  MODIFY COLUMN `client_item_id` int(11) NULL COMMENT '官方客戶端物品顯示 ID',
  MODIFY COLUMN `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否載入正式服務端；0 為停用';

ALTER TABLE `god2_game`.`item_asset_mappings`
  MODIFY COLUMN `item_id` bigint(20) NOT NULL COMMENT '共用物品索引 ID',
  MODIFY COLUMN `client_item_id` int(11) NOT NULL COMMENT '官方客戶端物品 ID',
  MODIFY COLUMN `catalog_type` varchar(30) NOT NULL COMMENT '目錄類型：一般物品、武器、裝備或法寶',
  MODIFY COLUMN `icon_code` int(11) NOT NULL COMMENT '官方全域圖示代碼',
  MODIFY COLUMN `icon_atlas_id` int(11) NULL COMMENT '對應的官方 RomMaps 圖集 ID',
  MODIFY COLUMN `icon_sprite_index` int(11) NULL COMMENT '圖集中以 0 起算的圖示序號',
  MODIFY COLUMN `model_key` varchar(256) NOT NULL COMMENT '官方客戶端外觀或模型鍵',
  MODIFY COLUMN `icon_validation_status` varchar(30) NOT NULL COMMENT '圖示驗證狀態',
  MODIFY COLUMN `model_validation_status` varchar(30) NOT NULL COMMENT '模型驗證狀態',
  MODIFY COLUMN `overall_validation_status` varchar(30) NOT NULL COMMENT '整體資源驗證狀態',
  MODIFY COLUMN `validation_note_zh_tw` varchar(500) NULL COMMENT '繁體中文驗證說明';

ALTER TABLE `god2_game`.`item_effects`
  MODIFY COLUMN `effect_type` varchar(32) NOT NULL COMMENT '服務端效果類型：回復生命或回復法力',
  MODIFY COLUMN `usage_scope` varchar(16) NOT NULL COMMENT '可使用場景：一般場景、戰鬥或兩者皆可',
  MODIFY COLUMN `target_policy` varchar(32) NOT NULL COMMENT '目標規則：限自身或允許指定其他目標',
  MODIFY COLUMN `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否允許正式服務端執行';

ALTER TABLE `god2_game`.`item_icon_atlases`
  MODIFY COLUMN `atlas_id` int(11) NOT NULL COMMENT 'RomMaps 圖集列 ID',
  MODIFY COLUMN `minimum_icon_code` int(11) NOT NULL COMMENT '全域圖示代碼下限（含）',
  MODIFY COLUMN `maximum_icon_code` int(11) NOT NULL COMMENT '全域圖示代碼上限（含）',
  MODIFY COLUMN `validation_status` varchar(30) NOT NULL COMMENT '圖集驗證狀態';

ALTER TABLE `god2_game`.`item_registry`
  MODIFY COLUMN `client_item_id` int(11) NULL COMMENT '官方客戶端物品顯示 ID',
  MODIFY COLUMN `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否載入正式服務端；0 為停用';

ALTER TABLE `god2_game`.`item_usage_rules`
  MODIFY COLUMN `normal_use` tinyint(1) NULL COMMENT '是否可在一般場景使用',
  MODIFY COLUMN `battle_use` tinyint(1) NULL COMMENT '是否可在戰鬥中使用',
  MODIFY COLUMN `equippable` tinyint(1) NULL COMMENT '是否可裝備',
  MODIFY COLUMN `use_on_other` tinyint(1) NULL COMMENT '是否可指定其他目標',
  MODIFY COLUMN `hotkey_allowed` tinyint(1) NULL COMMENT '是否可放入快捷鍵',
  MODIFY COLUMN `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否允許正式服務端執行';

ALTER TABLE `god2_game`.`magic_treasures`
  MODIFY COLUMN `client_item_id` int(11) NULL COMMENT '官方客戶端物品顯示 ID',
  MODIFY COLUMN `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否載入正式服務端；0 為停用';

ALTER TABLE `god2_game`.`maps`
  MODIFY COLUMN `world_map_x` int(11) NULL COMMENT '官方世界地圖 UI X 座標；不是戰鬥或怪物出生座標',
  MODIFY COLUMN `world_map_y` int(11) NULL COMMENT '官方世界地圖 UI Y 座標；不是戰鬥或怪物出生座標';

ALTER TABLE `god2_game`.`npc_appearance_identities`
  MODIFY COLUMN `appearance_identity_id` bigint(20) NOT NULL COMMENT 'NPC 外觀識別 ID',
  MODIFY COLUMN `client_build_id` varchar(64) NOT NULL COMMENT '客戶端版本 ID',
  MODIFY COLUMN `client_entity_handle` int(10) unsigned NOT NULL COMMENT '客戶端實體控制代碼',
  MODIFY COLUMN `name_zh_tw` varchar(150) NOT NULL COMMENT 'NPC 繁體中文名稱',
  MODIFY COLUMN `official_selector` int(10) unsigned NOT NULL COMMENT '官方外觀選擇值',
  MODIFY COLUMN `official_resource_type` tinyint(3) unsigned NOT NULL COMMENT '官方資源類型',
  MODIFY COLUMN `official_resource_ordinal` tinyint(3) unsigned NOT NULL COMMENT '官方資源序號',
  MODIFY COLUMN `runtime_family_status` varchar(30) NOT NULL COMMENT '服務端外觀家族對應狀態',
  MODIFY COLUMN `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否啟用此 NPC 外觀識別';
