#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

set -a
. /home/ubuntu/.config/god2/server-v2.env
set +a

test "${GOD2_DB_HOST:-}" = "127.0.0.1"
test "${GOD2_DB_PORT:-}" = "3308"
test "${GOD2_DB_USER:-}" = "god2_v2"
test -n "${GOD2_DB_PASSWORD:-}"
export GOD2_RUN_DB_INTEGRATION=1

container="${GOD2_DB_CONTAINER:-god2-runtime-db-test}"
test "$container" = "god2-runtime-db-test"
fixture_name="stackfixture_$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"

export GOD2_STACK_FIXTURE_USER="stacktest_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
export GOD2_STACK_FIXTURE_PASSWORD="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"

db_admin() {
  sudo docker exec -i "$container" sh -lc '
    password="$(cat "$MARIADB_ROOT_PASSWORD_FILE")"
    exec mariadb -uroot -p"$password" --batch --raw --skip-column-names
  '
}

cleanup() {
  result=$?
  trap - EXIT
  echo "=== 清理臨時 FIXTURE ==="

  if ! remaining="$(db_admin <<SQL
USE god2_player;
START TRANSACTION;
SET @account=(
  SELECT account_id FROM accounts
  WHERE username='$fixture_name' AND status='Disabled'
);
SET @character=(
  SELECT character_id FROM characters
  WHERE account_id=@account AND name='$fixture_name'
    AND enabled=0 AND admin_note='InventoryStackCommittedFixture'
);
DELETE s FROM inventory_item_identity_sequence AS s
JOIN character_inventory AS i ON i.inventory_id=s.PersistentInventoryItemId
WHERE i.character_id=@character;
DELETE FROM inventory_audit_ledger WHERE CharacterId=@character;
DELETE FROM inventory_transaction_idempotency WHERE CharacterId=@character;
DELETE FROM character_inventory WHERE character_id=@character;
DELETE FROM player_inventory_state WHERE CharacterId=@character;
DELETE FROM characters WHERE character_id=@character;
DELETE FROM accounts WHERE account_id=@account
  AND NOT EXISTS (SELECT 1 FROM characters WHERE account_id=@account);
COMMIT;
DROP USER IF EXISTS '$GOD2_STACK_FIXTURE_USER'@'172.17.0.1';
SELECT
  (SELECT COUNT(*) FROM accounts WHERE username='$fixture_name')
  + (SELECT COUNT(*) FROM mysql.user WHERE User='$GOD2_STACK_FIXTURE_USER');
SQL
  )"; then
    echo "Fixture 清理失敗：$fixture_name"
    exit 1
  fi

  echo "remaining_stack_fixture=$remaining"
  if test "$remaining" != "0"; then
    echo "Fixture 未完整清理：$fixture_name"
    exit 1
  fi
  exit "$result"
}
trap cleanup EXIT

db_admin <<SQL
CREATE USER '$GOD2_STACK_FIXTURE_USER'@'172.17.0.1'
IDENTIFIED BY '$GOD2_STACK_FIXTURE_PASSWORD';
GRANT CREATE TEMPORARY TABLES ON god2_game.*
TO '$GOD2_STACK_FIXTURE_USER'@'172.17.0.1';
GRANT SELECT ON god2_player.characters
TO '$GOD2_STACK_FIXTURE_USER'@'172.17.0.1';
GRANT SELECT,UPDATE ON god2_player.player_inventory_state
TO '$GOD2_STACK_FIXTURE_USER'@'172.17.0.1';
GRANT SELECT,INSERT,UPDATE,DELETE ON god2_player.character_inventory
TO '$GOD2_STACK_FIXTURE_USER'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_item_identity_sequence
TO '$GOD2_STACK_FIXTURE_USER'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_transaction_idempotency
TO '$GOD2_STACK_FIXTURE_USER'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_audit_ledger
TO '$GOD2_STACK_FIXTURE_USER'@'172.17.0.1';
SQL

fixture_id="$(db_admin <<SQL
USE god2_player;
START TRANSACTION;
INSERT INTO accounts (username,password_hash,status)
VALUES ('$fixture_name','DISABLED_TEST_FIXTURE_NO_LOGIN','Disabled');
SET @account=LAST_INSERT_ID();
INSERT INTO characters (account_id,name,status,enabled,admin_note)
VALUES (@account,'$fixture_name','Disabled',0,'InventoryStackCommittedFixture');
SET @character=LAST_INSERT_ID();
INSERT INTO player_inventory_state
  (CharacterId,InventoryId,Capacity,InventoryVersion,
   MutationSequence,DirtyState,UpdatedAtUtc)
VALUES (@character,UUID(),8,0,0,'Clean',UTC_TIMESTAMP(6));
COMMIT;
SELECT @character;
SQL
)"
[[ "$fixture_id" =~ ^[0-9]+$ ]]
test "$fixture_id" -gt 1
export GOD2_STACK_FIXTURE_CHARACTER_ID="$fixture_id"

echo "臨時角色 ID：$fixture_id"
if test "${1:-}" = "--all"; then
  test -n "${GOD2_TEST_PASSWORD:-}" || {
    echo "缺少 GOD2_TEST_PASSWORD，停止完整測試"
    exit 1
  }
  ./Automation/test-v2-merchant-command.sh --all
else
dotnet test \
  tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj \
  --configuration Release \
  --filter 'FullyQualifiedName~InventoryGrantStackCommittedTests' \
  --verbosity minimal

fi
