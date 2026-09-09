-- The Map 19 grocery NPC template is shared by the two build-pinned handles
-- 1504 and 4638.  Migration 113 enabled its Merchant binding but left the
-- canonical NPC evidence status at the older EvidenceBlocked value.
UPDATE `god2_game`.`npcs` npc_row
JOIN `god2_game`.`merchants` merchant_row
  ON merchant_row.`merchant_id` = npc_row.`merchant_id`
 AND merchant_row.`npc_id` = npc_row.`npc_id`
SET npc_row.`evidence_status` = 'Derived',
    npc_row.`admin_note` = 'Derived from two build-pinned Map 19 grocery spawns (handles 1504 and 4638); Merchant open is enabled, stock remains evidence-gated.'
WHERE npc_row.`npc_id` = 1075128734
  AND npc_row.`interaction_family` = 'Merchant'
  AND npc_row.`merchant_id` = 286397401
  AND npc_row.`enabled` = 1
  AND merchant_row.`enabled` = 1
  AND (
      SELECT COUNT(DISTINCT spawn_row.`observed_client_entity_handle`)
      FROM `god2_game`.`npc_spawns` spawn_row
      JOIN `god2_game`.`maps` map_row
        ON map_row.`map_id` = spawn_row.`map_id`
      WHERE spawn_row.`npc_id` = npc_row.`npc_id`
        AND spawn_row.`observed_client_entity_handle` IN (1504, 4638)
        AND spawn_row.`wire_evidence_status` = 'Verified'
        AND spawn_row.`identity_evidence_status` = 'Verified'
        AND spawn_row.`coordinate_evidence_status` = 'Verified'
        AND spawn_row.`service_evidence_status` = 'Derived'
        AND spawn_row.`enabled` = 1
        AND map_row.`client_map_id` = 19
        AND map_row.`client_area_id` = 4
        AND map_row.`enabled` = 1
  ) = 2;

