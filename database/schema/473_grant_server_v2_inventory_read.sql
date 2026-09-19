-- Server V2 reads authoritative inventory snapshots without mutation rights.
GRANT SELECT
ON `god2_player`.`player_inventory_state`
TO `god2_runtime_role`;

GRANT SELECT
ON `god2_player`.`character_inventory`
TO `god2_runtime_role`;
