UPDATE `god2_game`.`portals` portal_row
JOIN `god2_game`.`maps` source_map
  ON source_map.`map_id` = portal_row.`source_map_id`
JOIN `god2_game`.`maps` destination_map
  ON destination_map.`map_id` = portal_row.`destination_map_id`
SET portal_row.`source_x` = 16,
    portal_row.`source_y` = 20,
    portal_row.`source_radius` = 1,
    portal_row.`admin_note` = 'Formal external client observation 2026-08-12: map 19 visual exit reaches 16,20; 28,34 is the map 19 arrival position.'
WHERE source_map.`client_map_id` = 19
  AND source_map.`client_area_id` = 4
  AND destination_map.`client_map_id` = 3
  AND destination_map.`client_area_id` = 4
  AND portal_row.`enabled` = 1;
