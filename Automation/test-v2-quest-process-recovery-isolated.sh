#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
test "$#" -eq 0
test "$(git branch --show-current)" = "codex/inventory-grant-20261003"
export GOD2_DB_HOST=127.0.0.1 GOD2_DB_PORT=3310
container="god2-merchant-db-isolated"
test "$(sudo docker inspect -f '{{.Name}}' "$container")" = "/$container"
test "$(sudo docker port "$container" 3306/tcp)" = "127.0.0.1:3310"
user_host="$(sudo docker inspect -f '{{range .NetworkSettings.Networks}}{{.Gateway}}{{end}}' "$container")"
[[ "$user_host" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]

db_admin() {
  sudo docker exec -i "$container" sh -lc '
    set -eu
    password="$(cat /run/secrets/merchant-root-password)"
    exec mariadb --no-defaults --database=god2_player -uroot --password="$password" --batch --raw --skip-column-names
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
  0) echo "STOP: isolated quest reward schema missing"; exit 1 ;;
  3) ;;
  *) echo "V2 任務表不完整，停止：$present"; exit 1 ;;
esac

progress_present="$(db_admin <<'SQL'
SELECT COUNT(*) FROM information_schema.TABLES
WHERE TABLE_SCHEMA='god2_player'
  AND TABLE_NAME IN (
    'v2_quest_objective_progress','v2_quest_progress_events'
  );
SQL
)"
case "$progress_present" in
  0) echo "STOP: isolated quest progress schema missing"; exit 1 ;;
  2) ;;
  *) echo "V2 進度表不完整，停止"; exit 1 ;;
esac

quest_id="$(python3 -c 'import secrets; print(6000000000 + secrets.randbelow(1000000000))')"

evidence="$(mktemp -d docs/handoff/quest-3310-$(date +%Y%m%d-%H%M%S)-XXXXXX)"
echo "evidence=$evidence"
git rev-parse HEAD >"$evidence/source-commit.txt"
exec > >(tee "$evidence/runner.log") 2>&1

schema_ok="$(db_admin <<'SQL_SCHEMA'
SELECT COUNT(*) FROM information_schema.TABLES
WHERE TABLE_SCHEMA='god2_player' AND ENGINE='InnoDB'
 AND TABLE_NAME IN (
 'v2_quest_instances','v2_quest_reward_snapshots','v2_quest_reward_claims',
 'v2_quest_objective_progress','v2_quest_progress_events');
SQL_SCHEMA
)"
test "$schema_ok" = "5" || { echo "STOP: quest tables must be InnoDB"; exit 1; }

fixture_name="questfixture_$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"
test_user="questtest_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
test_password="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
fixture_sql="$(mktemp)"
export GOD2_QUEST_FIXTURE_USER="$test_user"
export GOD2_QUEST_FIXTURE_PASSWORD="$test_password"
fault_user="questfault_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
export GOD2_QUEST_ACCEPT_FAULT_USER="$fault_user"
export GOD2_RUN_DB_INTEGRATION=1

cleanup() {
  result=$?
  trap - EXIT
  rm -f "$fixture_sql"
  if ! remaining="$(db_admin <<SQL
DELIMITER //
BEGIN NOT ATOMIC
 DECLARE EXIT HANDLER FOR SQLEXCEPTION
 BEGIN
   ROLLBACK;
   RESIGNAL;
 END;
START TRANSACTION;
SET @account=(
  SELECT account_id FROM accounts
  WHERE username='$fixture_name' AND status='Disabled' FOR UPDATE
);
SET @character=(
  SELECT character_id FROM characters
  WHERE account_id=@account AND name='$fixture_name'
    AND enabled=0 AND admin_note='QuestRewardCommittedFixture' FOR UPDATE
);
SET @loop_character=(
  SELECT character_id FROM characters
  WHERE account_id=@account AND name='${fixture_name}_loop'
    AND enabled=0 AND admin_note='QuestRewardCommittedFixture' FOR UPDATE
);
SET @accept_character=(
  SELECT character_id FROM characters
  WHERE account_id=@account AND name='${fixture_name}_accept'
    AND enabled=0 AND admin_note='QuestRewardCommittedFixture' FOR UPDATE
);
SET @owned_accounts=(SELECT COUNT(*) FROM accounts
 WHERE username='$fixture_name' AND status='Disabled');
SET @owned_characters=(SELECT COUNT(*) FROM characters
 WHERE account_id=@account AND enabled=0 AND status='Disabled'
   AND admin_note='QuestRewardCommittedFixture'
   AND name IN ('$fixture_name','${fixture_name}_loop','${fixture_name}_accept'));
SET @locked_quest=NULL;
SELECT quest_id INTO @locked_quest FROM god2_game.quests
 WHERE quest_id=$quest_id AND code='$fixture_name' FOR UPDATE;
SET @owned_quests=(SELECT COUNT(*) FROM god2_game.quests
 WHERE quest_id=$quest_id AND code='$fixture_name'
   AND name_zh_tw='SYNTHETIC_QUEST_FIXTURE' AND quest_type='FixtureOnly');
IF NOT ((
 (@owned_accounts=1 AND @owned_characters=3 AND @owned_quests=1
  AND @character IS NOT NULL AND @loop_character IS NOT NULL
  AND @accept_character IS NOT NULL
  AND (SELECT COUNT(*) FROM characters WHERE account_id=@account)=3)
 OR
 (@owned_accounts=0 AND @owned_characters=0 AND @owned_quests=0
  AND (SELECT COUNT(*) FROM accounts WHERE username='$fixture_name')=0
  AND (SELECT COUNT(*) FROM characters
       WHERE name IN ('$fixture_name','${fixture_name}_loop','${fixture_name}_accept'))=0
  AND (SELECT COUNT(*) FROM god2_game.quests
       WHERE quest_id=$quest_id OR code='$fixture_name')=0)
) <=> 1) THEN
 SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='QUEST_FIXTURE_IDENTITY_REJECTED';
END IF;
IF (SELECT COUNT(*) FROM god2_game.portals WHERE required_quest_id=$quest_id)
 +(SELECT COUNT(*) FROM god2_game.quest_objectives WHERE quest_id=$quest_id)
 +(SELECT COUNT(*) FROM god2_game.quest_rewards WHERE quest_id=$quest_id)
 +(SELECT COUNT(*) FROM v2_quest_instances
   WHERE QuestId=$quest_id
     AND CharacterId NOT IN (@character,@loop_character,@accept_character)) <> 0 THEN
 SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='QUEST_UNEXPECTED_DEPENDENCY';
END IF;
DELETE FROM v2_quest_progress_events
WHERE CharacterId IN (@character,@loop_character,@accept_character);
DELETE p FROM v2_quest_objective_progress p
JOIN v2_quest_instances q ON q.QuestInstanceId=p.QuestInstanceId
WHERE q.CharacterId IN (@character,@loop_character,@accept_character);
DELETE FROM v2_quest_reward_claims WHERE CharacterId IN (@character,@loop_character,@accept_character);
DELETE s FROM v2_quest_reward_snapshots s
JOIN v2_quest_instances q ON q.QuestInstanceId=s.QuestInstanceId
WHERE q.CharacterId IN (@character,@loop_character,@accept_character);
DELETE FROM v2_quest_instances WHERE CharacterId IN (@character,@loop_character,@accept_character);
DELETE s FROM inventory_item_identity_sequence s
JOIN character_inventory i ON i.inventory_id=s.PersistentInventoryItemId
WHERE i.character_id IN (@character,@loop_character,@accept_character);
DELETE FROM character_inventory WHERE character_id IN (@character,@loop_character,@accept_character);
DELETE FROM characters WHERE character_id IN (@character,@loop_character,@accept_character);
IF ROW_COUNT() <> @owned_characters THEN
 SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='QUEST_CHARACTER_DELETE_COUNT';
END IF;
DELETE FROM accounts WHERE account_id=@account
  AND NOT EXISTS (SELECT 1 FROM characters WHERE account_id=@account);
IF ROW_COUNT() <> @owned_accounts THEN
 SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='QUEST_ACCOUNT_DELETE_COUNT';
END IF;
DELETE FROM god2_game.quests
WHERE quest_id=$quest_id AND code='$fixture_name'
  AND name_zh_tw='SYNTHETIC_QUEST_FIXTURE' AND quest_type='FixtureOnly';
IF ROW_COUNT() <> @owned_quests THEN
 SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='QUEST_DELETE_COUNT_MISMATCH';
END IF;
COMMIT;
END//
DELIMITER ;
DROP USER IF EXISTS '$test_user'@'$user_host';
DROP USER IF EXISTS '$fault_user'@'$user_host';
SELECT
  (SELECT COUNT(*) FROM accounts WHERE username='$fixture_name')
  +
  (SELECT COUNT(*) FROM mysql.user WHERE User IN ('$test_user','$fault_user'))
  + (SELECT COUNT(*) FROM characters
     WHERE name IN ('$fixture_name','${fixture_name}_loop','${fixture_name}_accept'))
  + (SELECT COUNT(*) FROM god2_game.quests
     WHERE quest_id=$quest_id OR code='$fixture_name')
  + (SELECT COUNT(*) FROM v2_quest_instances WHERE QuestId=$quest_id);
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
collision="$(db_admin <<SQL
SELECT
 (SELECT COUNT(*) FROM accounts WHERE username='$fixture_name')
 +(SELECT COUNT(*) FROM characters
   WHERE name IN ('$fixture_name','${fixture_name}_loop','${fixture_name}_accept'))
 +(SELECT COUNT(*) FROM god2_game.quests WHERE quest_id=$quest_id OR code='$fixture_name')
 +(SELECT COUNT(*) FROM mysql.user WHERE User IN ('$test_user','$fault_user'));
SQL
)"
test "$collision" = "0" || { echo "STOP: quest identity collision"; exit 1; }
trap cleanup EXIT

db_admin <<SQL
CREATE USER '$test_user'@'$user_host' IDENTIFIED BY '$test_password';
CREATE USER '$fault_user'@'$user_host' IDENTIFIED BY '$test_password';
GRANT SELECT ON god2_player.characters TO '$fault_user'@'$user_host';
GRANT SELECT ON god2_game.quests TO '$fault_user'@'$user_host';
GRANT SELECT,INSERT,UPDATE ON god2_player.v2_quest_instances
  TO '$fault_user'@'$user_host';
GRANT SELECT,INSERT,UPDATE ON god2_player.v2_quest_objective_progress
  TO '$fault_user'@'$user_host';
GRANT SELECT ON god2_player.v2_quest_reward_snapshots
  TO '$fault_user'@'$user_host';
GRANT SELECT ON god2_player.characters TO '$test_user'@'$user_host';
GRANT SELECT,UPDATE ON god2_player.player_inventory_state TO '$test_user'@'$user_host';
GRANT SELECT,INSERT,UPDATE,DELETE ON god2_player.character_inventory TO '$test_user'@'$user_host';
GRANT SELECT,INSERT ON god2_player.inventory_item_identity_sequence TO '$test_user'@'$user_host';
GRANT SELECT,INSERT ON god2_player.inventory_transaction_idempotency TO '$test_user'@'$user_host';
GRANT SELECT,INSERT ON god2_player.inventory_audit_ledger TO '$test_user'@'$user_host';
GRANT SELECT ON god2_game.quests TO '$test_user'@'$user_host';
GRANT SELECT ON god2_game.items TO '$test_user'@'$user_host';
GRANT SELECT,INSERT,UPDATE ON god2_player.v2_quest_instances TO '$test_user'@'$user_host';
GRANT SELECT,INSERT ON god2_player.v2_quest_reward_snapshots TO '$test_user'@'$user_host';
GRANT SELECT,INSERT ON god2_player.v2_quest_reward_claims TO '$test_user'@'$user_host';
GRANT SELECT,INSERT,UPDATE ON god2_player.v2_quest_objective_progress TO '$test_user'@'$user_host';
GRANT SELECT,INSERT ON god2_player.v2_quest_progress_events TO '$test_user'@'$user_host';
SQL

fixture_id="$(db_admin <<SQL
DELIMITER //
BEGIN NOT ATOMIC
  DECLARE EXIT HANDLER FOR SQLEXCEPTION
  BEGIN
    ROLLBACK;
    RESIGNAL;
  END;
START TRANSACTION;
INSERT INTO god2_game.quests
 (quest_id,code,name_zh_tw,quest_type,enabled)
VALUES
 ($quest_id,'$fixture_name','SYNTHETIC_QUEST_FIXTURE','FixtureOnly',1);
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
INSERT INTO characters (account_id,name,status,enabled,admin_note)
VALUES (@account,'${fixture_name}_loop','Disabled',0,'QuestRewardCommittedFixture');
SET @loop_character=LAST_INSERT_ID();
INSERT INTO player_inventory_state
  (CharacterId,InventoryId,Capacity,InventoryVersion,
   MutationSequence,DirtyState,UpdatedAtUtc)
VALUES (@loop_character,UUID(),8,0,0,'Clean',UTC_TIMESTAMP(6));
INSERT INTO characters
  (account_id,name,status,enabled,admin_note,level)
VALUES
  (@account,'${fixture_name}_accept','Disabled',0,
   'QuestRewardCommittedFixture',1);
SET @accept_character=LAST_INSERT_ID();
INSERT INTO player_inventory_state
  (CharacterId,InventoryId,Capacity,InventoryVersion,
   MutationSequence,DirtyState,UpdatedAtUtc)
VALUES (@accept_character,UUID(),8,0,0,'Clean',UTC_TIMESTAMP(6));
COMMIT;
SELECT CONCAT(@character,',',@loop_character,',',@accept_character);
END//
DELIMITER ;
SQL
)"
IFS=',' read -r fixture_id loop_id accept_id <<<"$fixture_id"
[[ "$accept_id" =~ ^[0-9]+$ ]]
test "$accept_id" -gt 1
export GOD2_QUEST_ACCEPT_CHARACTER_ID="$accept_id"
[[ "$loop_id" =~ ^[0-9]+$ ]]
test "$loop_id" -gt 1
export GOD2_QUEST_LOOP_CHARACTER_ID="$loop_id"
[[ "$fixture_id" =~ ^[0-9]+$ ]]
test "$fixture_id" -gt 1
export GOD2_QUEST_FIXTURE_CHARACTER_ID="$fixture_id"

instance_ids="$(python3 - "$fixture_id" "$loop_id" "$quest_id" "$fixture_sql" <<'PY'
import hashlib
import json
import sys
import uuid
from pathlib import Path

character, loop_character, quest, output = sys.argv[1:]
assert all(value.isdigit() for value in (character, loop_character, quest))
ids = [str(uuid.uuid4()) for _ in range(4)]
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
    owner = loop_character if index == 3 else character
    state = "Accepted" if index == 3 else "Ready"
    completion = "NULL" if index == 3 else f"'{event}'"
    ready_at = "NULL" if index == 3 else "UTC_TIMESTAMP(6)"
    lines.append(f"""
INSERT INTO god2_player.v2_quest_instances
 (QuestInstanceId,CharacterId,QuestId,DefinitionFingerprint,
  State,CompletionEventId,ReadyAtUtc)
VALUES
 ('{instance}',{owner},{quest},'{'a' * 64}',
  '{state}',{completion},{ready_at});
INSERT INTO god2_player.v2_quest_reward_snapshots
 (QuestInstanceId,RewardFingerprint,RewardJson,EvidenceReference)
VALUES
 ('{instance}','{fingerprint}','{raw}','FixtureOnly:{instance}');
""")
lines.append(f"""
INSERT INTO god2_player.v2_quest_objective_progress
 (QuestInstanceId,ObjectiveId,ObjectiveKind,TargetId,RequiredCount)
VALUES
 ('{ids[3]}',1,'DefeatMonster',100,2),
 ('{ids[3]}',2,'InteractNpc',200,1);
""")
lines.append("COMMIT;")
Path(output).write_text("\n".join(lines))
print(",".join(ids))
PY
)"
export GOD2_QUEST_FIXTURE_INSTANCES="$instance_ids"
db_admin < "$fixture_sql"

echo "獨立停用角色 ID：$fixture_id"
project="tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj"
namespace="God2.ServerV2.Persistence.IntegrationTests"

run_phase() {
  local phase="$1" expected="$2" filter="$3"
  shift 3
  python3 Automation/regression_evidence.py run dotnet test "$project" --configuration Release "$@" \
    --filter "$filter" \
    --logger "trx;LogFileName=$phase.trx" \
    --results-directory "$evidence" --verbosity minimal \
    2>&1 | tee "$evidence/$phase.log"

  python3 - "$evidence/$phase.trx" "$expected" <<'PY_TRX'
import sys
import xml.etree.ElementTree as ET
c = ET.parse(sys.argv[1]).getroot().find(".//{*}Counters")
assert c is not None
assert all(c.get(k) == sys.argv[2] for k in ("total","executed","passed"))
assert c.get("failed") == c.get("notExecuted") == "0"
print(f"PASS: quest phase {sys.argv[2]}/{sys.argv[2]}")
PY_TRX
}

producer_filter="FullyQualifiedName=$namespace.QuestRewardClaimCommittedTests.Owned_transactions_commit_once_and_roll_back_claim_failure"
producer_filter+="|FullyQualifiedName=$namespace.QuestProgressClosedLoopTests.Trusted_events_progress_once_then_allow_atomic_reward_claim"
producer_filter+="|FullyQualifiedName=$namespace.QuestAcceptanceClosedLoopTests.Acceptance_registers_once_then_progress_and_claim_complete"

run_phase producer 3 "$producer_filter"
run_phase recovery 1 \
  "FullyQualifiedName=$namespace.QuestProcessRecoveryTests.Separate_process_reads_completed_quest_without_dispatch" \
  --no-build --no-restore

echo "PASS: isolated quest producer/recovery 4/4"
