UPDATE `god2_game`.`merchants` merchant_row
JOIN `god2_game`.`npcs` npc_row
  ON npc_row.`npc_id` = merchant_row.`npc_id`
SET merchant_row.`enabled` = 1,
    merchant_row.`admin_note` = 'Enabled for the verified Map 19 grocery NPC interaction profile (handles 1504 and 4638).'
WHERE merchant_row.`merchant_id` = 286397401
  AND merchant_row.`npc_id` = 1075128734
  AND merchant_row.`name_zh_tw` = '雜貨老闆'
  AND npc_row.`npc_id` = 1075128734
  AND npc_row.`merchant_id` = 286397401
  AND npc_row.`interaction_family` = 'Merchant'
  AND npc_row.`enabled` = 1
  AND EXISTS (
      SELECT 1
      FROM `god2_game`.`npc_spawns` spawn_row
      JOIN `god2_game`.`maps` map_row
        ON map_row.`map_id` = spawn_row.`map_id`
      WHERE spawn_row.`npc_id` = npc_row.`npc_id`
        AND spawn_row.`observed_client_entity_handle` IN (1504, 4638)
        AND spawn_row.`enabled` = 1
        AND map_row.`client_map_id` = 19
        AND map_row.`client_area_id` = 4
        AND map_row.`enabled` = 1);
