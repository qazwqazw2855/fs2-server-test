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

fixture_name="positionfixture_$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"
test_user="positiontest_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
test_password="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"

db_admin() {
  sudo docker exec -i "$container" sh -lc '
    password="$(cat "$MARIADB_ROOT_PASSWORD_FILE")"
    exec mariadb -uroot -p"$password" --batch --raw --skip-column-names
  '
}

cleanup() {
  result=$?
  trap - EXIT
  if ! remaining="$(db_admin <<SQL
USE god2_player;
START TRANSACTION;
DELETE c FROM characters c
JOIN accounts a ON a.account_id=c.account_id
WHERE a.username='$fixture_name' AND a.status='Disabled'
  AND c.name='$fixture_name'
  AND c.admin_note='CharacterPositionCommittedFixture';
DELETE FROM accounts
WHERE username='$fixture_name' AND status='Disabled'
  AND NOT EXISTS (
    SELECT 1 FROM characters WHERE characters.account_id=accounts.account_id
  );
COMMIT;
DROP USER IF EXISTS '$test_user'@'172.17.0.1';
SELECT
 (SELECT COUNT(*) FROM accounts WHERE username='$fixture_name')
 +(SELECT COUNT(*) FROM characters WHERE name='$fixture_name')
 +(SELECT COUNT(*) FROM mysql.user WHERE User='$test_user');
SQL
  )"; then
    echo "Fixture 清理失敗：$fixture_name"
    exit 1
  fi
  echo "remaining_position_fixture=$remaining"
  test "$remaining" = "0" || exit 1
  echo "Position-committed exit code: $result"
  exit "$result"
}
trap cleanup EXIT

bounds="$(db_admin <<'SQL'
SELECT COUNT(*) FROM god2_game.maps
WHERE map_id=170015007 AND enabled=1
  AND 16 BETWEEN minimum_x AND maximum_x
  AND 17 BETWEEN minimum_x AND maximum_x
  AND 14 BETWEEN minimum_y AND maximum_y
  AND 15 BETWEEN minimum_y AND maximum_y;
SQL
)"
test "$bounds" = "1" || { echo "Fixture 地圖邊界不符合"; exit 1; }

db_admin <<SQL
CREATE USER '$test_user'@'172.17.0.1' IDENTIFIED BY '$test_password';
GRANT SELECT ON god2_player.characters TO '$test_user'@'172.17.0.1';
GRANT UPDATE (position_x,position_y,runtime_version,concurrency_token,updated_at_utc)
ON god2_player.characters TO '$test_user'@'172.17.0.1';
SQL

ids="$(db_admin <<SQL
USE god2_player;
START TRANSACTION;
INSERT INTO accounts (username,password_hash,status)
VALUES ('$fixture_name','DISABLED_TEST_FIXTURE_NO_LOGIN','Disabled');
SET @account=LAST_INSERT_ID();
INSERT INTO characters
 (account_id,name,status,enabled,admin_note,map_id,
  position_x,position_y,runtime_version,concurrency_token)
VALUES
 (@account,'$fixture_name','Disabled',1,'CharacterPositionCommittedFixture',
  170015007,17,15,0,REPLACE(UUID(),'-',''));
SET @character=LAST_INSERT_ID();
COMMIT;
SELECT @account,@character;
SQL
)"
read -r account_id character_id <<< "$ids"
[[ "$account_id" =~ ^[0-9]+$ ]]
[[ "$character_id" =~ ^[0-9]+$ ]]
test "$account_id" -gt 1
test "$character_id" -gt 1

export GOD2_POSITION_FIXTURE_USER="$test_user"
export GOD2_POSITION_FIXTURE_PASSWORD="$test_password"
export GOD2_POSITION_FIXTURE_ACCOUNT_ID="$account_id"
export GOD2_POSITION_FIXTURE_CHARACTER_ID="$character_id"
export GOD2_RUN_DB_INTEGRATION=1

dotnet test \
  tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj \
  -c Release \
  --filter 'FullyQualifiedName~CharacterPositionCommittedTests' \
  --verbosity minimal
