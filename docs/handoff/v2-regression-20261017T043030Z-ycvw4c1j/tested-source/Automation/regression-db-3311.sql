-- Read-only checks based on EnabledPortalBuildIntegrationTests and CharacterOne_InventorySnapshot_IsReadOnlyAndComplete.
-- Columns: check_name, actual_value, expected_value; condition is actual_value = expected_value.
SELECT 'enabled_portals.expected_ids', COUNT(*), 5
FROM god2_game.portals p
JOIN god2_game.maps s ON s.map_id=p.source_map_id
JOIN god2_game.maps d ON d.map_id=p.destination_map_id
WHERE p.enabled=1 AND s.enabled=1 AND d.enabled=1
AND p.portal_id IN (1,2,3,4,170015007)
UNION ALL
SELECT 'enabled_portals.invalid_build_or_destination', COUNT(*), 0
FROM god2_game.portals p
JOIN god2_game.maps s ON s.map_id=p.source_map_id
JOIN god2_game.maps d ON d.map_id=p.destination_map_id
WHERE p.enabled=1 AND s.enabled=1 AND d.enabled=1
AND (p.portal_id NOT IN (1,2,3,4,170015007)
 OR NOT ((s.client_build_id='god2-opt-6b127086e0c0'
 AND d.client_build_id='god2-opt-6b127086e0c0'
 AND p.destination_x BETWEEN d.minimum_x AND d.maximum_x
 AND p.destination_y BETWEEN d.minimum_y AND d.maximum_y) <=> 1))
UNION ALL
SELECT 'character_one.inventory_state', COUNT(*), 1
FROM god2_player.player_inventory_state
WHERE CharacterId=1 AND Capacity=32 AND InventoryVersion=1
 AND MutationSequence=1 AND DirtyState='Clean'
UNION ALL
SELECT 'character_one.inventory_count', COUNT(*), 1
FROM god2_player.character_inventory WHERE character_id=1
UNION ALL
SELECT 'character_one.inventory_slot_zero', COUNT(*), 1
FROM god2_player.character_inventory
WHERE character_id=1 AND slot_index=0 AND item_id=253231541 AND quantity=1
UNION ALL
SELECT 'temporary_baseline_fault_users.absent', COUNT(*), 0
FROM mysql.user WHERE User REGEXP '^grantfault_[0-9a-f]{16}$';
