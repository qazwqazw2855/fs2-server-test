using System;
using System.IO;
using Xunit;

public sealed class DatabaseConsolidationMigrationTests
{
    [Fact]
    public void Migration151ArchivesXjzEvidenceOutsideFormalGameplaySchema()
    {
        var sql = ReadSchema("151_archive_xjz_evidence_tables_out_of_formal_game_schema.sql");

        Assert.Contains("CREATE DATABASE IF NOT EXISTS `god2_research`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_xjz_current_evidence_pack_summary`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("RENAME TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`xjz_evidence_pack_sources` LIKE `god2_game`.`xjz_evidence_pack_sources`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT IGNORE INTO `god2_research`.`xjz_god_menu_evidence` SELECT * FROM `god2_game`.`xjz_god_menu_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`xjz_combat_pet_quality_star_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`xjz_item_effect_visual_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`xjz_monster_visual_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`xjz_mission_text_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`xjz_daily_mission_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_research`.`vw_xjz_current_evidence_pack_summary`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration152ArchivesVerifiedEvidenceOutsideFormalGameplaySchema()
    {
        var sql = ReadSchema("152_archive_verified_evidence_tables_out_of_formal_game_schema.sql");

        Assert.Contains("CREATE DATABASE IF NOT EXISTS `god2_research`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("RENAME TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_item_requirements_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_weapon_static_stat_evidence_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`item_requirement_evidence` LIKE `god2_game`.`item_requirement_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT IGNORE INTO `god2_research`.`item_static_permission_evidence` SELECT * FROM `god2_game`.`item_static_permission_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`equipment_set_bonus_static_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`equipment_set_item_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`equipment_slot_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`equipment_slot_verified_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`equipment_static_stat_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`item_class_restriction_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`item_gender_restriction_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`item_requirement_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`item_static_permission_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`npc_appearance_source_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`npc_coordinate_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`weapon_attack_range_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE `god2_game`.`weapon_static_stat_evidence`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration153ArchivesLegacyGod2ResearchTables()
    {
        var sql = ReadSchema("153_archive_legacy_god2_research_tables.sql");

        Assert.Contains("CREATE DATABASE IF NOT EXISTS `god2_research`", sql, StringComparison.Ordinal);
        Assert.Contains("SET FOREIGN_KEY_CHECKS=0", sql, StringComparison.Ordinal);
        Assert.Contains("SET FOREIGN_KEY_CHECKS=1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("RENAME TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE PROCEDURE `god2`.`archive_legacy_god2_research_table`", sql, StringComparison.Ordinal);
        Assert.Contains("CALL `god2`.`archive_legacy_god2_research_table`('content_raw_records', 'content_raw_records')", sql, StringComparison.Ordinal);
        Assert.Contains("CALL `god2`.`archive_legacy_god2_research_table`('content_staging_records', 'content_staging_records')", sql, StringComparison.Ordinal);
        Assert.Contains("CALL `god2`.`archive_legacy_god2_research_table`('npc_coordinate_evidence', 'legacy_god2_npc_coordinate_evidence')", sql, StringComparison.Ordinal);
        Assert.Contains("CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_evidence_sources', 'packet_capture_evidence_sources')", sql, StringComparison.Ordinal);
        Assert.Contains("CALL `god2`.`archive_legacy_god2_research_table`('quest_objective_candidates', 'quest_objective_candidates')", sql, StringComparison.Ordinal);
        Assert.Contains("CALL `god2`.`archive_legacy_god2_research_table`('verified_monster_observations', 'verified_monster_observations')", sql, StringComparison.Ordinal);
        Assert.Contains("CALL `god2`.`archive_legacy_god2_research_table`('verified_skill_observations', 'verified_skill_observations')", sql, StringComparison.Ordinal);
        Assert.Contains("DROP PROCEDURE IF EXISTS `god2`.`archive_legacy_god2_research_table`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration154ArchivesContentFieldEvidenceOutsideLegacyGod2()
    {
        var sql = ReadSchema("154_archive_content_field_evidence.sql");

        Assert.Contains("CREATE DATABASE IF NOT EXISTS `god2_research`", sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE `god2_game_meta`.`field_mappings`", sql, StringComparison.Ordinal);
        Assert.Contains("SET `source_schema`='god2_research'", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_research`.`content_field_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("RENAME TABLE `god2`.`content_field_evidence` TO `god2_research`.`content_field_evidence`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT IGNORE INTO `god2_research`.`content_field_evidence`", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration155ArchivesRecoveryValidationTablesOutsideLegacyGod2()
    {
        var sql = ReadSchema("155_archive_recovery_validation_tables.sql");

        Assert.Contains("CREATE DATABASE IF NOT EXISTS `god2_research`", sql, StringComparison.Ordinal);
        Assert.Contains("RENAME TABLE", sql, StringComparison.Ordinal);
        Assert.Contains("`god2`.`content_client_table_layouts` TO `god2_research`.`content_client_table_layouts`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2`.`content_localization_audit` TO `god2_research`.`content_localization_audit`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2`.`content_source_inventory` TO `god2_research`.`content_source_inventory`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2`.`content_validated_records` TO `god2_research`.`content_validated_records`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2`.`content_validation_results` TO `god2_research`.`content_validation_results`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT IGNORE INTO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration156GrantsReadOnlyResearchSchemaToRuntimeAndBuilder()
    {
        var sql = ReadSchema("156_grant_readonly_research_schema_to_runtime_tools.sql");

        Assert.Contains("GRANT SELECT ON `god2_research`.* TO 'god2_server'@'localhost'", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT ON `god2_research`.* TO 'god2_server'@'127.0.0.1'", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT ON `god2_research`.* TO 'god2_catalog_builder'@'localhost'", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT ON `god2_research`.* TO 'god2_catalog_builder'@'127.0.0.1'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("GRANT INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT DELETE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration157ArchivesPhase3ManifestTablesOutsideLegacyGod2()
    {
        var sql = ReadSchema("157_archive_phase3_manifest_tables_out_of_formal_god2.sql");

        Assert.Contains("CREATE DATABASE IF NOT EXISTS `god2_research`", sql, StringComparison.Ordinal);
        Assert.Contains("SET FOREIGN_KEY_CHECKS=0", sql, StringComparison.Ordinal);
        Assert.Contains("RENAME TABLE", sql, StringComparison.Ordinal);
        Assert.Contains("`god2`.`content_phase3_field_closure` TO `god2_research`.`content_phase3_field_closure`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2`.`content_phase3_promotions` TO `god2_research`.`content_phase3_promotions`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2`.`content_production_manifest` TO `god2_research`.`content_production_manifest`", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_phase3_field_closure` TO 'god2_server'@'localhost'", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_phase3_promotions` TO 'god2_server'@'localhost'", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_production_manifest` TO 'god2_server'@'localhost'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT IGNORE INTO", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration158MovesMonsterCaptureEligibilityViewOutOfFormalGameplaySchema()
    {
        var sql = ReadSchema("158_move_monster_capture_eligibility_view_to_research.sql");

        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_monster_capture_eligibility_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_research`.`vw_monster_capture_eligibility_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("monster_row.`monster_id` AS `monster_id`", sql, StringComparison.Ordinal);
        Assert.Contains("monster_row.`capture_eligibility` AS `capture_eligibility`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("鎬墿ID", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("鍙崟鎹?", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration159DropsOnlyVerifiedEmptyFormalViews()
    {
        var sql = ReadSchema("159_drop_empty_formal_views.sql");

        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_verified_skill_target_shapes`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_monster_drops_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_monster_drops_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_monster_spawns_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_monster_spawns_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_monster_skills_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_monster_skills_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_quest_rewards_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_status_effects_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_equipment_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_player`.`vw_character_equipment_enhancement`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_player`.`vw_character_pet_skills_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_player`.`vw_character_pets_full`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vw_items_full", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("vw_monsters_readable", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("vw_pet_templates_readable", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration160DropsOnlyEmptyNonRuntimeExtensionTables()
    {
        var sql = ReadSchema("160_drop_empty_non_runtime_extension_tables.sql");

        Assert.Contains("DROP TABLE IF EXISTS `god2_player`.`character_formation_members`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_player`.`character_formations`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_player`.`character_mounts`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_player`.`character_quests`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_player`.`character_skills`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_player`.`character_immortal_skills`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`mount_template_skills`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`mount_templates`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`formation_bonuses`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`formation_slots`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`combine_recipe_materials`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`consumables`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`skill_effects`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`skill_levels`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`immortal_template_skills`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`npc_dialog_options`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`quest_prerequisites`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2_game`.`containers`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2_game`.`container_rewards`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration161DropsOnlyEmptyLegacyGod2Tables()
    {
        var sql = ReadSchema("161_drop_empty_legacy_god2_tables.sql");

        Assert.Contains("DROP TABLE IF EXISTS `god2`.`battle_audit`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`quest_audit`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`battle_pets`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`immortals`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`reward_items`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`rewards`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2_game`.`containers`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2`.`battle_instances`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2`.`quest_instances`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2`.`skill_executions`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration162ArchivesRuntimeSourceObservationTablesOutsideFormalGod2()
    {
        var sql = ReadSchema("162_archive_runtime_source_observation_tables.sql");

        Assert.Contains("CREATE DATABASE IF NOT EXISTS `god2_research`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`advanced_headless_verification_transactions`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`official_skill_book_catalog` LIKE `god2`.`official_skill_book_catalog`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT IGNORE INTO `god2_research`.`latest_controlled_gameplay_observation` SELECT * FROM `god2`.`latest_controlled_gameplay_observation`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`verified_battle_action_semantics`", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`official_skill_book_catalog` TO 'god2_server'@'localhost'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2_player`.`character_lifecycle_idempotency`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2_player`.`world_interaction_idempotency`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2_player`.`inventory_item_identity_sequence`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration163DropsLegacyDuplicatesAndEmptyCatalogPlaceholdersOnly()
    {
        var sql = ReadSchema("163_drop_legacy_runtime_duplicates_and_empty_catalog_placeholders.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`character_map_identity_migrations` LIKE `god2`.`character_map_identity_migrations`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT IGNORE INTO `god2_research`.`legacy_god2_inventory_item_identity_sequence` SELECT * FROM `god2`.`inventory_item_identity_sequence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`battle_actor_recovery`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`character_lifecycle_idempotency`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`world_interaction_idempotency`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`combine_recipes`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`level_experience`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`map_rules`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2_player`.`character_lifecycle_idempotency`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2_player`.`world_interaction_idempotency`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2_player`.`inventory_item_identity_sequence`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration164SplitsFinalGameplayCatalogEvidenceColumnsOutOfFormalTables()
    {
        var sql = ReadSchema("164_split_final_gameplay_catalog_evidence_columns.sql");

        Assert.Contains("`god2_research`.`class_stat_growth_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`immortal_rank_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`pet_growth_archetype_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_official_character_attribute_growth`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_immortal_ranks_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `exact_client_source_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_article_sn`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_official_character_attribute_growth`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_immortal_ranks_readable`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("MIN(`growth`.`evidence_status`)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("rank_row.`exact_client_source_sha256` AS `exact_client_source_sha256`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2_game`.`class_stat_growth`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2_game`.`immortal_ranks`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE IF EXISTS `god2_game`.`pet_growth_archetypes`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration165SplitsCharacterCreationProfileEvidenceOutOfFormalTables()
    {
        var sql = ReadSchema("165_split_character_creation_profile_evidence.sql");

        Assert.Contains("`god2_research`.`legacy_character_creation_profile_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`character_creation_profile_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_character_creation_profiles_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `EvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `EvidenceReference`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_reference`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `admin_note`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_character_creation_profiles_full`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("profile.`evidence_status` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("profile.`admin_note` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration166SplitsMagicSkillDamageEvidenceOutOfFormalFormulaTable()
    {
        var sql = ReadSchema("166_split_magic_skill_damage_evidence.sql");

        Assert.Contains("`god2_research`.`magic_skill_damage_coefficient_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_magic_skill_damage_coefficients_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `meditation_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `origin_version`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `region_compatibility_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_url`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `admin_note`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("coefficient.`evidence_status` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("coefficient.`source_url` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration167SplitsPhysicalSkillDamageEvidenceOutOfFormalFormulaTable()
    {
        var sql = ReadSchema("167_split_physical_skill_damage_evidence.sql");

        Assert.Contains("`god2_research`.`physical_skill_damage_coefficient_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_physical_skill_damage_coefficients_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_physical_skill_coefficients_sources`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `independent_source_count`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `primary_source_url`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `corroborating_source_url`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `admin_note`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("coefficient.`evidence_status` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("coefficient.`primary_source_url` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration168SplitsEquipmentEnhancementEvidenceOutOfFormalRuleTables()
    {
        var sql = ReadSchema("168_split_equipment_enhancement_evidence.sql");

        Assert.Contains("`god2_research`.`equipment_enhancement_material_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`equipment_enhancement_rate_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_equipment_enhancement_rules_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_equipment_enhancement_material_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_equipment_enhancement_rate_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_reference_zh_tw`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("material.`evidence_status` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("rate.`source_reference_zh_tw` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration169SplitsPetGrowthEvidenceOutOfFormalGrowthTables()
    {
        var sql = ReadSchema("169_split_pet_growth_evidence.sql");

        Assert.Contains("`god2_research`.`pet_growth_grade_rule_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`pet_automatic_growth_allocation_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_pet_growth_grades_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_pet_category_growth_values_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_pet_growth_grade_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_pet_auto_growth_values`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_value_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_article_sn`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_url`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `admin_note`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("allocation.`evidence_status` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("allocation.`source_url` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration170SplitsBattlePetCatalogEvidenceOutOfFormalPetTables()
    {
        var sql = ReadSchema("170_split_battle_pet_catalog_evidence.sql");

        Assert.Contains("`god2_research`.`pet_category_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`pet_template_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`pet_skill_learning_item_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_pet_categories_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_pet_templates_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_pet_templates_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_pet_categories_source`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_pet_categories_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_pet_templates_source_article`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_article_sn`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_section_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `admin_note`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("template_row.`evidence_status` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("category_row.`source_article_sn`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("mapping.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration171SplitsImmortalTemplateAndBaselineEvidenceOutOfFormalTables()
    {
        var sql = ReadSchema("171_split_immortal_template_and_baseline_evidence.sql");

        Assert.Contains("`god2_research`.`immortal_template_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`immortal_base_stat_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_immortal_templates_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_immortal_level_one_baseline`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_reference`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `admin_note`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_immortal_templates_full`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_immortal_level_one_baseline`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("stats_row.`evidence_status` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("stats_row.`evidence_reference` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration172SplitsItemCatalogEvidenceOutOfFormalItemTables()
    {
        var sql = ReadSchema("172_split_item_catalog_evidence.sql");

        Assert.Contains("`god2_research`.`item_catalog_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_all_item_definitions`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_items_classified`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_item_catalog_health`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `admin_note`", sql, StringComparison.Ordinal);
        Assert.Contains("'item_registry'", sql, StringComparison.Ordinal);
        Assert.Contains("'magic_treasures'", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_all_item_definitions`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_items_classified`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("item_row.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`evidence_status`,`enabled`,`admin_note`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration173SplitsMonsterCatalogEvidenceOutOfFormalMonsterTables()
    {
        var sql = ReadSchema("173_split_monster_catalog_evidence.sql");

        Assert.Contains("`god2_research`.`monster_catalog_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("'monsters'", sql, StringComparison.Ordinal);
        Assert.Contains("'monster_spawns'", sql, StringComparison.Ordinal);
        Assert.Contains("'monster_drops'", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_monsters_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_monster_spawns_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_monster_drops_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `admin_note`", sql, StringComparison.Ordinal);
        Assert.Contains("monster_row.`max_mp` AS `最大MP`", sql, StringComparison.Ordinal);
        Assert.Contains("drop_row.`drop_rate` AS `掉落機率`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("monster_row.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("spawn_row.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("drop_row.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MonsterRuntimeNoLongerReadsFormalMonsterCatalogEvidence()
    {
        var catalogSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbCanonicalCatalogRepositories.cs"));
        var runtimeSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbRuntimeData.cs"));

        Assert.Contains("SELECT `monster_id`,`code`,`name_zh_tw`,`level`,`max_hp`,`max_mp`", catalogSource, StringComparison.Ordinal);
        Assert.Contains("'canonical-monster-spawn',`enabled` FROM `god2_game`.`monster_spawns`", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`magic_defense`,`evidence_status`,`updated_at_utc`", catalogSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`spawn_count`,`evidence_status`,`enabled` FROM `god2_game`.`monster_spawns`", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`vw_all_item_definitions` WHERE `enabled`=1 AND `evidence_status`", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`monsters` WHERE `enabled`=1 AND `evidence_status`", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration174SplitsItemUsageAndEffectEvidenceOutOfFormalRuntimeTables()
    {
        var sql = ReadSchema("174_split_item_usage_and_effect_evidence.sql");

        Assert.Contains("`god2_research`.`item_usage_rule_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`item_effect_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`client_item_effect_visual_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`client_item_usage_flag_addition_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_item_effects_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_items_classified`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_item_usage_rules_field_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_item_effects_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `field_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `text_rule_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `activation_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("effect_row.`numeric_value` AS `效果數值`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("rule_row.`text_rule_evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("effect_row.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItemRuntimeNoLongerReadsFormalUsageOrEffectEvidenceColumns()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbGameplayInventoryRepository.cs"));

        Assert.Contains("rule_row.`enabled`=1 AND rule_row.`runtime_eligible`=1 THEN rule_row.`class_restriction_zh_tw`", source, StringComparison.Ordinal);
        Assert.Contains("'runtime-item-effect' AS `EvidenceStatus`", source, StringComparison.Ordinal);
        Assert.Contains("'god2_game.item_effects' AS `Source`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("rule_row.`text_rule_evidence_status`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`evidence_status` AS `EvidenceStatus`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`source_reference_zh_tw` AS `Source`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration175SplitsMerchantInventoryEvidenceOutOfFormalShopInventory()
    {
        var sql = ReadSchema("175_split_merchant_inventory_evidence.sql");

        Assert.Contains("`god2_research`.`merchant_inventory_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_merchant_inventory_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `admin_note`", sql, StringComparison.Ordinal);
        Assert.Contains("inventory_row.`selling_price` AS `售價`", sql, StringComparison.Ordinal);
        Assert.Contains("inventory_row.`purchasing_price` AS `回收價`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("inventory_row.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("inventory_row.`admin_note`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration176SplitsQuestCatalogEvidenceOutOfFormalQuestTables()
    {
        var sql = ReadSchema("176_split_quest_catalog_evidence.sql");

        Assert.Contains("`god2_research`.`quest_catalog_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`quest_objective_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`quest_reward_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_quests_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_quest_objectives_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_quest_rewards_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `admin_note`", sql, StringComparison.Ordinal);
        Assert.Contains("quest_row.`description_zh_tw` AS `任務描述`", sql, StringComparison.Ordinal);
        Assert.Contains("objective_row.`required_quantity` AS `需要數量`", sql, StringComparison.Ordinal);
        Assert.Contains("reward_row.`experience` AS `經驗`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("quest_row.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("objective_row.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("reward_row.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuestRuntimeNoLongerReadsFormalQuestEvidenceColumns()
    {
        var catalogSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbCanonicalCatalogRepositories.cs"));
        var gameplaySource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbCanonicalGameplayContentRuntime.cs"));
        var runtimeDataSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbRuntimeData.cs"));

        Assert.Contains("SELECT `quest_id`,`code`,`name_zh_tw`,`quest_type`", catalogSource, StringComparison.Ordinal);
        Assert.Contains("quest_row.`description_zh_tw`,'Verified'", gameplaySource, StringComparison.Ordinal);
        Assert.Contains("`description_zh_tw`,'Verified'", gameplaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("`repeatable`,`evidence_status`", catalogSource, StringComparison.Ordinal);
        Assert.DoesNotContain("quest_row.`evidence_status`", gameplaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("`description_zh_tw`,`evidence_status`", gameplaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("`quests` WHERE `enabled`=1 AND `evidence_status`", runtimeDataSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration177SplitsSkillCatalogEvidenceOutOfFormalSkillTable()
    {
        var sql = ReadSchema("177_split_skill_catalog_evidence.sql");

        Assert.Contains("`god2_research`.`skill_catalog_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_skills_full`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_skill_client_metadata_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `client_metadata_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `admin_note`", sql, StringComparison.Ordinal);
        Assert.Contains("skill_row.`mp_cost` AS `MP消耗`", sql, StringComparison.Ordinal);
        Assert.Contains("skill_row.`target_scope_zh_tw` AS `作用範圍`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("skill_row.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("skill_row.`client_metadata_evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SkillRuntimeNoLongerReadsFormalSkillEvidenceColumns()
    {
        var catalogSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbCanonicalCatalogRepositories.cs"));
        var runtimeDataSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbRuntimeData.cs"));

        Assert.Contains("SELECT `skill_id`,`code`,`name_zh_tw`,`skill_family`", catalogSource, StringComparison.Ordinal);
        Assert.Contains("SELECT 0;", runtimeDataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`consumes_turn`,`evidence_status`", catalogSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`skills` WHERE `enabled`=1 AND `evidence_status`", runtimeDataSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration178SplitsWorldEntryEvidenceOutOfFormalRuntimeTables()
    {
        var sql = ReadSchema("178_split_world_entry_evidence.sql");

        Assert.Contains("`god2_research`.`map_catalog_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`npc_catalog_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`npc_spawn_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`portal_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS `ix_portals_evidence_gate`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_portals_evidence_gate`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_portals_enabled`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `identity_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `wire_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_source_type`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT `ck_portals_enabled` CHECK (`enabled` IN (0,1))", sql, StringComparison.Ordinal);
        Assert.Contains("spawn_row.`position_x` AS", sql, StringComparison.Ordinal);
        Assert.Contains("npc_row.`interaction_family` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("spawn_row.`wire_evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("npc_row.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("map_row.`identity_evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorldEntryRuntimeNoLongerReadsFormalMapNpcOrPortalEvidenceColumns()
    {
        var catalogSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbCanonicalCatalogRepositories.cs"));
        var runtimeDataSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbRuntimeData.cs"));

        Assert.Contains("SELECT `npc_id`,`code`,`name_zh_tw`,`npc_type`,`default_dialog_id`,`merchant_id`", catalogSource, StringComparison.Ordinal);
        Assert.Contains("'canonical-map'", runtimeDataSource, StringComparison.Ordinal);
        Assert.Contains("'canonical-npc-spawn-wire'", runtimeDataSource, StringComparison.Ordinal);
        Assert.Contains("'canonical-map-identity','canonical-map-coordinate'", runtimeDataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`merchant_id`,`evidence_status`", catalogSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`minimum_y`,`maximum_y`,`identity_evidence_status`", catalogSource, StringComparison.Ordinal);
        Assert.DoesNotContain("spawn.`wire_evidence_status`", runtimeDataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("spawn.`identity_evidence_status`", runtimeDataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`identity_evidence_status`,`coordinate_evidence_status`,`enabled`", runtimeDataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("spawn_row.`wire_evidence_status`", runtimeDataSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration179SplitsClientResourceLinkEvidenceOutOfFormalResourceTables()
    {
        var sql = ReadSchema("179_split_client_resource_link_evidence.sql");

        Assert.Contains("`god2_research`.`client_map_resource_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`client_map_resource_identity_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`portal_resource_link_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`npc_appearance_identity_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_client_map_resources_gate`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_portal_resource_links_enabled`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `raw_fields_json`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `identity_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT `ck_portal_resource_links_enabled` CHECK (`enabled` IN (0,1))", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`map_identity_evidence_status` IN", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`trigger_evidence_status` IN", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration180SplitsSkillRuntimeEvidenceOutOfFormalSkillTables()
    {
        var sql = ReadSchema("180_split_skill_runtime_evidence.sql");

        Assert.Contains("`god2_research`.`status_effect_catalog_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`crafting_recipe_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`skill_client_metadata_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`skill_client_metadata_mapping_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`public_beta_skill_effect_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("CHANGE COLUMN `level_candidate` `skill_level` int NULL", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `target_shape_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `packet_evidence_boundary`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("`skill_level` AS `技能階級`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`level_candidate` AS `技能階級`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`packet_evidence_boundary` AS `證據邊界`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SkillRuntimeNoLongerReadsFormalSkillMetadataEvidenceColumns()
    {
        var metadataSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbClientSkillMetadataCatalogRepository.cs"));
        var skillRuntimeSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbSkillRuntimeStore.cs"));
        var statusRuntimeSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbStatusEffectRuntimeStore.cs"));
        var catalogSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbCanonicalCatalogRepositories.cs"));

        Assert.Contains("FormalRuntimeCatalog", metadataSource, StringComparison.Ordinal);
        Assert.Contains("`skill_level` AS `level_candidate`", skillRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("god2_game.public_beta_skill_effect_v0", skillRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("god2_game.public_beta_skill_effect_v0", statusRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("moved-to-god2_research.public_beta_skill_effect_evidence", skillRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("moved-to-god2_research.public_beta_skill_effect_evidence", statusRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`target_shape_evidence_status`", metadataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`evidence_status`, `live_verified`", metadataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`packet_evidence_boundary`,`source_sha256`", skillRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`packet_evidence_boundary`,`source_sha256`", statusRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`currency_cost`,`evidence_status`", catalogSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration181SplitsLegacySkillEvidenceOutOfFormalSkillTables()
    {
        var sql = ReadSchema("181_split_legacy_skill_evidence.sql");

        Assert.Contains("`god2_research`.`legacy_skill_catalog_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_skill_content_profile_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_skill_semantic_profile_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS `IX_SkillContentProfiles_Family`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS `IX_SkillSemanticProfiles_Identity`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadJson`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadSha256`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `SkillFamilyEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `SemanticEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacySkillRuntimeAndImporterNoLongerDependOnFormalSkillPayloadColumns()
    {
        var runtimeSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbSkillRuntimeStore.cs"));
        var importSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "OfficialImports",
            "OfficialDataImport.cs"));

        Assert.Contains("legacy-skill-catalog-formal", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_skill_catalog_evidence`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`PayloadJson`, `PayloadSha256`", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `skills` (`Id`, `Code`, `Name`, `MaxLevel`, `RequiredLevel`, `RecoveryStatus`, `SourceReference`, `PayloadJson`", importSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration182SplitsLegacyOfficialRuntimePayloadsOutOfFormalTables()
    {
        var sql = ReadSchema("182_split_legacy_official_runtime_payloads.sql");

        Assert.Contains("`god2_research`.`legacy_official_runtime_payload_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT 'items',CAST(`Id` AS char)", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT 'localization_entries',CONCAT(`Language`,':',`TextKey`)", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `RawMetadata`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `CoordinateEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadJson`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadSha256`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfficialRuntimeImporterWritesPayloadsToResearchInsteadOfFormalLegacyTables()
    {
        var importSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "OfficialImports",
            "OfficialDataImport.cs"));

        Assert.Contains("UpsertLegacyOfficialRuntimePayloadAsync", importSource, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_official_runtime_payload_evidence`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ADD COLUMN IF NOT EXISTS `PayloadJson`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ADD COLUMN IF NOT EXISTS `PayloadSha256`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`SourceReference`, `PayloadJson`, `PayloadSha256`, `ImportedAtUtc`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `maps` (`Id`, `Code`, `Name`, `Width`, `Height`, `CreatedAtUtc`, `UpdatedAtUtc`, `RecoveryStatus`, `SourceReference`, `PayloadJson`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `items` (`Id`, `Code`, `Name`, `ItemType`, `MaxStack`, `SellPrice`, `RecoveryStatus`, `SourceReference`, `PayloadJson`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `npcs` (`Id`, `Code`, `Name`, `MapId`, `PositionX`, `PositionY`, `RecoveryStatus`, `SourceReference`, `PayloadJson`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `monsters` (`Id`, `Code`, `Name`, `Level`, `MaxHp`, `Attack`, `Defense`, `RecoveryStatus`, `SourceReference`, `PayloadJson`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `quests` (`Id`, `Code`, `Name`, `RequiredLevel`, `StartNpcId`, `EndNpcId`, `RecoveryStatus`, `SourceReference`, `PayloadJson`", importSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration183SplitsOfficialImportRecordPayloadsOutOfFormalLedger()
    {
        var sql = ReadSchema("183_split_official_import_record_payloads.sql");

        Assert.Contains("`god2_research`.`official_import_record_payload_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT `Category`,`RecordKey`,`PayloadJson`,`PayloadSha256`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadJson`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadSha256`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfficialImportLedgerWritesPayloadsToResearchInsteadOfFormalLedger()
    {
        var importSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "OfficialImports",
            "OfficialDataImport.cs"));

        Assert.Contains("UpsertOfficialImportRecordPayloadAsync", importSource, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`official_import_record_payload_evidence`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`PayloadJson` longtext NOT NULL CHECK", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`OriginalId`, `SourceHash`, `PayloadJson`, `PayloadSha256`, `ImportedAtUtc`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`PayloadJson` = VALUES(`PayloadJson`)", importSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration207MovesOfficialImportLedgerOutOfFormalRuntimeSchema()
    {
        var sql = ReadSchema("207_move_official_import_ledger_to_research_schema.sql");
        var importSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "OfficialImports",
            "OfficialDataImport.cs"));

        Assert.Contains("`god2_research`.`official_import_categories`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`official_import_records`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`official_import_reference_issues`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`official_import_reference_issues`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`official_import_records`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`official_import_categories`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_research`.`official_import_categories`", importSource, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_research`.`official_import_records`", importSource, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_research`.`official_import_reference_issues`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `official_import_categories`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `official_import_records`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `official_import_reference_issues`", importSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration209MovesCatalogSyncMetadataOutOfFormalMetadataSchema()
    {
        var sql = ReadSchema("209_move_catalog_sync_metadata_to_research_schema.sql");
        var builderSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameCatalogBuilder",
            "Program.cs"));

        Assert.Contains("`god2_research`.`field_mappings`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`sync_runs`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`field_provenance`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`sync_conflicts`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`unmapped_fields`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game_meta`.`field_mappings`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game_meta`.`sync_runs`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game_meta`.`field_provenance`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game_meta`.`sync_conflicts`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game_meta`.`unmapped_fields`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`field_mappings`", builderSource, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`sync_runs`", builderSource, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`field_provenance`", builderSource, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`sync_conflicts`", builderSource, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`unmapped_fields`", builderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`god2_game_meta`.`field_mappings`", builderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`god2_game_meta`.`sync_runs`", builderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`god2_game_meta`.`field_provenance`", builderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`god2_game_meta`.`sync_conflicts`", builderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`god2_game_meta`.`unmapped_fields`", builderSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration184SplitsLegacyWorldAndSpawnEvidenceOutOfFormalLegacyTables()
    {
        var sql = ReadSchema("184_split_legacy_world_spawn_evidence.sql");

        Assert.Contains("`god2_research`.`legacy_client_map_identity_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_npc_client_identity_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_npc_spawn_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_monster_spawn_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_monster_spawn_semantic_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `CK_ClientMapIdentities_ProductionGate`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT `CK_ClientMapIdentities_RuntimeGate`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `IdentityEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `CoordinateEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `WireEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `OpaqueTemplateSha256`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `EvidenceReference`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `FieldEvidenceJson`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyContentReleaseCoverageNoLongerDependsOnWorldEvidenceColumns()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbPromotedGameplayContentRuntime.cs"));

        Assert.Contains("`client_map_identities` WHERE `ProductionEnabled`=1 AND `CoordinateScaleX`>0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`client_map_identities` WHERE `ProductionEnabled`=1 AND `IdentityEvidenceStatus`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration185SplitsLegacyProfileRelationshipEvidenceOutOfFormalLegacyTables()
    {
        var sql = ReadSchema("185_split_legacy_profile_relationship_evidence.sql");

        Assert.Contains("`god2_research`.`legacy_item_content_profile_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_monster_drop_relationship_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_container_item_relationship_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_equipment_set_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_quest_content_profile_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS `IX_MonsterDropRelationships_State`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `ProbabilityEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `ChanceEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `ObjectiveEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `EffectEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `IdentityEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PromotedContentRuntimeNoLongerDependsOnLegacyProfileRelationshipEvidenceColumns()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbPromotedGameplayContentRuntime.cs"));

        Assert.Contains("`EffectiveDropChance`>=0", source, StringComparison.Ordinal);
        Assert.Contains("'formal-runtime-quest-objectives'", source, StringComparison.Ordinal);
        Assert.Contains("'formal-runtime-enabled'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`ChanceEvidenceStatus`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`ProbabilityEvidenceStatus`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("profile.`IdentityEvidenceStatus`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("member.`EvidenceStatus`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`EffectEvidenceStatus`='Verified'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration186SplitsLegacyMonsterSemanticEvidenceOutOfFormalMonsterProfiles()
    {
        var sql = ReadSchema("186_split_legacy_monster_semantic_evidence.sql");

        Assert.Contains("`god2_research`.`legacy_monster_semantic_profile_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`level_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `LevelEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `StatEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `RewardEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MonsterSemanticRuntimeAndRecoveryNoLongerDependOnLegacyEvidenceColumns()
    {
        var runtimeSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbPromotedGameplayContentRuntime.cs"));
        var recoverySource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase3SemanticRecovery.cs"));
        var probeSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.AutomationEnvironmentProbe",
            "Program.cs"));

        Assert.Contains("`PhysicalAttack` IS NOT NULL", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("CASE WHEN mp.`Level` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END", recoverySource, StringComparison.Ordinal);
        Assert.DoesNotContain("mp.`LevelEvidenceStatus`", recoverySource, StringComparison.Ordinal);
        Assert.DoesNotContain("semantic.`StatEvidenceStatus`", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`RewardEvidenceStatus` IN", probeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration187SplitsContentRuntimeReleaseEvidenceOutOfFormalReleaseTable()
    {
        var sql = ReadSchema("187_split_content_runtime_release_evidence.sql");

        Assert.Contains("`god2_research`.`content_runtime_release_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT `ReleaseId`,`SourceRunId`,`EvidenceReference`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `EvidenceReference`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PromotedContentRuntimeReadsFormalRuntimeCatalogMetaInsteadOfLegacyContentRelease()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbPromotedGameplayContentRuntime.cs"));

        Assert.Contains("`god2_game_meta`.`runtime_catalog_releases`", source, StringComparison.Ordinal);
        Assert.Contains("release_row.`catalog_release_id`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("release_row.`EvidenceReference`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `content_runtime_releases`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE `content_runtime_releases`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration188RenamesMonsterCombatRuntimeMetadataForFormalSchema()
    {
        var sql = ReadSchema("188_rename_monster_combat_runtime_metadata.sql");

        Assert.Contains("`RuntimeMetadataJson` longtext", sql, StringComparison.Ordinal);
        Assert.Contains("`RuntimeMetadataJson`=`RawMetadata`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `RawMetadata`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CombatMutationStoreUsesFormalRuntimeMetadataColumn()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbCombatMutationStore.cs"));

        Assert.Contains("`RuntimeMetadataJson`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`RawMetadata`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration189RenamesGameMetaSyncObservationColumnsForFormalSchema()
    {
        var sql = ReadSchema("189_rename_game_meta_sync_observation_columns.sql");

        Assert.Contains("`blocked_source_value_count` bigint", sql, StringComparison.Ordinal);
        Assert.Contains("`blocked_source_value_count`=`blocked_candidate_count`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `blocked_candidate_count`", sql, StringComparison.Ordinal);
        Assert.Contains("`proposed_source_value` longtext", sql, StringComparison.Ordinal);
        Assert.Contains("`proposed_source_value`=`candidate_value`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `candidate_value`", sql, StringComparison.Ordinal);
        Assert.Contains("`observation_status` varchar(30)", sql, StringComparison.Ordinal);
        Assert.Contains("`observation_status`=`evidence_level`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_level`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameCatalogBuilderWritesRenamedGameMetaSyncColumns()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameCatalogBuilder",
            "Program.cs"));

        Assert.Contains("`blocked_source_value_count`", source, StringComparison.Ordinal);
        Assert.Contains("`proposed_source_value`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`blocked_candidate_count`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`candidate_value`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration190RenamesRuntimeVerificationCaseHashForFormalSchema()
    {
        var sql = ReadSchema("190_rename_runtime_verification_case_hash.sql");

        Assert.Contains("`verification_case_sha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`verification_case_sha256`=`payload_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `payload_sha256`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration191RenamesBattleV2DurabilityPayloadColumnsForFormalSchema()
    {
        var sql = ReadSchema("191_rename_battle_v2_durability_payload_columns.sql");

        Assert.Contains("`CommandSchemaVersion` int", sql, StringComparison.Ordinal);
        Assert.Contains("`CanonicalCommandJson` longtext", sql, StringComparison.Ordinal);
        Assert.Contains("`CommandFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`SnapshotSchemaVersion` int", sql, StringComparison.Ordinal);
        Assert.Contains("`EventSchemaVersion` int", sql, StringComparison.Ordinal);
        Assert.Contains("`EventFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`FinalizationFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `CanonicalPayload`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadVersion`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadHash`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration192RenamesStatusEffectIdempotencyHashColumnsForFormalSchema()
    {
        var sql = ReadSchema("192_rename_status_effect_idempotency_hash_columns.sql");

        Assert.Contains("`ApplicationFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`ApplicationFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("`RemovalFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`RemovalFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("`OperationFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`OperationFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadHash`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration193RenamesSkillRuntimeHashColumnsForFormalSchema()
    {
        var sql = ReadSchema("193_rename_skill_runtime_hash_columns.sql");

        Assert.Contains("`ExecutionFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`ExecutionFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("`OperationFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`OperationFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadHash`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration194RenamesQuestRuntimeHashColumnsForFormalSchema()
    {
        var sql = ReadSchema("194_rename_quest_runtime_hash_columns.sql");

        Assert.Contains("`OperationFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`OperationFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("`ProgressFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`ProgressFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("`RewardFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`RewardFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadHash`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration195RenamesFinalRuntimeIdempotencyHashColumnsForFormalSchema()
    {
        var sql = ReadSchema("195_rename_final_runtime_idempotency_hash_columns.sql");

        Assert.Contains("`ActionFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`ActionFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("`OperationFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`OperationFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("`CombatFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`CombatFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("`TransactionFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`TransactionFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadHash`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeStoresNoLongerUsePayloadHashAsADatabaseColumnName()
    {
        foreach (var sourcePath in new[]
                 {
                     Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Persistence", "MariaDbBattleArchitectureV2Store.cs"),
                     Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Persistence", "MariaDbCombatMutationStore.cs"),
                     Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Persistence", "MariaDbGameplayInventoryRepository.cs"),
                     Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Persistence", "MariaDbTurnBasedBattleStore.cs")
                 })
        {
            var source = File.ReadAllText(sourcePath);
            Assert.DoesNotContain("`PayloadHash`", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Migration196RenamesPlayerSideOperationFingerprintColumnsForFormalSchema()
    {
        var sql = ReadSchema("196_rename_player_side_operation_fingerprint_columns.sql");

        Assert.Contains("`lifecycle_fingerprint_sha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`lifecycle_fingerprint_sha256`=`payload_hash`", sql, StringComparison.Ordinal);
        Assert.Contains("`enhancement_fingerprint_sha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`enhancement_fingerprint_sha256`=`payload_hash`", sql, StringComparison.Ordinal);
        Assert.Contains("`item_use_fingerprint_sha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`item_use_fingerprint_sha256`=`payload_hash`", sql, StringComparison.Ordinal);
        Assert.Contains("`operation_fingerprint_sha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`operation_fingerprint_sha256`=`payload_hash`", sql, StringComparison.Ordinal);
        Assert.Contains("`operation_fingerprint` varchar(200)", sql, StringComparison.Ordinal);
        Assert.Contains("`operation_fingerprint`=`payload_fingerprint`", sql, StringComparison.Ordinal);
        Assert.Contains("`InteractionFingerprintSha256` char(64)", sql, StringComparison.Ordinal);
        Assert.Contains("`InteractionFingerprintSha256`=`PayloadHash`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `payload_hash`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `payload_fingerprint`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `PayloadHash`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayerSideRepositoriesNoLongerUsePayloadNamedDatabaseColumns()
    {
        foreach (var sourcePath in new[]
                 {
                     Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Persistence", "MariaDbEquipmentEnhancementRepository.cs"),
                     Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Persistence", "MariaDbItemUseRepository.cs"),
                     Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Persistence", "MariaDbPetLifecycleRepository.cs"),
                     Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Persistence", "MariaDbPlayerSocialRepository.cs"),
                     Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Persistence", "MariaDbRuntimeRepositories.cs"),
                     Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Persistence", "MariaDbWorldInteractionRepository.cs")
                 })
        {
            var source = File.ReadAllText(sourcePath);
            Assert.DoesNotContain("`payload_hash`", source, StringComparison.Ordinal);
            Assert.DoesNotContain("`payload_fingerprint`", source, StringComparison.Ordinal);
            Assert.DoesNotContain("`PayloadHash`", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RepositoryRootDoesNotContainCompilerIntermediateObjectFiles()
    {
        var root = RepositoryRoot();
        var objectFiles = Directory.GetFiles(root, "*.obj", SearchOption.TopDirectoryOnly);

        Assert.Empty(objectFiles);
        Assert.False(File.Exists(Path.Combine(root, "desktop.ini")), "Windows shell metadata must not remain in the repository root.");
    }

    [Fact]
    public void RepositoryRootDoesNotContainLegacyBackupOrLogDataFolders()
    {
        var root = RepositoryRoot();

        Assert.False(Directory.Exists(Path.Combine(root, "backups")), "Long-lived backup data belongs under the declared db/ policy roots, not the repository root.");
        Assert.False(Directory.Exists(Path.Combine(root, "logs")), "Runtime logs belong in generated artifact locations, not as a persistent repository-root data folder.");
    }

    [Fact]
    public void BuildRootDoesNotContainDisposableOrLegacyValidationOutputFolders()
    {
        var buildRoot = Path.Combine(RepositoryRoot(), "Build");

        Assert.False(Directory.Exists(Path.Combine(buildRoot, "DisposableValidationFixtures")), "Disposable validation fixtures must not remain in the repository Build root.");
        Assert.False(Directory.Exists(Path.Combine(buildRoot, "LegacyLowerArtifacts-20260811")), "Legacy one-off build artifacts must not remain in the repository Build root.");
        Assert.False(Directory.Exists(Path.Combine(buildRoot, "NonFinalValidationAttempts")), "Non-final validation attempts must not remain in the repository Build root.");
    }

    [Fact]
    public void FormalRuntimeSchemaPackagingUsesDatabaseSchemaOnly()
    {
        var project = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.ConsoleHost",
            "God2.ClassicServer.ConsoleHost.csproj"));

        Assert.Contains(@"..\..\database\schema\**\*.*", project, StringComparison.Ordinal);
        Assert.DoesNotContain(@"..\..\db\imports", project, StringComparison.Ordinal);
        Assert.DoesNotContain(@"db\imports", project, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceFirstImportRootsRemainSeparateFromFormalRuntimeSchema()
    {
        var importsRoot = Path.Combine(RepositoryRoot(), "db", "imports");

        Assert.True(Directory.Exists(Path.Combine(importsRoot, "official")), "Official import evidence root is required for content recovery.");
        Assert.True(Directory.Exists(Path.Combine(importsRoot, "live")), "Live import evidence root is required for verified runtime captures.");
        Assert.True(Directory.Exists(Path.Combine(importsRoot, "evidence")), "Packet/database evidence root is required for evidence-first protocol recovery.");
        Assert.True(Directory.Exists(Path.Combine(importsRoot, "supplemental")), "Supplemental evidence root is required for incremental recovery packs.");
    }

    [Fact]
    public void ItemInventoryRuntimeNoLongerReadsCatalogEvidenceFromUnifiedItemView()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbGameplayInventoryRepository.cs"));

        Assert.Contains("FROM `god2_game`.`items` item_row", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `god2_game`.`vw_all_item_definitions` item_row", source, StringComparison.Ordinal);
        Assert.Contains("'canonical-item-catalog' AS `ContentVersion`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("item_row.`evidence_status`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CONCAT('canonical-',item_row.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration210ArchivesLegacyGod2ClassicSchemaOutOfFormalDatabase()
    {
        var sql = ReadSchema("210_archive_legacy_god2classic_schema_to_research.sql");

        Assert.Contains("`god2_research`.`legacy_god2classic_schema_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_god2classic_items`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`legacy_god2classic_official_import_records`", sql, StringComparison.Ordinal);
        Assert.Contains("舊 god2classic schema 為早期匯入/證據殘留", sql, StringComparison.Ordinal);
        Assert.Contains("DROP DATABASE IF EXISTS `god2classic`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration211RenamesFormalProbabilityOriginalColumnsToEvidenceColumns()
    {
        var sql = ReadSchema("211_rename_formal_probability_original_columns.sql");

        Assert.Contains("`EvidenceDropChance` decimal(18,9) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`EvidenceProbability` decimal(18,9) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("SET `EvidenceDropChance`=`OriginalDropChance`", sql, StringComparison.Ordinal);
        Assert.Contains("SET `EvidenceProbability`=`OriginalProbability`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `OriginalDropChance`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `OriginalProbability`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration212RenamesFormalProbabilityEvidenceColumnsToDeclaredRuntimeColumns()
    {
        var sql = ReadSchema("212_rename_formal_probability_evidence_columns_to_declared.sql");

        Assert.Contains("`DeclaredDropChance` decimal(18,9) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`DeclaredProbability` decimal(18,9) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("SET `DeclaredDropChance`=`EvidenceDropChance`", sql, StringComparison.Ordinal);
        Assert.Contains("SET `DeclaredProbability`=`EvidenceProbability`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `EvidenceDropChance`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `EvidenceProbability`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentRuntimeAndRecoveryToolsNoLongerUseResearchProbabilityColumnNames()
    {
        foreach (var sourcePath in new[]
                 {
                     Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Persistence", "MariaDbPromotedGameplayContentRuntime.cs"),
                     Path.Combine(RepositoryRoot(), "tools", "God2.AutomationEnvironmentProbe", "Program.cs"),
                     Path.Combine(RepositoryRoot(), "tools", "God2.GameplayContentRecovery", "Phase2MariaDbPromoter.cs"),
                     Path.Combine(RepositoryRoot(), "tools", "God2.GameplayContentRecovery", "Phase3DeepSemanticAnalyzer.cs"),
                     Path.Combine(RepositoryRoot(), "tools", "God2.GameplayContentRecovery", "Phase3SemanticRecovery.cs"),
                     Path.Combine(RepositoryRoot(), "tools", "God2.GameplayContentRecovery", "MariaDbContentRecoveryStore.cs")
                 })
        {
            var source = File.ReadAllText(sourcePath);
            Assert.DoesNotContain("`OriginalDropChance`", source, StringComparison.Ordinal);
            Assert.DoesNotContain("`OriginalProbability`", source, StringComparison.Ordinal);
            Assert.DoesNotContain("`EvidenceDropChance`", source, StringComparison.Ordinal);
            Assert.DoesNotContain("`EvidenceProbability`", source, StringComparison.Ordinal);
            Assert.Contains("Declared", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Migration213ArchivesFormalSourceReferencesOutOfRuntimeTables()
    {
        var sql = ReadSchema("213_archive_formal_source_references.sql");

        Assert.Contains("`god2_research`.`formal_source_reference_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("`source_reference` varchar(512) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("舊正式內容表的匯入來源字串已移入 research 封存", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE `maps` DROP COLUMN IF EXISTS `SourceReference`", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE `items` DROP COLUMN IF EXISTS `SourceReference`", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE `skills` DROP COLUMN IF EXISTS `SourceReference`", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE `dialogs` DROP COLUMN IF EXISTS `SourceReference`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfficialImportRuntimeWritesNoFormalSourceReferenceColumns()
    {
        var importSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "OfficialImports",
            "OfficialDataImport.cs"));
        var skillRuntimeSource = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbSkillRuntimeStore.cs"));

        Assert.DoesNotContain("ADD COLUMN IF NOT EXISTS `SourceReference`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`RecoveryStatus`, `SourceReference`", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("VALUES(@id, @code", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("VALUES (`SourceReference`)", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("`RecoveryStatus`, `SourceReference`", skillRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration214MovesContentLocalizationGlossaryOutOfFormalSchema()
    {
        var sql = ReadSchema("214_move_content_localization_glossary_to_research_schema.sql");

        Assert.Contains("`god2_research`.`content_localization_glossary`", sql, StringComparison.Ordinal);
        Assert.Contains("LIKE `content_localization_glossary`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `content_localization_glossary`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameplayContentRecoveryWritesLocalizationGlossaryToResearchSchema()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "MariaDbContentRecoveryStore.cs"));

        Assert.Contains("INSERT INTO `god2_research`.`content_localization_glossary`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `content_localization_glossary`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration215ArchivesRelationshipSourceMetadataOutOfFormalRelationshipTables()
    {
        var sql = ReadSchema("215_archive_relationship_source_metadata.sql");

        Assert.Contains("`god2_research`.`relationship_source_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("'monster_drop_relationships'", sql, StringComparison.Ordinal);
        Assert.Contains("'container_item_relationships'", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `SourceType`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `SourceFile`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `SourceIdentity`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `SourceHash`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `Confidence`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RelationshipPromotionToolsWriteSourceMetadataToResearchArchive()
    {
        var promoter = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));
        var phase3 = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase3SemanticRecovery.cs"));

        Assert.Contains("INSERT INTO `god2_research`.`relationship_source_archive`", promoter, StringComparison.Ordinal);
        Assert.DoesNotContain("`DropRelationshipStatus`,`IsDropEnabled`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`Confidence`", promoter, StringComparison.Ordinal);
        Assert.DoesNotContain("`RelationshipStatus`,`Enabled`,`SourceHash`,`SourceIdentity`", promoter, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN `god2_research`.`relationship_source_archive`", phase3, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_OBJECT('sourceType',d.`SourceType`", phase3, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration216ArchivesEquipmentAndPetInnateSourceHashesOutOfFormalTables()
    {
        var sql = ReadSchema("216_archive_equipment_and_pet_innate_source_hashes.sql");

        Assert.Contains("`god2_research`.`content_profile_source_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("'equipment_set_definitions'", sql, StringComparison.Ordinal);
        Assert.Contains("'pet_innate_definitions'", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `SourceHash`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PromotedContentToolsArchiveEquipmentAndPetInnateSourceHashesToResearch()
    {
        var promoter = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));
        var phase3 = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase3SemanticRecovery.cs"));
        var runtime = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbPromotedGameplayContentRuntime.cs"));

        Assert.Contains("INSERT INTO `god2_research`.`content_profile_source_archive`", promoter, StringComparison.Ordinal);
        Assert.DoesNotContain("`SetId`,`RunId`,`NameZhTw`,`RequiredPieces`,`EffectsZhTw`,`SourceHash`", promoter, StringComparison.Ordinal);
        Assert.DoesNotContain("`InnateId`,`RunId`,`NameZhTw`,`ArtifactNameZhTw`,`DescriptionZhTw`,`EffectReference`,`EffectValue`,`SourceHash`", promoter, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN `god2_research`.`content_profile_source_archive`", phase3, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN `god2_research`.`content_profile_source_archive`", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("d.`SourceHash` REGEXP", phase3, StringComparison.Ordinal);
        Assert.DoesNotContain("`ProductionBonusEnabled`,`SourceHash`", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration217ArchivesItemPetAndQuestProfileSourceMetadataOutOfFormalTables()
    {
        var sql = ReadSchema("217_archive_item_pet_and_quest_profile_source_metadata.sql");

        Assert.Contains("`god2_research`.`content_profile_source_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("'item_content_profiles'", sql, StringComparison.Ordinal);
        Assert.Contains("'pet_content_profiles'", sql, StringComparison.Ordinal);
        Assert.Contains("'quest_content_profiles'", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `SourceSection`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `SourceHash`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX `UX_ItemContentProfiles_Client`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItemPetAndQuestProfileToolsUseResearchArchiveForSourceMetadata()
    {
        var promoter = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));
        var phase3 = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase3SemanticRecovery.cs"));
        var runtime = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbPromotedGameplayContentRuntime.cs"));

        Assert.Contains("'item_content_profiles'", promoter, StringComparison.Ordinal);
        Assert.Contains("'pet_content_profiles'", promoter, StringComparison.Ordinal);
        Assert.Contains("'quest_content_profiles'", promoter, StringComparison.Ordinal);
        Assert.DoesNotContain("`ItemId`,`RunId`,`ClientItemId`,`SourceSection`", promoter, StringComparison.Ordinal);
        Assert.DoesNotContain("`ProfileId`,`RunId`,`QuestId`,`ClientQuestId`,`NameZhTw`,`DescriptionZhTw`,`StepsJson`,`StartNpcClientId`,`EndNpcClientId`,`RewardTextZhTw`,`SourceHash`", promoter, StringComparison.Ordinal);
        Assert.DoesNotContain("`ProfileId`,`RunId`,`ClientPetId`,`ItemId`,`NameZhTw`,`PetFamily`,`GrowthType`,`BaseStatsJson`,`SkillReferencesJson`,`EvolutionReferencesJson`,`SourceHash`", promoter, StringComparison.Ordinal);
        Assert.Contains("s.`RecordIdentity`=p.`ProfileId` AND s.`SourceHash`=o.`SourceHash`", phase3, StringComparison.Ordinal);
        Assert.DoesNotContain("p.`SourceHash`=o.`SourceHash`", phase3, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN `god2_research`.`content_profile_source_archive`", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("profile.`SourceHash` NOT REGEXP", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration218ArchivesWorldIdentitySourceMetadataOutOfFormalTables()
    {
        var sql = ReadSchema("218_archive_world_identity_source_metadata.sql");

        Assert.Contains("`god2_research`.`world_identity_source_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("'client_map_identities'", sql, StringComparison.Ordinal);
        Assert.Contains("'npc_client_identities'", sql, StringComparison.Ordinal);
        Assert.Contains("'npc_spawns'", sql, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX `UX_NpcSpawns_ObservedEntity`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `SourceType`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `SourceIdentity`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `SourceHash`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `ProvenanceJson`", sql, StringComparison.Ordinal);
        Assert.Contains("`IX_NpcSpawns_ObservedEntity`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AutomationProbeReadsWorldIdentitySourceMetadataFromResearchArchive()
    {
        var probe = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.AutomationEnvironmentProbe",
            "Program.cs"));

        Assert.Contains("LEFT JOIN `god2_research`.`world_identity_source_archive`", probe, StringComparison.Ordinal);
        Assert.Contains("source.`FormalTable`='client_map_identities'", probe, StringComparison.Ordinal);
        Assert.Contains("source.`FormalTable`='npc_client_identities'", probe, StringComparison.Ordinal);
        Assert.Contains("source.`FormalTable`='npc_spawns'", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("identity.`SourceType`, identity.`SourceHash`", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("`ResourceKey`, 'formal-runtime-identity' AS `IdentityEvidenceStatus`, `SourceHash`", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("'formal-runtime-service' AS `ServiceEvidenceStatus`, `ProductionEnabled`, `SourceHash`", probe, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration219ArchivesContentRecoveryRunSummariesOutOfFormalLedger()
    {
        var sql = ReadSchema("219_archive_content_recovery_run_summaries.sql");

        Assert.Contains("`god2_research`.`content_recovery_run_summary_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN `SummaryJson`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameplayContentRecoveryWritesRunSummariesToResearchArchive()
    {
        var store = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "MariaDbContentRecoveryStore.cs"));
        var phase3 = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase3SemanticRecovery.cs"));

        Assert.Contains("INSERT INTO `god2_research`.`content_recovery_run_summary_archive`", store, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_research`.`content_recovery_run_summary_archive`", phase3, StringComparison.Ordinal);
        Assert.DoesNotContain("SET `Status`=@status, `CompletedAtUtc`=UTC_TIMESTAMP(6), `SummaryJson`=@summary", store, StringComparison.Ordinal);
        Assert.DoesNotContain("SET `Status`=@status,`CompletedAtUtc`=UTC_TIMESTAMP(6),`SummaryJson`=@summary", phase3, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration220ArchivesSemanticProfileSourceRunsOutOfFormalTables()
    {
        var sql = ReadSchema("220_archive_semantic_profile_source_runs.sql");

        Assert.Contains("`god2_research`.`semantic_profile_source_run_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("'monster_semantic_profiles'", sql, StringComparison.Ordinal);
        Assert.Contains("'monster_spawn_semantics'", sql, StringComparison.Ordinal);
        Assert.Contains("'skill_semantic_profiles'", sql, StringComparison.Ordinal);
        Assert.Contains("DROP FOREIGN KEY `FK_MonsterSemanticProfiles_SourceRun`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP FOREIGN KEY `FK_MonsterSpawnSemantics_SourceRun`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP FOREIGN KEY `FK_SkillSemanticProfiles_SourceRun`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `SourceRunId`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `ResourceKey`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase3SemanticRecoveryWritesSemanticSourceRunsToResearchArchive()
    {
        var phase3 = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase3SemanticRecovery.cs"));

        Assert.Contains("INSERT INTO `god2_research`.`semantic_profile_source_run_archive`", phase3, StringComparison.Ordinal);
        Assert.DoesNotContain("`RunId`,`SourceRunId`,`MonsterId`", phase3, StringComparison.Ordinal);
        Assert.DoesNotContain("`RunId`,`SourceRunId`,`SkillId`", phase3, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT @run,@sourceRun,m.`Id`", phase3, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT @run,@sourceRun,s.`Id`", phase3, StringComparison.Ordinal);
    }

    [Fact]
    public void OfficialCatalogWritersDoNotReintroduceOriginalChineseColumnsIntoFormalTables()
    {
        var root = RepositoryRoot();
        var writers = new[]
        {
            "OfficialNpcSourceRowMigrationWriter.cs",
            "OfficialNpcCatalogMigrationWriter.cs",
            "OfficialMapMigrationWriter.cs",
            "OfficialItemUsageFlagCatalogWriter.cs",
            "OfficialItemEffectVisualCatalogWriter.cs"
        };

        foreach (var writer in writers)
        {
            var source = File.ReadAllText(Path.Combine(root, "tools", "God2.GameplayContentRecovery", writer));
            Assert.DoesNotContain("name_original", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("display_name_original", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("visual_description_original", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("authored_mechanical_description_original", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Migration221ArchivesPetAndCaptureSourceMetadataOutOfFormalTables()
    {
        var sql = ReadSchema("221_archive_pet_and_capture_source_metadata.sql");

        Assert.Contains("`god2_research`.`pet_category_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`pet_template_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`monster_capture_rule_source_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`character_pet_skill_rule_source_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_url`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `capture_rule_source_article_sn`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `learning_rule_source_article_sn`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PetLifecycleRepositoryDoesNotWriteLearningRuleSourceArticleIntoFormalPlayerTable()
    {
        var repository = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbPetLifecycleRepository.cs"));

        Assert.DoesNotContain("learning_rule_source_article_sn", repository, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration222ArchivesItemVisualAndUsageSourceLocatorsOutOfFormalTables()
    {
        var sql = ReadSchema("222_archive_item_visual_and_usage_source_locators.sql");

        Assert.Contains("`god2_research`.`client_item_effect_visual_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`client_item_usage_flag_addition_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_table`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_section`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `resource_key`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD PRIMARY KEY (`client_item_id`)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItemVisualAndUsageWritersDoNotReintroduceSourceLocatorColumnsIntoFormalTables()
    {
        var usageWriter = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "OfficialItemUsageFlagCatalogWriter.cs"));
        var visualWriter = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "OfficialItemEffectVisualCatalogWriter.cs"));

        Assert.DoesNotContain("`source_section` varchar", usageWriter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`resource_key` varchar", usageWriter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIMARY KEY (`source_section`,`client_item_id`)", usageWriter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`client_item_effect_visuals`" + Environment.NewLine +
            "    (`effect_id`,`source_table`", visualWriter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`ix_client_item_effect_visual_source`", visualWriter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration223ArchivesItemSourceTypesOutOfFormalCatalogTables()
    {
        var sql = ReadSchema("223_archive_item_source_types_out_of_formal_catalog.sql");

        Assert.Contains("`god2_research`.`item_catalog_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("'item_registry'", sql, StringComparison.Ordinal);
        Assert.Contains("'magic_treasures'", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_item_type`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS `ix_items_source_item_type`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_all_item_definitions`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_items_classified`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("item_row.`source_item_type` AS `來源類型`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`,`source_item_type`,`required_level`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItemRuntimeAndReadableViewGeneratorNoLongerReadFormalSourceItemType()
    {
        var repository = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbGameplayInventoryRepository.cs"));
        var zhTwViews = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.ZhTwDatabaseViews",
            "Program.cs"));

        Assert.Contains("FROM `god2_game`.`items` item_row", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `god2_game`.`vw_all_item_definitions` item_row", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("item_row.`source_item_type`", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("item_row.`source_item_type` AS `來源類型`", zhTwViews, StringComparison.Ordinal);
        Assert.DoesNotContain("`,`source_item_type`,`required_level`", zhTwViews, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration224ArchivesItemEffectVisualResourcePathsOutOfFormalTables()
    {
        var sql = ReadSchema("224_archive_item_effect_visual_resource_paths.sql");

        Assert.Contains("`god2_research`.`client_item_effect_visual_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `rom_resource_path`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `rtg_resource_path`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `rom_resource_path`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `rtg_resource_path`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_client_item_effect_visuals_readable`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("AS `ROM資源`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("AS `RTG資源`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItemEffectVisualWriterStoresResourcePathsOnlyInResearchEvidence()
    {
        var writer = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "OfficialItemEffectVisualCatalogWriter.cs"));

        Assert.DoesNotContain("`effect_id`,`rom_resource_path`,`rtg_resource_path`,`sound_index`", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("`rom_resource_path`=VALUES(`rom_resource_path`),`rtg_resource_path`=VALUES(`rtg_resource_path`)", writer, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_research`.`client_item_effect_visual_evidence`", writer, StringComparison.Ordinal);
        Assert.Contains("`effect_id`,`source_table`,`rom_resource_path`,`rtg_resource_path`,`source_path`,`source_row`,`source_sha256`,`evidence_status`", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("AS `ROM資源`", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("AS `RTG資源`", writer, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration225ArchivesItemIconAtlasAssetEvidenceOutOfFormalTables()
    {
        var sql = ReadSchema("225_archive_item_icon_atlas_asset_evidence.sql");

        Assert.Contains("`god2_research`.`item_icon_atlas_asset_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `resource_path_resolved`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `file_size_bytes`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `file_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `validated_at_utc`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_item_asset_validation`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("AS `本機圖檔`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("file_sha256` AS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItemAssetVerifierWritesIconFileEvidenceOnlyToResearch()
    {
        var verifier = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.ItemAssetVerifier",
            "Program.cs"));

        Assert.Contains("INSERT INTO `god2_research`.`item_icon_atlas_asset_evidence`", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("`atlas_id`,`minimum_icon_code`,`maximum_icon_code`,`resource_path_original`,`resource_path_resolved`", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("`atlas_id`,`minimum_icon_code`,`maximum_icon_code`,`resource_path_resolved`,`file_size_bytes`", verifier, StringComparison.Ordinal);
        Assert.Contains("`atlas_id`,`minimum_icon_code`,`maximum_icon_code`,`validation_status`", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemAssetVerifierReadsIconsAndModelsFromFormalCatalogInsteadOfLegacyItems()
    {
        var verifier = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.ItemAssetVerifier",
            "Program.cs"));
        var migration = ReadSchema("376_promote_item_model_keys_to_formal_catalog.sql");

        Assert.Contains("registry_row.`icon_id`", verifier, StringComparison.Ordinal);
        Assert.Contains("registry_row.`model_key`", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("JOIN `god2`.`items` source_row", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("PayloadJson", verifier, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `model_key`", migration, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2`.`item_content_profiles` profile_row", migration, StringComparison.Ordinal);
        Assert.Contains("SET registry_row.`model_key`", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase2ItemProfilePromotionKeepsFormalCatalogModelKeysSynchronized()
    {
        var promoter = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));

        Assert.Contains("UPDATE `god2_game`.`item_registry`", promoter, StringComparison.Ordinal);
        Assert.Contains("UPDATE `god2_game`.`items`", promoter, StringComparison.Ordinal);
        Assert.Contains("UPDATE `god2_game`.`weapons`", promoter, StringComparison.Ordinal);
        Assert.Contains("UPDATE `god2_game`.`equipment`", promoter, StringComparison.Ordinal);
        Assert.Contains("UPDATE `god2_game`.`magic_treasures`", promoter, StringComparison.Ordinal);
        Assert.Contains("SET `model_key`=@model", promoter, StringComparison.Ordinal);
        Assert.Contains("AND @model<>''", promoter, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration377DropsLegacyGod2ItemProfileReadableViewsOnlyAfterDependencyGuard()
    {
        var sql = ReadSchema("377_drop_legacy_god2_item_profile_readable_views.sql");

        Assert.Contains("@legacy_item_profile_view_refs", sql, StringComparison.Ordinal);
        Assert.Contains("SIGNAL SQLSTATE", sql, StringComparison.Ordinal);
        Assert.Contains("45000", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2`.`vw_items_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2`.`vw_item_content_profiles_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2`.`vw_container_item_relationships_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2`.`vw_equipment_sets_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2`.`vw_pet_profile_summary_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2`.`vw_blackbox_item_container_observations_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2`.`vw_blackbox_pet_panel_priority_observations_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2`.`vw_blackbox_pet_skill_static_observations_readable`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration378SynchronizesLegacyEquipmentSetsIntoFormalCatalogTables()
    {
        var sql = ReadSchema("378_sync_equipment_sets_to_formal_catalog.sql");

        Assert.Contains("INSERT INTO `god2_game`.`item_sets`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`equipment_set_definitions` staging_row", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_game`.`item_set_members`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`equipment_set_members` member_row", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`item_registry` item_row", sql, StringComparison.Ordinal);
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase2EquipmentSetPromotionKeepsFormalCatalogTablesSynchronized()
    {
        var promoter = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));

        Assert.Contains("INSERT INTO `god2_game`.`item_sets`", promoter, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_game`.`item_set_members`", promoter, StringComparison.Ordinal);
        Assert.Contains("由 Phase2 equipment_set_definitions 同步", promoter, StringComparison.Ordinal);
        Assert.Contains("由 Phase2 equipment_set_members 同步", promoter, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration379SynchronizesLegacyContainerRelationshipsIntoFormalRewardTables()
    {
        var sql = ReadSchema("379_sync_container_relationships_to_formal_rewards.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_game`.`containers`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_game`.`container_rewards`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`container_item_relationships` relationship_row", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_game`.`containers`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_game`.`container_rewards`", sql, StringComparison.Ordinal);
        Assert.Contains("ROW_NUMBER() OVER", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`item_registry` reward_item", sql, StringComparison.Ordinal);
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase2ContainerPromotionKeepsFormalRewardTablesSynchronized()
    {
        var promoter = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));

        Assert.Contains("INSERT INTO `god2_game`.`containers`", promoter, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_game`.`container_rewards`", promoter, StringComparison.Ordinal);
        Assert.Contains("FROM `god2_game`.`item_registry` registry_row", promoter, StringComparison.Ordinal);
        Assert.Contains("FROM `god2_game`.`container_rewards` existing_reward", promoter, StringComparison.Ordinal);
        Assert.Contains("FROM `god2_game`.`container_rewards` next_reward", promoter, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration380SynchronizesLegacySkillProfilesIntoFormalSkillCatalog()
    {
        var sql = ReadSchema("380_sync_skill_profiles_to_formal_catalog.sql");

        Assert.Contains("INSERT INTO `god2_game`.`skills`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`skills` skill_row", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`skill_content_profiles`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`skill_semantic_profiles`", sql, StringComparison.Ordinal);
        Assert.Contains("`mp_cost`=COALESCE(VALUES(`mp_cost`), `mp_cost`)", sql, StringComparison.Ordinal);
        Assert.Contains("`target_type`=COALESCE(VALUES(`target_type`), `target_type`)", sql, StringComparison.Ordinal);
        Assert.Contains("`enabled`=CASE WHEN `god2_game`.`skills`.`enabled` = 1 OR VALUES(`enabled`) = 1 THEN 1 ELSE 0 END", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase2SkillPromotionKeepsFormalSkillCatalogSynchronized()
    {
        var promoter = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));

        Assert.Contains("INSERT INTO `god2_game`.`skills`", promoter, StringComparison.Ordinal);
        Assert.Contains("FROM `skills` source_skill", promoter, StringComparison.Ordinal);
        Assert.Contains("`official_client_item_id`=COALESCE(VALUES(`official_client_item_id`),`official_client_item_id`)", promoter, StringComparison.Ordinal);
        Assert.Contains("`skill_family`=COALESCE(VALUES(`skill_family`),`skill_family`)", promoter, StringComparison.Ordinal);
        Assert.Contains("`target_type`=COALESCE(VALUES(`target_type`),`target_type`)", promoter, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration381SynchronizesLegacyQuestProfilesIntoFormalQuestCatalog()
    {
        var sql = ReadSchema("381_sync_quest_profiles_to_formal_catalog.sql");

        Assert.Contains("INSERT INTO `god2_game`.`quests`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`quests` quest_row", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`quest_content_profiles`", sql, StringComparison.Ordinal);
        Assert.Contains("`description_zh_tw`=COALESCE(VALUES(`description_zh_tw`), `description_zh_tw`)", sql, StringComparison.Ordinal);
        Assert.Contains("`completion_text_zh_tw`=COALESCE(VALUES(`completion_text_zh_tw`), `completion_text_zh_tw`)", sql, StringComparison.Ordinal);
        Assert.Contains("`enabled`=CASE WHEN `god2_game`.`quests`.`enabled` = 1 OR VALUES(`enabled`) = 1 THEN 1 ELSE 0 END", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase2QuestPromotionKeepsFormalQuestCatalogSynchronized()
    {
        var promoter = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));

        Assert.Contains("INSERT INTO `god2_game`.`quests`", promoter, StringComparison.Ordinal);
        Assert.Contains("FROM `quests` source_quest", promoter, StringComparison.Ordinal);
        Assert.Contains("`description_zh_tw`=COALESCE(VALUES(`description_zh_tw`),`description_zh_tw`)", promoter, StringComparison.Ordinal);
        Assert.Contains("`completion_text_zh_tw`=COALESCE(VALUES(`completion_text_zh_tw`),`completion_text_zh_tw`)", promoter, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration382SynchronizesLegacyNpcProfilesIntoFormalNpcCatalog()
    {
        var sql = ReadSchema("382_sync_npc_profiles_to_formal_catalog.sql");

        Assert.Contains("INSERT INTO `god2_game`.`npcs`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`npcs` npc_row", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`npc_client_identities`", sql, StringComparison.Ordinal);
        Assert.Contains("`resource_key`=COALESCE(VALUES(`resource_key`), `resource_key`)", sql, StringComparison.Ordinal);
        Assert.Contains("`interaction_family`=COALESCE(VALUES(`interaction_family`), `interaction_family`)", sql, StringComparison.Ordinal);
        Assert.Contains("`enabled`=CASE WHEN `god2_game`.`npcs`.`enabled` = 1 OR VALUES(`enabled`) = 1 THEN 1 ELSE 0 END", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase2NpcCoordinatePromotionKeepsFormalNpcCatalogSynchronized()
    {
        var promoter = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));

        Assert.Contains("INSERT INTO `god2_game`.`npcs`", promoter, StringComparison.Ordinal);
        Assert.Contains("FROM `npcs` source_npc", promoter, StringComparison.Ordinal);
        Assert.Contains("`npc_type`=VALUES(`npc_type`)", promoter, StringComparison.Ordinal);
        Assert.Contains("`interaction_family`=COALESCE(VALUES(`interaction_family`),`interaction_family`)", promoter, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration383SynchronizesLegacyPetInnatesIntoFormalPetInnateCatalog()
    {
        var sql = ReadSchema("383_sync_pet_innates_to_formal_catalog.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_game`.`pet_innate_definitions`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_game`.`pet_innate_definitions`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`pet_innate_definitions` innate_row", sql, StringComparison.Ordinal);
        Assert.Contains("`effect_evidence_status`=VALUES(`effect_evidence_status`)", sql, StringComparison.Ordinal);
        Assert.Contains("`enabled`=VALUES(`enabled`)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PetInnatePromotionAndRuntimeUseFormalPetInnateCatalog()
    {
        var root = RepositoryRoot();
        var promoter = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));
        var runtime = File.ReadAllText(Path.Combine(
            root,
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbPromotedGameplayContentRuntime.cs"));

        Assert.Contains("INSERT INTO `god2_game`.`pet_innate_definitions`", promoter, StringComparison.Ordinal);
        Assert.Contains("`effect_evidence_status`=VALUES(`effect_evidence_status`)", promoter, StringComparison.Ordinal);
        Assert.Contains("FROM `god2_game`.`pet_innate_definitions`", runtime, StringComparison.Ordinal);
        Assert.Contains("WHERE `enabled`=1", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `pet_innate_definitions`", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration384RestoresMerchantInventoryCandidateResearchTableForPromotionPipeline()
    {
        var sql = ReadSchema("384_restore_merchant_inventory_candidate_research_table.sql");

        Assert.Contains("CREATE DATABASE IF NOT EXISTS `god2_research`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`merchant_inventory_candidates`", sql, StringComparison.Ordinal);
        Assert.Contains("`RelationshipEvidenceStatus` varchar(32) NOT NULL DEFAULT 'Candidate'", sql, StringComparison.Ordinal);
        Assert.Contains("`PriceEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked'", sql, StringComparison.Ordinal);
        Assert.Contains("`ProductionSaleEnabled` tinyint(1) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("`PromotionRunId` char(36) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS `IX_MerchantInventoryCandidates_Promotion`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration385RestoresCoreResearchPipelineTablesRequiredByRecoveryTools()
    {
        var sql = ReadSchema("385_restore_core_research_pipeline_tables.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`content_client_table_layouts`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`content_field_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`content_production_manifest`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`content_profile_source_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`relationship_source_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`npc_coordinate_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`quest_objective_candidates`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`historical_gameplay_observations`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`content_phase3_field_closure`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`content_phase3_promotions`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`semantic_profile_source_run_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`content_recovery_run_summary_archive`", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.* TO 'god2_server'@'localhost'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration226ArchivesClientMapResourceFileEvidenceOutOfFormalTables()
    {
        var sql = ReadSchema("226_archive_client_map_resource_file_evidence.sql");

        Assert.Contains("`god2_research`.`client_map_resource_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `can_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `resource_file_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `navigation_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS `ux_client_map_resources_can_record`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `client_executable_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `can_record_index`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `can_relative_path`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `can_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `resource_file_relative_path`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `resource_file_sha256`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `navigation_relative_path`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `navigation_sha256`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapResourceToolsUseResearchForFileEvidenceAndIdentitySources()
    {
        var root = RepositoryRoot();
        var store = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.GameplayContentRecovery",
            "ClientMapResourceCatalogStore.cs"));
        var mapWriter = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.GameplayContentRecovery",
            "OfficialMapMigrationWriter.cs"));
        var catalogBuilder = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.GameCatalogBuilder",
            "Program.cs"));

        Assert.Contains("INSERT INTO `god2_research`.`client_map_resource_evidence`", store, StringComparison.Ordinal);
        Assert.DoesNotContain("`client_build_id`,`client_executable_sha256`,`can_relative_path`,`can_sha256`", store, StringComparison.Ordinal);
        Assert.DoesNotContain("`client_build_id`,`client_executable_sha256`,`can_relative_path`,`can_sha256`,", store, StringComparison.Ordinal);
        Assert.DoesNotContain("`resource_format`,`resource_file_relative_path`,`resource_file_sha256`", store, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_research`.`client_map_resource_identity_evidence`", mapWriter, StringComparison.Ordinal);
        Assert.DoesNotContain("`world_map_x`,`world_map_y`,`source_row`,`source_sha256`", mapWriter, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_research`.`client_map_resource_evidence` evidence_row", catalogBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("NULLIF(TRIM(`resource_file_sha256`),'')", catalogBuilder, StringComparison.Ordinal);
    }

    [Fact]
    public void PortalCoverageAndWriterReadEvidenceFromResearch()
    {
        var root = RepositoryRoot();
        var catalogBuilder = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.GameCatalogBuilder",
            "Program.cs"));
        var portalWriter = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.GameplayContentRecovery",
            "OfficialPortalLinkMigrationWriter.cs"));

        Assert.Contains("JOIN `god2_research`.`portal_evidence` evidence_row", catalogBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("portal.`client_build_id`", catalogBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("portal.`source_reference`", catalogBuilder, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_research`.`portal_resource_link_evidence`", portalWriter, StringComparison.Ordinal);
        Assert.DoesNotContain("`can_relative_path`,`can_record_index`,`can_record_type`,`source_sha256`,`identity_evidence_status`,`enabled`", portalWriter, StringComparison.Ordinal);
        Assert.DoesNotContain("portal_row.`client_build_id`", portalWriter, StringComparison.Ordinal);
        Assert.DoesNotContain("portal_row.`source_sha256`", portalWriter, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration386SynchronizesLegacyMonsterDropRelationshipsIntoFormalDrops()
    {
        var sql = ReadSchema("386_sync_monster_drop_relationships_to_formal_drops.sql");

        Assert.Contains("INSERT INTO `god2_game`.`monster_drops`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`monster_drop_relationships` relationship_row", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`monsters` monster_row", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`item_registry` item_row", sql, StringComparison.Ordinal);
        Assert.Contains("relationship_row.`ProductionDropEnabled`", sql, StringComparison.Ordinal);
        Assert.Contains("CASE WHEN `god2_game`.`monster_drops`.`enabled`=1 THEN 1 ELSE VALUES(`enabled`) END", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase2MonsterDropPromotionKeepsFormalDropTableSynchronized()
    {
        var root = RepositoryRoot();
        var promoter = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));

        Assert.Contains("INSERT INTO `god2_game`.`monster_drops`", promoter, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`monsters` legacy_monster", promoter, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`monsters` monster_row ON monster_row.`code`=legacy_monster.`Code`", promoter, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`item_registry` item_row", promoter, StringComparison.Ordinal);
        Assert.Contains("'Phase2 monster_drop_relationships'", promoter, StringComparison.Ordinal);
        Assert.Contains("`drop_policy_zh_tw`=VALUES(`drop_policy_zh_tw`)", promoter, StringComparison.Ordinal);
        Assert.Contains("`updated_at_utc`=UTC_TIMESTAMP(6)", promoter, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration387SynchronizesLegacyMonsterIdentitiesBeforeFormalDrops()
    {
        var sql = ReadSchema("387_sync_legacy_monsters_for_formal_drops.sql");

        Assert.Contains("INSERT INTO `god2_game`.`monsters`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`monsters` monster_row", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("relationship_row.`MonsterId` = monster_row.`Id`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_game`.`monster_drops`", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`monsters` monster_row", sql, StringComparison.Ordinal);
        Assert.Contains("只同步既有欄位，未證實戰鬥公式不覆蓋", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase2MonsterDropPromotionEnsuresFormalMonsterIdentityBeforeDrop()
    {
        var root = RepositoryRoot();
        var promoter = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));

        Assert.Contains("INSERT INTO `god2_game`.`monsters`", promoter, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`monsters` monster_row", promoter, StringComparison.Ordinal);
        Assert.Contains("WHERE monster_row.`Id`=@monster", promoter, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_game`.`monster_drops`", promoter, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`monsters` legacy_monster", promoter, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`monsters` monster_row ON monster_row.`code`=legacy_monster.`Code`", promoter, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration388UsesDropTablesToForceSyncMonsterDropFormalRows()
    {
        var sql = ReadSchema("388_force_sync_drop_monsters_and_relationships_to_formal.sql");

        Assert.Contains("JOIN `god2`.`drop_tables` drop_table", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2`.`monsters` legacy_monster", sql, StringComparison.Ordinal);
        Assert.Contains("legacy_monster.`Id` = drop_table.`MonsterId`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_game`.`monsters`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_game`.`monster_drops`", sql, StringComparison.Ordinal);
        Assert.Contains("drop_table.`MonsterId`", sql, StringComparison.Ordinal);
        Assert.Contains("relationship_row.`ProductionDropEnabled`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration389SynchronizesMonsterDropsByFormalMonsterCode()
    {
        var sql = ReadSchema("389_sync_monster_drops_by_formal_monster_code.sql");

        Assert.Contains("JOIN `god2`.`drop_tables` drop_table", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2`.`monsters` legacy_monster", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`monsters` formal_monster", sql, StringComparison.Ordinal);
        Assert.Contains("formal_monster.`code` = legacy_monster.`Code`", sql, StringComparison.Ordinal);
        Assert.Contains("formal_monster.`monster_id`", sql, StringComparison.Ordinal);
        Assert.Contains("relationship_row.`ProductionDropEnabled`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase2MonsterDropPromotionUsesFormalMonsterCodeMapping()
    {
        var root = RepositoryRoot();
        var promoter = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.GameplayContentRecovery",
            "Phase2MariaDbPromoter.cs"));

        Assert.Contains("FROM `god2`.`monsters` legacy_monster", promoter, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`monsters` monster_row ON monster_row.`code`=legacy_monster.`Code`", promoter, StringComparison.Ordinal);
        Assert.Contains("WHERE legacy_monster.`Id`=@monster", promoter, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_game`.`monster_drops`", promoter, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration390PromotesOnlySafeMonsterHpMpReconciliationSources()
    {
        var sql = ReadSchema("390_promote_safe_monster_hpmp_reconciliation_sources.sql");

        Assert.Contains("UPDATE `god2_game`.`monsters` formal_monster", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`vw_monster_hpmp_reconciliation` reconciliation", sql, StringComparison.Ordinal);
        Assert.Contains("reconciliation.`同步判定` IN ('可人工確認：名稱唯一命中','可人工確認：代碼與名稱同時命中')", sql, StringComparison.Ordinal);
        Assert.Contains("formal_monster.`max_hp` = reconciliation.`最大生命`", sql, StringComparison.Ordinal);
        Assert.Contains("formal_monster.`max_mp` = reconciliation.`最大法力`", sql, StringComparison.Ordinal);
        Assert.Contains("只提升 HP/MP 來源標記，不宣稱官方實測", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("禁止自動同步", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration391ExcludesBlockedMonsterHpMpReconciliationSources()
    {
        var sql = ReadSchema("391_exclude_blocked_monster_hpmp_reconciliation_sources.sql");

        Assert.Contains("WHERE reconciliation.`同步判定` LIKE '禁止自動同步%'", sql, StringComparison.Ordinal);
        Assert.Contains("HPMP reconciliation 待確認", sql, StringComparison.Ordinal);
        Assert.Contains("保留既有 HP/MP 數值，但不得自動提升為安全證據", sql, StringComparison.Ordinal);
        Assert.Contains("AND NOT EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("blocked.`同步判定` LIKE '禁止自動同步%'", sql, StringComparison.Ordinal);
        Assert.Contains("HPMP reconciliation 證據", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration392PublishesReadablePetContentProfileCandidatesWithoutHashesOrJsonPayloads()
    {
        var sql = ReadSchema("392_publish_readable_pet_content_profile_candidates.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`pet_content_profile_candidates`", sql, StringComparison.Ordinal);
        Assert.Contains("`profile_label_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("`sync_status_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("`sync_policy_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`pet_content_profiles` profile_row", sql, StringComparison.Ordinal);
        Assert.Contains("client_pet_id 可對到正式模板，但 profile 標籤不是寵物名稱，禁止自動覆蓋正式寵物", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceHash", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("payload", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration393PublishesPetContentProfileCandidateRowsWithoutMachineIds()
    {
        var sql = ReadSchema("393_publish_pet_content_profile_candidate_rows.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`pet_content_profile_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("`candidate_row_id` bigint NOT NULL AUTO_INCREMENT", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`pet_content_profiles` profile_row", sql, StringComparison.Ordinal);
        Assert.Contains("client_pet_id 可對到正式模板，但 profile 標籤不是寵物名稱，禁止自動覆蓋正式寵物", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIMARY KEY (`client_pet_id`, `profile_label_zh_tw`)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceHash", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration394PublishesReadableQuestContentProfileRowsWithoutHashesOrStepsJson()
    {
        var sql = ReadSchema("394_publish_readable_quest_content_profile_candidate_rows.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`quest_content_profile_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("`candidate_row_id` bigint NOT NULL AUTO_INCREMENT", sql, StringComparison.Ordinal);
        Assert.Contains("`profile_text_zh_tw` text NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`has_steps_payload` tinyint(1) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`quest_content_profiles` profile_row", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN `god2_game`.`quests` formal_quest", sql, StringComparison.Ordinal);
        Assert.Contains("不搬原始 StepsJson", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceHash", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`StepsJson` text", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration395PublishesReadableItemContentProfileRowsWithoutHashesOrPayloads()
    {
        var sql = ReadSchema("395_publish_readable_item_content_profile_rows.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`item_content_profile_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("`item_name_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("`physical_attack_bonus`", sql, StringComparison.Ordinal);
        Assert.Contains("`magic_defense_bonus`", sql, StringComparison.Ordinal);
        Assert.Contains("`hp_bonus`", sql, StringComparison.Ordinal);
        Assert.Contains("`mp_bonus`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`item_content_profiles` profile_row", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN `god2_game`.`item_registry` item_row", sql, StringComparison.Ordinal);
        Assert.Contains("可用於核對裝備欄位與攻防 HP/MP 加成", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceHash", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Payload", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration396PublishesReadableSkillProfileRowsWithoutHashesOrJsonPayloads()
    {
        var sql = ReadSchema("396_publish_readable_skill_profile_candidate_rows.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`skill_content_profile_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`skill_semantic_profile_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("`has_effect_reference` tinyint(1) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("`has_status_reference` tinyint(1) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`skill_content_profiles` profile_row", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`skill_semantic_profiles` profile_row", sql, StringComparison.Ordinal);
        Assert.Contains("仍不直接啟用未解析效果", sql, StringComparison.Ordinal);
        Assert.Contains("MP 或效果證據不足，禁止自動覆蓋正式技能", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientProfileId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceHash", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("EffectReferencesJson` longtext", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("StatusReferencesJson` longtext", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration397PublishesReadableRelationshipRowsWithoutRelationshipHashes()
    {
        var sql = ReadSchema("397_publish_readable_relationship_candidate_rows.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`container_reward_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`equipment_set_member_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`monster_drop_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("TRUNCATE TABLE `god2_research`.`container_reward_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("TRUNCATE TABLE `god2_research`.`equipment_set_member_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("TRUNCATE TABLE `god2_research`.`monster_drop_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`container_item_relationships` relationship_row", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN `god2_game`.`containers` formal_container", sql, StringComparison.Ordinal);
        Assert.Contains("formal_reward.`container_id` = formal_container.`container_id`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`equipment_set_members` member_row", sql, StringComparison.Ordinal);
        Assert.Contains("formal_member.`set_id` IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`monster_drop_relationships` relationship_row", sql, StringComparison.Ordinal);
        Assert.Contains("已同步 god2_game.container_rewards", sql, StringComparison.Ordinal);
        Assert.Contains("已同步 god2_game.item_sets 與 item_set_members", sql, StringComparison.Ordinal);
        Assert.Contains("已同步 god2_game.monster_drops", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("RelationshipId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceHash", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Payload", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration398RebuildsReadableRelationshipRowsWithoutJoinFanout()
    {
        var sql = ReadSchema("398_rebuild_readable_relationship_candidate_rows_without_join_fanout.sql");

        Assert.Contains("TRUNCATE TABLE `god2_research`.`container_reward_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("TRUNCATE TABLE `god2_research`.`equipment_set_member_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("TRUNCATE TABLE `god2_research`.`monster_drop_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("WHEN EXISTS (", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`container_item_relationships` relationship_row", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`equipment_set_members` member_row", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`monster_drop_relationships` relationship_row", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("RelationshipId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceHash", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Payload", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration399PublishesDatabaseReadabilitySurfaceWithoutStoredPayloads()
    {
        var sql = ReadSchema("399_publish_database_readability_surface.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`database_readability_surface`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`database_field_readability_audit`", sql, StringComparison.Ordinal);
        Assert.Contains("`game_function_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("`safe_to_browse`", sql, StringComparison.Ordinal);
        Assert.Contains("`contains_tool_only_fields`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM information_schema.`TABLES` table_row", sql, StringComparison.Ordinal);
        Assert.Contains("FROM information_schema.`COLUMNS` column_row", sql, StringComparison.Ordinal);
        Assert.Contains("工具歸檔表保留給證據回溯", sql, StringComparison.Ordinal);
        Assert.Contains("可人工瀏覽；只保留已整理語意與對應狀態", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`payload` longtext", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`raw` longtext", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`hash` char", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration400CleansCandidateReadabilitySurfaceMachineFieldNames()
    {
        var sql = ReadSchema("400_clean_candidate_readability_surface_machine_field_names.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`merchant_inventory_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`quest_objective_candidate_rows`", sql, StringComparison.Ordinal);
        Assert.Contains("`has_unreviewed_steps` tinyint(1) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("`merchant_name_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("`formal_quest_name_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("`monster_name_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("formal_quest.`code` = CONCAT('quest_', candidate.`ClientQuestId`)", sql, StringComparison.Ordinal);
        Assert.Contains("人工檢查請看對應 candidate_rows 可讀表", sql, StringComparison.Ordinal);
        Assert.Contains("TRUNCATE TABLE `god2_research`.`database_field_readability_audit`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`RunId`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`SourceHash`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`SourceIdentity`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration401RefreshesReadabilitySurfaceAfterCandidateCleanup()
    {
        var sql = ReadSchema("401_refresh_readability_surface_after_candidate_cleanup.sql");

        Assert.Contains("UPDATE `god2_research`.`database_readability_surface` surface", sql, StringComparison.Ordinal);
        Assert.Contains("surface.`contains_tool_only_fields`", sql, StringComparison.Ordinal);
        Assert.Contains("可人工瀏覽；目前未發現工具追蹤欄位。", sql, StringComparison.Ordinal);
        Assert.Contains("TRUNCATE TABLE `god2_research`.`database_field_readability_audit`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_research`.`database_field_readability_audit`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration402RefinesReadableDatabaseGameFunctionLabels()
    {
        var sql = ReadSchema("402_refine_readable_database_game_function_labels.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`database_traditional_chinese_surface_audit`", sql, StringComparison.Ordinal);
        Assert.Contains("正式遊戲資料與人工可讀候選表欄位名稱", sql, StringComparison.Ordinal);
        Assert.Contains("沒有發現 zh_cn、name_original、simplified、original", sql, StringComparison.Ordinal);
        Assert.Contains("裝備打造、配方材料與產出資料", sql, StringComparison.Ordinal);
        Assert.Contains("裝備強化、強化材料與成功率資料", sql, StringComparison.Ordinal);
        Assert.Contains("裝備穿戴、屬性加成與外觀模型資料", sql, StringComparison.Ordinal);
        Assert.Contains("神仙模板、階級與基礎能力資料", sql, StringComparison.Ordinal);
        Assert.Contains("戰鬥陣型與隊伍站位資料", sql, StringComparison.Ordinal);
        Assert.Contains("技能狀態、增益減益與持續效果資料", sql, StringComparison.Ordinal);
        Assert.Contains("裝備套裝部位與套裝效果候選資料", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration403PublishesFormalTableServerCorrespondenceAudit()
    {
        var sql = ReadSchema("403_publish_formal_table_server_correspondence_audit.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`formal_table_server_correspondence`", sql, StringComparison.Ordinal);
        Assert.Contains("`direct_runtime_reference` tinyint(1) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("`tool_pipeline_reference` tinyint(1) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("'服務端直接對應'", sql, StringComparison.Ordinal);
        Assert.Contains("'工具同步或可讀檢查對應'", sql, StringComparison.Ordinal);
        Assert.Contains("'正式保留待接服務端'", sql, StringComparison.Ordinal);
        Assert.Contains("'formal_table_server_correspondence'", sql, StringComparison.Ordinal);
        Assert.Contains("'character_classes','class_level_stats','class_stat_growth','immortal_ranks','item_set_bonuses'", sql, StringComparison.Ordinal);
        Assert.Contains("未直接刪除有正式語意的資料表", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration404MarksCharacterClassTablesAsRuntimeCorresponded()
    {
        var sql = ReadSchema("404_mark_character_class_runtime_correspondence.sql");

        Assert.Contains("`table_name` IN ('character_classes','class_level_stats')", sql, StringComparison.Ordinal);
        Assert.Contains("`direct_runtime_reference` = 1", sql, StringComparison.Ordinal);
        Assert.Contains("'服務端直接對應'", sql, StringComparison.Ordinal);
        Assert.Contains("正式創角 runtime 已對應", sql, StringComparison.Ordinal);
        Assert.Contains("角色建立所需的正式職業與 1 級基礎資料", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration405MarksClassStatGrowthAsRuntimeCatalogCorresponded()
    {
        var sql = ReadSchema("405_mark_class_stat_growth_runtime_catalog_correspondence.sql");

        Assert.Contains("`table_name` = 'class_stat_growth'", sql, StringComparison.Ordinal);
        Assert.Contains("`direct_runtime_reference` = 1", sql, StringComparison.Ordinal);
        Assert.Contains("'服務端直接對應'", sql, StringComparison.Ordinal);
        Assert.Contains("正式 runtime catalog 已對應", sql, StringComparison.Ordinal);
        Assert.Contains("職業屬性成長已納入正式 runtime catalog", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration406MarksImmortalRanksAsRuntimeCatalogCorresponded()
    {
        var sql = ReadSchema("406_mark_immortal_ranks_runtime_catalog_correspondence.sql");

        Assert.Contains("`table_name` = 'immortal_ranks'", sql, StringComparison.Ordinal);
        Assert.Contains("`direct_runtime_reference` = 1", sql, StringComparison.Ordinal);
        Assert.Contains("'服務端直接對應'", sql, StringComparison.Ordinal);
        Assert.Contains("正式 runtime catalog 已對應", sql, StringComparison.Ordinal);
        Assert.Contains("神仙品階已納入正式 runtime catalog", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration407MarksPetGrowthArchetypesAsRuntimeCatalogCorresponded()
    {
        var sql = ReadSchema("407_mark_pet_growth_archetypes_runtime_catalog_correspondence.sql");

        Assert.Contains("`table_name` = 'pet_growth_archetypes'", sql, StringComparison.Ordinal);
        Assert.Contains("`direct_runtime_reference` = 1", sql, StringComparison.Ordinal);
        Assert.Contains("'服務端直接對應'", sql, StringComparison.Ordinal);
        Assert.Contains("正式 runtime catalog 已對應", sql, StringComparison.Ordinal);
        Assert.Contains("神仙與寵物成長類型已納入正式 runtime catalog", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration408MarksItemSetBonusesAsRuntimeCatalogCorrespondedWithoutEnablingBonuses()
    {
        var sql = ReadSchema("408_mark_item_set_bonuses_runtime_catalog_correspondence.sql");

        Assert.Contains("`table_name` = 'item_set_bonuses'", sql, StringComparison.Ordinal);
        Assert.Contains("`direct_runtime_reference` = 1", sql, StringComparison.Ordinal);
        Assert.Contains("runtime catalog", sql, StringComparison.Ordinal);
        Assert.Contains("item_set_bonuses", sql, StringComparison.Ordinal);
        Assert.Contains("release manifest", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE `god2_game`.`item_set_bonuses`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration409MarksMonsterDesignRulesAsRuntimeCatalogCorrespondedWithoutApplyingDesign()
    {
        var sql = ReadSchema("409_mark_monster_design_rules_runtime_catalog_correspondence.sql");

        Assert.Contains("'monster_combat_stat_design_rules'", sql, StringComparison.Ordinal);
        Assert.Contains("'monster_drop_design_rules'", sql, StringComparison.Ordinal);
        Assert.Contains("'monster_spawn_design_rules'", sql, StringComparison.Ordinal);
        Assert.Contains("`direct_runtime_reference` = 1", sql, StringComparison.Ordinal);
        Assert.Contains("runtime catalog", sql, StringComparison.Ordinal);
        Assert.Contains("release manifest", sql, StringComparison.Ordinal);
        Assert.Contains("不自動套用設計規則覆蓋正式怪物資料", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE `god2_game`.`monsters`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `god2_game`.`monster_drops`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `god2_game`.`monster_spawns`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration410PublishesSchemaConsolidationStatusWithoutDroppingLegacySchema()
    {
        var sql = ReadSchema("410_publish_schema_consolidation_status.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`database_schema_consolidation_status`", sql, StringComparison.Ordinal);
        Assert.Contains("'god2_game' AS `schema_name`", sql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL SELECT 'god2_player'", sql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL SELECT 'god2_game_meta'", sql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL SELECT 'god2_research'", sql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL SELECT 'god2'", sql, StringComparison.Ordinal);
        Assert.Contains("舊版混合 runtime 與來源資料", sql, StringComparison.Ordinal);
        Assert.Contains("禁止直接刪除", sql, StringComparison.Ordinal);
        Assert.Contains("下一批先盤點舊 god2", sql, StringComparison.Ordinal);
        Assert.Contains("'schema_consolidation_status'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP DATABASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSchema(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "database", "schema")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate database/schema.");
        }

        return File.ReadAllText(Path.Combine(directory.FullName, "database", "schema", name));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
