-- AWS runtime DB account: existing Docker bridge host identity.
-- Apply with a DB administrator; does not create users or change passwords.

GRANT SELECT
ON god2_player.characters
TO 'god2_v2'@'172.17.0.1';

GRANT SELECT
ON god2_game.items
TO 'god2_v2'@'172.17.0.1';

GRANT SELECT
ON god2_game.item_registry
TO 'god2_v2'@'172.17.0.1';

GRANT SELECT, UPDATE
ON god2_player.player_inventory_state
TO 'god2_v2'@'172.17.0.1';

GRANT SELECT, INSERT, UPDATE, DELETE
ON god2_player.character_inventory
TO 'god2_v2'@'172.17.0.1';

GRANT SELECT, INSERT
ON god2_player.inventory_item_identity_sequence
TO 'god2_v2'@'172.17.0.1';

GRANT SELECT, INSERT
ON god2_player.inventory_transaction_idempotency
TO 'god2_v2'@'172.17.0.1';

GRANT SELECT, INSERT
ON god2_player.inventory_audit_ledger
TO 'god2_v2'@'172.17.0.1';

-- Read-only verification that an item grant leaves the wallet unchanged.
GRANT SELECT
ON god2_player.player_currency_balances
TO 'god2_v2'@'172.17.0.1';
