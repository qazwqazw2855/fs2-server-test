-- Read-only checks derived from the isolated merchant/position/quest preflight and EXIT cleanup rules.
-- Columns: check_name, actual_value, expected_value; condition is actual_value = expected_value.
SELECT 'god2_player.accounts.empty', COUNT(*), 0 FROM god2_player.accounts
UNION ALL
SELECT 'god2_player.characters.empty', COUNT(*), 0 FROM god2_player.characters
UNION ALL
SELECT 'god2_player.character_inventory.empty', COUNT(*), 0 FROM god2_player.character_inventory
UNION ALL
SELECT 'god2_player.inventory_item_identity_sequence.empty', COUNT(*), 0 FROM god2_player.inventory_item_identity_sequence
UNION ALL
SELECT 'god2_player.inventory_audit_ledger.empty', COUNT(*), 0 FROM god2_player.inventory_audit_ledger
UNION ALL
SELECT 'god2_player.inventory_transaction_idempotency.empty', COUNT(*), 0 FROM god2_player.inventory_transaction_idempotency
UNION ALL
SELECT 'god2_player.v2_merchant_purchase_journal.empty', COUNT(*), 0 FROM god2_player.v2_merchant_purchase_journal
UNION ALL
SELECT 'god2_player.v2_merchant_sale_journal.empty', COUNT(*), 0 FROM god2_player.v2_merchant_sale_journal
UNION ALL
SELECT 'god2_player.player_inventory_state.empty', COUNT(*), 0 FROM god2_player.player_inventory_state
UNION ALL
SELECT 'god2_player.player_currency_balances.empty', COUNT(*), 0 FROM god2_player.player_currency_balances
UNION ALL
SELECT 'god2_player.v2_quest_instances.empty', COUNT(*), 0 FROM god2_player.v2_quest_instances
UNION ALL
SELECT 'god2_player.v2_quest_reward_snapshots.empty', COUNT(*), 0 FROM god2_player.v2_quest_reward_snapshots
UNION ALL
SELECT 'god2_player.v2_quest_reward_claims.empty', COUNT(*), 0 FROM god2_player.v2_quest_reward_claims
UNION ALL
SELECT 'god2_player.v2_quest_objective_progress.empty', COUNT(*), 0 FROM god2_player.v2_quest_objective_progress
UNION ALL
SELECT 'god2_player.v2_quest_progress_events.empty', COUNT(*), 0 FROM god2_player.v2_quest_progress_events
UNION ALL
SELECT 'god2_game.merchants.empty', COUNT(*), 0 FROM god2_game.merchants
UNION ALL
SELECT 'god2_game.merchant_inventory.empty', COUNT(*), 0 FROM god2_game.merchant_inventory
UNION ALL
SELECT 'god2_game.items.total', COUNT(*), 1 FROM god2_game.items
UNION ALL
SELECT 'god2_game.items.canonical', COUNT(*), 1 FROM god2_game.items WHERE item_id=253231541
UNION ALL
SELECT 'god2_game.item_registry.total', COUNT(*), 1 FROM god2_game.item_registry
UNION ALL
SELECT 'god2_game.item_registry.canonical', COUNT(*), 1 FROM god2_game.item_registry WHERE item_id=253231541
UNION ALL
SELECT 'synthetic_position_maps.absent', COUNT(*), 0 FROM god2_game.maps WHERE name_zh_tw='SYNTHETIC_POSITION_FIXTURE' OR code LIKE 'positionfixture\_%'
UNION ALL
SELECT 'synthetic_quests.absent', COUNT(*), 0 FROM god2_game.quests WHERE quest_type='FixtureOnly' OR code LIKE 'questfixture\_%'
UNION ALL
SELECT 'temporary_users.absent', COUNT(*), 0 FROM mysql.user WHERE User REGEXP '^(shoptest|shopfault|shopreceipt|shopaudit|positiontest|questtest|questfault)_[0-9a-f]{16}$'
UNION ALL
SELECT 'fault_triggers.absent', COUNT(*), 0 FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA='god2_player' AND TRIGGER_NAME REGEXP '^fixture_(shopreceipt|shopaudit)_[0-9a-f]{16}$';
