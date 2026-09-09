-- Canonical runtime authority, readable HeidiSQL views and least-privilege roles.

CREATE TABLE IF NOT EXISTS `god2_game_meta`.`runtime_catalog_releases` (
    `catalog_release_id` char(36) NOT NULL COMMENT 'Immutable Catalog Release UUID',
    `catalog_fingerprint` char(64) NOT NULL COMMENT 'Catalog SHA-256',
    `catalog_record_count` bigint NOT NULL COMMENT 'Catalog 正式列總數',
    `status` varchar(30) NOT NULL COMMENT 'Catalog 狀態',
    `active_slot` tinyint AS (CASE WHEN `status`='Active' THEN 1 ELSE NULL END) STORED COMMENT '確保同時只有一個 Active Catalog',
    `built_at_utc` datetime(6) NOT NULL COMMENT '建立 UTC 時間',
    `activated_at_utc` datetime(6) NULL COMMENT '啟用 UTC 時間',
    `validation_run_id` char(36) NOT NULL COMMENT '對應驗證執行 UUID',
    `builder_version` varchar(100) NOT NULL COMMENT 'Catalog Builder 版本',
    `failure_message` text NULL COMMENT '驗證失敗內容',
    PRIMARY KEY (`catalog_release_id`),
    UNIQUE KEY `ux_runtime_catalog_releases_active` (`active_slot`),
    KEY `ix_runtime_catalog_releases_built` (`built_at_utc`),
    CONSTRAINT `ck_runtime_catalog_releases_status` CHECK (`status` IN ('Active','Superseded','Rejected'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Runtime immutable catalog 版本；一次只有一個 Active';

CREATE TABLE IF NOT EXISTS `god2_game_meta`.`runtime_authority` (
    `authority_key` varchar(100) NOT NULL COMMENT 'Runtime 權威設定代碼',
    `authority_value` varchar(500) NOT NULL COMMENT 'Runtime 權威設定值',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`authority_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Runtime Catalog 權威切換設定';

INSERT INTO `god2_game_meta`.`runtime_authority` (`authority_key`,`authority_value`)
VALUES
    ('static_gameplay_schema','god2_game'),
    ('player_state_schema','god2_player'),
    ('metadata_schema','god2_game_meta'),
    ('normal_runtime_evidence_scan','Disabled'),
    ('catalog_reload_mode','ValidateThenAtomicSwap')
ON DUPLICATE KEY UPDATE `authority_value`=VALUES(`authority_value`);

CREATE OR REPLACE VIEW `god2_game`.`vw_items_full` AS
SELECT `item_id` AS `物品ID`,`code` AS `代碼`,`name_zh_tw` AS `名稱`,`name_original` AS `原文名稱`,
       `item_category` AS `分類`,`item_family` AS `家族`,`required_level` AS `需求等級`,
       `maximum_stack` AS `最大堆疊`,`buy_price` AS `買價`,`sell_price` AS `賣價`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`items`;

CREATE OR REPLACE VIEW `god2_game`.`vw_equipment_full` AS
SELECT item_row.`item_id` AS `物品ID`,item_row.`name_zh_tw` AS `名稱`,equipment_row.`equipment_type` AS `裝備類型`,
       equipment_row.`equipment_slot` AS `裝備欄位`,equipment_row.`hp_bonus` AS `HP加成`,equipment_row.`mp_bonus` AS `MP加成`,
       equipment_row.`strength_bonus` AS `力量加成`,equipment_row.`constitution_bonus` AS `體力加成`,
       equipment_row.`intelligence_bonus` AS `智力加成`,equipment_row.`speed_bonus` AS `速度加成`,
       equipment_row.`physical_attack_bonus` AS `物理攻擊加成`,equipment_row.`physical_defense_bonus` AS `物理防禦加成`,
       equipment_row.`magic_attack_bonus` AS `法術攻擊加成`,equipment_row.`magic_defense_bonus` AS `法術防禦加成`,
       equipment_row.`enabled` AS `啟用狀態`,equipment_row.`admin_note` AS `管理備註`
FROM `god2_game`.`equipment` equipment_row
JOIN `god2_game`.`items` item_row ON item_row.`item_id`=equipment_row.`item_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monsters_full` AS
SELECT `monster_id` AS `怪物ID`,`code` AS `代碼`,`name_zh_tw` AS `名稱`,`name_original` AS `原文名稱`,`level` AS `等級`,
       `max_hp` AS `最大HP`,`max_mp` AS `最大MP`,`strength` AS `力量`,`constitution` AS `體力`,
       `intelligence` AS `智力`,`speed` AS `速度`,`metal` AS `金`,`wood` AS `木`,`water` AS `水`,`fire` AS `火`,`earth` AS `土`,
       `physical_attack` AS `物理攻擊`,`physical_defense` AS `物理防禦`,`magic_attack` AS `法術攻擊`,`magic_defense` AS `法術防禦`,
       `experience_reward` AS `經驗獎勵`,`currency_reward` AS `貨幣獎勵`,`ai_profile_id` AS `AI模板ID`,
       `boss` AS `首領`,`elite` AS `菁英`,`aggressive` AS `主動`,`evidence_status` AS `證據狀態`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`monsters`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_skills_full` AS
SELECT `monster_skill_id` AS `配置ID`,`monster_id` AS `怪物ID`,`monster_name_cache` AS `怪物名稱`,
       `skill_id` AS `技能ID`,`skill_name_cache` AS `技能名稱`,`priority` AS `優先值`,`trigger_type` AS `觸發類型`,
       `trigger_hp_percent` AS `觸發HP比例`,`trigger_round_min` AS `最早回合`,`trigger_round_max` AS `最晚回合`,
       `target_type` AS `目標類型`,`use_probability` AS `使用機率`,`cooldown_rounds` AS `冷卻回合`,
       `maximum_uses` AS `最大使用次數`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`monster_skills`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_spawns_full` AS
SELECT `spawn_id` AS `出生點ID`,`monster_id` AS `怪物ID`,`monster_name_cache` AS `怪物名稱`,`map_id` AS `地圖ID`,
       `map_name_cache` AS `地圖名稱`,`position_x` AS `X`,`position_y` AS `Y`,`direction` AS `朝向`,
       `spawn_count` AS `出生數量`,`spawn_radius` AS `出生半徑`,`respawn_seconds_min` AS `最短重生秒數`,
       `respawn_seconds_max` AS `最長重生秒數`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`monster_spawns`;

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_drops_full` AS
SELECT `drop_id` AS `掉落ID`,`monster_id` AS `怪物ID`,`monster_name_cache` AS `怪物名稱`,
       `item_id` AS `物品ID`,`item_name_cache` AS `物品名稱`,`minimum_quantity` AS `最小數量`,
       `maximum_quantity` AS `最大數量`,`drop_rate` AS `掉率`,`drop_rate_unit` AS `掉率單位`,
       `is_guaranteed` AS `保證掉落`,`evidence_status` AS `證據狀態`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`monster_drops`;

CREATE OR REPLACE VIEW `god2_game`.`vw_npcs_full` AS
SELECT `npc_id` AS `NPC_ID`,`code` AS `代碼`,`name_zh_tw` AS `名稱`,`name_original` AS `原文名稱`,
       `npc_type` AS `NPC類型`,`resource_id` AS `資源ID`,`default_dialog_id` AS `預設對話ID`,
       `merchant_id` AS `商店ID`,`quest_provider` AS `任務提供者`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`npcs`;

CREATE OR REPLACE VIEW `god2_game`.`vw_npc_spawns_full` AS
SELECT `spawn_id` AS `出生點ID`,`npc_id` AS `NPC_ID`,`npc_name_cache` AS `NPC名稱`,`map_id` AS `地圖ID`,
       `map_name_cache` AS `地圖名稱`,`position_x` AS `X`,`position_y` AS `Y`,`direction` AS `朝向`,
       `wire_evidence_status` AS `協定證據狀態`,`identity_evidence_status` AS `識別證據狀態`,
       `coordinate_evidence_status` AS `座標證據狀態`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`npc_spawns`;

CREATE OR REPLACE VIEW `god2_game`.`vw_merchants_full` AS
SELECT `merchant_id` AS `商店ID`,`npc_id` AS `NPC_ID`,`name_zh_tw` AS `商店名稱`,`merchant_type` AS `商店類型`,
       `buyback_enabled` AS `允許回收`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`merchants`;

CREATE OR REPLACE VIEW `god2_game`.`vw_merchant_inventory_full` AS
SELECT `merchant_inventory_id` AS `庫存ID`,`merchant_id` AS `商店ID`,`merchant_name_cache` AS `商店名稱`,
       `item_id` AS `物品ID`,`item_name_cache` AS `物品名稱`,`display_order` AS `顯示順序`,
       `selling_price` AS `售價`,`purchasing_price` AS `回收價`,`pack_count` AS `包裝數量`,
       `quantity_limit` AS `數量限制`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`merchant_inventory`;

CREATE OR REPLACE VIEW `god2_game`.`vw_skills_full` AS
SELECT `skill_id` AS `技能ID`,`code` AS `代碼`,`name_zh_tw` AS `名稱`,`name_original` AS `原文名稱`,
       `skill_family` AS `技能家族`,`skill_category` AS `技能分類`,`damage_type` AS `傷害類型`,`element` AS `五行`,
       `required_level` AS `需求等級`,`maximum_level` AS `最大等級`,`mp_cost` AS `MP消耗`,`hp_cost` AS `HP消耗`,
       `cooldown_rounds` AS `冷卻回合`,`cast_rounds` AS `施放回合`,`duration_rounds` AS `持續回合`,
       `target_side` AS `目標陣營`,`target_type` AS `目標類型`,`consumes_turn` AS `消耗回合`,
       `evidence_status` AS `證據狀態`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`skills`;

CREATE OR REPLACE VIEW `god2_game`.`vw_status_effects_full` AS
SELECT `status_effect_id` AS `狀態ID`,`name_zh_tw` AS `名稱`,`effect_type` AS `效果類型`,`element` AS `五行`,
       `duration_rounds` AS `持續回合`,`maximum_stacks` AS `最大層數`,`stack_mode` AS `疊加模式`,
       `apply_phase` AS `套用階段`,`tick_phase` AS `生效階段`,`expire_phase` AS `到期階段`,
       `base_value` AS `基礎值`,`can_dispel` AS `可解除`,`is_buff` AS `Buff`,`is_debuff` AS `Debuff`,
       `enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`status_effects`;

CREATE OR REPLACE VIEW `god2_game`.`vw_quests_full` AS
SELECT `quest_id` AS `任務ID`,`code` AS `代碼`,`name_zh_tw` AS `名稱`,`name_original` AS `原文名稱`,
       `quest_type` AS `任務類型`,`start_npc_id` AS `起始NPC`,`end_npc_id` AS `結束NPC`,
       `required_level` AS `需求等級`,`maximum_level` AS `最高等級`,`repeatable` AS `可重複`,
       `repeat_interval_seconds` AS `重複間隔秒數`,`evidence_status` AS `證據狀態`,
       `enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`quests`;

CREATE OR REPLACE VIEW `god2_game`.`vw_quest_objectives_full` AS
SELECT `objective_id` AS `目標ID`,`quest_id` AS `任務ID`,`objective_order` AS `順序`,`objective_type` AS `目標類型`,
       `target_id` AS `目標實體ID`,`target_name_cache` AS `目標名稱`,`required_quantity` AS `需求數量`,
       `map_id` AS `地圖ID`,`description_zh_tw` AS `目標說明`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`quest_objectives`;

CREATE OR REPLACE VIEW `god2_game`.`vw_quest_rewards_full` AS
SELECT `reward_id` AS `獎勵ID`,`quest_id` AS `任務ID`,`reward_order` AS `順序`,`reward_type` AS `獎勵類型`,
       `item_id` AS `物品ID`,`item_name_cache` AS `物品名稱`,`quantity` AS `數量`,`experience` AS `經驗`,
       `currency` AS `貨幣`,`selection_group` AS `選擇群組`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`quest_rewards`;

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_templates_full` AS
SELECT `pet_template_id` AS `戰寵模板ID`,`code` AS `代碼`,`name_zh_tw` AS `名稱`,`name_original` AS `原文名稱`,
       `base_level` AS `基礎等級`,`base_max_hp` AS `基礎最大HP`,`base_max_mp` AS `基礎最大MP`,`base_max_lifespan` AS `基礎最大壽命`,
       `base_strength` AS `力量`,`base_constitution` AS `體力`,`base_intelligence` AS `智力`,`base_speed` AS `速度`,
       `base_metal` AS `金`,`base_wood` AS `木`,`base_water` AS `水`,`base_fire` AS `火`,`base_earth` AS `土`,
       `maximum_skill_slots` AS `最大技能槽`,`enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`pet_templates`;

CREATE OR REPLACE VIEW `god2_game`.`vw_immortal_templates_full` AS
SELECT `immortal_template_id` AS `神仙模板ID`,`code` AS `代碼`,`name_zh_tw` AS `名稱`,`name_original` AS `原文名稱`,
       `initial_rank_id` AS `初始階位`,`initial_level` AS `初始等級`,`base_max_hp` AS `基礎最大HP`,`base_max_mp` AS `基礎最大MP`,
       `base_strength` AS `力量`,`base_constitution` AS `體力`,`base_intelligence` AS `智力`,`base_speed` AS `速度`,
       `base_metal` AS `金`,`base_wood` AS `木`,`base_water` AS `水`,`base_fire` AS `火`,`base_earth` AS `土`,
       `maximum_innate_skill_slots` AS `固有技能槽`,`maximum_active_skill_slots` AS `主動技能槽`,
       `enabled` AS `啟用狀態`,`admin_note` AS `管理備註`
FROM `god2_game`.`immortal_templates`;

CREATE OR REPLACE VIEW `god2_player`.`vw_characters_full` AS
SELECT character_row.`character_id` AS `角色ID`,character_row.`account_id` AS `帳號ID`,character_row.`name` AS `名稱`,
       character_row.`class_name_cache` AS `職業`,character_row.`title_name_cache` AS `稱號`,character_row.`level` AS `等級`,
       character_row.`experience` AS `經驗`,character_row.`reputation` AS `名聲`,character_row.`rebirth_count` AS `轉生次數`,
       character_row.`remaining_stat_points` AS `剩餘配點`,character_row.`current_hp` AS `目前HP`,character_row.`max_hp` AS `最大HP`,
       character_row.`current_mp` AS `目前MP`,character_row.`max_mp` AS `最大MP`,
       character_row.`strength_base` AS `力量基礎`,character_row.`strength_bonus` AS `力量加成`,COALESCE(character_row.`strength_base`,0)+COALESCE(character_row.`strength_bonus`,0) AS `力量合計`,
       character_row.`constitution_base` AS `體力基礎`,character_row.`constitution_bonus` AS `體力加成`,COALESCE(character_row.`constitution_base`,0)+COALESCE(character_row.`constitution_bonus`,0) AS `體力合計`,
       character_row.`intelligence_base` AS `智力基礎`,character_row.`intelligence_bonus` AS `智力加成`,COALESCE(character_row.`intelligence_base`,0)+COALESCE(character_row.`intelligence_bonus`,0) AS `智力合計`,
       character_row.`speed_base` AS `速度基礎`,character_row.`speed_bonus` AS `速度加成`,COALESCE(character_row.`speed_base`,0)+COALESCE(character_row.`speed_bonus`,0) AS `速度合計`,
       CASE WHEN character_row.`metal_base` IS NULL AND character_row.`metal_bonus` IS NULL THEN NULL ELSE COALESCE(character_row.`metal_base`,0)+COALESCE(character_row.`metal_bonus`,0) END AS `金`,
       CASE WHEN character_row.`wood_base` IS NULL AND character_row.`wood_bonus` IS NULL THEN NULL ELSE COALESCE(character_row.`wood_base`,0)+COALESCE(character_row.`wood_bonus`,0) END AS `木`,
       CASE WHEN character_row.`water_base` IS NULL AND character_row.`water_bonus` IS NULL THEN NULL ELSE COALESCE(character_row.`water_base`,0)+COALESCE(character_row.`water_bonus`,0) END AS `水`,
       CASE WHEN character_row.`fire_base` IS NULL AND character_row.`fire_bonus` IS NULL THEN NULL ELSE COALESCE(character_row.`fire_base`,0)+COALESCE(character_row.`fire_bonus`,0) END AS `火`,
       CASE WHEN character_row.`earth_base` IS NULL AND character_row.`earth_bonus` IS NULL THEN NULL ELSE COALESCE(character_row.`earth_base`,0)+COALESCE(character_row.`earth_bonus`,0) END AS `土`,
       CASE WHEN character_row.`physical_attack_base` IS NULL AND character_row.`physical_attack_bonus` IS NULL THEN NULL ELSE COALESCE(character_row.`physical_attack_base`,0)+COALESCE(character_row.`physical_attack_bonus`,0) END AS `物理攻擊`,
       CASE WHEN character_row.`physical_defense_base` IS NULL AND character_row.`physical_defense_bonus` IS NULL THEN NULL ELSE COALESCE(character_row.`physical_defense_base`,0)+COALESCE(character_row.`physical_defense_bonus`,0) END AS `物理防禦`,
       CASE WHEN character_row.`magic_attack_base` IS NULL AND character_row.`magic_attack_bonus` IS NULL THEN NULL ELSE COALESCE(character_row.`magic_attack_base`,0)+COALESCE(character_row.`magic_attack_bonus`,0) END AS `法術攻擊`,
       CASE WHEN character_row.`magic_defense_base` IS NULL AND character_row.`magic_defense_bonus` IS NULL THEN NULL ELSE COALESCE(character_row.`magic_defense_base`,0)+COALESCE(character_row.`magic_defense_bonus`,0) END AS `法術防禦`,
       map_row.`name_zh_tw` AS `地圖`,character_row.`position_x` AS `X`,character_row.`position_y` AS `Y`,character_row.`enabled` AS `啟用狀態`
FROM `god2_player`.`characters` character_row
LEFT JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=character_row.`map_id`;

CREATE OR REPLACE VIEW `god2_player`.`vw_character_life_skills_full` AS
SELECT character_row.`character_id` AS `角色ID`,character_row.`name` AS `角色名稱`,life_skill_row.`name_zh_tw` AS `生活技能`,
       life_skill_row.`code` AS `生活技能代碼`,progress_row.`level` AS `等級`,progress_row.`experience` AS `經驗`,
       progress_row.`proficiency` AS `熟練度`,progress_row.`progress_value` AS `目前進度`,progress_row.`is_unlocked` AS `是否解鎖`,
       progress_row.`is_active` AS `是否啟用`,progress_row.`success_rate_bonus` AS `成功率加成`,
       progress_row.`quality_rate_bonus` AS `品質加成`,progress_row.`critical_craft_rate_bonus` AS `特殊製作率加成`,
       progress_row.`daily_use_count` AS `今日使用次數`,progress_row.`daily_use_limit` AS `每日限制`,
       progress_row.`last_used_at_utc` AS `最後使用時間`,progress_row.`admin_note` AS `管理備註`
FROM `god2_player`.`character_life_skills` progress_row
JOIN `god2_player`.`characters` character_row ON character_row.`character_id`=progress_row.`character_id`
JOIN `god2_game`.`life_skills` life_skill_row ON life_skill_row.`life_skill_id`=progress_row.`life_skill_id`;

CREATE OR REPLACE VIEW `god2_player`.`vw_character_pets_full` AS
SELECT pet_row.`pet_instance_id` AS `戰寵實例ID`,pet_row.`owner_character_id` AS `角色ID`,owner_row.`name` AS `角色名稱`,
       pet_row.`name` AS `戰寵名稱`,template_row.`name_zh_tw` AS `戰寵模板`,pet_row.`level` AS `等級`,pet_row.`experience` AS `經驗`,
       pet_row.`current_hp` AS `目前HP`,pet_row.`max_hp` AS `最大HP`,pet_row.`current_mp` AS `目前MP`,pet_row.`max_mp` AS `最大MP`,
       pet_row.`current_lifespan` AS `目前壽命`,pet_row.`max_lifespan` AS `最大壽命`,pet_row.`rebirth_count` AS `轉生次數`,
       pet_row.`remaining_stat_points` AS `剩餘配點`,pet_row.`fusion_expires_at_utc` AS `合體到期時間`,pet_row.`is_active` AS `目前選用`,pet_row.`enabled` AS `啟用狀態`
FROM `god2_player`.`character_pets` pet_row
JOIN `god2_player`.`characters` owner_row ON owner_row.`character_id`=pet_row.`owner_character_id`
JOIN `god2_game`.`pet_templates` template_row ON template_row.`pet_template_id`=pet_row.`pet_template_id`;

CREATE OR REPLACE VIEW `god2_player`.`vw_character_immortals_full` AS
SELECT immortal_row.`immortal_instance_id` AS `神仙實例ID`,immortal_row.`owner_character_id` AS `角色ID`,owner_row.`name` AS `角色名稱`,
       immortal_row.`name` AS `神仙名稱`,template_row.`name_zh_tw` AS `神仙模板`,immortal_row.`rank_name_cache` AS `階位`,
       immortal_row.`level` AS `等級`,immortal_row.`experience` AS `經驗`,immortal_row.`conversation_experience` AS `交談EXP候選值`,
       immortal_row.`current_hp` AS `目前HP`,immortal_row.`max_hp` AS `最大HP`,immortal_row.`current_mp` AS `目前MP`,immortal_row.`max_mp` AS `最大MP`,
       immortal_row.`is_active` AS `目前選用`,immortal_row.`enabled` AS `啟用狀態`
FROM `god2_player`.`character_immortals` immortal_row
JOIN `god2_player`.`characters` owner_row ON owner_row.`character_id`=immortal_row.`owner_character_id`
JOIN `god2_game`.`immortal_templates` template_row ON template_row.`immortal_template_id`=immortal_row.`immortal_template_id`;

-- Password-free roles. Administrators assign these roles to concrete accounts outside migrations.
CREATE ROLE IF NOT EXISTS `god2_runtime_role`;
CREATE ROLE IF NOT EXISTS `god2_catalog_builder_role`;
CREATE ROLE IF NOT EXISTS `god2_content_admin_role`;

GRANT SELECT ON `god2_game`.* TO `god2_runtime_role`;
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_player`.* TO `god2_runtime_role`;
GRANT SELECT ON `god2_game_meta`.`runtime_authority` TO `god2_runtime_role`;
GRANT SELECT ON `god2_game_meta`.`runtime_catalog_releases` TO `god2_runtime_role`;

GRANT SELECT ON `god2`.* TO `god2_catalog_builder_role`;
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, TRIGGER ON `god2_game`.* TO `god2_catalog_builder_role`;
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, TRIGGER ON `god2_player`.* TO `god2_catalog_builder_role`;
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, TRIGGER ON `god2_game_meta`.* TO `god2_catalog_builder_role`;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_game`.* TO `god2_content_admin_role`;
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_player`.* TO `god2_content_admin_role`;
GRANT SELECT ON `god2_game_meta`.`admin_field_locks` TO `god2_content_admin_role`;
GRANT SELECT ON `god2_game_meta`.`admin_change_audit` TO `god2_content_admin_role`;
