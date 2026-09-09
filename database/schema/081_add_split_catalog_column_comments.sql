ALTER TABLE `god2_game`.`equipment`
    MODIFY COLUMN `client_item_id` int NULL COMMENT '官方客戶端道具ID',
    MODIFY COLUMN `code` varchar(100) NULL COMMENT '可讀裝備代碼',
    MODIFY COLUMN `name_zh_tw` varchar(200) NULL COMMENT '裝備繁體名稱',
    MODIFY COLUMN `name_original` varchar(200) NULL COMMENT '裝備原文名稱',
    MODIFY COLUMN `description_zh_tw` text NULL COMMENT '裝備繁體說明',
    MODIFY COLUMN `item_category` varchar(50) NULL COMMENT '主要物品分類',
    MODIFY COLUMN `item_family` varchar(50) NULL COMMENT '裝備細分類',
    MODIFY COLUMN `source_item_type` varchar(32) NULL COMMENT '官方客戶端來源類型',
    MODIFY COLUMN `required_level` int NULL COMMENT '需求角色等級',
    MODIFY COLUMN `maximum_stack` int NULL COMMENT '最大堆疊數量',
    MODIFY COLUMN `weight` int NULL COMMENT '裝備重量',
    MODIFY COLUMN `buy_price` bigint NULL COMMENT '購買價格',
    MODIFY COLUMN `sell_price` bigint NULL COMMENT '出售價格',
    MODIFY COLUMN `droppable` tinyint(1) NULL COMMENT '是否可丟棄',
    MODIFY COLUMN `tradable` tinyint(1) NULL COMMENT '是否可交易',
    MODIFY COLUMN `storable` tinyint(1) NULL COMMENT '是否可存入倉庫',
    MODIFY COLUMN `stackable` tinyint(1) NULL COMMENT '是否可堆疊',
    MODIFY COLUMN `usable` tinyint(1) NULL COMMENT '是否可使用',
    MODIFY COLUMN `equippable` tinyint(1) NULL COMMENT '是否可裝備',
    MODIFY COLUMN `use_on_other` tinyint(1) NULL COMMENT '是否可對他人使用',
    MODIFY COLUMN `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '資料證據狀態',
    MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立UTC時間',
    MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新UTC時間';

ALTER TABLE `god2_game`.`item_usage_rules`
    MODIFY COLUMN `item_id` bigint NOT NULL COMMENT '共用物品索引ID',
    MODIFY COLUMN `class_restriction_zh_tw` varchar(200) NULL COMMENT '可使用職業限制',
    MODIFY COLUMN `minimum_rebirth` int NULL COMMENT '最低轉生次數',
    MODIFY COLUMN `gender_restriction_zh_tw` varchar(20) NULL COMMENT '性別限制',
    MODIFY COLUMN `equipment_target_restriction_zh_tw` varchar(200) NULL COMMENT '裝備或插卡部位限制',
    MODIFY COLUMN `condition_text_zh_tw` text NULL COMMENT '可讀限制條件原文',
    MODIFY COLUMN `field_evidence_status` varchar(30) NOT NULL DEFAULT 'Recovered' COMMENT '客戶端欄位證據狀態',
    MODIFY COLUMN `text_rule_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '說明文字推導證據狀態',
    MODIFY COLUMN `source_reference_zh_tw` varchar(300) NOT NULL COMMENT '可讀證據來源',
    MODIFY COLUMN `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用限制規則',
    MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新UTC時間';

ALTER TABLE `god2_game`.`weapons`
    MODIFY COLUMN `weapon_type_zh_tw` varchar(100) NULL COMMENT '武器類型',
    MODIFY COLUMN `attack_range` int NULL COMMENT '攻擊距離',
    MODIFY COLUMN `physical_attack_bonus` int NULL COMMENT '物理攻擊加成',
    MODIFY COLUMN `magic_attack_bonus` int NULL COMMENT '法術攻擊加成',
    MODIFY COLUMN `base_durability` int NULL COMMENT '基礎耐久',
    MODIFY COLUMN `maximum_enhancement` int NULL COMMENT '最大強化等級',
    MODIFY COLUMN `socket_count` int NULL COMMENT '基礎鑲嵌槽數',
    MODIFY COLUMN `class_restriction_zh_tw` varchar(200) NULL COMMENT '可使用職業限制';

ALTER TABLE `god2_game`.`magic_treasures`
    MODIFY COLUMN `treasure_type_zh_tw` varchar(100) NULL COMMENT '法寶類型',
    MODIFY COLUMN `battle_usable` tinyint(1) NULL COMMENT '戰鬥中是否可使用',
    MODIFY COLUMN `effect_description_zh_tw` text NULL COMMENT '法寶效果說明',
    MODIFY COLUMN `base_durability` int NULL COMMENT '基礎耐久',
    MODIFY COLUMN `maximum_enhancement` int NULL COMMENT '最大強化等級',
    MODIFY COLUMN `socket_count` int NULL COMMENT '基礎鑲嵌槽數',
    MODIFY COLUMN `class_restriction_zh_tw` varchar(200) NULL COMMENT '可使用職業限制';

ALTER TABLE `god2_game`.`item_icon_atlases`
    MODIFY COLUMN `validated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '圖集驗證UTC時間';

ALTER TABLE `god2_game`.`item_asset_mappings`
    MODIFY COLUMN `validated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '物品資源驗證UTC時間';

ALTER TABLE `god2_player`.`equipment_instances`
    MODIFY COLUMN `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '裝備實例是否有效',
    MODIFY COLUMN `admin_note` varchar(500) NULL COMMENT '管理備註',
    MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立UTC時間',
    MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新UTC時間';
