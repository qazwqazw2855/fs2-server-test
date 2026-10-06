#!/usr/bin/env bash
set -euo pipefail
test "$#" -eq 0
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

set -a
. /home/ubuntu/.config/god2/server-v2.env
set +a
test "${GOD2_DB_HOST:-}" = "127.0.0.1"
test "${GOD2_DB_PORT:-}" = "3308"
container="${GOD2_DB_CONTAINER:-god2-runtime-db-test}"
test "$container" = "god2-runtime-db-test"

db_admin() {
  sudo docker exec -i "$container" sh -lc '
    password="$(cat "$MARIADB_ROOT_PASSWORD_FILE")"
    exec mariadb -uroot -p"$password" --batch --raw --skip-column-names
  '
}

fixture_name="shopfixture_$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"
test_user="shoptest_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
test_password="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
fault_user="shopfault_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
merchant_id="$(python3 -c 'import secrets; print(4000000000 + secrets.randbelow(1000000000))')"

cleanup() {
  result=$?
  trap - EXIT
  if ! remaining="$(db_admin <<SQL
USE god2_player;
START TRANSACTION;
SET @account=(SELECT account_id FROM accounts
  WHERE username='$fixture_name' AND status='Disabled');
SET @character=(SELECT character_id FROM characters
  WHERE account_id=@account AND name='$fixture_name'
    AND enabled=0 AND admin_note='MerchantPurchaseFixture');
DELETE s FROM inventory_item_identity_sequence s
LEFT JOIN character_inventory i ON i.inventory_id=s.PersistentInventoryItemId
LEFT JOIN inventory_audit_ledger a ON a.InventoryItemId=s.PersistentInventoryItemId
WHERE i.character_id=@character OR a.CharacterId=@character;
DELETE FROM character_inventory WHERE character_id=@character;
DELETE FROM characters WHERE character_id=@character;
DELETE FROM accounts WHERE account_id=@account
  AND NOT EXISTS (SELECT 1 FROM characters WHERE account_id=@account);
DELETE i FROM god2_game.merchant_inventory i
JOIN god2_game.merchants m ON m.merchant_id=i.merchant_id
WHERE m.merchant_id=$merchant_id AND m.admin_note='$fixture_name';
DELETE FROM god2_game.merchants
WHERE merchant_id=$merchant_id AND admin_note='$fixture_name';
COMMIT;
DROP USER IF EXISTS '$test_user'@'172.17.0.1';
DROP USER IF EXISTS '$fault_user'@'172.17.0.1';
SELECT
  (SELECT COUNT(*) FROM accounts WHERE username='$fixture_name')
  + (SELECT COUNT(*) FROM mysql.user WHERE User IN ('$test_user','$fault_user'))
  + (SELECT COUNT(*) FROM god2_game.merchants WHERE admin_note='$fixture_name');
SQL
  )"; then
    echo "Fixture 清理失敗：$fixture_name"
    exit 1
  fi
  echo "remaining_shop_fixture=$remaining"
  test "$remaining" = "0" || exit 1
  echo "Merchant-TCP-test exit code: $result"
  exit "$result"
}
trap cleanup EXIT

db_admin <<SQL
CREATE USER '$test_user'@'172.17.0.1' IDENTIFIED BY '$test_password';
GRANT SELECT ON god2_player.characters TO '$test_user'@'172.17.0.1';
GRANT SELECT,UPDATE ON god2_player.player_inventory_state TO '$test_user'@'172.17.0.1';
GRANT SELECT,UPDATE ON god2_player.player_currency_balances TO '$test_user'@'172.17.0.1';
GRANT SELECT,INSERT,UPDATE,DELETE ON god2_player.character_inventory TO '$test_user'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_item_identity_sequence TO '$test_user'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_transaction_idempotency TO '$test_user'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_audit_ledger TO '$test_user'@'172.17.0.1';
GRANT SELECT ON god2_game.items TO '$test_user'@'172.17.0.1';
GRANT SELECT ON god2_game.merchants TO '$test_user'@'172.17.0.1';
GRANT SELECT ON god2_game.merchant_inventory TO '$test_user'@'172.17.0.1';
CREATE USER '$fault_user'@'172.17.0.1' IDENTIFIED BY '$test_password';
GRANT SELECT ON god2_player.characters TO '$fault_user'@'172.17.0.1';
GRANT SELECT,UPDATE ON god2_player.player_inventory_state TO '$fault_user'@'172.17.0.1';
GRANT SELECT ON god2_player.player_currency_balances TO '$fault_user'@'172.17.0.1';
GRANT SELECT,INSERT,UPDATE,DELETE ON god2_player.character_inventory TO '$fault_user'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_item_identity_sequence TO '$fault_user'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_transaction_idempotency TO '$fault_user'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_audit_ledger TO '$fault_user'@'172.17.0.1';
GRANT SELECT ON god2_game.items TO '$fault_user'@'172.17.0.1';
GRANT SELECT ON god2_game.merchants TO '$fault_user'@'172.17.0.1';
GRANT SELECT ON god2_game.merchant_inventory TO '$fault_user'@'172.17.0.1';
SQL

fixture_id="$(db_admin <<SQL
USE god2_player;
START TRANSACTION;
INSERT INTO accounts (username,password_hash,status)
VALUES ('$fixture_name','DISABLED_TEST_FIXTURE_NO_LOGIN','Disabled');
SET @account=LAST_INSERT_ID();
INSERT INTO characters (account_id,name,status,enabled,admin_note)
VALUES (@account,'$fixture_name','Disabled',0,'MerchantPurchaseFixture');
SET @character=LAST_INSERT_ID();
INSERT INTO player_inventory_state
  (CharacterId,InventoryId,Capacity,InventoryVersion,MutationSequence,DirtyState,UpdatedAtUtc)
VALUES (@character,UUID(),8,0,0,'Clean',UTC_TIMESTAMP(6));
INSERT INTO player_currency_balances
  (CharacterId,CurrencyType,Balance,Version,DirtyState,UpdatedAtUtc)
VALUES (@character,'Gold',100,0,'Clean',UTC_TIMESTAMP(6));
INSERT INTO god2_game.merchants
  (merchant_id,name_zh_tw,buyback_enabled,enabled,admin_note)
VALUES ($merchant_id,'交易測試商店',1,1,'$fixture_name');
INSERT INTO god2_game.merchant_inventory
  (merchant_inventory_id,merchant_id,item_id,selling_price,purchasing_price,pack_count,quantity_limit,enabled)
VALUES ($merchant_id,$merchant_id,253231541,40,4,1,1,1);
COMMIT;
SELECT @character;
SQL
)"
[[ "$fixture_id" =~ ^[0-9]+$ ]]
test "$fixture_id" -gt 1
export GOD2_RUN_DB_INTEGRATION=1
export GOD2_MERCHANT_COMMAND_FIXTURE_CHARACTER_ID="$fixture_id"
export GOD2_MERCHANT_COMMAND_FIXTURE_MERCHANT_ID="$merchant_id"
export GOD2_MERCHANT_COMMAND_FIXTURE_USER="$test_user"
export GOD2_MERCHANT_COMMAND_FIXTURE_PASSWORD="$test_password"
export GOD2_MERCHANT_COMMAND_FAULT_USER="$fault_user"

dotnet test \
  tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj \
  --configuration Release \
  --filter 'FullyQualifiedName~InventoryRestoreTcpBlockedIntegrationTests' \
  --verbosity minimal
