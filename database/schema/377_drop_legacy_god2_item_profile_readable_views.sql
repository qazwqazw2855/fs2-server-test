SET @legacy_item_profile_view_refs := (
    SELECT COUNT(*)
    FROM information_schema.VIEWS view_row
    WHERE view_row.TABLE_SCHEMA IN ('god2', 'god2_game', 'god2_player', 'god2_research')
      AND view_row.TABLE_NAME NOT IN (
          'vw_blackbox_internal_identifier_cleanup_observations_readable',
          'vw_blackbox_item_container_observations_readable',
          'vw_blackbox_item_container_set_sanitized_observations_readable',
          'vw_blackbox_pet_panel_priority_observations_readable',
          'vw_blackbox_pet_skill_static_observations_readable',
          'vw_items_readable',
          'vw_items_sanitized_readable',
          'vw_item_content_profiles_readable',
          'vw_item_content_profiles_sanitized_readable',
          'vw_container_item_relationships_readable',
          'vw_container_item_relationships_sanitized_readable',
          'vw_equipment_sets_readable',
          'vw_equipment_sets_sanitized_readable',
          'vw_pet_content_profiles_readable',
          'vw_pet_egg_relationships_readable',
          'vw_pet_profile_summary_readable'
      )
      AND (
             LOWER(view_row.VIEW_DEFINITION) LIKE '%vw_items_readable%'
          OR LOWER(view_row.VIEW_DEFINITION) LIKE '%vw_items_sanitized_readable%'
          OR LOWER(view_row.VIEW_DEFINITION) LIKE '%vw_item_content_profiles_readable%'
          OR LOWER(view_row.VIEW_DEFINITION) LIKE '%vw_item_content_profiles_sanitized_readable%'
          OR LOWER(view_row.VIEW_DEFINITION) LIKE '%vw_container_item_relationships_readable%'
          OR LOWER(view_row.VIEW_DEFINITION) LIKE '%vw_container_item_relationships_sanitized_readable%'
          OR LOWER(view_row.VIEW_DEFINITION) LIKE '%vw_equipment_sets_readable%'
          OR LOWER(view_row.VIEW_DEFINITION) LIKE '%vw_equipment_sets_sanitized_readable%'
          OR LOWER(view_row.VIEW_DEFINITION) LIKE '%vw_pet_content_profiles_readable%'
          OR LOWER(view_row.VIEW_DEFINITION) LIKE '%vw_pet_egg_relationships_readable%'
          OR LOWER(view_row.VIEW_DEFINITION) LIKE '%vw_pet_profile_summary_readable%'
      )
);

SET @legacy_item_profile_view_rows := (
    SELECT COUNT(*)
    FROM information_schema.VIEWS view_row
    WHERE view_row.TABLE_SCHEMA = 'god2'
      AND view_row.TABLE_NAME IN (
          'vw_blackbox_internal_identifier_cleanup_observations_readable',
          'vw_blackbox_item_container_observations_readable',
          'vw_blackbox_item_container_set_sanitized_observations_readable',
          'vw_blackbox_pet_panel_priority_observations_readable',
          'vw_blackbox_pet_skill_static_observations_readable',
          'vw_items_readable',
          'vw_items_sanitized_readable',
          'vw_item_content_profiles_readable',
          'vw_item_content_profiles_sanitized_readable',
          'vw_container_item_relationships_readable',
          'vw_container_item_relationships_sanitized_readable',
          'vw_equipment_sets_readable',
          'vw_equipment_sets_sanitized_readable',
          'vw_pet_content_profiles_readable',
          'vw_pet_egg_relationships_readable',
          'vw_pet_profile_summary_readable'
      )
);

SET @legacy_item_profile_guard_sql := IF(
    @legacy_item_profile_view_refs = 0,
    'SELECT 1',
    'SIGNAL SQLSTATE ''45000'' SET MESSAGE_TEXT = ''legacy item/profile readable views still have external dependents'''
);

PREPARE legacy_item_profile_guard_stmt FROM @legacy_item_profile_guard_sql;
EXECUTE legacy_item_profile_guard_stmt;
DEALLOCATE PREPARE legacy_item_profile_guard_stmt;

DROP VIEW IF EXISTS `god2`.`vw_blackbox_internal_identifier_cleanup_observations_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_item_container_observations_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_item_container_set_sanitized_observations_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_pet_panel_priority_observations_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_pet_skill_static_observations_readable`;
DROP VIEW IF EXISTS `god2`.`vw_pet_profile_summary_readable`;
DROP VIEW IF EXISTS `god2`.`vw_pet_egg_relationships_readable`;
DROP VIEW IF EXISTS `god2`.`vw_pet_content_profiles_readable`;
DROP VIEW IF EXISTS `god2`.`vw_equipment_sets_sanitized_readable`;
DROP VIEW IF EXISTS `god2`.`vw_container_item_relationships_sanitized_readable`;
DROP VIEW IF EXISTS `god2`.`vw_item_content_profiles_sanitized_readable`;
DROP VIEW IF EXISTS `god2`.`vw_items_sanitized_readable`;
DROP VIEW IF EXISTS `god2`.`vw_equipment_sets_readable`;
DROP VIEW IF EXISTS `god2`.`vw_container_item_relationships_readable`;
DROP VIEW IF EXISTS `god2`.`vw_item_content_profiles_readable`;
DROP VIEW IF EXISTS `god2`.`vw_items_readable`;
