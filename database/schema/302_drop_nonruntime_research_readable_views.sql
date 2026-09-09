-- Remove non-runtime research, legacy, queue, readiness, and review views from
-- the formal database surface. These views do not store gameplay data and are
-- not referenced by the runtime; owner-facing formal DB views must describe
-- actual gameplay content rather than temporary cleanup work queues.

DROP VIEW IF EXISTS `god2`.`vw_battle_runtime_table_function_overview_readable`;
DROP VIEW IF EXISTS `god2`.`vw_player_runtime_table_function_overview_readable`;

DROP VIEW IF EXISTS `god2_game`.`vw_capture_gap_priority_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_content_evidence_inventory_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_item_asset_validation`;
DROP VIEW IF EXISTS `god2_game`.`vw_item_catalog_health`;
DROP VIEW IF EXISTS `god2_game`.`vw_legacy_dialogs_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_legacy_items_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_legacy_monster_drop_tables_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_legacy_monsters_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_legacy_pet_content_profiles_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_legacy_quests_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_legacy_skills_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_legacy_world_interactions_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_combat_stat_design_candidates_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_combat_stat_design_queue_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_combat_stat_design_queue_summary_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_combat_stat_runtime_readiness_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_combat_stat_source_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_combat_stat_source_summary_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_drop_activation_review_queue_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_drop_activation_review_summary_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_drop_first_wave_test_candidates_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_drop_source_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_first_wave_drop_runtime_readiness_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_first_wave_spawn_runtime_readiness_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_first_wave_test_candidate_summary_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_runtime_table_function_overview_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_spawn_activation_review_queue_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_spawn_activation_review_summary_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_spawn_design_candidates_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_spawn_drop_runtime_readiness_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_spawn_first_wave_test_candidates_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_monster_spawn_source_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_pet_growth_manual_fill_queue`;
DROP VIEW IF EXISTS `god2_game`.`vw_pet_skill_mapping_queue`;
