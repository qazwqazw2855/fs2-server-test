-- Canonical, HeidiSQL-editable gameplay and player schemas.
-- Evidence/ETL tables remain in `god2` and are intentionally not modified here.

CREATE DATABASE IF NOT EXISTS `god2_game`
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS `god2_player`
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS `god2_game_meta`
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_game`.`items` (
    `item_id` bigint NOT NULL COMMENT '物品 ID',
    `code` varchar(100) NULL COMMENT '穩定物品代碼；未知時為 NULL',
    `name_zh_tw` varchar(200) NOT NULL COMMENT '物品繁體中文名稱',
    `name_original` varchar(200) NULL COMMENT '來源原文名稱',
    `description_zh_tw` text NULL COMMENT '物品繁體中文說明',
    `item_category` varchar(50) NOT NULL COMMENT '物品分類',
    `item_family` varchar(50) NULL COMMENT '物品家族或子分類',
    `icon_id` int NULL COMMENT '圖示資源 ID',
    `model_id` int NULL COMMENT '模型資源 ID',
    `required_level` int NULL COMMENT '需求角色等級；未知時為 NULL',
    `maximum_stack` int NULL COMMENT '最大堆疊數；未知時為 NULL',
    `weight` int NULL COMMENT '物品重量；未知時為 NULL',
    `buy_price` bigint NULL COMMENT '基礎買價；未知時為 NULL',
    `sell_price` bigint NULL COMMENT '基礎賣價；未知時為 NULL',
    `droppable` tinyint(1) NULL COMMENT '是否可掉落；未知時為 NULL',
    `tradable` tinyint(1) NULL COMMENT '是否可交易；未知時為 NULL',
    `storable` tinyint(1) NULL COMMENT '是否可存入倉庫；未知時為 NULL',
    `stackable` tinyint(1) NULL COMMENT '是否可堆疊；未知時為 NULL',
    `usable` tinyint(1) NULL COMMENT '是否可使用；未知時為 NULL',
    `equippable` tinyint(1) NULL COMMENT '是否可裝備；未知時為 NULL',
    `use_on_other` tinyint(1) NULL COMMENT '是否可對其他目標使用；未知時為 NULL',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '欄目整體證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否載入正式 Runtime；0 為停用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`item_id`),
    UNIQUE KEY `ux_items_code` (`code`),
    KEY `ix_items_name_zh_tw` (`name_zh_tw`),
    KEY `ix_items_name_original` (`name_original`),
    KEY `ix_items_category_family` (`item_category`,`item_family`),
    KEY `ix_items_enabled` (`enabled`),
    CONSTRAINT `ck_items_enabled` CHECK (`enabled` IN (0,1)),
    CONSTRAINT `ck_items_evidence_status` CHECK (`evidence_status` IN ('Verified','Recovered','Derived','Candidate','EvidenceBlocked','Unknown'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='服主可直接編輯的正式物品目錄';

CREATE TABLE IF NOT EXISTS `god2_game`.`item_sets` (
    `set_id` bigint NOT NULL COMMENT '套裝 ID',
    `name_zh_tw` varchar(200) NOT NULL COMMENT '套裝繁體中文名稱',
    `name_original` varchar(200) NULL COMMENT '套裝原文名稱',
    `description_zh_tw` text NULL COMMENT '套裝說明',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`set_id`),
    KEY `ix_item_sets_enabled` (`enabled`),
    CONSTRAINT `ck_item_sets_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='物品套裝定義';

CREATE TABLE IF NOT EXISTS `god2_game`.`equipment` (
    `item_id` bigint NOT NULL COMMENT '對應物品 ID',
    `equipment_type` varchar(50) NULL COMMENT '裝備類型',
    `equipment_slot` varchar(50) NULL COMMENT '裝備欄位',
    `class_mask` bigint NULL COMMENT '可使用職業位元遮罩',
    `gender_mask` bigint NULL COMMENT '可使用性別位元遮罩',
    `required_strength` int NULL COMMENT '需求力量；未知時為 NULL',
    `required_intelligence` int NULL COMMENT '需求智力；未知時為 NULL',
    `hp_bonus` bigint NULL COMMENT '最大 HP 加成',
    `mp_bonus` bigint NULL COMMENT '最大 MP 加成',
    `strength_bonus` int NULL COMMENT '力量加成',
    `constitution_bonus` int NULL COMMENT '體力加成',
    `intelligence_bonus` int NULL COMMENT '智力加成',
    `speed_bonus` int NULL COMMENT '速度加成',
    `physical_attack_bonus` int NULL COMMENT '物理攻擊加成',
    `physical_defense_bonus` int NULL COMMENT '物理防禦加成',
    `magic_attack_bonus` int NULL COMMENT '法術攻擊加成',
    `magic_defense_bonus` int NULL COMMENT '法術防禦加成',
    `metal_bonus` int NULL COMMENT '金屬性加成',
    `wood_bonus` int NULL COMMENT '木屬性加成',
    `water_bonus` int NULL COMMENT '水屬性加成',
    `fire_bonus` int NULL COMMENT '火屬性加成',
    `earth_bonus` int NULL COMMENT '土屬性加成',
    `metal_resistance` int NULL COMMENT '金抗性',
    `wood_resistance` int NULL COMMENT '木抗性',
    `water_resistance` int NULL COMMENT '水抗性',
    `fire_resistance` int NULL COMMENT '火抗性',
    `earth_resistance` int NULL COMMENT '土抗性',
    `durability` int NULL COMMENT '耐久度',
    `maximum_enhancement` int NULL COMMENT '最大強化等級',
    `socket_count` int NULL COMMENT '鑲嵌槽數',
    `set_id` bigint NULL COMMENT '套裝 ID',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`item_id`),
    KEY `ix_equipment_set_id` (`set_id`),
    KEY `ix_equipment_enabled` (`enabled`),
    CONSTRAINT `fk_equipment_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `fk_equipment_set` FOREIGN KEY (`set_id`) REFERENCES `god2_game`.`item_sets` (`set_id`),
    CONSTRAINT `ck_equipment_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='物品裝備屬性';

CREATE TABLE IF NOT EXISTS `god2_game`.`status_effects` (
    `status_effect_id` bigint NOT NULL COMMENT '狀態效果 ID',
    `name_zh_tw` varchar(150) NOT NULL COMMENT '狀態效果繁體中文名稱',
    `effect_type` varchar(50) NOT NULL COMMENT '效果類型',
    `element` varchar(20) NULL COMMENT '五行屬性',
    `duration_rounds` int NULL COMMENT '效果持續回合數',
    `maximum_stacks` int NULL COMMENT '最大疊加層數',
    `stack_mode` varchar(30) NULL COMMENT '疊加模式',
    `apply_phase` varchar(30) NULL COMMENT '套用階段',
    `tick_phase` varchar(30) NULL COMMENT '每回合生效階段',
    `expire_phase` varchar(30) NULL COMMENT '到期階段',
    `value_type` varchar(30) NULL COMMENT '效果數值型別',
    `base_value` decimal(18,6) NULL COMMENT '效果基礎值；未知時為 NULL',
    `can_dispel` tinyint(1) NULL COMMENT '是否可解除',
    `is_buff` tinyint(1) NULL COMMENT '是否為增益效果',
    `is_debuff` tinyint(1) NULL COMMENT '是否為減益效果',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`status_effect_id`),
    KEY `ix_status_effects_enabled` (`enabled`),
    CONSTRAINT `ck_status_effects_phases` CHECK ((`apply_phase` IS NULL OR `apply_phase` IN ('BattleStart','RoundStart','BeforeAction','AfterAction','RoundEnd','BattleEnd','Unknown')) AND (`tick_phase` IS NULL OR `tick_phase` IN ('BattleStart','RoundStart','BeforeAction','AfterAction','RoundEnd','BattleEnd','Unknown')) AND (`expire_phase` IS NULL OR `expire_phase` IN ('BattleStart','RoundStart','BeforeAction','AfterAction','RoundEnd','BattleEnd','Unknown'))),
    CONSTRAINT `ck_status_effects_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='回合制 Buff、Debuff 與狀態效果';

CREATE TABLE IF NOT EXISTS `god2_game`.`character_classes` (
    `class_id` bigint NOT NULL COMMENT '職業 ID',
    `code` varchar(100) NULL COMMENT '穩定職業代碼',
    `name_zh_tw` varchar(100) NOT NULL COMMENT '職業繁體中文名稱',
    `name_original` varchar(100) NULL COMMENT '職業原文名稱',
    `resource_id` int NULL COMMENT '客戶端資源 ID',
    `description_zh_tw` text NULL COMMENT '職業說明',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`class_id`),
    UNIQUE KEY `ux_character_classes_code` (`code`),
    CONSTRAINT `ck_character_classes_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色職業定義';

CREATE TABLE IF NOT EXISTS `god2_game`.`class_level_stats` (
    `class_id` bigint NOT NULL COMMENT '職業 ID',
    `level` int NOT NULL COMMENT '職業等級',
    `base_max_hp` bigint NULL COMMENT '該等級基礎最大 HP',
    `base_max_mp` bigint NULL COMMENT '該等級基礎最大 MP',
    `base_strength` int NULL COMMENT '基礎力量',
    `base_constitution` int NULL COMMENT '基礎體力',
    `base_intelligence` int NULL COMMENT '基礎智力',
    `base_speed` int NULL COMMENT '基礎速度',
    `base_metal` int NULL COMMENT '基礎金屬性',
    `base_wood` int NULL COMMENT '基礎木屬性',
    `base_water` int NULL COMMENT '基礎水屬性',
    `base_fire` int NULL COMMENT '基礎火屬性',
    `base_earth` int NULL COMMENT '基礎土屬性',
    `available_stat_points` int NULL COMMENT '可用配點',
    `required_experience` bigint NULL COMMENT '升級需求經驗',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`class_id`,`level`),
    CONSTRAINT `fk_class_level_stats_class` FOREIGN KEY (`class_id`) REFERENCES `god2_game`.`character_classes` (`class_id`),
    CONSTRAINT `ck_class_level_stats_level` CHECK (`level` > 0),
    CONSTRAINT `ck_class_level_stats_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='職業逐級能力模板';

CREATE TABLE IF NOT EXISTS `god2_game`.`class_stat_growth` (
    `class_id` bigint NOT NULL COMMENT '職業 ID',
    `stat_name` varchar(50) NOT NULL COMMENT '能力欄位名稱',
    `growth_type` varchar(30) NOT NULL COMMENT '成長公式型別',
    `growth_value` decimal(18,8) NULL COMMENT '成長值；公式未確認時為 NULL',
    `level_interval` int NULL COMMENT '成長發生的等級間隔',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'EvidenceBlocked' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`class_id`,`stat_name`,`growth_type`),
    CONSTRAINT `fk_class_stat_growth_class` FOREIGN KEY (`class_id`) REFERENCES `god2_game`.`character_classes` (`class_id`),
    CONSTRAINT `ck_class_stat_growth_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='職業能力成長規則';

CREATE TABLE IF NOT EXISTS `god2_game`.`life_skills` (
    `life_skill_id` bigint NOT NULL COMMENT '生活技能 ID',
    `code` varchar(50) NOT NULL COMMENT '生活技能唯一代碼',
    `name_zh_tw` varchar(100) NOT NULL COMMENT '生活技能繁體中文名稱',
    `name_original` varchar(100) NULL COMMENT '生活技能原文名稱',
    `description_zh_tw` text NULL COMMENT '生活技能說明',
    `maximum_level` int NULL COMMENT '最大等級；未知時為 NULL',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`life_skill_id`),
    UNIQUE KEY `ux_life_skills_code` (`code`),
    CONSTRAINT `ck_life_skills_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色四項生活技能定義';

CREATE TABLE IF NOT EXISTS `god2_game`.`pet_growth_profiles` (
    `growth_profile_id` bigint NOT NULL COMMENT '戰寵成長模板 ID',
    `name_zh_tw` varchar(150) NOT NULL COMMENT '成長模板名稱',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'EvidenceBlocked' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`growth_profile_id`),
    CONSTRAINT `ck_pet_growth_profiles_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='戰寵成長模板';

CREATE TABLE IF NOT EXISTS `god2_game`.`pet_templates` (
    `pet_template_id` bigint NOT NULL COMMENT '戰寵模板 ID',
    `code` varchar(100) NULL COMMENT '穩定戰寵代碼',
    `name_zh_tw` varchar(150) NOT NULL COMMENT '戰寵繁體中文名稱',
    `name_original` varchar(150) NULL COMMENT '戰寵原文名稱',
    `pet_family` varchar(50) NULL COMMENT '戰寵家族',
    `resource_id` int NULL COMMENT '客戶端資源 ID',
    `base_level` int NULL COMMENT '模板基礎等級',
    `base_max_hp` bigint NULL COMMENT '模板基礎最大 HP',
    `base_max_mp` bigint NULL COMMENT '模板基礎最大 MP',
    `base_max_lifespan` bigint NULL COMMENT '模板基礎最大壽命',
    `base_strength` int NULL COMMENT '模板基礎力量',
    `base_constitution` int NULL COMMENT '模板基礎體力',
    `base_intelligence` int NULL COMMENT '模板基礎智力',
    `base_speed` int NULL COMMENT '模板基礎速度',
    `base_metal` int NULL COMMENT '模板基礎金屬性',
    `base_wood` int NULL COMMENT '模板基礎木屬性',
    `base_water` int NULL COMMENT '模板基礎水屬性',
    `base_fire` int NULL COMMENT '模板基礎火屬性',
    `base_earth` int NULL COMMENT '模板基礎土屬性',
    `base_physical_attack` int NULL COMMENT '模板基礎物理攻擊',
    `base_physical_defense` int NULL COMMENT '模板基礎物理防禦',
    `base_magic_attack` int NULL COMMENT '模板基礎法術攻擊',
    `base_magic_defense` int NULL COMMENT '模板基礎法術防禦',
    `initial_stat_points` int NULL COMMENT '初始可用配點',
    `maximum_skill_slots` int NULL COMMENT '最大技能槽數',
    `growth_profile_id` bigint NULL COMMENT '成長模板 ID',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`pet_template_id`),
    UNIQUE KEY `ux_pet_templates_code` (`code`),
    KEY `ix_pet_templates_growth_profile` (`growth_profile_id`),
    CONSTRAINT `fk_pet_templates_growth_profile` FOREIGN KEY (`growth_profile_id`) REFERENCES `god2_game`.`pet_growth_profiles` (`growth_profile_id`),
    CONSTRAINT `ck_pet_templates_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='戰寵靜態模板';

CREATE TABLE IF NOT EXISTS `god2_game`.`immortal_ranks` (
    `rank_id` bigint NOT NULL COMMENT '神仙階位 ID',
    `name_zh_tw` varchar(100) NOT NULL COMMENT '階位繁體中文名稱',
    `name_original` varchar(100) NULL COMMENT '階位原文名稱',
    `display_order` int NOT NULL COMMENT '顯示順序',
    `required_level` int NULL COMMENT '需求等級；未知時為 NULL',
    `required_experience` bigint NULL COMMENT '需求經驗；未知時為 NULL',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`rank_id`),
    KEY `ix_immortal_ranks_display_order` (`display_order`),
    CONSTRAINT `ck_immortal_ranks_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='神仙階位定義';

CREATE TABLE IF NOT EXISTS `god2_game`.`immortal_templates` (
    `immortal_template_id` bigint NOT NULL COMMENT '神仙模板 ID',
    `code` varchar(100) NULL COMMENT '穩定神仙代碼',
    `name_zh_tw` varchar(150) NOT NULL COMMENT '神仙繁體中文名稱',
    `name_original` varchar(150) NULL COMMENT '神仙原文名稱',
    `resource_id` int NULL COMMENT '客戶端資源 ID',
    `immortal_family` varchar(50) NULL COMMENT '神仙家族',
    `initial_rank_id` bigint NULL COMMENT '初始階位 ID',
    `initial_level` int NULL COMMENT '初始等級',
    `base_max_hp` bigint NULL COMMENT '模板基礎最大 HP',
    `base_max_mp` bigint NULL COMMENT '模板基礎最大 MP',
    `base_strength` int NULL COMMENT '模板基礎力量',
    `base_constitution` int NULL COMMENT '模板基礎體力',
    `base_intelligence` int NULL COMMENT '模板基礎智力',
    `base_speed` int NULL COMMENT '模板基礎速度',
    `base_metal` int NULL COMMENT '模板基礎金屬性',
    `base_wood` int NULL COMMENT '模板基礎木屬性',
    `base_water` int NULL COMMENT '模板基礎水屬性',
    `base_fire` int NULL COMMENT '模板基礎火屬性',
    `base_earth` int NULL COMMENT '模板基礎土屬性',
    `base_physical_attack` int NULL COMMENT '模板基礎物理攻擊',
    `base_physical_defense` int NULL COMMENT '模板基礎物理防禦',
    `base_magic_attack` int NULL COMMENT '模板基礎法術攻擊',
    `base_magic_defense` int NULL COMMENT '模板基礎法術防禦',
    `maximum_innate_skill_slots` int NULL COMMENT '最大固有技能槽數',
    `maximum_active_skill_slots` int NULL COMMENT '最大主動技能槽數',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`immortal_template_id`),
    UNIQUE KEY `ux_immortal_templates_code` (`code`),
    KEY `ix_immortal_templates_initial_rank` (`initial_rank_id`),
    CONSTRAINT `fk_immortal_templates_initial_rank` FOREIGN KEY (`initial_rank_id`) REFERENCES `god2_game`.`immortal_ranks` (`rank_id`),
    CONSTRAINT `ck_immortal_templates_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='神仙靜態模板';

CREATE TABLE IF NOT EXISTS `god2_game`.`mount_templates` (
    `mount_template_id` bigint NOT NULL COMMENT '坐騎模板 ID',
    `code` varchar(100) NULL COMMENT '穩定坐騎代碼',
    `name_zh_tw` varchar(150) NOT NULL COMMENT '坐騎繁體中文名稱',
    `name_original` varchar(150) NULL COMMENT '坐騎原文名稱',
    `resource_id` int NULL COMMENT '客戶端資源 ID',
    `speed_bonus` int NULL COMMENT '世界移動速度加成；未知時為 NULL',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`mount_template_id`),
    UNIQUE KEY `ux_mount_templates_code` (`code`),
    CONSTRAINT `ck_mount_templates_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='坐騎靜態模板';

CREATE TABLE IF NOT EXISTS `god2_game`.`maps` (
    `map_id` bigint NOT NULL COMMENT '地圖 ID',
    `code` varchar(100) NULL COMMENT '穩定地圖代碼',
    `name_zh_tw` varchar(150) NOT NULL COMMENT '地圖繁體中文名稱',
    `name_original` varchar(150) NULL COMMENT '地圖原文名稱',
    `resource_id` int NULL COMMENT '地圖資源 ID',
    `resource_identity` varchar(256) NULL COMMENT '客戶端地圖資源識別',
    `width` int NULL COMMENT '來源地圖寬度；未知時為 NULL',
    `height` int NULL COMMENT '來源地圖高度；未知時為 NULL',
    `minimum_x` int NULL COMMENT '有效最小 X 座標',
    `maximum_x` int NULL COMMENT '有效最大 X 座標',
    `minimum_y` int NULL COMMENT '有效最小 Y 座標',
    `maximum_y` int NULL COMMENT '有效最大 Y 座標',
    `allow_teleport` tinyint(1) NULL COMMENT '是否允許傳送',
    `allow_escape` tinyint(1) NULL COMMENT '是否允許脫離',
    `allow_resurrection` tinyint(1) NULL COMMENT '是否允許復活',
    `allow_mount` tinyint(1) NULL COMMENT '是否允許坐騎',
    `allow_pet` tinyint(1) NULL COMMENT '是否允許戰寵',
    `experience_rate` decimal(12,8) NULL COMMENT '地圖經驗倍率；未知時為 NULL',
    `drop_rate` decimal(12,8) NULL COMMENT '地圖掉落倍率；未知時為 NULL',
    `monster_density_rate` decimal(12,8) NULL COMMENT '怪物密度倍率；未知時為 NULL',
    `client_build_id` varchar(64) NULL COMMENT '已驗證客戶端版本 ID',
    `client_map_id` int NULL COMMENT '客戶端地圖 ID',
    `client_area_id` int NULL COMMENT '客戶端區域 ID',
    `coordinate_scale_x` decimal(18,8) NULL COMMENT '座標 X 轉換倍率',
    `coordinate_scale_y` decimal(18,8) NULL COMMENT '座標 Y 轉換倍率',
    `coordinate_offset_x` decimal(18,8) NULL COMMENT '座標 X 轉換位移',
    `coordinate_offset_y` decimal(18,8) NULL COMMENT '座標 Y 轉換位移',
    `identity_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '地圖識別證據狀態',
    `coordinate_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '座標證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`map_id`),
    UNIQUE KEY `ux_maps_code` (`code`),
    KEY `ix_maps_enabled` (`enabled`),
    CONSTRAINT `ck_maps_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='正式地圖目錄';

CREATE TABLE IF NOT EXISTS `god2_game`.`map_rules` (
    `map_id` bigint NOT NULL COMMENT '地圖 ID',
    `rule_key` varchar(100) NOT NULL COMMENT '地圖規則代碼',
    `numeric_value` decimal(24,8) NULL COMMENT '規則數值',
    `text_value` varchar(1000) NULL COMMENT '規則文字值',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`map_id`,`rule_key`),
    CONSTRAINT `fk_map_rules_map` FOREIGN KEY (`map_id`) REFERENCES `god2_game`.`maps` (`map_id`),
    CONSTRAINT `ck_map_rules_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='逐地圖規則參數';

CREATE TABLE IF NOT EXISTS `god2_game`.`monster_ai_profiles` (
    `ai_profile_id` bigint NOT NULL COMMENT '怪物 AI 模板 ID',
    `name_zh_tw` varchar(150) NOT NULL COMMENT 'AI 模板名稱',
    `description_zh_tw` text NULL COMMENT 'AI 模板說明',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`ai_profile_id`),
    CONSTRAINT `ck_monster_ai_profiles_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='怪物回合制 AI 模板';

CREATE TABLE IF NOT EXISTS `god2_game`.`monsters` (
    `monster_id` bigint NOT NULL COMMENT '怪物 ID',
    `code` varchar(100) NULL COMMENT '穩定怪物代碼',
    `name_zh_tw` varchar(150) NOT NULL COMMENT '怪物繁體中文名稱',
    `name_original` varchar(150) NULL COMMENT '怪物原文名稱',
    `monster_family` varchar(50) NULL COMMENT '怪物家族',
    `resource_id` int NULL COMMENT '客戶端資源 ID',
    `level` int NULL COMMENT '怪物等級；未知時為 NULL',
    `max_hp` bigint NULL COMMENT '怪物最大 HP；未知時為 NULL',
    `max_mp` bigint NULL COMMENT '怪物最大 MP；未知時為 NULL',
    `strength` int NULL COMMENT '怪物力量',
    `constitution` int NULL COMMENT '怪物體力',
    `intelligence` int NULL COMMENT '怪物智力',
    `speed` int NULL COMMENT '怪物速度；不代表已確認先手公式',
    `metal` int NULL COMMENT '怪物金屬性',
    `wood` int NULL COMMENT '怪物木屬性',
    `water` int NULL COMMENT '怪物水屬性',
    `fire` int NULL COMMENT '怪物火屬性',
    `earth` int NULL COMMENT '怪物土屬性',
    `physical_attack` int NULL COMMENT '怪物物理攻擊',
    `physical_defense` int NULL COMMENT '怪物物理防禦',
    `magic_attack` int NULL COMMENT '怪物法術攻擊',
    `magic_defense` int NULL COMMENT '怪物法術防禦',
    `experience_reward` bigint NULL COMMENT '擊敗經驗獎勵',
    `currency_reward` bigint NULL COMMENT '擊敗貨幣獎勵',
    `ai_profile_id` bigint NULL COMMENT '回合制 AI 模板 ID',
    `boss` tinyint(1) NULL COMMENT '是否為首領',
    `elite` tinyint(1) NULL COMMENT '是否為菁英',
    `aggressive` tinyint(1) NULL COMMENT '是否主動進入戰鬥',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`monster_id`),
    UNIQUE KEY `ux_monsters_code` (`code`),
    KEY `ix_monsters_name_zh_tw` (`name_zh_tw`),
    KEY `ix_monsters_ai_profile` (`ai_profile_id`),
    KEY `ix_monsters_enabled` (`enabled`),
    CONSTRAINT `fk_monsters_ai_profile` FOREIGN KEY (`ai_profile_id`) REFERENCES `god2_game`.`monster_ai_profiles` (`ai_profile_id`),
    CONSTRAINT `ck_monsters_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='回合制怪物模板';

CREATE TABLE IF NOT EXISTS `god2_game`.`npcs` (
    `npc_id` bigint NOT NULL COMMENT 'NPC ID',
    `code` varchar(100) NULL COMMENT '穩定 NPC 代碼',
    `name_zh_tw` varchar(150) NOT NULL COMMENT 'NPC 繁體中文名稱',
    `name_original` varchar(150) NULL COMMENT 'NPC 原文名稱',
    `npc_type` varchar(50) NOT NULL COMMENT 'NPC 類型',
    `resource_id` int NULL COMMENT '客戶端資源 ID',
    `resource_key` varchar(256) NULL COMMENT '客戶端資源鍵',
    `interaction_family` varchar(64) NULL COMMENT '互動服務家族',
    `default_dialog_id` bigint NULL COMMENT '預設對話 ID',
    `merchant_id` bigint NULL COMMENT '商店 ID',
    `quest_provider` tinyint(1) NULL COMMENT '是否提供任務',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`npc_id`),
    UNIQUE KEY `ux_npcs_code` (`code`),
    KEY `ix_npcs_enabled` (`enabled`),
    CONSTRAINT `ck_npcs_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='NPC 模板；與怪物分表';

CREATE TABLE IF NOT EXISTS `god2_game`.`skills` (
    `skill_id` bigint NOT NULL COMMENT '技能 ID',
    `code` varchar(100) NULL COMMENT '穩定技能代碼',
    `name_zh_tw` varchar(150) NOT NULL COMMENT '技能繁體中文名稱',
    `name_original` varchar(150) NULL COMMENT '技能原文名稱',
    `description_zh_tw` text NULL COMMENT '技能繁體中文說明',
    `skill_family` varchar(50) NULL COMMENT '技能家族',
    `skill_category` varchar(50) NULL COMMENT '技能分類',
    `damage_type` varchar(30) NULL COMMENT '傷害類型',
    `element` varchar(20) NULL COMMENT '五行屬性',
    `required_level` int NULL COMMENT '需求等級',
    `required_class_id` bigint NULL COMMENT '需求職業 ID',
    `maximum_level` int NULL COMMENT '技能最大等級',
    `mp_cost` bigint NULL COMMENT '技能 MP 消耗；未知時為 NULL',
    `hp_cost` bigint NULL COMMENT '技能 HP 消耗；未知時為 NULL',
    `item_cost_id` bigint NULL COMMENT '施放消耗物品 ID',
    `item_cost_count` int NULL COMMENT '施放消耗物品數量',
    `cooldown_rounds` int NULL COMMENT '技能冷卻回合數',
    `cooldown_ms` int NULL COMMENT '世界或介面冷卻毫秒',
    `cast_rounds` int NULL COMMENT '施放所需回合數',
    `duration_rounds` int NULL COMMENT '技能持續回合數',
    `action_priority` int NULL COMMENT '行動優先值；公式未知時為 NULL',
    `target_side` varchar(30) NULL COMMENT '目標陣營',
    `target_type` varchar(50) NULL COMMENT '目標選擇型別',
    `target_count` int NULL COMMENT '目標數量',
    `can_target_self` tinyint(1) NULL COMMENT '是否可選自己',
    `can_target_ally` tinyint(1) NULL COMMENT '是否可選友方',
    `can_target_enemy` tinyint(1) NULL COMMENT '是否可選敵方',
    `can_target_dead` tinyint(1) NULL COMMENT '是否可選死亡目標',
    `consumes_turn` tinyint(1) NULL COMMENT '是否消耗回合',
    `can_critical` tinyint(1) NULL COMMENT '是否可暴擊',
    `can_miss` tinyint(1) NULL COMMENT '是否可能未命中',
    `base_power` decimal(18,6) NULL COMMENT '技能基礎威力',
    `hit_rate` decimal(12,8) NULL COMMENT '技能命中率；未知時為 NULL',
    `critical_rate` decimal(12,8) NULL COMMENT '技能暴擊率；未知時為 NULL',
    `status_effect_id` bigint NULL COMMENT '主要狀態效果 ID',
    `animation_id` int NULL COMMENT '動畫資源 ID',
    `effect_id` int NULL COMMENT '特效資源 ID',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`skill_id`),
    UNIQUE KEY `ux_skills_code` (`code`),
    KEY `ix_skills_required_class` (`required_class_id`),
    KEY `ix_skills_status_effect` (`status_effect_id`),
    KEY `ix_skills_enabled` (`enabled`),
    CONSTRAINT `fk_skills_required_class` FOREIGN KEY (`required_class_id`) REFERENCES `god2_game`.`character_classes` (`class_id`),
    CONSTRAINT `fk_skills_item_cost` FOREIGN KEY (`item_cost_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `fk_skills_status_effect` FOREIGN KEY (`status_effect_id`) REFERENCES `god2_game`.`status_effects` (`status_effect_id`),
    CONSTRAINT `ck_skills_damage_type` CHECK (`damage_type` IS NULL OR `damage_type` IN ('Physical','Magic','Pure','Healing','Status','Unknown')),
    CONSTRAINT `ck_skills_element` CHECK (`element` IS NULL OR `element` IN ('Metal','Wood','Water','Fire','Earth','None','Unknown')),
    CONSTRAINT `ck_skills_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='回合制正式技能目錄';

CREATE TABLE IF NOT EXISTS `god2_game`.`consumables` (
    `item_id` bigint NOT NULL COMMENT '對應物品 ID',
    `use_type` varchar(50) NULL COMMENT '使用方式',
    `target_type` varchar(50) NULL COMMENT '目標型別',
    `hp_restore` bigint NULL COMMENT 'HP 回復量',
    `mp_restore` bigint NULL COMMENT 'MP 回復量',
    `status_effect_id` bigint NULL COMMENT '套用狀態效果 ID',
    `skill_id` bigint NULL COMMENT '觸發技能 ID',
    `cooldown_rounds` int NULL COMMENT '冷卻回合數',
    `cooldown_ms` int NULL COMMENT '冷卻毫秒',
    `duration_rounds` int NULL COMMENT '效果持續回合數',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`item_id`),
    KEY `ix_consumables_status_effect` (`status_effect_id`),
    KEY `ix_consumables_skill` (`skill_id`),
    CONSTRAINT `fk_consumables_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `fk_consumables_status_effect` FOREIGN KEY (`status_effect_id`) REFERENCES `god2_game`.`status_effects` (`status_effect_id`),
    CONSTRAINT `fk_consumables_skill` FOREIGN KEY (`skill_id`) REFERENCES `god2_game`.`skills` (`skill_id`),
    CONSTRAINT `ck_consumables_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='消耗品效果';

CREATE TABLE IF NOT EXISTS `god2_game`.`skill_levels` (
    `skill_id` bigint NOT NULL COMMENT '技能 ID',
    `skill_level` int NOT NULL COMMENT '技能等級',
    `required_character_level` int NULL COMMENT '需求角色等級',
    `mp_cost` bigint NULL COMMENT '此級 MP 消耗',
    `hp_cost` bigint NULL COMMENT '此級 HP 消耗',
    `base_power` decimal(18,6) NULL COMMENT '此級基礎威力',
    `effect_value` decimal(18,6) NULL COMMENT '此級效果值',
    `duration_rounds` int NULL COMMENT '此級持續回合數',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`skill_id`,`skill_level`),
    CONSTRAINT `fk_skill_levels_skill` FOREIGN KEY (`skill_id`) REFERENCES `god2_game`.`skills` (`skill_id`),
    CONSTRAINT `ck_skill_levels_level` CHECK (`skill_level` > 0),
    CONSTRAINT `ck_skill_levels_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='技能逐級數值';

CREATE TABLE IF NOT EXISTS `god2_game`.`skill_effects` (
    `skill_effect_id` bigint NOT NULL COMMENT '技能效果 ID',
    `skill_id` bigint NOT NULL COMMENT '技能 ID',
    `effect_order` int NOT NULL COMMENT '效果執行順序',
    `effect_type` varchar(50) NOT NULL COMMENT '效果類型',
    `target_type` varchar(50) NULL COMMENT '效果目標型別',
    `value_type` varchar(30) NULL COMMENT '數值型別',
    `base_value` decimal(18,6) NULL COMMENT '基礎效果值',
    `scaling_rule` varchar(500) NULL COMMENT '已確認縮放規則；未知時為 NULL',
    `duration_rounds` int NULL COMMENT '持續回合數',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`skill_effect_id`),
    UNIQUE KEY `ux_skill_effects_order` (`skill_id`,`effect_order`),
    CONSTRAINT `fk_skill_effects_skill` FOREIGN KEY (`skill_id`) REFERENCES `god2_game`.`skills` (`skill_id`),
    CONSTRAINT `ck_skill_effects_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='技能效果明細';

CREATE TABLE IF NOT EXISTS `god2_game`.`pet_template_skills` (
    `pet_template_id` bigint NOT NULL COMMENT '戰寵模板 ID',
    `slot_index` int NOT NULL COMMENT '技能槽索引',
    `skill_id` bigint NOT NULL COMMENT '技能 ID',
    `skill_name_cache` varchar(150) NULL COMMENT '技能名稱快取',
    `skill_level` int NULL COMMENT '技能等級',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`pet_template_id`,`slot_index`),
    KEY `ix_pet_template_skills_skill` (`skill_id`),
    CONSTRAINT `fk_pet_template_skills_template` FOREIGN KEY (`pet_template_id`) REFERENCES `god2_game`.`pet_templates` (`pet_template_id`),
    CONSTRAINT `fk_pet_template_skills_skill` FOREIGN KEY (`skill_id`) REFERENCES `god2_game`.`skills` (`skill_id`),
    CONSTRAINT `ck_pet_template_skills_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='戰寵模板技能';

CREATE TABLE IF NOT EXISTS `god2_game`.`immortal_template_skills` (
    `immortal_template_id` bigint NOT NULL COMMENT '神仙模板 ID',
    `skill_category` varchar(30) NOT NULL COMMENT '技能分類',
    `slot_index` int NOT NULL COMMENT '技能槽索引',
    `skill_id` bigint NOT NULL COMMENT '技能 ID',
    `skill_name_cache` varchar(150) NULL COMMENT '技能名稱快取',
    `skill_level` int NULL COMMENT '技能等級',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`immortal_template_id`,`skill_category`,`slot_index`),
    KEY `ix_immortal_template_skills_skill` (`skill_id`),
    CONSTRAINT `fk_immortal_template_skills_template` FOREIGN KEY (`immortal_template_id`) REFERENCES `god2_game`.`immortal_templates` (`immortal_template_id`),
    CONSTRAINT `fk_immortal_template_skills_skill` FOREIGN KEY (`skill_id`) REFERENCES `god2_game`.`skills` (`skill_id`),
    CONSTRAINT `ck_immortal_template_skills_category` CHECK (`skill_category` IN ('Innate','Active','Passive','Unknown')),
    CONSTRAINT `ck_immortal_template_skills_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='神仙模板技能';

CREATE TABLE IF NOT EXISTS `god2_game`.`mount_template_skills` (
    `mount_template_id` bigint NOT NULL COMMENT '坐騎模板 ID',
    `slot_index` int NOT NULL COMMENT '技能槽索引',
    `skill_id` bigint NOT NULL COMMENT '技能 ID',
    `skill_name_cache` varchar(150) NULL COMMENT '技能名稱快取',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`mount_template_id`,`slot_index`),
    KEY `ix_mount_template_skills_skill` (`skill_id`),
    CONSTRAINT `fk_mount_template_skills_template` FOREIGN KEY (`mount_template_id`) REFERENCES `god2_game`.`mount_templates` (`mount_template_id`),
    CONSTRAINT `fk_mount_template_skills_skill` FOREIGN KEY (`skill_id`) REFERENCES `god2_game`.`skills` (`skill_id`),
    CONSTRAINT `ck_mount_template_skills_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='坐騎模板技能';

CREATE TABLE IF NOT EXISTS `god2_game`.`portals` (
    `portal_id` bigint NOT NULL COMMENT '傳送門 ID',
    `name_zh_tw` varchar(150) NOT NULL COMMENT '傳送門繁體中文名稱',
    `source_map_id` bigint NULL COMMENT '來源地圖 ID；未知時為 NULL',
    `source_x` int NULL COMMENT '來源 X 座標',
    `source_y` int NULL COMMENT '來源 Y 座標',
    `source_radius` int NULL COMMENT '觸發半徑',
    `destination_map_id` bigint NULL COMMENT '目的地圖 ID；未知時為 NULL',
    `destination_x` int NULL COMMENT '目的 X 座標',
    `destination_y` int NULL COMMENT '目的 Y 座標',
    `destination_direction` int NULL COMMENT '目的朝向',
    `required_level` int NULL COMMENT '需求等級',
    `required_quest_id` bigint NULL COMMENT '需求任務 ID；外鍵在任務建立後補上',
    `cost_item_id` bigint NULL COMMENT '傳送消耗物品 ID',
    `cost_quantity` int NULL COMMENT '消耗數量',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`portal_id`),
    KEY `ix_portals_source_map` (`source_map_id`),
    KEY `ix_portals_destination_map` (`destination_map_id`),
    KEY `ix_portals_cost_item` (`cost_item_id`),
    CONSTRAINT `fk_portals_source_map` FOREIGN KEY (`source_map_id`) REFERENCES `god2_game`.`maps` (`map_id`),
    CONSTRAINT `fk_portals_destination_map` FOREIGN KEY (`destination_map_id`) REFERENCES `god2_game`.`maps` (`map_id`),
    CONSTRAINT `fk_portals_cost_item` FOREIGN KEY (`cost_item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_portals_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='地圖傳送門';

CREATE TABLE IF NOT EXISTS `god2_game`.`monster_skills` (
    `monster_skill_id` bigint NOT NULL COMMENT '怪物技能配置 ID',
    `monster_id` bigint NOT NULL COMMENT '怪物 ID',
    `monster_name_cache` varchar(150) NULL COMMENT '怪物名稱快取',
    `skill_id` bigint NOT NULL COMMENT '技能 ID',
    `skill_name_cache` varchar(150) NULL COMMENT '技能名稱快取',
    `priority` int NULL COMMENT '選技優先值',
    `trigger_type` varchar(50) NULL COMMENT '觸發類型',
    `trigger_value` decimal(18,6) NULL COMMENT '觸發值',
    `trigger_hp_percent` decimal(9,6) NULL COMMENT 'HP 百分比觸發值',
    `trigger_round_min` int NULL COMMENT '最早觸發回合',
    `trigger_round_max` int NULL COMMENT '最晚觸發回合',
    `target_type` varchar(50) NULL COMMENT '目標型別',
    `use_probability` decimal(12,8) NULL COMMENT '使用機率；未知時為 NULL',
    `cooldown_rounds` int NULL COMMENT '額外冷卻回合數',
    `maximum_uses` int NULL COMMENT '單場最大使用次數',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用；未知機率時必須為 0',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`monster_skill_id`),
    KEY `ix_monster_skills_monster` (`monster_id`),
    KEY `ix_monster_skills_skill` (`skill_id`),
    CONSTRAINT `fk_monster_skills_monster` FOREIGN KEY (`monster_id`) REFERENCES `god2_game`.`monsters` (`monster_id`),
    CONSTRAINT `fk_monster_skills_skill` FOREIGN KEY (`skill_id`) REFERENCES `god2_game`.`skills` (`skill_id`),
    CONSTRAINT `ck_monster_skills_probability` CHECK (`use_probability` IS NULL OR (`use_probability` >= 0 AND `use_probability` <= 1)),
    CONSTRAINT `ck_monster_skills_enabled` CHECK (`enabled` IN (0,1) AND NOT (`enabled`=1 AND `use_probability` IS NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='怪物回合制技能配置';

CREATE TABLE IF NOT EXISTS `god2_game`.`monster_spawns` (
    `spawn_id` bigint NOT NULL COMMENT '怪物出生點 ID',
    `monster_id` bigint NOT NULL COMMENT '怪物 ID',
    `monster_name_cache` varchar(150) NULL COMMENT '怪物名稱快取',
    `map_id` bigint NOT NULL COMMENT '地圖 ID',
    `map_name_cache` varchar(150) NULL COMMENT '地圖名稱快取',
    `position_x` int NULL COMMENT '出生 X 座標',
    `position_y` int NULL COMMENT '出生 Y 座標',
    `direction` int NULL COMMENT '出生朝向',
    `spawn_count` int NULL COMMENT '出生數量',
    `random_x` int NULL COMMENT 'X 隨機偏移範圍',
    `random_y` int NULL COMMENT 'Y 隨機偏移範圍',
    `spawn_radius` int NULL COMMENT '出生半徑',
    `respawn_seconds_min` int NULL COMMENT '最短重生秒數',
    `respawn_seconds_max` int NULL COMMENT '最長重生秒數',
    `group_id` bigint NULL COMMENT '出生群組 ID',
    `instance_key` varchar(100) NULL COMMENT '副本識別鍵',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`spawn_id`),
    KEY `ix_monster_spawns_monster` (`monster_id`),
    KEY `ix_monster_spawns_map` (`map_id`),
    KEY `ix_monster_spawns_enabled` (`enabled`),
    CONSTRAINT `fk_monster_spawns_monster` FOREIGN KEY (`monster_id`) REFERENCES `god2_game`.`monsters` (`monster_id`),
    CONSTRAINT `fk_monster_spawns_map` FOREIGN KEY (`map_id`) REFERENCES `god2_game`.`maps` (`map_id`),
    CONSTRAINT `ck_monster_spawns_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='怪物出生點';

CREATE TABLE IF NOT EXISTS `god2_game`.`monster_drops` (
    `drop_id` bigint NOT NULL COMMENT '怪物掉落關係 ID',
    `monster_id` bigint NOT NULL COMMENT '怪物 ID',
    `monster_name_cache` varchar(150) NULL COMMENT '怪物名稱快取',
    `item_id` bigint NOT NULL COMMENT '物品 ID',
    `item_name_cache` varchar(200) NULL COMMENT '物品名稱快取',
    `minimum_quantity` int NULL COMMENT '最小數量',
    `maximum_quantity` int NULL COMMENT '最大數量',
    `drop_rate` decimal(12,8) NULL COMMENT '掉落率；未知時為 NULL，不得填 0 冒充未知',
    `drop_rate_unit` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '掉落率單位',
    `drop_group` varchar(50) NULL COMMENT '互斥或共同掉落群組',
    `is_guaranteed` tinyint(1) NULL COMMENT '是否保證掉落',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用；未知掉落率時必須為 0',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`drop_id`),
    KEY `ix_monster_drops_monster` (`monster_id`),
    KEY `ix_monster_drops_item` (`item_id`),
    KEY `ix_monster_drops_enabled` (`enabled`),
    CONSTRAINT `fk_monster_drops_monster` FOREIGN KEY (`monster_id`) REFERENCES `god2_game`.`monsters` (`monster_id`),
    CONSTRAINT `fk_monster_drops_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_monster_drops_unit` CHECK (`drop_rate_unit` IN ('Percent','Probability','Ppm','Unknown')),
    CONSTRAINT `ck_monster_drops_rate` CHECK (`drop_rate` IS NULL OR `drop_rate` >= 0),
    CONSTRAINT `ck_monster_drops_enabled` CHECK (`enabled` IN (0,1) AND NOT (`enabled`=1 AND `drop_rate` IS NULL AND COALESCE(`is_guaranteed`,0)=0))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='怪物掉落關係';

CREATE TABLE IF NOT EXISTS `god2_game`.`monster_ai_rules` (
    `ai_rule_id` bigint NOT NULL COMMENT 'AI 規則 ID',
    `ai_profile_id` bigint NOT NULL COMMENT 'AI 模板 ID',
    `priority` int NOT NULL COMMENT '規則優先順序',
    `minimum_round` int NULL COMMENT '最早適用回合',
    `maximum_round` int NULL COMMENT '最晚適用回合',
    `self_hp_percent_min` decimal(9,6) NULL COMMENT '自身 HP 百分比下限',
    `self_hp_percent_max` decimal(9,6) NULL COMMENT '自身 HP 百分比上限',
    `ally_hp_percent_max` decimal(9,6) NULL COMMENT '友方 HP 百分比上限',
    `enemy_hp_percent_max` decimal(9,6) NULL COMMENT '敵方 HP 百分比上限',
    `required_status_effect_id` bigint NULL COMMENT '必須存在的狀態效果 ID',
    `forbidden_status_effect_id` bigint NULL COMMENT '禁止存在的狀態效果 ID',
    `skill_id` bigint NULL COMMENT '要使用的技能 ID',
    `target_selector` varchar(50) NULL COMMENT '目標選擇器',
    `use_probability` decimal(12,8) NULL COMMENT '規則使用機率；未知時為 NULL',
    `cooldown_rounds` int NULL COMMENT '規則冷卻回合數',
    `maximum_uses` int NULL COMMENT '單場最大使用次數',
    `fallback_action` varchar(50) NULL COMMENT '規則不成立時行動',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`ai_rule_id`),
    UNIQUE KEY `ux_monster_ai_rules_priority` (`ai_profile_id`,`priority`),
    KEY `ix_monster_ai_rules_skill` (`skill_id`),
    CONSTRAINT `fk_monster_ai_rules_profile` FOREIGN KEY (`ai_profile_id`) REFERENCES `god2_game`.`monster_ai_profiles` (`ai_profile_id`),
    CONSTRAINT `fk_monster_ai_rules_required_status` FOREIGN KEY (`required_status_effect_id`) REFERENCES `god2_game`.`status_effects` (`status_effect_id`),
    CONSTRAINT `fk_monster_ai_rules_forbidden_status` FOREIGN KEY (`forbidden_status_effect_id`) REFERENCES `god2_game`.`status_effects` (`status_effect_id`),
    CONSTRAINT `fk_monster_ai_rules_skill` FOREIGN KEY (`skill_id`) REFERENCES `god2_game`.`skills` (`skill_id`),
    CONSTRAINT `ck_monster_ai_rules_probability` CHECK (`use_probability` IS NULL OR (`use_probability` >= 0 AND `use_probability` <= 1)),
    CONSTRAINT `ck_monster_ai_rules_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='可逐列編輯的怪物 AI 規則';

CREATE TABLE IF NOT EXISTS `god2_game`.`npc_spawns` (
    `spawn_id` bigint NOT NULL COMMENT 'NPC 出生點 ID',
    `npc_id` bigint NOT NULL COMMENT 'NPC ID',
    `npc_name_cache` varchar(150) NULL COMMENT 'NPC 名稱快取',
    `map_id` bigint NOT NULL COMMENT '地圖 ID',
    `map_name_cache` varchar(150) NULL COMMENT '地圖名稱快取',
    `position_x` int NULL COMMENT '出生 X 座標',
    `position_y` int NULL COMMENT '出生 Y 座標',
    `direction` int NULL COMMENT 'NPC 朝向',
    `instance_key` varchar(100) NULL COMMENT '副本識別鍵',
    `client_build_id` varchar(64) NULL COMMENT '客戶端版本 ID',
    `observed_client_entity_handle` bigint unsigned NULL COMMENT '已驗證客戶端實體 Handle',
    `official_resource_type` tinyint unsigned NULL COMMENT '已驗證官方資源類型',
    `official_resource_ordinal` tinyint unsigned NULL COMMENT '已驗證官方資源序號',
    `official_selector_high_bits` tinyint unsigned NULL COMMENT '已驗證官方選擇器高位',
    `official_direction_code` tinyint unsigned NULL COMMENT '已驗證官方方向碼',
    `official_state_code` tinyint unsigned NULL COMMENT '已驗證官方狀態碼',
    `wire_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '線上協定證據狀態',
    `identity_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '實體識別證據狀態',
    `coordinate_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '座標證據狀態',
    `service_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '服務證據狀態',
    `application_message_sha256` char(64) NULL COMMENT '應用訊息證據 SHA-256',
    `opaque_template_sha256` char(64) NULL COMMENT '不透明模板證據 SHA-256',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`spawn_id`),
    KEY `ix_npc_spawns_npc` (`npc_id`),
    KEY `ix_npc_spawns_map` (`map_id`),
    KEY `ix_npc_spawns_enabled` (`enabled`),
    UNIQUE KEY `ux_npc_spawns_client_handle` (`client_build_id`,`observed_client_entity_handle`),
    CONSTRAINT `fk_npc_spawns_npc` FOREIGN KEY (`npc_id`) REFERENCES `god2_game`.`npcs` (`npc_id`),
    CONSTRAINT `fk_npc_spawns_map` FOREIGN KEY (`map_id`) REFERENCES `god2_game`.`maps` (`map_id`),
    CONSTRAINT `ck_npc_spawns_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='NPC 出生點';

CREATE TABLE IF NOT EXISTS `god2_game`.`npc_dialogs` (
    `dialog_id` bigint NOT NULL COMMENT '對話 ID',
    `code` varchar(100) NULL COMMENT '穩定對話代碼',
    `npc_id` bigint NULL COMMENT '所屬 NPC ID',
    `title_zh_tw` varchar(200) NULL COMMENT '對話標題',
    `body_zh_tw` text NULL COMMENT '對話繁體中文內容',
    `body_original` text NULL COMMENT '對話原文內容',
    `dialog_type` varchar(50) NULL COMMENT '對話類型',
    `next_dialog_id` bigint NULL COMMENT '下一段對話 ID',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`dialog_id`),
    UNIQUE KEY `ux_npc_dialogs_code` (`code`),
    KEY `ix_npc_dialogs_npc` (`npc_id`),
    KEY `ix_npc_dialogs_next` (`next_dialog_id`),
    CONSTRAINT `fk_npc_dialogs_npc` FOREIGN KEY (`npc_id`) REFERENCES `god2_game`.`npcs` (`npc_id`),
    CONSTRAINT `fk_npc_dialogs_next` FOREIGN KEY (`next_dialog_id`) REFERENCES `god2_game`.`npc_dialogs` (`dialog_id`),
    CONSTRAINT `ck_npc_dialogs_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='NPC 對話內容';

CREATE TABLE IF NOT EXISTS `god2_game`.`npc_dialog_options` (
    `option_id` bigint NOT NULL COMMENT '對話選項 ID',
    `dialog_id` bigint NOT NULL COMMENT '對話 ID',
    `display_order` int NOT NULL COMMENT '顯示順序',
    `option_text_zh_tw` varchar(500) NOT NULL COMMENT '選項繁體中文文字',
    `action_type` varchar(50) NULL COMMENT '選項行動類型；未知時為 NULL',
    `action_target_id` bigint NULL COMMENT '行動目標 ID',
    `next_dialog_id` bigint NULL COMMENT '下一段對話 ID',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`option_id`),
    UNIQUE KEY `ux_npc_dialog_options_order` (`dialog_id`,`display_order`),
    CONSTRAINT `fk_npc_dialog_options_dialog` FOREIGN KEY (`dialog_id`) REFERENCES `god2_game`.`npc_dialogs` (`dialog_id`),
    CONSTRAINT `fk_npc_dialog_options_next` FOREIGN KEY (`next_dialog_id`) REFERENCES `god2_game`.`npc_dialogs` (`dialog_id`),
    CONSTRAINT `ck_npc_dialog_options_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='NPC 對話選項';

CREATE TABLE IF NOT EXISTS `god2_game`.`merchants` (
    `merchant_id` bigint NOT NULL COMMENT '商店 ID',
    `npc_id` bigint NULL COMMENT '所屬 NPC ID',
    `name_zh_tw` varchar(150) NOT NULL COMMENT '商店繁體中文名稱',
    `merchant_type` varchar(50) NULL COMMENT '商店類型',
    `buyback_enabled` tinyint(1) NULL COMMENT '是否允許回收物品',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`merchant_id`),
    KEY `ix_merchants_npc` (`npc_id`),
    CONSTRAINT `fk_merchants_npc` FOREIGN KEY (`npc_id`) REFERENCES `god2_game`.`npcs` (`npc_id`),
    CONSTRAINT `ck_merchants_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='商店定義';

CREATE TABLE IF NOT EXISTS `god2_game`.`merchant_inventory` (
    `merchant_inventory_id` bigint NOT NULL COMMENT '商店庫存列 ID',
    `merchant_id` bigint NOT NULL COMMENT '商店 ID',
    `merchant_name_cache` varchar(150) NULL COMMENT '商店名稱快取',
    `item_id` bigint NOT NULL COMMENT '物品 ID',
    `item_name_cache` varchar(200) NULL COMMENT '物品名稱快取',
    `display_order` int NULL COMMENT '顯示順序',
    `selling_price` bigint NULL COMMENT '玩家購買價格',
    `purchasing_price` bigint NULL COMMENT '商店回收價格',
    `pack_count` int NULL COMMENT '每次購買包裝數量',
    `quantity_limit` int NULL COMMENT '購買數量限制',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`merchant_inventory_id`),
    UNIQUE KEY `ux_merchant_inventory_item` (`merchant_id`,`item_id`),
    KEY `ix_merchant_inventory_enabled` (`enabled`),
    CONSTRAINT `fk_merchant_inventory_merchant` FOREIGN KEY (`merchant_id`) REFERENCES `god2_game`.`merchants` (`merchant_id`),
    CONSTRAINT `fk_merchant_inventory_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_merchant_inventory_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='商店販售與回收清單';

SET @god2_ddl = IF(
    EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA='god2_game' AND TABLE_NAME='npcs' AND CONSTRAINT_NAME='fk_npcs_default_dialog'),
    'SELECT 1',
    'ALTER TABLE `god2_game`.`npcs` ADD CONSTRAINT `fk_npcs_default_dialog` FOREIGN KEY (`default_dialog_id`) REFERENCES `god2_game`.`npc_dialogs` (`dialog_id`)');
PREPARE god2_ddl_statement FROM @god2_ddl;
EXECUTE god2_ddl_statement;
DEALLOCATE PREPARE god2_ddl_statement;
SET @god2_ddl = IF(
    EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA='god2_game' AND TABLE_NAME='npcs' AND CONSTRAINT_NAME='fk_npcs_merchant'),
    'SELECT 1',
    'ALTER TABLE `god2_game`.`npcs` ADD CONSTRAINT `fk_npcs_merchant` FOREIGN KEY (`merchant_id`) REFERENCES `god2_game`.`merchants` (`merchant_id`)');
PREPARE god2_ddl_statement FROM @god2_ddl;
EXECUTE god2_ddl_statement;
DEALLOCATE PREPARE god2_ddl_statement;

CREATE TABLE IF NOT EXISTS `god2_game`.`quests` (
    `quest_id` bigint NOT NULL COMMENT '任務 ID',
    `code` varchar(100) NULL COMMENT '穩定任務代碼',
    `name_zh_tw` varchar(200) NOT NULL COMMENT '任務繁體中文名稱',
    `name_original` varchar(200) NULL COMMENT '任務原文名稱',
    `quest_type` varchar(50) NULL COMMENT '任務類型',
    `start_npc_id` bigint NULL COMMENT '起始 NPC ID',
    `end_npc_id` bigint NULL COMMENT '結束 NPC ID',
    `required_level` int NULL COMMENT '最低需求等級',
    `maximum_level` int NULL COMMENT '最高可接等級',
    `repeatable` tinyint(1) NULL COMMENT '是否可重複',
    `repeat_interval_seconds` int NULL COMMENT '重複間隔秒數',
    `description_zh_tw` text NULL COMMENT '任務繁體中文說明',
    `completion_text_zh_tw` text NULL COMMENT '任務完成文字',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`quest_id`),
    UNIQUE KEY `ux_quests_code` (`code`),
    KEY `ix_quests_start_npc` (`start_npc_id`),
    KEY `ix_quests_end_npc` (`end_npc_id`),
    KEY `ix_quests_enabled` (`enabled`),
    CONSTRAINT `fk_quests_start_npc` FOREIGN KEY (`start_npc_id`) REFERENCES `god2_game`.`npcs` (`npc_id`),
    CONSTRAINT `fk_quests_end_npc` FOREIGN KEY (`end_npc_id`) REFERENCES `god2_game`.`npcs` (`npc_id`),
    CONSTRAINT `ck_quests_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='任務定義';

SET @god2_ddl = IF(
    EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA='god2_game' AND TABLE_NAME='portals' AND CONSTRAINT_NAME='fk_portals_required_quest'),
    'SELECT 1',
    'ALTER TABLE `god2_game`.`portals` ADD CONSTRAINT `fk_portals_required_quest` FOREIGN KEY (`required_quest_id`) REFERENCES `god2_game`.`quests` (`quest_id`)');
PREPARE god2_ddl_statement FROM @god2_ddl;
EXECUTE god2_ddl_statement;
DEALLOCATE PREPARE god2_ddl_statement;

CREATE TABLE IF NOT EXISTS `god2_game`.`quest_prerequisites` (
    `quest_id` bigint NOT NULL COMMENT '任務 ID',
    `prerequisite_order` int NOT NULL COMMENT '前置條件順序',
    `prerequisite_type` varchar(50) NOT NULL COMMENT '前置條件類型',
    `required_quest_id` bigint NULL COMMENT '需求任務 ID',
    `required_level` int NULL COMMENT '需求等級',
    `required_item_id` bigint NULL COMMENT '需求物品 ID',
    `required_quantity` int NULL COMMENT '需求數量',
    `required_class_id` bigint NULL COMMENT '需求職業 ID',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    PRIMARY KEY (`quest_id`,`prerequisite_order`),
    KEY `ix_quest_prerequisites_required_quest` (`required_quest_id`),
    CONSTRAINT `fk_quest_prerequisites_quest` FOREIGN KEY (`quest_id`) REFERENCES `god2_game`.`quests` (`quest_id`),
    CONSTRAINT `fk_quest_prerequisites_required_quest` FOREIGN KEY (`required_quest_id`) REFERENCES `god2_game`.`quests` (`quest_id`),
    CONSTRAINT `fk_quest_prerequisites_item` FOREIGN KEY (`required_item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `fk_quest_prerequisites_class` FOREIGN KEY (`required_class_id`) REFERENCES `god2_game`.`character_classes` (`class_id`),
    CONSTRAINT `ck_quest_prerequisites_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='任務前置條件';

CREATE TABLE IF NOT EXISTS `god2_game`.`quest_objectives` (
    `objective_id` bigint NOT NULL COMMENT '任務目標 ID',
    `quest_id` bigint NOT NULL COMMENT '任務 ID',
    `objective_order` int NOT NULL COMMENT '目標順序',
    `objective_type` varchar(50) NOT NULL COMMENT '目標類型',
    `target_id` bigint NULL COMMENT '目標實體 ID',
    `target_name_cache` varchar(200) NULL COMMENT '目標名稱快取',
    `required_quantity` int NULL COMMENT '需求數量',
    `map_id` bigint NULL COMMENT '限定地圖 ID',
    `description_zh_tw` text NULL COMMENT '目標繁體中文說明',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`objective_id`),
    UNIQUE KEY `ux_quest_objectives_order` (`quest_id`,`objective_order`),
    KEY `ix_quest_objectives_map` (`map_id`),
    CONSTRAINT `fk_quest_objectives_quest` FOREIGN KEY (`quest_id`) REFERENCES `god2_game`.`quests` (`quest_id`),
    CONSTRAINT `fk_quest_objectives_map` FOREIGN KEY (`map_id`) REFERENCES `god2_game`.`maps` (`map_id`),
    CONSTRAINT `ck_quest_objectives_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='任務目標';

CREATE TABLE IF NOT EXISTS `god2_game`.`quest_rewards` (
    `reward_id` bigint NOT NULL COMMENT '任務獎勵 ID',
    `quest_id` bigint NOT NULL COMMENT '任務 ID',
    `reward_order` int NOT NULL COMMENT '獎勵順序',
    `reward_type` varchar(50) NOT NULL COMMENT '獎勵類型',
    `item_id` bigint NULL COMMENT '獎勵物品 ID',
    `item_name_cache` varchar(200) NULL COMMENT '物品名稱快取',
    `quantity` int NULL COMMENT '獎勵數量',
    `experience` bigint NULL COMMENT '獎勵經驗',
    `currency` bigint NULL COMMENT '獎勵貨幣',
    `selection_group` varchar(50) NULL COMMENT '選擇獎勵群組',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`reward_id`),
    UNIQUE KEY `ux_quest_rewards_order` (`quest_id`,`reward_order`),
    KEY `ix_quest_rewards_item` (`item_id`),
    CONSTRAINT `fk_quest_rewards_quest` FOREIGN KEY (`quest_id`) REFERENCES `god2_game`.`quests` (`quest_id`),
    CONSTRAINT `fk_quest_rewards_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_quest_rewards_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='任務獎勵';

CREATE TABLE IF NOT EXISTS `god2_game`.`item_set_members` (
    `set_id` bigint NOT NULL COMMENT '套裝 ID',
    `item_id` bigint NOT NULL COMMENT '套裝物品 ID',
    `slot_name` varchar(50) NULL COMMENT '套裝部位名稱',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`set_id`,`item_id`),
    CONSTRAINT `fk_item_set_members_set` FOREIGN KEY (`set_id`) REFERENCES `god2_game`.`item_sets` (`set_id`),
    CONSTRAINT `fk_item_set_members_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_item_set_members_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='套裝物品成員';

CREATE TABLE IF NOT EXISTS `god2_game`.`item_set_bonuses` (
    `set_id` bigint NOT NULL COMMENT '套裝 ID',
    `required_pieces` int NOT NULL COMMENT '觸發所需件數',
    `bonus_type` varchar(50) NOT NULL COMMENT '套裝加成類型',
    `bonus_value` decimal(18,6) NULL COMMENT '加成值；未知時為 NULL',
    `status_effect_id` bigint NULL COMMENT '觸發狀態效果 ID',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`set_id`,`required_pieces`,`bonus_type`),
    CONSTRAINT `fk_item_set_bonuses_set` FOREIGN KEY (`set_id`) REFERENCES `god2_game`.`item_sets` (`set_id`),
    CONSTRAINT `fk_item_set_bonuses_status` FOREIGN KEY (`status_effect_id`) REFERENCES `god2_game`.`status_effects` (`status_effect_id`),
    CONSTRAINT `ck_item_set_bonuses_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='套裝件數加成';

CREATE TABLE IF NOT EXISTS `god2_game`.`combine_recipes` (
    `combine_recipe_id` bigint NOT NULL COMMENT '一般合成配方 ID',
    `name_zh_tw` varchar(200) NOT NULL COMMENT '合成配方名稱',
    `result_item_id` bigint NULL COMMENT '合成結果物品 ID',
    `result_quantity` int NULL COMMENT '合成結果數量',
    `success_rate` decimal(12,8) NULL COMMENT '成功率；未知時為 NULL',
    `currency_cost` bigint NULL COMMENT '貨幣成本',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用；未知成功率時必須為 0',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`combine_recipe_id`),
    CONSTRAINT `fk_combine_recipes_result_item` FOREIGN KEY (`result_item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_combine_recipes_enabled` CHECK (`enabled` IN (0,1) AND NOT (`enabled`=1 AND `success_rate` IS NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='一般物品合成配方';

CREATE TABLE IF NOT EXISTS `god2_game`.`combine_recipe_materials` (
    `combine_recipe_id` bigint NOT NULL COMMENT '一般合成配方 ID',
    `material_order` int NOT NULL COMMENT '材料順序',
    `item_id` bigint NOT NULL COMMENT '材料物品 ID',
    `item_name_cache` varchar(200) NULL COMMENT '材料名稱快取',
    `required_quantity` int NULL COMMENT '需求數量',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    PRIMARY KEY (`combine_recipe_id`,`material_order`),
    CONSTRAINT `fk_combine_recipe_materials_recipe` FOREIGN KEY (`combine_recipe_id`) REFERENCES `god2_game`.`combine_recipes` (`combine_recipe_id`),
    CONSTRAINT `fk_combine_recipe_materials_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_combine_recipe_materials_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='一般合成配方材料';

CREATE TABLE IF NOT EXISTS `god2_game`.`containers` (
    `container_id` bigint NOT NULL COMMENT '容器定義 ID',
    `item_id` bigint NOT NULL COMMENT '容器物品 ID',
    `name_zh_tw` varchar(200) NOT NULL COMMENT '容器名稱',
    `roll_count` int NULL COMMENT '開啟抽取次數',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`container_id`),
    UNIQUE KEY `ux_containers_item` (`item_id`),
    CONSTRAINT `fk_containers_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_containers_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='可開啟容器定義';

CREATE TABLE IF NOT EXISTS `god2_game`.`container_rewards` (
    `container_id` bigint NOT NULL COMMENT '容器定義 ID',
    `reward_order` int NOT NULL COMMENT '獎勵順序',
    `item_id` bigint NOT NULL COMMENT '獎勵物品 ID',
    `item_name_cache` varchar(200) NULL COMMENT '物品名稱快取',
    `minimum_quantity` int NULL COMMENT '最小數量',
    `maximum_quantity` int NULL COMMENT '最大數量',
    `reward_probability` decimal(12,8) NULL COMMENT '獎勵機率；未知時為 NULL',
    `reward_group` varchar(50) NULL COMMENT '獎勵群組',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用；未知機率時必須為 0',
    PRIMARY KEY (`container_id`,`reward_order`),
    CONSTRAINT `fk_container_rewards_container` FOREIGN KEY (`container_id`) REFERENCES `god2_game`.`containers` (`container_id`),
    CONSTRAINT `fk_container_rewards_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_container_rewards_enabled` CHECK (`enabled` IN (0,1) AND NOT (`enabled`=1 AND `reward_probability` IS NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='容器獎勵';

CREATE TABLE IF NOT EXISTS `god2_game`.`crafting_recipes` (
    `recipe_id` bigint NOT NULL COMMENT '生活技能配方 ID',
    `life_skill_id` bigint NOT NULL COMMENT '生活技能 ID',
    `name_zh_tw` varchar(200) NOT NULL COMMENT '配方繁體中文名稱',
    `name_original` varchar(200) NULL COMMENT '配方原文名稱',
    `recipe_category` varchar(100) NULL COMMENT '配方分類',
    `required_life_skill_level` int NULL COMMENT '需求生活技能等級',
    `required_character_level` int NULL COMMENT '需求角色等級',
    `base_success_rate` decimal(12,8) NULL COMMENT '製作基礎成功率；未知時為 NULL，不得填 0',
    `base_quality_rate` decimal(12,8) NULL COMMENT '製作基礎品質率；未知時為 NULL',
    `currency_cost` bigint NULL COMMENT '製作貨幣成本',
    `craft_duration_ms` int NULL COMMENT '製作所需毫秒',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用；未知成功率時必須為 0',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`recipe_id`),
    KEY `ix_crafting_recipes_life_skill` (`life_skill_id`),
    KEY `ix_crafting_recipes_enabled` (`enabled`),
    CONSTRAINT `fk_crafting_recipes_life_skill` FOREIGN KEY (`life_skill_id`) REFERENCES `god2_game`.`life_skills` (`life_skill_id`),
    CONSTRAINT `ck_crafting_recipes_rates` CHECK ((`base_success_rate` IS NULL OR (`base_success_rate` >= 0 AND `base_success_rate` <= 1)) AND (`base_quality_rate` IS NULL OR (`base_quality_rate` >= 0 AND `base_quality_rate` <= 1))),
    CONSTRAINT `ck_crafting_recipes_enabled` CHECK (`enabled` IN (0,1) AND NOT (`enabled`=1 AND `base_success_rate` IS NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='生活技能製作配方';

CREATE TABLE IF NOT EXISTS `god2_game`.`crafting_recipe_materials` (
    `recipe_id` bigint NOT NULL COMMENT '生活技能配方 ID',
    `material_order` int NOT NULL COMMENT '材料順序',
    `item_id` bigint NOT NULL COMMENT '材料物品 ID',
    `item_name_cache` varchar(200) NULL COMMENT '材料名稱快取',
    `required_quantity` int NULL COMMENT '需求數量',
    `consume_on_failure` tinyint(1) NULL COMMENT '失敗時是否消耗',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    PRIMARY KEY (`recipe_id`,`material_order`),
    CONSTRAINT `fk_crafting_recipe_materials_recipe` FOREIGN KEY (`recipe_id`) REFERENCES `god2_game`.`crafting_recipes` (`recipe_id`),
    CONSTRAINT `fk_crafting_recipe_materials_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_crafting_recipe_materials_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='生活技能配方材料';

CREATE TABLE IF NOT EXISTS `god2_game`.`crafting_recipe_outputs` (
    `recipe_id` bigint NOT NULL COMMENT '生活技能配方 ID',
    `output_order` int NOT NULL COMMENT '產出順序',
    `item_id` bigint NOT NULL COMMENT '產出物品 ID',
    `item_name_cache` varchar(200) NULL COMMENT '產出物品名稱快取',
    `minimum_quantity` int NULL COMMENT '最小產出數量',
    `maximum_quantity` int NULL COMMENT '最大產出數量',
    `output_probability` decimal(12,8) NULL COMMENT '產出機率；未知時為 NULL',
    `quality_tier` int NULL COMMENT '產出品質階級',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用；未知機率時必須為 0',
    PRIMARY KEY (`recipe_id`,`output_order`),
    CONSTRAINT `fk_crafting_recipe_outputs_recipe` FOREIGN KEY (`recipe_id`) REFERENCES `god2_game`.`crafting_recipes` (`recipe_id`),
    CONSTRAINT `fk_crafting_recipe_outputs_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_crafting_recipe_outputs_enabled` CHECK (`enabled` IN (0,1) AND NOT (`enabled`=1 AND `output_probability` IS NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='生活技能配方產出';

CREATE TABLE IF NOT EXISTS `god2_game`.`formations` (
    `formation_id` bigint NOT NULL COMMENT '陣型 ID',
    `name_zh_tw` varchar(150) NOT NULL COMMENT '陣型繁體中文名稱',
    `description_zh_tw` text NULL COMMENT '陣型說明',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`formation_id`),
    CONSTRAINT `ck_formations_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='回合制陣型定義';

CREATE TABLE IF NOT EXISTS `god2_game`.`formation_slots` (
    `formation_id` bigint NOT NULL COMMENT '陣型 ID',
    `slot_index` int NOT NULL COMMENT '陣型格索引',
    `row_index` int NULL COMMENT '陣型列索引',
    `column_index` int NULL COMMENT '陣型欄索引',
    `allowed_entity_type` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '允許參與者類型',
    `speed_modifier` decimal(18,8) NULL COMMENT '速度修正；未知時為 NULL',
    `damage_modifier` decimal(18,8) NULL COMMENT '傷害修正；未知時為 NULL',
    `defense_modifier` decimal(18,8) NULL COMMENT '防禦修正；未知時為 NULL',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`formation_id`,`slot_index`),
    CONSTRAINT `fk_formation_slots_formation` FOREIGN KEY (`formation_id`) REFERENCES `god2_game`.`formations` (`formation_id`),
    CONSTRAINT `ck_formation_slots_entity_type` CHECK (`allowed_entity_type` IN ('Character','Pet','Immortal','Monster','Any','Unknown')),
    CONSTRAINT `ck_formation_slots_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='資料驅動的陣型格';

CREATE TABLE IF NOT EXISTS `god2_game`.`formation_bonuses` (
    `formation_id` bigint NOT NULL COMMENT '陣型 ID',
    `bonus_order` int NOT NULL COMMENT '加成順序',
    `bonus_type` varchar(50) NOT NULL COMMENT '加成類型',
    `bonus_value` decimal(18,8) NULL COMMENT '加成值；未知時為 NULL',
    `status_effect_id` bigint NULL COMMENT '狀態效果 ID',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`formation_id`,`bonus_order`),
    CONSTRAINT `fk_formation_bonuses_formation` FOREIGN KEY (`formation_id`) REFERENCES `god2_game`.`formations` (`formation_id`),
    CONSTRAINT `fk_formation_bonuses_status` FOREIGN KEY (`status_effect_id`) REFERENCES `god2_game`.`status_effects` (`status_effect_id`),
    CONSTRAINT `ck_formation_bonuses_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='陣型加成';

CREATE TABLE IF NOT EXISTS `god2_game`.`level_experience` (
    `level` int NOT NULL COMMENT '角色等級',
    `required_experience` bigint NULL COMMENT '升至下一級需求經驗；未知時為 NULL',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'EvidenceBlocked' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`level`),
    CONSTRAINT `ck_level_experience_level` CHECK (`level` > 0),
    CONSTRAINT `ck_level_experience_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色等級經驗需求';

CREATE TABLE IF NOT EXISTS `god2_game`.`server_rates` (
    `rate_key` varchar(100) NOT NULL COMMENT '伺服器倍率代碼',
    `display_name_zh_tw` varchar(200) NOT NULL COMMENT '倍率繁體中文名稱',
    `rate_value` decimal(24,8) NULL COMMENT '倍率值；未知時為 NULL',
    `description_zh_tw` text NULL COMMENT '倍率說明',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`rate_key`),
    CONSTRAINT `ck_server_rates_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='伺服器全域倍率';

CREATE TABLE IF NOT EXISTS `god2_game`.`battle_rule_parameters` (
    `parameter_key` varchar(100) NOT NULL COMMENT '回合規則參數代碼',
    `display_name_zh_tw` varchar(200) NOT NULL COMMENT '參數繁體中文名稱',
    `numeric_value` decimal(24,8) NULL COMMENT '數值型參數；未知時為 NULL',
    `text_value` varchar(1000) NULL COMMENT '文字型參數；未知時為 NULL',
    `description_zh_tw` text NULL COMMENT '參數說明',
    `evidence_status` varchar(30) NOT NULL DEFAULT 'EvidenceBlocked' COMMENT '證據狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`parameter_key`),
    CONSTRAINT `ck_battle_rule_parameters_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='回合制規則參數；未確認公式保持停用';

-- Player-owned state. Static templates are referenced from god2_game.
CREATE TABLE IF NOT EXISTS `god2_player`.`accounts` (
    `account_id` bigint NOT NULL AUTO_INCREMENT COMMENT '帳號 ID',
    `username` varchar(64) NOT NULL COMMENT '登入帳號',
    `password_hash` varchar(255) NOT NULL COMMENT '密碼雜湊；不得保存明文密碼',
    `status` varchar(32) NOT NULL COMMENT '帳號狀態',
    `failed_login_count` int NOT NULL DEFAULT 0 COMMENT '連續登入失敗次數',
    `locked_until_utc` datetime(6) NULL COMMENT '帳號鎖定到期 UTC 時間',
    `last_login_at_utc` datetime(6) NULL COMMENT '最後登入 UTC 時間',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`account_id`),
    UNIQUE KEY `ux_accounts_username` (`username`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='安全帳號資料；只保存密碼雜湊';

CREATE TABLE IF NOT EXISTS `god2_player`.`characters` (
    `character_id` bigint NOT NULL COMMENT '角色 ID',
    `account_id` bigint NOT NULL COMMENT '帳號 ID',
    `name` varchar(100) NOT NULL COMMENT '角色名稱',
    `class_id` bigint NULL COMMENT '職業 ID',
    `class_name_cache` varchar(100) NULL COMMENT '職業名稱快取',
    `title_id` bigint NULL COMMENT '稱號 ID',
    `title_name_cache` varchar(100) NULL COMMENT '稱號名稱快取',
    `level` int NULL COMMENT '角色等級；未知時為 NULL',
    `experience` bigint NULL COMMENT '角色經驗；未知時為 NULL',
    `reputation` bigint NULL COMMENT '角色名聲；未知時為 NULL',
    `rebirth_count` int NULL COMMENT '角色轉生次數；未知時為 NULL',
    `remaining_stat_points` int NULL COMMENT '剩餘配點；未知時為 NULL',
    `current_hp` bigint NULL COMMENT '目前 HP',
    `max_hp` bigint NULL COMMENT '最大 HP',
    `current_mp` bigint NULL COMMENT '目前 MP',
    `max_mp` bigint NULL COMMENT '最大 MP',
    `strength_base` int NULL COMMENT '力量基礎值', `strength_bonus` int NULL COMMENT '力量永久加成值，不含戰鬥暫時 Buff',
    `constitution_base` int NULL COMMENT '體力基礎值', `constitution_bonus` int NULL COMMENT '體力永久加成值',
    `intelligence_base` int NULL COMMENT '智力基礎值', `intelligence_bonus` int NULL COMMENT '智力永久加成值',
    `speed_base` int NULL COMMENT '速度基礎值；出手順序公式尚需證據確認', `speed_bonus` int NULL COMMENT '速度永久加成值',
    `metal_base` int NULL COMMENT '金屬性基礎值', `metal_bonus` int NULL COMMENT '金屬性永久加成值',
    `wood_base` int NULL COMMENT '木屬性基礎值', `wood_bonus` int NULL COMMENT '木屬性永久加成值',
    `water_base` int NULL COMMENT '水屬性基礎值', `water_bonus` int NULL COMMENT '水屬性永久加成值',
    `fire_base` int NULL COMMENT '火屬性基礎值', `fire_bonus` int NULL COMMENT '火屬性永久加成值',
    `earth_base` int NULL COMMENT '土屬性基礎值', `earth_bonus` int NULL COMMENT '土屬性永久加成值',
    `physical_attack_base` int NULL COMMENT '物理攻擊基礎值', `physical_attack_bonus` int NULL COMMENT '物理攻擊永久加成值',
    `physical_defense_base` int NULL COMMENT '物理防禦基礎值', `physical_defense_bonus` int NULL COMMENT '物理防禦永久加成值',
    `magic_attack_base` int NULL COMMENT '法術攻擊基礎值', `magic_attack_bonus` int NULL COMMENT '法術攻擊永久加成值',
    `magic_defense_base` int NULL COMMENT '法術防禦基礎值', `magic_defense_bonus` int NULL COMMENT '法術防禦永久加成值',
    `map_id` bigint NULL COMMENT '目前地圖 ID',
    `position_x` int NULL COMMENT '目前 X 座標',
    `position_y` int NULL COMMENT '目前 Y 座標',
    `direction` int NULL COMMENT '目前朝向',
    `status` varchar(30) NOT NULL COMMENT '角色狀態',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    `last_played_at_utc` datetime(6) NULL COMMENT '最後遊玩 UTC 時間',
    PRIMARY KEY (`character_id`),
    UNIQUE KEY `ux_characters_account_name` (`account_id`,`name`),
    KEY `ix_characters_account` (`account_id`),
    KEY `ix_characters_name` (`name`),
    KEY `ix_characters_class` (`class_id`),
    KEY `ix_characters_map` (`map_id`),
    CONSTRAINT `fk_characters_account` FOREIGN KEY (`account_id`) REFERENCES `god2_player`.`accounts` (`account_id`),
    CONSTRAINT `fk_characters_class` FOREIGN KEY (`class_id`) REFERENCES `god2_game`.`character_classes` (`class_id`),
    CONSTRAINT `fk_characters_map` FOREIGN KEY (`map_id`) REFERENCES `god2_game`.`maps` (`map_id`),
    CONSTRAINT `ck_characters_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='一列一個玩家角色';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_inventory` (
    `inventory_id` bigint NOT NULL AUTO_INCREMENT COMMENT '背包列 ID',
    `character_id` bigint NOT NULL COMMENT '角色 ID',
    `slot_index` int NOT NULL COMMENT '背包格索引',
    `item_id` bigint NOT NULL COMMENT '物品 ID',
    `item_name_cache` varchar(200) NULL COMMENT '物品名稱快取',
    `quantity` bigint NULL COMMENT '物品數量',
    `bound` tinyint(1) NULL COMMENT '是否綁定',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否有效',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`inventory_id`),
    UNIQUE KEY `ux_character_inventory_slot` (`character_id`,`slot_index`),
    KEY `ix_character_inventory_item` (`item_id`),
    CONSTRAINT `fk_character_inventory_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_character_inventory_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_character_inventory_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色背包';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_equipment` (
    `character_id` bigint NOT NULL COMMENT '角色 ID',
    `equipment_slot` varchar(50) NOT NULL COMMENT '裝備欄位',
    `inventory_id` bigint NULL COMMENT '背包列 ID',
    `item_id` bigint NOT NULL COMMENT '物品 ID',
    `item_name_cache` varchar(200) NULL COMMENT '物品名稱快取',
    `enhancement_level` int NULL COMMENT '強化等級',
    `durability` int NULL COMMENT '目前耐久',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否有效',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`character_id`,`equipment_slot`),
    KEY `ix_character_equipment_inventory` (`inventory_id`),
    KEY `ix_character_equipment_item` (`item_id`),
    CONSTRAINT `fk_character_equipment_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_character_equipment_inventory` FOREIGN KEY (`inventory_id`) REFERENCES `god2_player`.`character_inventory` (`inventory_id`),
    CONSTRAINT `fk_character_equipment_item` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`items` (`item_id`),
    CONSTRAINT `ck_character_equipment_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色已裝備物品';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_skills` (
    `character_id` bigint NOT NULL COMMENT '角色 ID',
    `skill_id` bigint NOT NULL COMMENT '技能 ID',
    `skill_name_cache` varchar(150) NULL COMMENT '技能名稱快取',
    `skill_level` int NULL COMMENT '技能等級',
    `learned_at_utc` datetime(6) NULL COMMENT '學會 UTC 時間',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否有效',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`character_id`,`skill_id`),
    CONSTRAINT `fk_character_skills_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_character_skills_skill` FOREIGN KEY (`skill_id`) REFERENCES `god2_game`.`skills` (`skill_id`),
    CONSTRAINT `ck_character_skills_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色戰鬥技能';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_quests` (
    `character_id` bigint NOT NULL COMMENT '角色 ID',
    `quest_id` bigint NOT NULL COMMENT '任務 ID',
    `status` varchar(30) NOT NULL COMMENT '任務狀態',
    `progress_value` bigint NULL COMMENT '任務進度',
    `accepted_at_utc` datetime(6) NULL COMMENT '接受 UTC 時間',
    `completed_at_utc` datetime(6) NULL COMMENT '完成 UTC 時間',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否有效',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`character_id`,`quest_id`),
    CONSTRAINT `fk_character_quests_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_character_quests_quest` FOREIGN KEY (`quest_id`) REFERENCES `god2_game`.`quests` (`quest_id`),
    CONSTRAINT `ck_character_quests_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色任務狀態';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_pets` (
    `pet_instance_id` bigint NOT NULL COMMENT '玩家戰寵實例 ID',
    `owner_character_id` bigint NOT NULL COMMENT '擁有角色 ID',
    `pet_template_id` bigint NOT NULL COMMENT '戰寵模板 ID',
    `name` varchar(150) NOT NULL COMMENT '戰寵名稱',
    `level` int NULL COMMENT '戰寵等級', `experience` bigint NULL COMMENT '戰寵經驗',
    `current_hp` bigint NULL COMMENT '目前 HP', `max_hp` bigint NULL COMMENT '最大 HP',
    `current_mp` bigint NULL COMMENT '目前 MP', `max_mp` bigint NULL COMMENT '最大 MP',
    `current_lifespan` bigint NULL COMMENT '戰寵目前壽命', `max_lifespan` bigint NULL COMMENT '戰寵最大壽命；未知時為 NULL',
    `strength_base` int NULL COMMENT '力量基礎值', `strength_bonus` int NULL COMMENT '力量永久加成值',
    `constitution_base` int NULL COMMENT '體力基礎值', `constitution_bonus` int NULL COMMENT '體力永久加成值',
    `intelligence_base` int NULL COMMENT '智力基礎值', `intelligence_bonus` int NULL COMMENT '智力永久加成值',
    `speed_base` int NULL COMMENT '速度基礎值', `speed_bonus` int NULL COMMENT '速度永久加成值',
    `metal_base` int NULL COMMENT '金屬性基礎值', `metal_bonus` int NULL COMMENT '金屬性永久加成值',
    `wood_base` int NULL COMMENT '木屬性基礎值', `wood_bonus` int NULL COMMENT '木屬性永久加成值',
    `water_base` int NULL COMMENT '水屬性基礎值', `water_bonus` int NULL COMMENT '水屬性永久加成值',
    `fire_base` int NULL COMMENT '火屬性基礎值', `fire_bonus` int NULL COMMENT '火屬性永久加成值',
    `earth_base` int NULL COMMENT '土屬性基礎值', `earth_bonus` int NULL COMMENT '土屬性永久加成值',
    `physical_attack_base` int NULL COMMENT '物理攻擊基礎值', `physical_attack_bonus` int NULL COMMENT '物理攻擊永久加成值',
    `physical_defense_base` int NULL COMMENT '物理防禦基礎值', `physical_defense_bonus` int NULL COMMENT '物理防禦永久加成值',
    `magic_attack_base` int NULL COMMENT '法術攻擊基礎值', `magic_attack_bonus` int NULL COMMENT '法術攻擊永久加成值',
    `magic_defense_base` int NULL COMMENT '法術防禦基礎值', `magic_defense_bonus` int NULL COMMENT '法術防禦永久加成值',
    `remaining_stat_points` int NULL COMMENT '剩餘配點',
    `rebirth_count` int NULL COMMENT '轉生次數',
    `fusion_expires_at_utc` datetime(6) NULL COMMENT '戰寵合體效果到期 UTC 時間；詳細語意需由證據確認',
    `is_deployed` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否出戰',
    `is_active` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否為目前選用戰寵',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否有效',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`pet_instance_id`),
    KEY `ix_character_pets_owner` (`owner_character_id`),
    KEY `ix_character_pets_template` (`pet_template_id`),
    CONSTRAINT `fk_character_pets_owner` FOREIGN KEY (`owner_character_id`) REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_character_pets_template` FOREIGN KEY (`pet_template_id`) REFERENCES `god2_game`.`pet_templates` (`pet_template_id`),
    CONSTRAINT `ck_character_pets_flags` CHECK (`is_deployed` IN (0,1) AND `is_active` IN (0,1) AND `enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='玩家戰寵實例';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_pet_skills` (
    `pet_instance_id` bigint NOT NULL COMMENT '戰寵實例 ID',
    `slot_index` int NOT NULL COMMENT '技能槽索引；至少支援四槽且可擴充',
    `skill_id` bigint NULL COMMENT '技能 ID；空槽為 NULL',
    `skill_name_cache` varchar(150) NULL COMMENT '技能名稱快取',
    `skill_level` int NULL COMMENT '技能等級',
    `is_unlocked` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否解鎖',
    `is_enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`pet_instance_id`,`slot_index`),
    KEY `ix_character_pet_skills_skill` (`skill_id`),
    CONSTRAINT `fk_character_pet_skills_pet` FOREIGN KEY (`pet_instance_id`) REFERENCES `god2_player`.`character_pets` (`pet_instance_id`),
    CONSTRAINT `fk_character_pet_skills_skill` FOREIGN KEY (`skill_id`) REFERENCES `god2_game`.`skills` (`skill_id`),
    CONSTRAINT `ck_character_pet_skills_flags` CHECK (`is_unlocked` IN (0,1) AND `is_enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='玩家戰寵技能槽';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_immortals` (
    `immortal_instance_id` bigint NOT NULL COMMENT '玩家神仙實例 ID',
    `owner_character_id` bigint NOT NULL COMMENT '擁有角色 ID',
    `immortal_template_id` bigint NOT NULL COMMENT '神仙模板 ID',
    `name` varchar(150) NOT NULL COMMENT '神仙顯示名稱',
    `rank_id` bigint NULL COMMENT '神仙階位 ID', `rank_name_cache` varchar(100) NULL COMMENT '階位名稱快取',
    `level` int NULL COMMENT '神仙等級', `experience` bigint NULL COMMENT '神仙等級經驗',
    `conversation_experience` bigint NULL COMMENT '神仙交談 EXP；實際升階或親密度語意尚未驗證',
    `conversation_level` int NULL COMMENT '交談等級候選值；不得接入升階公式',
    `current_hp` bigint NULL COMMENT '目前 HP', `max_hp` bigint NULL COMMENT '最大 HP',
    `current_mp` bigint NULL COMMENT '目前 MP', `max_mp` bigint NULL COMMENT '最大 MP',
    `strength_base` int NULL COMMENT '力量基礎值', `strength_bonus` int NULL COMMENT '力量永久加成值',
    `constitution_base` int NULL COMMENT '體力基礎值', `constitution_bonus` int NULL COMMENT '體力永久加成值',
    `intelligence_base` int NULL COMMENT '智力基礎值', `intelligence_bonus` int NULL COMMENT '智力永久加成值',
    `speed_base` int NULL COMMENT '速度基礎值', `speed_bonus` int NULL COMMENT '速度永久加成值',
    `metal_base` int NULL COMMENT '金屬性基礎值', `metal_bonus` int NULL COMMENT '金屬性永久加成值',
    `wood_base` int NULL COMMENT '木屬性基礎值', `wood_bonus` int NULL COMMENT '木屬性永久加成值',
    `water_base` int NULL COMMENT '水屬性基礎值', `water_bonus` int NULL COMMENT '水屬性永久加成值',
    `fire_base` int NULL COMMENT '火屬性基礎值', `fire_bonus` int NULL COMMENT '火屬性永久加成值',
    `earth_base` int NULL COMMENT '土屬性基礎值', `earth_bonus` int NULL COMMENT '土屬性永久加成值',
    `physical_attack_base` int NULL COMMENT '物理攻擊基礎值', `physical_attack_bonus` int NULL COMMENT '物理攻擊永久加成值',
    `physical_defense_base` int NULL COMMENT '物理防禦基礎值', `physical_defense_bonus` int NULL COMMENT '物理防禦永久加成值',
    `magic_attack_base` int NULL COMMENT '法術攻擊基礎值', `magic_attack_bonus` int NULL COMMENT '法術攻擊永久加成值',
    `magic_defense_base` int NULL COMMENT '法術防禦基礎值', `magic_defense_bonus` int NULL COMMENT '法術防禦永久加成值',
    `is_active` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否為目前選用神仙',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否有效',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`immortal_instance_id`),
    KEY `ix_character_immortals_owner` (`owner_character_id`),
    KEY `ix_character_immortals_template` (`immortal_template_id`),
    KEY `ix_character_immortals_rank` (`rank_id`),
    CONSTRAINT `fk_character_immortals_owner` FOREIGN KEY (`owner_character_id`) REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_character_immortals_template` FOREIGN KEY (`immortal_template_id`) REFERENCES `god2_game`.`immortal_templates` (`immortal_template_id`),
    CONSTRAINT `fk_character_immortals_rank` FOREIGN KEY (`rank_id`) REFERENCES `god2_game`.`immortal_ranks` (`rank_id`),
    CONSTRAINT `ck_character_immortals_flags` CHECK (`is_active` IN (0,1) AND `enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='玩家神仙實例';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_immortal_skills` (
    `immortal_instance_id` bigint NOT NULL COMMENT '神仙實例 ID',
    `skill_category` varchar(30) NOT NULL COMMENT '技能分類',
    `slot_index` int NOT NULL COMMENT '技能槽索引',
    `skill_id` bigint NULL COMMENT '技能 ID；空槽為 NULL',
    `skill_name_cache` varchar(150) NULL COMMENT '技能名稱快取',
    `skill_level` int NULL COMMENT '技能等級',
    `is_unlocked` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否解鎖',
    `is_equipped` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否裝配',
    `is_enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`immortal_instance_id`,`skill_category`,`slot_index`),
    KEY `ix_character_immortal_skills_skill` (`skill_id`),
    CONSTRAINT `fk_character_immortal_skills_immortal` FOREIGN KEY (`immortal_instance_id`) REFERENCES `god2_player`.`character_immortals` (`immortal_instance_id`),
    CONSTRAINT `fk_character_immortal_skills_skill` FOREIGN KEY (`skill_id`) REFERENCES `god2_game`.`skills` (`skill_id`),
    CONSTRAINT `ck_character_immortal_skills_category` CHECK (`skill_category` IN ('Innate','Active','Passive','Unknown')),
    CONSTRAINT `ck_character_immortal_skills_flags` CHECK (`is_unlocked` IN (0,1) AND `is_equipped` IN (0,1) AND `is_enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='玩家神仙固有、主動與被動技能槽';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_mounts` (
    `mount_instance_id` bigint NOT NULL COMMENT '玩家坐騎實例 ID',
    `owner_character_id` bigint NOT NULL COMMENT '擁有角色 ID',
    `mount_template_id` bigint NOT NULL COMMENT '坐騎模板 ID',
    `name` varchar(150) NULL COMMENT '坐騎名稱',
    `level` int NULL COMMENT '坐騎等級',
    `experience` bigint NULL COMMENT '坐騎經驗',
    `is_active` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否為目前坐騎',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否有效',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`mount_instance_id`),
    KEY `ix_character_mounts_owner` (`owner_character_id`),
    CONSTRAINT `fk_character_mounts_owner` FOREIGN KEY (`owner_character_id`) REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_character_mounts_template` FOREIGN KEY (`mount_template_id`) REFERENCES `god2_game`.`mount_templates` (`mount_template_id`),
    CONSTRAINT `ck_character_mounts_flags` CHECK (`is_active` IN (0,1) AND `enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='玩家坐騎實例';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_formations` (
    `character_id` bigint NOT NULL COMMENT '角色 ID',
    `formation_id` bigint NOT NULL COMMENT '陣型 ID',
    `is_active` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否為目前陣型',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`character_id`,`formation_id`),
    CONSTRAINT `fk_character_formations_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_character_formations_formation` FOREIGN KEY (`formation_id`) REFERENCES `god2_game`.`formations` (`formation_id`),
    CONSTRAINT `ck_character_formations_active` CHECK (`is_active` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色已解鎖陣型';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_formation_members` (
    `character_id` bigint NOT NULL COMMENT '角色 ID',
    `formation_id` bigint NOT NULL COMMENT '陣型 ID',
    `slot_index` int NOT NULL COMMENT '陣型格索引',
    `member_type` varchar(30) NOT NULL COMMENT '成員類型',
    `member_instance_id` bigint NOT NULL COMMENT '成員實例 ID',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否有效',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`character_id`,`formation_id`,`slot_index`),
    CONSTRAINT `fk_character_formation_members_formation` FOREIGN KEY (`character_id`,`formation_id`) REFERENCES `god2_player`.`character_formations` (`character_id`,`formation_id`),
    CONSTRAINT `fk_character_formation_members_slot` FOREIGN KEY (`formation_id`,`slot_index`) REFERENCES `god2_game`.`formation_slots` (`formation_id`,`slot_index`),
    CONSTRAINT `ck_character_formation_members_type` CHECK (`member_type` IN ('Character','Pet','Immortal','Unknown')),
    CONSTRAINT `ck_character_formation_members_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色陣型成員';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_life_skills` (
    `character_id` bigint NOT NULL COMMENT '角色 ID',
    `life_skill_id` bigint NOT NULL COMMENT '生活技能 ID',
    `level` int NULL COMMENT '角色生活技能等級；未知時為 NULL',
    `experience` bigint NULL COMMENT '角色生活技能經驗；未知時為 NULL',
    `proficiency` bigint NULL COMMENT '角色生活技能熟練度；詳細量尺尚需證據確認',
    `progress_value` bigint NULL COMMENT '目前進度值',
    `is_unlocked` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否解鎖',
    `is_active` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用',
    `success_rate_bonus` decimal(12,8) NULL COMMENT '成功率加成；未知時為 NULL',
    `quality_rate_bonus` decimal(12,8) NULL COMMENT '品質率加成；未知時為 NULL',
    `critical_craft_rate_bonus` decimal(12,8) NULL COMMENT '特殊製作率加成；未知時為 NULL',
    `daily_use_count` int NULL COMMENT '今日使用次數',
    `daily_use_limit` int NULL COMMENT '每日使用限制',
    `last_used_at_utc` datetime(6) NULL COMMENT '最後使用 UTC 時間',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`character_id`,`life_skill_id`),
    CONSTRAINT `fk_character_life_skills_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_character_life_skills_life_skill` FOREIGN KEY (`life_skill_id`) REFERENCES `god2_game`.`life_skills` (`life_skill_id`),
    CONSTRAINT `ck_character_life_skills_flags` CHECK (`is_unlocked` IN (0,1) AND `is_active` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色四項生活技能進度';

CREATE TABLE IF NOT EXISTS `god2_player`.`character_learned_recipes` (
    `character_id` bigint NOT NULL COMMENT '角色 ID',
    `recipe_id` bigint NOT NULL COMMENT '生活技能配方 ID',
    `learned_at_utc` datetime(6) NULL COMMENT '學會 UTC 時間',
    `proficiency` bigint NULL COMMENT '配方熟練度',
    `craft_count` bigint NULL COMMENT '製作次數',
    `success_count` bigint NULL COMMENT '成功次數',
    `failure_count` bigint NULL COMMENT '失敗次數',
    `highest_quality_tier` int NULL COMMENT '最高品質階級',
    `enabled` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否解鎖且有效',
    `admin_note` varchar(500) NULL COMMENT '服主管理備註',
    PRIMARY KEY (`character_id`,`recipe_id`),
    CONSTRAINT `fk_character_learned_recipes_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_character_learned_recipes_recipe` FOREIGN KEY (`recipe_id`) REFERENCES `god2_game`.`crafting_recipes` (`recipe_id`),
    CONSTRAINT `ck_character_learned_recipes_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='角色已學會生活技能配方';

-- Metadata used only by catalog builders, synchronization and audit.
CREATE TABLE IF NOT EXISTS `god2_game_meta`.`field_mappings` (
    `mapping_id` bigint NOT NULL AUTO_INCREMENT COMMENT '欄位映射 ID',
    `source_schema` varchar(64) NOT NULL COMMENT '來源 schema',
    `source_table` varchar(128) NOT NULL COMMENT '來源表',
    `source_key_column` varchar(128) NOT NULL COMMENT '來源主鍵欄位',
    `source_value_column` varchar(128) NOT NULL COMMENT '來源值欄位',
    `source_status_column` varchar(128) NULL COMMENT '來源證據狀態欄位',
    `source_gate_column` varchar(128) NULL COMMENT 'Recovered Production Gate 欄位',
    `target_schema` varchar(64) NOT NULL COMMENT '目標 schema',
    `target_table` varchar(128) NOT NULL COMMENT '目標表',
    `target_key_column` varchar(128) NOT NULL COMMENT '目標主鍵欄位',
    `target_field` varchar(128) NOT NULL COMMENT '目標欄位',
    `value_type` varchar(30) NOT NULL COMMENT '值轉換型別',
    `fill_null_only` tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否只填空白正式欄位',
    `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用此映射',
    `admin_note` varchar(500) NULL COMMENT '管理備註',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`mapping_id`),
    UNIQUE KEY `ux_field_mappings_target` (`source_schema`,`source_table`,`source_value_column`,`target_schema`,`target_table`,`target_field`),
    CONSTRAINT `ck_field_mappings_flags` CHECK (`fill_null_only` IN (0,1) AND `enabled` IN (0,1)),
    CONSTRAINT `ck_field_mappings_target_schema` CHECK (`target_schema` IN ('god2_game','god2_player'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Evidence 或正式來源到 canonical 欄位映射';

CREATE TABLE IF NOT EXISTS `god2_game_meta`.`sync_runs` (
    `sync_run_id` char(36) NOT NULL COMMENT '同步執行 UUID',
    `started_at_utc` datetime(6) NOT NULL COMMENT '開始 UTC 時間',
    `completed_at_utc` datetime(6) NULL COMMENT '完成 UTC 時間',
    `status` varchar(30) NOT NULL COMMENT '執行狀態',
    `source_watermark` varchar(500) NULL COMMENT '來源水位',
    `mapped_value_count` bigint NOT NULL DEFAULT 0 COMMENT '成功映射值數',
    `blocked_candidate_count` bigint NOT NULL DEFAULT 0 COMMENT '被阻擋 Candidate／Derived／EvidenceBlocked 數',
    `conflict_count` bigint NOT NULL DEFAULT 0 COMMENT '衝突數',
    `unmapped_count` bigint NOT NULL DEFAULT 0 COMMENT '未映射欄位數',
    `error_message` text NULL COMMENT '錯誤內容',
    PRIMARY KEY (`sync_run_id`),
    KEY `ix_sync_runs_started` (`started_at_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Canonical 同步執行紀錄';

CREATE TABLE IF NOT EXISTS `god2_game_meta`.`field_provenance` (
    `provenance_id` bigint NOT NULL AUTO_INCREMENT COMMENT '欄位來源 ID',
    `entity_schema` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '正式 schema',
    `entity_table` varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '正式表',
    `entity_id` varchar(512) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '正式實體主鍵文字',
    `field_name` varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '正式欄位名稱',
    `source_schema` varchar(64) NOT NULL COMMENT '來源 schema',
    `source_table` varchar(128) NOT NULL COMMENT '來源表',
    `source_identity` varchar(512) NOT NULL COMMENT '來源列識別',
    `source_status` varchar(30) NOT NULL COMMENT '來源證據狀態',
    `source_hash` char(64) NOT NULL COMMENT '來源值 SHA-256',
    `sync_run_id` char(36) NOT NULL COMMENT '同步執行 UUID',
    `recorded_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '記錄 UTC 時間',
    PRIMARY KEY (`provenance_id`),
    UNIQUE KEY `ux_field_provenance_current` (`entity_schema`,`entity_table`,`entity_id`,`field_name`),
    KEY `ix_field_provenance_sync_run` (`sync_run_id`),
    CONSTRAINT `fk_field_provenance_sync_run` FOREIGN KEY (`sync_run_id`) REFERENCES `god2_game_meta`.`sync_runs` (`sync_run_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Canonical 正式欄位來源';

CREATE TABLE IF NOT EXISTS `god2_game_meta`.`admin_field_locks` (
    `entity_schema` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '正式 schema',
    `entity_table` varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '正式表',
    `entity_id` varchar(512) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '實體主鍵文字',
    `field_name` varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '被鎖欄位名稱',
    `locked_value_hash` char(64) NOT NULL COMMENT '管理員值 SHA-256',
    `locked_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '鎖定 UTC 時間',
    `locked_by` varchar(255) NOT NULL COMMENT '修改資料庫使用者',
    `note` varchar(500) NULL COMMENT '鎖定備註',
    PRIMARY KEY (`entity_schema`,`entity_table`,`entity_id`,`field_name`),
    KEY `ix_admin_field_locks_time` (`locked_at_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='逐欄位管理員鎖；Evidence Sync 不得覆蓋';

CREATE TABLE IF NOT EXISTS `god2_game_meta`.`admin_change_audit` (
    `audit_id` bigint NOT NULL AUTO_INCREMENT COMMENT '稽核 ID',
    `entity_schema` varchar(64) NOT NULL COMMENT '正式 schema',
    `entity_table` varchar(128) NOT NULL COMMENT '正式表',
    `entity_id` varchar(512) NOT NULL COMMENT '實體主鍵文字',
    `field_name` varchar(128) NOT NULL COMMENT '修改欄位名稱',
    `old_value` longtext NULL COMMENT '修改前值',
    `new_value` longtext NULL COMMENT '修改後值',
    `changed_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '修改 UTC 時間',
    `changed_by` varchar(255) NOT NULL COMMENT '修改資料庫使用者',
    `change_source` varchar(30) NOT NULL COMMENT '修改來源',
    `note` varchar(500) NULL COMMENT '修改備註',
    PRIMARY KEY (`audit_id`),
    KEY `ix_admin_change_audit_entity` (`entity_schema`,`entity_table`,`entity_id`,`changed_at_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='管理員逐欄位修改稽核';

CREATE TABLE IF NOT EXISTS `god2_game_meta`.`sync_conflicts` (
    `conflict_id` bigint NOT NULL AUTO_INCREMENT COMMENT '同步衝突 ID',
    `sync_run_id` char(36) NOT NULL COMMENT '同步執行 UUID',
    `entity_schema` varchar(64) NOT NULL COMMENT '正式 schema',
    `entity_table` varchar(128) NOT NULL COMMENT '正式表',
    `entity_id` varchar(512) NOT NULL COMMENT '實體主鍵文字',
    `field_name` varchar(128) NOT NULL COMMENT '衝突欄位',
    `existing_value` longtext NULL COMMENT '保留的正式或管理員值',
    `candidate_value` longtext NULL COMMENT '同步候選值',
    `conflict_type` varchar(50) NOT NULL COMMENT '衝突類型',
    `source_status` varchar(30) NOT NULL COMMENT '來源證據狀態',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    `resolved_at_utc` datetime(6) NULL COMMENT '解決 UTC 時間',
    `resolution_note` varchar(500) NULL COMMENT '解決備註',
    PRIMARY KEY (`conflict_id`),
    KEY `ix_sync_conflicts_run` (`sync_run_id`),
    CONSTRAINT `fk_sync_conflicts_run` FOREIGN KEY (`sync_run_id`) REFERENCES `god2_game_meta`.`sync_runs` (`sync_run_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='同步衝突；同級衝突不得任選';

CREATE TABLE IF NOT EXISTS `god2_game_meta`.`unmapped_fields` (
    `unmapped_id` bigint NOT NULL AUTO_INCREMENT COMMENT '未映射欄位 ID',
    `sync_run_id` char(36) NOT NULL COMMENT '同步執行 UUID',
    `source_domain` varchar(128) NOT NULL COMMENT '來源領域',
    `source_field` varchar(128) NOT NULL COMMENT '來源欄位',
    `observed_count` bigint NOT NULL COMMENT '觀測筆數',
    `first_source_identity` varchar(512) NULL COMMENT '第一個來源識別範例',
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
    PRIMARY KEY (`unmapped_id`),
    UNIQUE KEY `ux_unmapped_fields_run_field` (`sync_run_id`,`source_domain`,`source_field`),
    CONSTRAINT `fk_unmapped_fields_run` FOREIGN KEY (`sync_run_id`) REFERENCES `god2_game_meta`.`sync_runs` (`sync_run_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='尚未映射 Evidence 欄位摘要';

CREATE TABLE IF NOT EXISTS `god2_game_meta`.`catalog_validation` (
    `validation_id` bigint NOT NULL AUTO_INCREMENT COMMENT '驗證結果 ID',
    `validation_run_id` char(36) NOT NULL COMMENT '驗證執行 UUID',
    `validated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '驗證 UTC 時間',
    `severity` varchar(20) NOT NULL COMMENT '嚴重度',
    `entity_schema` varchar(64) NULL COMMENT '相關 schema',
    `entity_table` varchar(128) NULL COMMENT '相關表',
    `entity_id` varchar(512) NULL COMMENT '相關主鍵',
    `field_name` varchar(128) NULL COMMENT '相關欄位',
    `rule_code` varchar(100) NOT NULL COMMENT '驗證規則代碼',
    `message_zh_tw` varchar(1000) NOT NULL COMMENT '繁體中文驗證訊息',
    PRIMARY KEY (`validation_id`),
    KEY `ix_catalog_validation_run` (`validation_run_id`,`severity`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Catalog 驗證明細';

CREATE TABLE IF NOT EXISTS `god2_game_meta`.`combatant_stat_observations` (
    `observation_id` bigint NOT NULL AUTO_INCREMENT COMMENT '能力觀測 ID',
    `session_id` varchar(128) NOT NULL COMMENT '觀測工作階段 ID',
    `entity_type` varchar(30) NOT NULL COMMENT '實體類型',
    `entity_id` bigint NOT NULL COMMENT '實體 ID',
    `observed_at_utc` datetime(6) NOT NULL COMMENT '觀測 UTC 時間',
    `level_observed` int NULL COMMENT '觀測等級',
    `current_hp_observed` bigint NULL COMMENT '觀測目前 HP', `max_hp_observed` bigint NULL COMMENT '觀測最大 HP',
    `current_mp_observed` bigint NULL COMMENT '觀測目前 MP', `max_mp_observed` bigint NULL COMMENT '觀測最大 MP',
    `strength_base_observed` int NULL COMMENT '觀測力量基礎', `strength_bonus_observed` int NULL COMMENT '觀測力量加成',
    `constitution_base_observed` int NULL COMMENT '觀測體力基礎', `constitution_bonus_observed` int NULL COMMENT '觀測體力加成',
    `intelligence_base_observed` int NULL COMMENT '觀測智力基礎', `intelligence_bonus_observed` int NULL COMMENT '觀測智力加成',
    `speed_base_observed` int NULL COMMENT '觀測速度基礎', `speed_bonus_observed` int NULL COMMENT '觀測速度加成',
    `metal_observed` int NULL COMMENT '觀測金屬性', `wood_observed` int NULL COMMENT '觀測木屬性',
    `water_observed` int NULL COMMENT '觀測水屬性', `fire_observed` int NULL COMMENT '觀測火屬性', `earth_observed` int NULL COMMENT '觀測土屬性',
    `physical_attack_observed` int NULL COMMENT '觀測物理攻擊', `physical_defense_observed` int NULL COMMENT '觀測物理防禦',
    `magic_attack_observed` int NULL COMMENT '觀測法術攻擊', `magic_defense_observed` int NULL COMMENT '觀測法術防禦',
    `evidence_level` varchar(30) NOT NULL COMMENT '證據等級',
    `confidence` decimal(9,6) NULL COMMENT '信心分數',
    `source_session_id` varchar(128) NULL COMMENT '來源 Session ID',
    `source_frame_ids` text NULL COMMENT '來源 Frame ID 清單',
    `source_hash` char(64) NOT NULL COMMENT '來源 SHA-256',
    PRIMARY KEY (`observation_id`),
    KEY `ix_combatant_stat_observations_entity` (`entity_type`,`entity_id`,`observed_at_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='畫面與封包能力觀測；不得直接覆蓋正式值';

CREATE TABLE IF NOT EXISTS `god2_game_meta`.`synchronization_watermarks` (
    `watermark_key` varchar(100) NOT NULL COMMENT '同步水位代碼',
    `watermark_value` varchar(1000) NULL COMMENT '同步水位值',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`watermark_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='同步冪等水位';
