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

db_admin() {
  sudo docker exec -i "$container" sh -lc '
    password="$(cat "$MARIADB_ROOT_PASSWORD_FILE")"
    exec mariadb -uroot -p"$password" --batch --raw --skip-column-names
  '
}

present="$(db_admin <<'SQL'
SELECT COUNT(*) FROM information_schema.TABLES
WHERE TABLE_SCHEMA='god2_player'
  AND TABLE_NAME IN (
    'v2_quest_instances',
    'v2_quest_reward_snapshots',
    'v2_quest_reward_claims'
  );
SQL
)"
case "$present" in
  0) db_admin < Automation/create-v2-quest-reward-runtime.sql ;;
  3) ;;
  *) echo "V2 任務表不完整，停止：$present"; exit 1 ;;
esac

quest_id="$(db_admin <<'SQL'
SELECT quest_id FROM god2_game.quests
WHERE enabled=1 ORDER BY quest_id LIMIT 1;
SQL
)"
[[ "$quest_id" =~ ^[0-9]+$ ]]

fixture_name="questfixture_$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"
test_user="questtest_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
test_password="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
fixture_sql="$(mktemp)"
export GOD2_QUEST_FIXTURE_USER="$test_user"
export GOD2_QUEST_FIXTURE_PASSWORD="$test_password"
export GOD2_RUN_DB_INTEGRATION=1

cleanup() {
  result=$?
  trap - EXIT
  rm -f "$fixture_sql"
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
    AND enabled=0 AND admin_note='QuestRewardCommittedFixture'
);
DELETE FROM v2_quest_reward_claims WHERE CharacterId=@character;
DELETE s FROM v2_quest_reward_snapshots s
JOIN v2_quest_instances q ON q.QuestInstanceId=s.QuestInstanceId
WHERE q.CharacterId=@character;
DELETE FROM v2_quest_instances WHERE CharacterId=@character;
DELETE s FROM inventory_item_identity_sequence s
JOIN character_inventory i ON i.inventory_id=s.PersistentInventoryItemId
WHERE i.character_id=@character;
DELETE FROM character_inventory WHERE character_id=@character;
DELETE FROM characters WHERE character_id=@character;
DELETE FROM accounts WHERE account_id=@account
  AND NOT EXISTS (SELECT 1 FROM characters WHERE account_id=@account);
COMMIT;
DROP USER IF EXISTS '$test_user'@'172.17.0.1';
SELECT
  (SELECT COUNT(*) FROM accounts WHERE username='$fixture_name')
  +
  (SELECT COUNT(*) FROM mysql.user WHERE User='$test_user');
SQL
  )"; then
    echo "Fixture 清理失敗：$fixture_name"
    exit 1
  fi
  echo "remaining_fixture_accounts_and_users=$remaining"
  test "$remaining" = "0" || exit 1
  echo "Committed-quest-test exit code: $result"
  exit "$result"
}
trap cleanup EXIT

db_admin <<SQL
CREATE USER '$test_user'@'172.17.0.1' IDENTIFIED BY '$test_password';
GRANT SELECT ON god2_player.characters TO '$test_user'@'172.17.0.1';
GRANT SELECT,UPDATE ON god2_player.player_inventory_state TO '$test_user'@'172.17.0.1';
GRANT SELECT,INSERT,UPDATE,DELETE ON god2_player.character_inventory TO '$test_user'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_item_identity_sequence TO '$test_user'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_transaction_idempotency TO '$test_user'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.inventory_audit_ledger TO '$test_user'@'172.17.0.1';
GRANT SELECT ON god2_game.items TO '$test_user'@'172.17.0.1';
GRANT SELECT,UPDATE ON god2_player.v2_quest_instances TO '$test_user'@'172.17.0.1';
GRANT SELECT ON god2_player.v2_quest_reward_snapshots TO '$test_user'@'172.17.0.1';
GRANT SELECT,INSERT ON god2_player.v2_quest_reward_claims TO '$test_user'@'172.17.0.1';
SQL

fixture_id="$(db_admin <<SQL
USE god2_player;
START TRANSACTION;
INSERT INTO accounts (username,password_hash,status)
VALUES ('$fixture_name','DISABLED_TEST_FIXTURE_NO_LOGIN','Disabled');
SET @account=LAST_INSERT_ID();
INSERT INTO characters (account_id,name,status,enabled,admin_note)
VALUES (@account,'$fixture_name','Disabled',0,'QuestRewardCommittedFixture');
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
export GOD2_QUEST_FIXTURE_CHARACTER_ID="$fixture_id"

instance_ids="$(python3 - "$fixture_id" "$quest_id" "$fixture_sql" <<'PY'
import hashlib
import json
import sys
import uuid
from pathlib import Path

character, quest, output = sys.argv[1:]
assert character.isdigit() and quest.isdigit()
ids = [str(uuid.uuid4()) for _ in range(3)]
lines = ["START TRANSACTION;"]
for index, instance in enumerate(ids):
    rewards = [
        dict(RewardId=1, RewardType="Item", ItemId=253231541, Quantity=1),
        dict(RewardId=2, RewardType="Item", ItemId=253231541,
             Quantity=8 if index == 1 else 1),
    ]
    raw = json.dumps(rewards, separators=(",", ":"))
    fingerprint = hashlib.sha256(raw.encode()).hexdigest()
    event = str(uuid.uuid4())
    lines.append(f"""
INSERT INTO god2_player.v2_quest_instances
 (QuestInstanceId,CharacterId,QuestId,DefinitionFingerprint,
  State,CompletionEventId,ReadyAtUtc)
VALUES
 ('{instance}',{character},{quest},'{'a' * 64}',
  'Ready','{event}',UTC_TIMESTAMP(6));
INSERT INTO god2_player.v2_quest_reward_snapshots
 (QuestInstanceId,RewardFingerprint,RewardJson,EvidenceReference)
VALUES
 ('{instance}','{fingerprint}','{raw}','FixtureOnly:{instance}');
""")
lines.append("COMMIT;")
Path(output).write_text("\n".join(lines))
print(",".join(ids))
PY
)"
export GOD2_QUEST_FIXTURE_INSTANCES="$instance_ids"
db_admin < "$fixture_sql"

echo "獨立停用角色 ID：$fixture_id"
if test "${1:-}" = "--all"; then
  test -n "${GOD2_TEST_PASSWORD:-}" || {
    echo "缺少 GOD2_TEST_PASSWORD，停止完整測試"
    exit 1
  }
  ./Automation/test-v2-inventory-grant-failure.sh --all
else
  dotnet test \
    tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj \
    --configuration Release \
    --filter 'FullyQualifiedName~QuestRewardClaimCommittedTests' \
    --verbosity minimal
fi
