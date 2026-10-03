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
fixture_name="grantfixture_$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"

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
    AND enabled=0 AND admin_note='InventoryGrantConcurrencyFixture'
);
DELETE s FROM inventory_item_identity_sequence AS s
JOIN character_inventory AS i ON i.inventory_id=s.PersistentInventoryItemId
WHERE i.character_id=@character;
DELETE FROM character_inventory WHERE character_id=@character;
DELETE FROM characters WHERE character_id=@character;
DELETE FROM accounts WHERE account_id=@account
  AND NOT EXISTS (SELECT 1 FROM characters WHERE account_id=@account);
COMMIT;
SELECT COUNT(*) FROM accounts WHERE username='$fixture_name';
SQL
  )"; then
    echo "Fixture 清理失敗：$fixture_name"
    exit 1
  fi

  echo "remaining_fixture_accounts=$remaining"
  if test "$remaining" != "0"; then
    echo "Fixture 未完整清理：$fixture_name"
    exit 1
  fi
  exit "$result"
}
trap cleanup EXIT

fixture_id="$(db_admin <<SQL
USE god2_player;
START TRANSACTION;
INSERT INTO accounts (username,password_hash,status)
VALUES ('$fixture_name','DISABLED_TEST_FIXTURE_NO_LOGIN','Disabled');
SET @account=LAST_INSERT_ID();
INSERT INTO characters (account_id,name,status,enabled,admin_note)
VALUES (@account,'$fixture_name','Disabled',0,'InventoryGrantConcurrencyFixture');
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
export GOD2_GRANT_FIXTURE_CHARACTER_ID="$fixture_id"

echo "臨時角色 ID：$fixture_id"
if test "${1:-}" = "--all"; then
  test -n "${GOD2_TEST_PASSWORD:-}" || {
    echo "缺少 GOD2_TEST_PASSWORD，停止完整測試"
    exit 1
  }
  ./Automation/update-progress-all.sh
else
dotnet test \
  tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj \
  --configuration Release \
  --filter 'FullyQualifiedName~InventoryGrantConcurrencyTests' \
  --verbosity minimal

fi
