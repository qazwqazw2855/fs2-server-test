#!/usr/bin/env bash
set -euo pipefail

container="${GOD2_DB_CONTAINER:-god2-runtime-db-test}"
command -v sudo >/dev/null
command -v docker >/dev/null

cat <<'SQL' | sudo docker exec -i "$container" sh -lc '
password="$(cat "$MARIADB_ROOT_PASSWORD_FILE")"
exec mariadb -uroot -p"$password" --batch --raw
'
-- Read-only Server V2 inventory readiness snapshot. No item mutations.
SELECT COUNT(*) AS inventory_states,
       COALESCE(SUM(Capacity <= 0),0) AS invalid_capacity,
       COALESCE(SUM(InventoryVersion < 0),0) AS negative_version,
       COALESCE(SUM(MutationSequence < 0),0) AS negative_mutation_sequence
FROM god2_player.player_inventory_state;

SELECT COUNT(*) AS active_slots,
       COALESCE(SUM(s.CharacterId IS NULL),0) AS slots_without_state,
       COALESCE(SUM(i.slot_index < 0 OR
                    (s.CharacterId IS NOT NULL AND i.slot_index >= s.Capacity)),0)
         AS slots_outside_capacity,
       COALESCE(SUM(i.quantity IS NULL OR i.quantity <= 0),0) AS invalid_quantity,
       COALESCE(SUM(i.quantity > 2147483647),0) AS quantity_outside_v2_int_range,
       COALESCE(SUM(i.item_instance_metadata IS NULL),0) AS null_metadata,
       COALESCE(SUM(g.item_id IS NULL),0) AS slots_without_formal_item,
       COALESCE(SUM(g.item_id IS NOT NULL AND g.maximum_stack IS NOT NULL
                    AND i.quantity > g.maximum_stack),0) AS above_declared_stack_limit
FROM god2_player.character_inventory AS i
LEFT JOIN god2_player.player_inventory_state AS s
  ON s.CharacterId = i.character_id
LEFT JOIN god2_game.items AS g
  ON g.item_id = i.item_id
WHERE i.enabled = 1 AND i.deleted_at_utc IS NULL;

SELECT COUNT(*) AS states_with_active_slots_over_capacity
FROM god2_player.player_inventory_state AS s
JOIN (
  SELECT character_id, COUNT(*) AS used_slots
  FROM god2_player.character_inventory
  WHERE enabled = 1 AND deleted_at_utc IS NULL
  GROUP BY character_id
) AS used ON used.character_id = s.CharacterId
WHERE used.used_slots > s.Capacity;
SQL
