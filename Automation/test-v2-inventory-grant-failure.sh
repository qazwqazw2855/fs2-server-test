#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

set -a
. /home/ubuntu/.config/god2/server-v2.env
set +a

test "${GOD2_DB_HOST:-}" = "127.0.0.1"
test "${GOD2_DB_PORT:-}" = "3308"

container="${GOD2_DB_CONTAINER:-god2-runtime-db-test}"
test "$container" = "god2-runtime-db-test"

export GOD2_GRANT_FAULT_USER="grantfault_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
export GOD2_GRANT_FAULT_PASSWORD="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"

db_admin() {
  sudo docker exec -i "$container" sh -lc '
    password="$(cat "$MARIADB_ROOT_PASSWORD_FILE")"
    exec mariadb -uroot -p"$password" --batch --raw --skip-column-names
  '
}

cleanup() {
  result=$?
  trap - EXIT
  if ! db_admin <<SQL
DROP USER IF EXISTS '$GOD2_GRANT_FAULT_USER'@'172.17.0.1';
SELECT COUNT(*) FROM mysql.user WHERE User='$GOD2_GRANT_FAULT_USER';
SQL
  then
    echo "臨時測試帳號清理失敗：$GOD2_GRANT_FAULT_USER"
    exit 1
  fi
  echo "Failure-test exit code: $result"
  exit "$result"
}
trap cleanup EXIT

db_admin <<SQL
CREATE USER '$GOD2_GRANT_FAULT_USER'@'172.17.0.1'
IDENTIFIED BY '$GOD2_GRANT_FAULT_PASSWORD';

GRANT CREATE TEMPORARY TABLES ON god2_player.*
TO '$GOD2_GRANT_FAULT_USER'@'172.17.0.1';
GRANT SELECT ON god2_player.characters
TO '$GOD2_GRANT_FAULT_USER'@'172.17.0.1';
GRANT SELECT,UPDATE ON god2_player.player_inventory_state
TO '$GOD2_GRANT_FAULT_USER'@'172.17.0.1';
GRANT SELECT,INSERT,UPDATE,DELETE ON god2_player.character_inventory
TO '$GOD2_GRANT_FAULT_USER'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_item_identity_sequence
TO '$GOD2_GRANT_FAULT_USER'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_transaction_idempotency
TO '$GOD2_GRANT_FAULT_USER'@'172.17.0.1';
GRANT SELECT ON god2_player.inventory_audit_ledger
TO '$GOD2_GRANT_FAULT_USER'@'172.17.0.1';
GRANT SELECT ON god2_game.items
TO '$GOD2_GRANT_FAULT_USER'@'172.17.0.1';
SQL

if test "${1:-}" = "--all"; then
  ./Automation/test-v2-inventory-grant-concurrency.sh --all
else
dotnet test \
  tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj \
  --configuration Release \
  --filter 'FullyQualifiedName~InventoryGrantFailureTests' \
  --verbosity minimal

fi
