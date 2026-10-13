#!/usr/bin/env bash
set -euo pipefail

test "$#" -eq 0
scenario="${GOD2_MERCHANT_RECOVERY_SCENARIO:-buy}"
case "$scenario" in
  buy|sell|command|tcp|basic|newserver|reconnect|lostresult|economy|restoreblocked|atomicbuy|atomicsell|stack) ;;
  *) echo "STOP: invalid recovery scenario"; exit 1 ;;
esac

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

test "$(git branch --show-current)" = \
  "codex/inventory-grant-20261003"

source Automation/merchant-isolated-3310-atomic-faults.sh

# Never source production server-v2.env.
export GOD2_DB_HOST="127.0.0.1"
export GOD2_DB_PORT="3310"
export GOD2_RUN_DB_INTEGRATION="1"

readonly container="god2-merchant-db-isolated"

test "$(sudo docker inspect -f '{{.Name}}' "$container")" = \
  "/god2-merchant-db-isolated"

test "$(sudo docker port "$container" 3306/tcp)" = \
  "127.0.0.1:3310"

db_admin() {
  sudo docker exec -i "$container" sh -lc '
    set -eu
    password="$(cat /run/secrets/merchant-root-password)"
    exec mariadb --no-defaults --database=god2_player -uroot --password="$password" \
      --batch --raw --skip-column-names
  '
}

echo "===== MERCHANT 3310 SAFETY CHECK ====="

db_admin <<'SQL'
SELECT
  @@port,
  @@hostname,
  (SELECT COUNT(*) FROM god2_game.items
   WHERE item_id=253231541),
  (SELECT COUNT(*) FROM god2_game.item_registry
   WHERE item_id=253231541),
  (SELECT COUNT(*) FROM god2_player.characters),
  (SELECT COUNT(*) FROM god2_player.accounts),
  (SELECT COUNT(*) FROM god2_game.merchants);
SQL

echo "===== STRICT FIXTURE PREFLIGHT ====="

preflight="$(db_admin <<'SQL'
SELECT
  (SELECT COUNT(*) FROM god2_player.accounts)
  + (SELECT COUNT(*) FROM god2_player.characters)
  + (SELECT COUNT(*) FROM god2_player.character_inventory)
  + (SELECT COUNT(*) FROM god2_player.inventory_item_identity_sequence)
  + (SELECT COUNT(*) FROM god2_player.inventory_audit_ledger)
  + (SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency)
  + (SELECT COUNT(*) FROM god2_player.v2_merchant_purchase_journal)
  + (SELECT COUNT(*) FROM god2_player.v2_merchant_sale_journal)
  + (SELECT COUNT(*) FROM god2_player.player_inventory_state)
  + (SELECT COUNT(*) FROM god2_player.player_currency_balances)
  + (SELECT COUNT(*) FROM god2_game.merchants)
  + (SELECT COUNT(*) FROM god2_game.merchant_inventory);
SQL
)"

test "$preflight" = "0" || {
  echo "STOP: 3310 contains existing fixture data: $preflight"
  exit 1
}

catalog="$(db_admin <<'SQL'
SELECT
  (SELECT COUNT(*) FROM god2_game.items)
  + (SELECT COUNT(*) FROM god2_game.item_registry)
  + (SELECT COUNT(*) FROM god2_game.items WHERE item_id=253231541)
  + (SELECT COUNT(*) FROM god2_game.item_registry WHERE item_id=253231541);
SQL
)"

test "$catalog" = "4" || {
  echo "STOP: unexpected 3310 item catalog: $catalog"
  exit 1
}

echo "PASS: 3310 fixture tables empty"
echo "PASS: canonical item catalog intact"
echo "===== PREFLIGHT COMPLETE ====="

echo "===== GENERATE ISOLATED FIXTURE IDENTITY ====="

fixture_name="shopfixture_$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"
test_user="shoptest_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
test_password="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
fault_user="shopfault_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
fault_password="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
merchant_id="$(python3 -c 'import secrets; print(4000000000 + secrets.randbelow(1000000000))')"

[[ "$fixture_name" =~ ^shopfixture_[0-9a-f]{32}$ ]]
[[ "$test_user" =~ ^shoptest_[0-9a-f]{16}$ ]]
[[ "$fault_user" =~ ^shopfault_[0-9a-f]{16}$ ]]
[[ "$test_password" =~ ^[0-9a-f]{64}$ ]]
[[ "$fault_password" =~ ^[0-9a-f]{64}$ ]]
[[ "$merchant_id" =~ ^[0-9]{10}$ ]]

echo "fixture_name=$fixture_name"
echo "test_user=$test_user"
echo "fault_user=$fault_user"
echo "merchant_id=$merchant_id"
echo "PASS: unique fixture identity generated"

echo "===== FIXTURE IDENTITY COLLISION CHECK ====="

collision_count="$(db_admin <<SQL
SELECT
  (SELECT COUNT(*) FROM god2_player.accounts
   WHERE username='$fixture_name')
  + (SELECT COUNT(*) FROM god2_player.characters
     WHERE name='$fixture_name')
  + (SELECT COUNT(*) FROM god2_game.merchants
     WHERE merchant_id=$merchant_id
        OR admin_note='$fixture_name')
  + (SELECT COUNT(*) FROM god2_game.merchant_inventory
     WHERE merchant_inventory_id=$merchant_id)
  + (SELECT COUNT(*) FROM mysql.user
     WHERE User IN ('$test_user','$fault_user'));
SQL
)"

test "$collision_count" = "0" || {
  echo "STOP: fixture identity collision detected"
  exit 1
}

echo "PASS: fixture identities do not collide"

echo "===== PREPARE SCOPED CLEANUP ====="

cleanup_fixture() {
  local identity_rows
  local account_count character_count merchant_count

  identity_rows="$(db_admin <<SQL
SELECT
  (SELECT COUNT(*)
   FROM god2_player.accounts
   WHERE username='$fixture_name'
     AND status='Disabled'),
  (SELECT COUNT(*)
   FROM god2_player.characters c
   JOIN god2_player.accounts a
     ON a.account_id=c.account_id
   WHERE a.username='$fixture_name'
     AND c.name='$fixture_name'
     AND c.enabled=0
     AND c.admin_note='MerchantPurchaseFixture'),
  (SELECT COUNT(*)
   FROM god2_game.merchants
   WHERE merchant_id=$merchant_id
     AND admin_note='$fixture_name');
SQL
)"

  read -r account_count character_count merchant_count <<< "$identity_rows"

  echo "CLEANUP CHECK: account=$account_count character=$character_count merchant=$merchant_count"

  case "$account_count:$character_count:$merchant_count" in
    0:0:0)
      local orphan_count

      orphan_count="$(db_admin <<SQL
SELECT
  (SELECT COUNT(*)
   FROM god2_game.merchant_inventory
   WHERE merchant_id=$merchant_id
      OR merchant_inventory_id=$merchant_id)
  + (SELECT COUNT(*)
     FROM god2_player.characters
     WHERE name='$fixture_name')
  + (SELECT COUNT(*)
     FROM god2_player.accounts
     WHERE username='$fixture_name')
  + (SELECT COUNT(*)
     FROM god2_game.merchants
     WHERE merchant_id=$merchant_id
        OR admin_note='$fixture_name')
  + (SELECT COUNT(*)
     FROM mysql.user
     WHERE User IN ('$test_user','$fault_user'));
SQL
)"

      test "$orphan_count" = "0" || {
        echo "STOP: residual fixture identities detected"
        return 1
      }

      echo "PASS: no residual fixture identities"
      echo "CLEANUP: fixture not created"
      ;;
    1:1:1)
      echo "CLEANUP: transactional identity verification and deletion"

      local cleanup_result remaining_rows

      if ! cleanup_result="$(
        {
          printf "SET @fixture_name='%s';\n" "$fixture_name"
          printf "SET @merchant_id=%s;\n" "$merchant_id"
          printf "START TRANSACTION;\n"
          cat Automation/merchant-isolated-3310-transactional-cleanup.sql
          printf "\nCOMMIT;\n"
        } | db_admin
      )"; then
        echo "STOP: transactional cleanup failed"
        return 1
      fi

      printf '%s\n' "$cleanup_result"
      [[ "$cleanup_result" == *"PASS: scoped cleanup complete; transaction remains active"* ]] || {
        echo "STOP: cleanup success marker missing"
        return 1
      }

      if ! remaining_rows="$(db_admin <<SQL
SELECT
  (SELECT COUNT(*) FROM god2_player.accounts
   WHERE username='$fixture_name')
  + (SELECT COUNT(*) FROM god2_player.characters
     WHERE name='$fixture_name')
  + (SELECT COUNT(*) FROM god2_game.merchants
     WHERE merchant_id=$merchant_id OR admin_note='$fixture_name')
  + (SELECT COUNT(*) FROM god2_game.merchant_inventory
     WHERE merchant_id=$merchant_id OR merchant_inventory_id=$merchant_id);
SQL
      )"; then
        echo "STOP: post-commit verification failed"
        return 1
      fi

      test "$remaining_rows" = "0" || {
        echo "STOP: fixture identities remain after cleanup"
        return 1
      }

      echo "PASS: transactional cleanup committed; fixture identities absent"
      ;;
    *)
      echo "STOP: partial or ambiguous fixture identity"
      return 1
      ;;
  esac
}

echo "PASS: scoped cleanup function prepared"

echo "===== PREPARE EXIT CLEANUP TRAP ====="

cleanup_on_exit() {
  local result=$?
  trap - EXIT

  echo "===== EXIT CLEANUP CHECK ====="

  if [[ "$atomic_fault_cleanup" == 1 ]]; then
    if ! cleanup_atomic_faults; then
      exit 1
    fi
  fi

  if [[ -n "${test_user_host:-}" ]]; then
    if ! db_admin <<SQL
DROP USER IF EXISTS '$test_user'@'$test_user_host';
SQL
    then
      echo "STOP: temporary DB user removal failed"
      exit 1
    fi
    echo "PASS: temporary DB user removed"
  fi

  if ! cleanup_fixture; then
    echo "STOP: cleanup verification failed"
    exit 1
  fi

  echo "PASS: exit cleanup verification completed"
  exit "$result"
}

trap cleanup_on_exit EXIT

echo "PASS: EXIT cleanup trap installed"
echo "===== CREATE ISOLATED FIXTURE ====="

db_admin <<SQL
DELIMITER //
BEGIN NOT ATOMIC
  DECLARE EXIT HANDLER FOR SQLEXCEPTION
  BEGIN
    ROLLBACK;
    RESIGNAL;
  END;

  START TRANSACTION;

  INSERT INTO god2_player.accounts (username,password_hash,status)
  VALUES ('$fixture_name','!disabled-fixture-no-login','Disabled');
  SET @created_account_id=LAST_INSERT_ID();

  INSERT INTO god2_player.characters
    (account_id,name,status,enabled,admin_note)
  VALUES
    (@created_account_id,'$fixture_name','Disabled',0,
     'MerchantPurchaseFixture');
  SET @created_character_id=LAST_INSERT_ID();

  INSERT INTO god2_player.player_inventory_state
    (CharacterId,InventoryId,Capacity,InventoryVersion,
     MutationSequence,DirtyState,UpdatedAtUtc)
  VALUES (@created_character_id,UUID(),8,0,0,'Clean',UTC_TIMESTAMP(6));

  INSERT INTO god2_player.player_currency_balances
    (CharacterId,CurrencyType,Balance,Version,DirtyState,UpdatedAtUtc)
  VALUES (@created_character_id,'Gold',100,0,'Clean',UTC_TIMESTAMP(6));

  INSERT INTO god2_game.merchants
    (merchant_id,name_zh_tw,buyback_enabled,enabled,admin_note)
  VALUES ($merchant_id,'$fixture_name',1,1,'$fixture_name');

  INSERT INTO god2_game.merchant_inventory
    (merchant_inventory_id,merchant_id,item_id,
     selling_price,purchasing_price,pack_count,quantity_limit,enabled)
  VALUES ($merchant_id,$merchant_id,253231541,40,4,1,1,1);

  COMMIT;
  SELECT 'PASS: isolated fixture committed',
         @created_account_id,@created_character_id;
END//
DELIMITER ;
SQL

echo "===== PREPARE RECOVERY TEST ACCOUNT ====="

test_user_host="$(sudo docker inspect -f \
  '{{range .NetworkSettings.Networks}}{{.Gateway}}{{end}}' "$container")"
[[ "$test_user_host" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]

db_admin <<SQL
CREATE USER '$test_user'@'$test_user_host' IDENTIFIED BY '$test_password';
GRANT SELECT ON god2_player.characters TO '$test_user'@'$test_user_host';
GRANT SELECT,UPDATE ON god2_player.player_inventory_state TO '$test_user'@'$test_user_host';
GRANT SELECT,UPDATE ON god2_player.player_currency_balances TO '$test_user'@'$test_user_host';
GRANT SELECT,INSERT,UPDATE,DELETE ON god2_player.character_inventory TO '$test_user'@'$test_user_host';
GRANT SELECT,INSERT ON god2_player.inventory_item_identity_sequence TO '$test_user'@'$test_user_host';
GRANT SELECT,INSERT ON god2_player.inventory_transaction_idempotency TO '$test_user'@'$test_user_host';
GRANT SELECT,INSERT ON god2_player.inventory_audit_ledger TO '$test_user'@'$test_user_host';
GRANT SELECT,INSERT ON god2_player.v2_merchant_purchase_journal TO '$test_user'@'$test_user_host';
GRANT SELECT,INSERT ON god2_player.v2_merchant_sale_journal TO '$test_user'@'$test_user_host';
GRANT SELECT ON god2_game.items TO '$test_user'@'$test_user_host';
GRANT SELECT ON god2_game.item_registry TO '$test_user'@'$test_user_host';
GRANT SELECT ON god2_game.merchants TO '$test_user'@'$test_user_host';
GRANT SELECT ON god2_game.merchant_inventory TO '$test_user'@'$test_user_host';
SQL

fixture_id="$(db_admin <<SQL
SELECT c.character_id
FROM god2_player.characters c
JOIN god2_player.accounts a ON a.account_id=c.account_id
WHERE a.username='$fixture_name' AND a.status='Disabled'
  AND c.name='$fixture_name' AND c.enabled=0
  AND c.admin_note='MerchantPurchaseFixture';
SQL
)"
[[ "$fixture_id" =~ ^[0-9]+$ ]]
test "$fixture_id" -gt 1

export GOD2_MERCHANT_COMMAND_FIXTURE_CHARACTER_ID="$fixture_id"
export GOD2_MERCHANT_COMMAND_FIXTURE_MERCHANT_ID="$merchant_id"
export GOD2_MERCHANT_COMMAND_FIXTURE_USER="$test_user"
export GOD2_MERCHANT_COMMAND_FIXTURE_PASSWORD="$test_password"

if [[ "$scenario" == stack ]]; then
  db_admin <<SQL
GRANT CREATE TEMPORARY TABLES ON god2_game.*
TO '$test_user'@'$test_user_host';
GRANT SELECT (account_id,username,status) ON god2_player.accounts
TO '$test_user'@'$test_user_host';
SQL
  export GOD2_STACK_FIXTURE_CHARACTER_ID="$fixture_id"
  export GOD2_STACK_FIXTURE_USER="$test_user"
  export GOD2_STACK_FIXTURE_PASSWORD="$test_password"
fi

if [[ "$scenario" == atomicbuy || "$scenario" == atomicsell ]]; then
  prepare_atomic_faults
fi

evidence="$(mktemp -d "docs/handoff/merchant-3310-${scenario}-$(date +%Y%m%d-%H%M%S)-XXXXXX")"
git rev-parse HEAD > "$evidence/source-commit.txt"
echo "evidence=$evidence"

project="tests/God2.ServerV2.Persistence.IntegrationTests/God2.ServerV2.Persistence.IntegrationTests.csproj"
namespace="God2.ServerV2.Persistence.IntegrationTests"

if [[ "$scenario" == buy ]]; then
  producer="$namespace.MerchantPurchaseJournalIntegrationTests.JournalPreservesRequestAndRejectsIdentityConflictsIsolated3310"
  recovery="$namespace.MerchantPurchaseJournalProcessRecoveryTests.SeparateProcessRecoversCommittedRequestWithoutDispatch"
elif [[ "$scenario" == sell ]]; then
  producer="$namespace.MerchantSaleTcpLostResultIntegrationTests.CommittedSaleWithLostResultDoesNotCreditTwiceIsolated3310"
  recovery="$namespace.MerchantSaleJournalProcessRecoveryTests.SeparateProcessRecoversCommittedSaleWithoutDispatch"
elif [[ "$scenario" == stack ]]; then
  producer="$namespace.InventoryGrantStackCommittedTests.Committed_stacks_replay_and_competing_versions_retry_safelyIsolated3310"
  recovery=""
elif [[ "$scenario" == atomicbuy ]]; then
  producer="$namespace.MerchantPurchaseIntegrationTests.Purchase_is_atomic_replayable_and_version_checkedIsolated3310"
  recovery=""
elif [[ "$scenario" == atomicsell ]]; then
  producer="$namespace.MerchantSaleIntegrationTests.Buy_sell_rolls_back_replays_and_rejects_reused_slot_identityIsolated3310"
  recovery=""
elif [[ "$scenario" == economy ]]; then
  producer="$namespace.CharacterEconomySnapshotIntegrationTests.ConcurrentCommitDoesNotMixInventoryAndWalletSnapshotsIsolated3310"
  recovery=""
elif [[ "$scenario" == restoreblocked ]]; then
  producer="$namespace.InventoryRestoreTcpBlockedIntegrationTests.UnsupportedRestoreLayoutClosesWithoutMutationIsolated3310"
  recovery=""
elif [[ "$scenario" == lostresult ]]; then
  producer="$namespace.MerchantPurchaseTcpLostResultIntegrationTests.CommittedBuyWithLostResultReplaysWithoutAnotherMutationIsolated3310"
  recovery=""
elif [[ "$scenario" == reconnect ]]; then
  producer="$namespace.MerchantPurchaseTcpReconnectIntegrationTests.ReconnectRestoresCommittedInventoryWithoutAnotherPurchaseIsolated3310"
  recovery=""
elif [[ "$scenario" == basic ]]; then
  producer="$namespace.MerchantPurchaseTcpIntegrationTests.TcpBuyCommitsMariaDbAndReturnsPurchaseResponseIsolated3310"
  recovery=""
elif [[ "$scenario" == newserver ]]; then
  producer="$namespace.MerchantPurchaseTcpNewServerIntegrationTests.NewServerRestoresCommittedInventoryWithoutAnotherPurchaseIsolated3310"
  recovery=""
elif [[ "$scenario" == tcp ]]; then
  producer="$namespace.MerchantBuySellTcpIntegrationTests.TcpBuyThenSellCommitsAndReturnsBothResponsesIsolated3310"
  recovery=""
else
  producer="$namespace.MerchantCommandIntegrationTests.Current_interaction_dispatches_durable_buy_and_sell_Isolated3310"
  recovery=""
fi

run_phase() {
  local phase="$1" filter="$2"
  shift 2

  dotnet test "$project" --configuration Release "$@" \
    --filter "FullyQualifiedName=$filter" \
    --logger "trx;LogFileName=$phase.trx" \
    --results-directory "$evidence" --verbosity minimal \
    2>&1 | tee "$evidence/$phase.log"

  python3 - "$evidence/$phase.trx" <<'PY_TRX'
import sys
import xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
counters = root.find(".//{*}Counters")
assert counters is not None, "STOP: missing TRX counters"
for key in ("total", "executed", "passed"):
    assert counters.get(key) == "1", f"STOP: {key}={counters.get(key)}"
assert counters.get("failed") == "0", "STOP: failed test"
assert counters.get("notExecuted") == "0", "STOP: skipped test"
print("PASS: exactly one test executed and passed")
PY_TRX
}

echo "===== $scenario PRODUCER ====="
run_phase producer "$producer"
if [[ -n "$recovery" ]]; then
  echo "===== $scenario SEPARATE-PROCESS RECOVERY ====="
  run_phase recovery "$recovery" --no-build
  echo "PASS: $scenario producer and separate-process recovery completed"
else
  echo "PASS: isolated $scenario integration completed"
fi
