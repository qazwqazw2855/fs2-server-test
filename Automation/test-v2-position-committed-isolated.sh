#!/usr/bin/env bash
set -euo pipefail
test "$#" -eq 0
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
test "$(git branch --show-current)" = "codex/inventory-grant-20261003"
container="god2-merchant-db-isolated"
test "$(sudo docker inspect -f '{{.Name}}' "$container")" = "/$container"
test "$(sudo docker port "$container" 3306/tcp)" = "127.0.0.1:3310"

db_admin() {
  sudo docker exec -i "$container" sh -lc '
    set -eu
    password="$(cat /run/secrets/merchant-root-password)"
    exec mariadb --no-defaults --database=god2_player \
      -uroot --password="$password" --batch --raw --skip-column-names
  '
}

fixture_name="positionfixture_$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"
test_user="positiontest_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
test_password="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
map_id="$(python3 -c 'import secrets; print(5000000000 + secrets.randbelow(1000000000))')"
user_host="$(sudo docker inspect -f '{{range .NetworkSettings.Networks}}{{.Gateway}}{{end}}' "$container")"
[[ "$user_host" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]

collision="$(db_admin <<SQL
SELECT
 (SELECT COUNT(*) FROM god2_player.accounts WHERE username='$fixture_name')
 +(SELECT COUNT(*) FROM god2_player.characters WHERE name='$fixture_name')
 +(SELECT COUNT(*) FROM god2_game.maps WHERE map_id=$map_id OR code='$fixture_name')
 +(SELECT COUNT(*) FROM mysql.user WHERE User='$test_user');
SQL
)"
test "$collision" = "0"

cleanup() {
  result=$?
  trap - EXIT
  if ! db_admin <<SQL
DELIMITER //
BEGIN NOT ATOMIC
  DECLARE EXIT HANDLER FOR SQLEXCEPTION
  BEGIN
    ROLLBACK;
    RESIGNAL;
  END;
  START TRANSACTION;
  SET @a=NULL, @c=NULL, @m=NULL;
  SELECT account_id INTO @a FROM god2_player.accounts
    WHERE username='$fixture_name' AND status='Disabled' FOR UPDATE;
  SELECT character_id INTO @c FROM god2_player.characters
    WHERE account_id=@a AND name='$fixture_name' AND enabled=1
      AND status='Disabled' AND deleted_at_utc IS NULL
      AND admin_note='CharacterPositionCommittedFixture'
      AND map_id=$map_id FOR UPDATE;
  SELECT map_id INTO @m FROM god2_game.maps
    WHERE map_id=$map_id AND code='$fixture_name'
      AND name_zh_tw='SYNTHETIC_POSITION_FIXTURE'
      AND client_build_id IS NULL FOR UPDATE;
  IF @a IS NULL AND @c IS NULL AND @m IS NULL THEN
    IF (SELECT COUNT(*) FROM god2_player.accounts WHERE username='$fixture_name')
       +(SELECT COUNT(*) FROM god2_player.characters WHERE name='$fixture_name')
       +(SELECT COUNT(*) FROM god2_game.maps WHERE map_id=$map_id OR code='$fixture_name') <> 0 THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='POSITION_RESIDUAL_IDENTITY';
    END IF;
  ELSE
    IF @a IS NULL OR @c IS NULL OR @m IS NULL
       OR (SELECT COUNT(*) FROM god2_player.characters WHERE account_id=@a) <> 1
       OR (SELECT COUNT(*) FROM god2_player.characters WHERE map_id=@m) <> 1 THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='POSITION_IDENTITY_REJECTED';
    END IF;
    DELETE FROM god2_player.characters WHERE character_id=@c;
    IF ROW_COUNT() <> 1 THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='POSITION_CHARACTER_DELETE';
    END IF;
    DELETE FROM god2_player.accounts WHERE account_id=@a;
    IF ROW_COUNT() <> 1 THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='POSITION_ACCOUNT_DELETE';
    END IF;
    DELETE FROM god2_game.maps WHERE map_id=@m AND code='$fixture_name';
    IF ROW_COUNT() <> 1 THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='POSITION_MAP_DELETE';
    END IF;
  END IF;
  COMMIT;
END//
DELIMITER ;
DROP USER IF EXISTS '$test_user'@'$user_host';
SQL
  then
    echo "STOP: position cleanup failed"
    exit 1
  fi
  remaining="$(db_admin <<SQL
SELECT
 (SELECT COUNT(*) FROM god2_player.accounts WHERE username='$fixture_name')
 +(SELECT COUNT(*) FROM god2_player.characters WHERE name='$fixture_name')
 +(SELECT COUNT(*) FROM god2_game.maps WHERE map_id=$map_id OR code='$fixture_name')
 +(SELECT COUNT(*) FROM mysql.user WHERE User='$test_user');
SQL
)"
  test "$remaining" = "0" || { echo "STOP: position residual rows"; exit 1; }
  echo "PASS: position fixture, synthetic map and user removed"
  exit "$result"
}
trap cleanup EXIT

db_admin <<SQL
CREATE USER '$test_user'@'$user_host' IDENTIFIED BY '$test_password';
GRANT SELECT ON god2_player.characters TO '$test_user'@'$user_host';
GRANT UPDATE (position_x,position_y,runtime_version,concurrency_token,updated_at_utc)
ON god2_player.characters TO '$test_user'@'$user_host';
SQL

ids="$(db_admin <<SQL
DELIMITER //
BEGIN NOT ATOMIC
  DECLARE EXIT HANDLER FOR SQLEXCEPTION
  BEGIN
    ROLLBACK;
    RESIGNAL;
  END;
  START TRANSACTION;
  INSERT INTO god2_game.maps
    (map_id,code,name_zh_tw,minimum_x,maximum_x,minimum_y,maximum_y,enabled)
  VALUES ($map_id,'$fixture_name','SYNTHETIC_POSITION_FIXTURE',0,100,0,100,1);
  INSERT INTO god2_player.accounts (username,password_hash,status)
  VALUES ('$fixture_name','DISABLED_TEST_FIXTURE_NO_LOGIN','Disabled');
  SET @a=LAST_INSERT_ID();
  INSERT INTO god2_player.characters
    (account_id,name,status,enabled,admin_note,map_id,
     position_x,position_y,runtime_version,concurrency_token)
  VALUES (@a,'$fixture_name','Disabled',1,'CharacterPositionCommittedFixture',
    $map_id,17,15,0,REPLACE(UUID(),'-',''));
  SET @c=LAST_INSERT_ID();
  COMMIT;
  SELECT @a,@c;
END//
DELIMITER ;
SQL
)"
read -r account_id character_id <<< "$ids"
[[ "$account_id" =~ ^[0-9]+$ && "$character_id" =~ ^[0-9]+$ ]]
test "$account_id" -gt 1
test "$character_id" -gt 1

export GOD2_RUN_DB_INTEGRATION=1 GOD2_DB_HOST=127.0.0.1 GOD2_DB_PORT=3310
export GOD2_POSITION_FIXTURE_USER="$test_user"
export GOD2_POSITION_FIXTURE_PASSWORD="$test_password"
export GOD2_POSITION_FIXTURE_ACCOUNT_ID="$account_id"
export GOD2_POSITION_FIXTURE_CHARACTER_ID="$character_id"

evidence="$(mktemp -d docs/handoff/position-3310-$(date +%Y%m%d-%H%M%S)-XXXXXX)"
echo "evidence=$evidence"
git rev-parse HEAD >"$evidence/source-commit.txt"
dotnet test \
  tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj \
  -c Release \
  --filter 'FullyQualifiedName=God2.ServerV2.Persistence.IntegrationTests.CharacterPositionCommittedTests.CommittedPositionIsReadByNewConnectionAndStaleWriteIsRejectedIsolated3310' \
  --logger 'trx;LogFileName=position.trx' \
  --results-directory "$evidence" --verbosity minimal \
  2>&1 | tee "$evidence/position.log"

python3 - "$evidence/position.trx" <<'PY_TRX'
import sys
import xml.etree.ElementTree as ET
c = ET.parse(sys.argv[1]).getroot().find(".//{*}Counters")
assert c is not None
assert all(c.get(k) == "1" for k in ("total", "executed", "passed"))
assert c.get("failed") == c.get("notExecuted") == "0"
print("PASS: isolated committed position 1/1")
PY_TRX
