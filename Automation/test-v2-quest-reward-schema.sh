#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

container="${GOD2_DB_CONTAINER:-god2-runtime-db-test}"
test "$container" = "god2-runtime-db-test"

fixture_schema="god2_quest_schema_$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"
schema_sql="$(mktemp)"

db_admin() {
  sudo docker exec -i "$container" sh -lc '
    password="$(cat "$MARIADB_ROOT_PASSWORD_FILE")"
    exec mariadb -uroot -p"$password" --batch --raw
  '
}

cleanup() {
  result=$?
  trap - EXIT
  if ! printf 'DROP DATABASE IF EXISTS `%s`;\n' "$fixture_schema" | db_admin; then
    echo "Schema 清理失敗：$fixture_schema"
    result=1
  fi
  rm -f "$schema_sql"
  echo "Schema-test exit code: $result"
  exit "$result"
}
trap cleanup EXIT

python3 - "$fixture_schema" "$schema_sql" <<'PY'
from pathlib import Path
import re
import sys

name, output = sys.argv[1:]
assert re.fullmatch(r"god2_quest_schema_[0-9a-f]{32}", name)
text = Path("Automation/create-v2-quest-reward-runtime.sql").read_text()
text = text.replace("god2_player.", f"`{name}`.")
text = text.replace("god2_game.", f"`{name}`.")
Path(output).write_text(text)
PY

cat <<SQL | db_admin
CREATE DATABASE \`$fixture_schema\`;
CREATE TABLE \`$fixture_schema\`.characters (
  character_id BIGINT NOT NULL PRIMARY KEY
) ENGINE=InnoDB;
CREATE TABLE \`$fixture_schema\`.quests (
  quest_id BIGINT NOT NULL PRIMARY KEY
) ENGINE=InnoDB;
SQL

db_admin < "$schema_sql"

cat <<SQL | db_admin
USE \`$fixture_schema\`;
INSERT INTO characters VALUES (1),(2);
INSERT INTO quests VALUES (1);
INSERT INTO v2_quest_instances
  (QuestInstanceId,CharacterId,QuestId,DefinitionFingerprint)
VALUES
  ('11111111-1111-1111-1111-111111111111',1,1,REPEAT('a',64));

INSERT INTO v2_quest_reward_snapshots
  (QuestInstanceId,RewardFingerprint,RewardJson,EvidenceReference)
VALUES
  ('11111111-1111-1111-1111-111111111111',
   REPEAT('b',64),'[]','SchemaFixtureOnly');
SQL

expect_rejection() {
  label="$1"
  expected_code="$2"
  sql="$3"
  if output="$(printf 'USE `%s`;\n%s\n' "$fixture_schema" "$sql" | db_admin 2>&1)"; then
    echo "FAIL：$label 未被拒絕"
    exit 1
  fi
  if ! rg -q "^ERROR ${expected_code} " <<<"$output"; then
    printf '%s\n' "$output"
    echo "FAIL：$label 未命中預期錯誤碼 $expected_code"
    exit 1
  fi
  echo "PASS：$label，錯誤碼 $expected_code"
}

expect_rejection "Ready 缺少完成事件" 4025 "
UPDATE v2_quest_instances SET State='Ready';"

expect_rejection "Completed 缺少完成時間" 4025 "
UPDATE v2_quest_instances
SET State='Completed',
    CompletionEventId='22222222-2222-2222-2222-222222222222',
    ReadyAtUtc=UTC_TIMESTAMP(6);"

expect_rejection "領獎角色與任務擁有者不符" 1452 "
INSERT INTO v2_quest_reward_claims
  (QuestInstanceId,CharacterId,ClaimTransactionId,
   RewardFingerprint,InventoryVersionBefore,InventoryVersionAfter,ResultJson)
VALUES
  ('11111111-1111-1111-1111-111111111111',2,
   '33333333-3333-3333-3333-333333333333',
   REPEAT('b',64),7,9,'{}');"

cat <<SQL | db_admin
USE \`$fixture_schema\`;
UPDATE v2_quest_instances
SET State='Ready',
    CompletionEventId='22222222-2222-2222-2222-222222222222',
    ReadyAtUtc=UTC_TIMESTAMP(6);

START TRANSACTION;
INSERT INTO v2_quest_reward_claims
  (QuestInstanceId,CharacterId,ClaimTransactionId,
   RewardFingerprint,InventoryVersionBefore,InventoryVersionAfter,ResultJson)
VALUES
  ('11111111-1111-1111-1111-111111111111',1,
   '33333333-3333-3333-3333-333333333333',
   REPEAT('b',64),7,9,'{}');

UPDATE v2_quest_instances
SET State='Completed',QuestVersion=1,CompletedAtUtc=UTC_TIMESTAMP(6);
COMMIT;
SQL

expect_rejection "同一任務實例重複領獎" 1062 "
INSERT INTO v2_quest_reward_claims
  (QuestInstanceId,CharacterId,ClaimTransactionId,
   RewardFingerprint,InventoryVersionBefore,InventoryVersionAfter,ResultJson)
VALUES
  ('11111111-1111-1111-1111-111111111111',1,
   '44444444-4444-4444-4444-444444444444',
   REPEAT('b',64),9,11,'{}');"

echo "PASS：實際 SQL 建表、合法完成流程與拒絕案例執行成功"
