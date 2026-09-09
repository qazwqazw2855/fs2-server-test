-- Fail closed when an initially imported child row points at a disabled parent.
-- This corrects only canonical runtime eligibility and never mutates the evidence database.
SET @god2_sync_mode := 1;

UPDATE `god2_game`.`portals` portal_row
LEFT JOIN `god2_game`.`maps` source_map ON source_map.`map_id` = portal_row.`source_map_id`
LEFT JOIN `god2_game`.`maps` destination_map ON destination_map.`map_id` = portal_row.`destination_map_id`
SET portal_row.`enabled` = 0,
    portal_row.`admin_note` = CONCAT_WS('；', NULLIF(portal_row.`admin_note`, ''), '父地圖尚未進入正式 Runtime，依 Evidence gate 自動停用')
WHERE portal_row.`enabled` = 1
  AND (COALESCE(source_map.`enabled`, 0) <> 1 OR COALESCE(destination_map.`enabled`, 0) <> 1)
  AND NOT EXISTS (
      SELECT 1
      FROM `god2_game_meta`.`admin_field_locks` field_lock
      WHERE field_lock.`entity_schema` = 'god2_game'
        AND field_lock.`entity_table` = 'portals'
        AND field_lock.`entity_id` = CONVERT(CAST(portal_row.`portal_id` AS CHAR) USING ascii) COLLATE ascii_bin
        AND field_lock.`field_name` = 'enabled'
  );

SET @god2_sync_mode := 0;
