CREATE TABLE IF NOT EXISTS `god2_research`.`formal_table_server_correspondence`
(
    `schema_name` varchar(64) NOT NULL,
    `table_name` varchar(128) NOT NULL,
    `game_function_zh_tw` varchar(160) NOT NULL,
    `direct_runtime_reference` tinyint(1) NOT NULL DEFAULT 0,
    `tool_pipeline_reference` tinyint(1) NOT NULL DEFAULT 0,
    `server_correspondence_status_zh_tw` varchar(64) NOT NULL,
    `runtime_usage_policy_zh_tw` varchar(240) NOT NULL,
    `cleanup_decision_zh_tw` varchar(160) NOT NULL,
    `reviewed_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`schema_name`,`table_name`),
    KEY `IX_formal_table_server_correspondence_status` (`server_correspondence_status_zh_tw`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

TRUNCATE TABLE `god2_research`.`formal_table_server_correspondence`;

INSERT INTO `god2_research`.`formal_table_server_correspondence`
    (`schema_name`,`table_name`,`game_function_zh_tw`,`direct_runtime_reference`,`tool_pipeline_reference`,
     `server_correspondence_status_zh_tw`,`runtime_usage_policy_zh_tw`,`cleanup_decision_zh_tw`)
SELECT
    'god2_game',
    surface.`table_name`,
    surface.`game_function_zh_tw`,
    CASE
        WHEN surface.`table_name` IN (
            'character_creation_profiles','crafting_recipes','equipment','equipment_enhancement_materials',
            'equipment_enhancement_rates','formations','immortal_base_stats','immortal_templates',
            'item_effects','item_registry','item_set_members','item_sets','item_usage_rules','life_skills',
            'magic_skill_damage_coefficients','magic_treasures','maps','merchant_inventory','merchants',
            'monster_ai_profiles','monster_ai_rules','monster_drops','monster_skills','monster_spawns','monsters',
            'npc_dialogs','npc_spawns','npcs','pet_automatic_growth_allocations','pet_categories',
            'pet_growth_grade_rules','pet_innate_definitions','pet_skill_learning_items','pet_template_skills',
            'pet_templates','physical_skill_damage_coefficients','portals','public_beta_skill_effect_v0',
            'quest_objectives','quest_rewards','quests','skill_client_metadata','skill_client_metadata_mappings',
            'skills','status_effects','weapons'
        ) THEN 1
        ELSE 0
    END,
    CASE
        WHEN surface.`table_name` IN (
            'character_creation_profiles','client_item_effect_visuals','client_item_usage_flag_additions',
            'client_map_resource_identities','client_map_resources','container_rewards','containers',
            'crafting_recipe_materials','crafting_recipe_outputs','crafting_recipes','equipment','item_asset_mappings',
            'item_effects','item_icon_atlases','item_registry','item_set_members','item_sets','item_usage_rules',
            'items','life_skills','magic_treasures','maps','merchant_inventory','merchants','monster_drops',
            'monster_skills','monster_spawns','monsters','npc_appearance_identities','npc_spawns','npcs',
            'pet_innate_definitions','portal_resource_links','portals','quests','skills','weapons'
        ) THEN 1
        ELSE 0
    END,
    CASE
        WHEN surface.`table_name` IN (
            'character_creation_profiles','crafting_recipes','equipment','equipment_enhancement_materials',
            'equipment_enhancement_rates','formations','immortal_base_stats','immortal_templates',
            'item_effects','item_registry','item_set_members','item_sets','item_usage_rules','life_skills',
            'magic_skill_damage_coefficients','magic_treasures','maps','merchant_inventory','merchants',
            'monster_ai_profiles','monster_ai_rules','monster_drops','monster_skills','monster_spawns','monsters',
            'npc_dialogs','npc_spawns','npcs','pet_automatic_growth_allocations','pet_categories',
            'pet_growth_grade_rules','pet_innate_definitions','pet_skill_learning_items','pet_template_skills',
            'pet_templates','physical_skill_damage_coefficients','portals','public_beta_skill_effect_v0',
            'quest_objectives','quest_rewards','quests','skill_client_metadata','skill_client_metadata_mappings',
            'skills','status_effects','weapons'
        ) THEN '服務端直接對應'
        WHEN surface.`table_name` IN (
            'client_item_effect_visuals','client_item_usage_flag_additions','client_map_resource_identities',
            'client_map_resources','container_rewards','containers','crafting_recipe_materials',
            'crafting_recipe_outputs','item_asset_mappings','item_icon_atlases','items',
            'npc_appearance_identities','portal_resource_links'
        ) THEN '工具同步或可讀檢查對應'
        ELSE '正式保留待接服務端'
    END,
    CASE
        WHEN surface.`table_name` IN (
            'character_creation_profiles','crafting_recipes','equipment','equipment_enhancement_materials',
            'equipment_enhancement_rates','formations','immortal_base_stats','immortal_templates',
            'item_effects','item_registry','item_set_members','item_sets','item_usage_rules','life_skills',
            'magic_skill_damage_coefficients','magic_treasures','maps','merchant_inventory','merchants',
            'monster_ai_profiles','monster_ai_rules','monster_drops','monster_skills','monster_spawns','monsters',
            'npc_dialogs','npc_spawns','npcs','pet_automatic_growth_allocations','pet_categories',
            'pet_growth_grade_rules','pet_innate_definitions','pet_skill_learning_items','pet_template_skills',
            'pet_templates','physical_skill_damage_coefficients','portals','public_beta_skill_effect_v0',
            'quest_objectives','quest_rewards','quests','skill_client_metadata','skill_client_metadata_mappings',
            'skills','status_effects','weapons'
        ) THEN '服務端程式碼已有直接表名參照；清理時不可刪除，後續只做欄位優化與資料補證。'
        WHEN surface.`table_name` IN (
            'client_item_effect_visuals','client_item_usage_flag_additions','client_map_resource_identities',
            'client_map_resources','container_rewards','containers','crafting_recipe_materials',
            'crafting_recipe_outputs','item_asset_mappings','item_icon_atlases','items',
            'npc_appearance_identities','portal_resource_links'
        ) THEN '目前主要由工具、builder 或可讀檢查流程參照；不直接刪，後續逐項接正式 runtime 或合併到主表。'
        ELSE '正式資料有外鍵、view、測試或設計規則用途；目前不刪，標記為待接服務端或待人工決策。'
    END,
    CASE
        WHEN surface.`table_name` IN (
            'character_classes','class_level_stats','class_stat_growth','immortal_ranks','item_set_bonuses',
            'monster_combat_stat_design_rules','monster_drop_design_rules','monster_spawn_design_rules',
            'pet_growth_archetypes'
        ) THEN '保留，不列為可刪；後續優先補 runtime 讀取或合併策略。'
        WHEN surface.`table_name` IN (
            'client_item_effect_visuals','client_item_usage_flag_additions','client_map_resource_identities',
            'client_map_resources','container_rewards','containers','crafting_recipe_materials',
            'crafting_recipe_outputs','item_asset_mappings','item_icon_atlases','items',
            'npc_appearance_identities','portal_resource_links'
        ) THEN '保留，作為工具同步或可讀檢查來源。'
        ELSE '保留，正式服務端對應中。'
    END
FROM `god2_research`.`database_readability_surface` surface
WHERE surface.`schema_name` = 'god2_game'
  AND surface.`safe_to_browse` = 1;

REPLACE INTO `god2_research`.`database_traditional_chinese_surface_audit`
    (`audit_name`,`checked_scope_zh_tw`,`issue_count`,`status_zh_tw`,`cleanup_policy_zh_tw`)
SELECT
    'formal_table_server_correspondence',
    'god2_game 正式資料表與服務端程式碼對應狀態',
    SUM(CASE WHEN `server_correspondence_status_zh_tw` = '正式保留待接服務端' THEN 1 ELSE 0 END),
    CASE
        WHEN SUM(CASE WHEN `server_correspondence_status_zh_tw` = '正式保留待接服務端' THEN 1 ELSE 0 END) = 0 THEN '通過'
        ELSE '有待接項目'
    END,
    '未直接刪除有正式語意的資料表；先列出服務端對應狀態，後續依功能逐項接 runtime 或合併。'
FROM `god2_research`.`formal_table_server_correspondence`;
