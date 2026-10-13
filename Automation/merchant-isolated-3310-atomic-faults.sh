# Sourced only by the guarded isolated 3310 runner.
atomic_fault_cleanup=0

prepare_atomic_faults() {
  receipt_user="shopreceipt_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
  audit_user="shopaudit_$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
  receipt_trigger="fixture_$receipt_user"
  audit_trigger="fixture_$audit_user"

  [[ "$receipt_user" =~ ^shopreceipt_[0-9a-f]{16}$ ]]
  [[ "$audit_user" =~ ^shopaudit_[0-9a-f]{16}$ ]]

  local collisions
  collisions="$(db_admin <<SQL
SELECT
  (SELECT COUNT(*) FROM mysql.user
   WHERE User IN ('$fault_user','$receipt_user','$audit_user'))
  + (SELECT COUNT(*) FROM information_schema.TRIGGERS
     WHERE TRIGGER_SCHEMA='god2_player'
       AND TRIGGER_NAME IN ('$receipt_trigger','$audit_trigger'));
SQL
  )"
  test "$collisions" = "0"
  atomic_fault_cleanup=1

  local receipt_privileges="SELECT,INSERT"
  local audit_privileges="SELECT,INSERT"
  if [[ "$scenario" == atomicsell ]]; then
    receipt_privileges="SELECT"
    audit_privileges="SELECT"
  fi

  db_admin <<SQL
CREATE USER '$fault_user'@'$test_user_host' IDENTIFIED BY '$test_password';
GRANT SELECT ON god2_player.characters TO '$fault_user'@'$test_user_host';
GRANT SELECT,UPDATE ON god2_player.player_inventory_state TO '$fault_user'@'$test_user_host';
GRANT SELECT ON god2_player.player_currency_balances TO '$fault_user'@'$test_user_host';
GRANT SELECT,INSERT,UPDATE,DELETE ON god2_player.character_inventory TO '$fault_user'@'$test_user_host';
GRANT SELECT,INSERT ON god2_player.inventory_item_identity_sequence TO '$fault_user'@'$test_user_host';
GRANT SELECT,INSERT ON god2_player.inventory_transaction_idempotency TO '$fault_user'@'$test_user_host';
GRANT SELECT,INSERT ON god2_player.inventory_audit_ledger TO '$fault_user'@'$test_user_host';
GRANT SELECT ON god2_game.items TO '$fault_user'@'$test_user_host';
GRANT SELECT ON god2_game.item_registry TO '$fault_user'@'$test_user_host';
GRANT SELECT ON god2_game.merchants TO '$fault_user'@'$test_user_host';
GRANT SELECT ON god2_game.merchant_inventory TO '$fault_user'@'$test_user_host';
CREATE USER '$receipt_user'@'$test_user_host' IDENTIFIED BY '$test_password';
GRANT SELECT ON god2_player.characters TO '$receipt_user'@'$test_user_host';
GRANT SELECT,UPDATE ON god2_player.player_inventory_state TO '$receipt_user'@'$test_user_host';
GRANT SELECT,UPDATE ON god2_player.player_currency_balances TO '$receipt_user'@'$test_user_host';
GRANT SELECT,INSERT,UPDATE,DELETE ON god2_player.character_inventory TO '$receipt_user'@'$test_user_host';
GRANT SELECT,INSERT ON god2_player.inventory_item_identity_sequence TO '$receipt_user'@'$test_user_host';
GRANT $receipt_privileges ON god2_player.inventory_transaction_idempotency TO '$receipt_user'@'$test_user_host';
GRANT SELECT,INSERT ON god2_player.inventory_audit_ledger TO '$receipt_user'@'$test_user_host';
GRANT SELECT ON god2_game.items TO '$receipt_user'@'$test_user_host';
GRANT SELECT ON god2_game.item_registry TO '$receipt_user'@'$test_user_host';
GRANT SELECT ON god2_game.merchants TO '$receipt_user'@'$test_user_host';
GRANT SELECT ON god2_game.merchant_inventory TO '$receipt_user'@'$test_user_host';
CREATE USER '$audit_user'@'$test_user_host' IDENTIFIED BY '$test_password';
GRANT SELECT ON god2_player.characters TO '$audit_user'@'$test_user_host';
GRANT SELECT,UPDATE ON god2_player.player_inventory_state TO '$audit_user'@'$test_user_host';
GRANT SELECT,UPDATE ON god2_player.player_currency_balances TO '$audit_user'@'$test_user_host';
GRANT SELECT,INSERT,UPDATE,DELETE ON god2_player.character_inventory TO '$audit_user'@'$test_user_host';
GRANT SELECT,INSERT ON god2_player.inventory_item_identity_sequence TO '$audit_user'@'$test_user_host';
GRANT SELECT,INSERT ON god2_player.inventory_transaction_idempotency TO '$audit_user'@'$test_user_host';
GRANT $audit_privileges ON god2_player.inventory_audit_ledger TO '$audit_user'@'$test_user_host';
GRANT SELECT ON god2_game.items TO '$audit_user'@'$test_user_host';
GRANT SELECT ON god2_game.item_registry TO '$audit_user'@'$test_user_host';
GRANT SELECT ON god2_game.merchants TO '$audit_user'@'$test_user_host';
GRANT SELECT ON god2_game.merchant_inventory TO '$audit_user'@'$test_user_host';
SQL

  if [[ "$scenario" == atomicbuy ]]; then
db_admin <<SQL
DELIMITER //
CREATE TRIGGER god2_player.$receipt_trigger
BEFORE INSERT ON god2_player.inventory_transaction_idempotency
FOR EACH ROW
BEGIN
  IF SUBSTRING_INDEX(USER(),'@',1)='$receipt_user'
     AND NEW.CharacterId=$fixture_id
     AND NEW.OperationType='V2MerchantBuy' THEN
    IF (SELECT COUNT(*) FROM god2_player.player_currency_balances
        WHERE CharacterId=$fixture_id AND CurrencyType='Gold'
          AND Balance=60 AND Version=1) <> 1 THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT='fixture_wallet_not_updated';
    END IF;
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='fixture_buy_receipt_after_wallet';
  END IF;
END//
CREATE TRIGGER god2_player.$audit_trigger
BEFORE INSERT ON god2_player.inventory_audit_ledger
FOR EACH ROW
BEGIN
  IF SUBSTRING_INDEX(USER(),'@',1)='$audit_user'
     AND NEW.CharacterId=$fixture_id
     AND NEW.OperationType='V2MerchantBuy' THEN
    IF (SELECT COUNT(*) FROM god2_player.player_currency_balances
        WHERE CharacterId=$fixture_id AND CurrencyType='Gold'
          AND Balance=60 AND Version=1) <> 1
       OR (SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
           WHERE CharacterId=$fixture_id AND OperationType='V2MerchantBuy'
             AND TransactionId=NEW.TransactionId) <> 1 THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT='fixture_wallet_or_receipt_missing';
    END IF;
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='fixture_buy_audit_after_receipt';
  END IF;
END//
DELIMITER ;
SQL
  fi

  for prefix in GOD2_SHOP GOD2_SALE; do
    export "${prefix}_FIXTURE_CHARACTER_ID=$fixture_id"
    export "${prefix}_FIXTURE_MERCHANT_ID=$merchant_id"
    export "${prefix}_FIXTURE_USER=$test_user"
    export "${prefix}_FIXTURE_PASSWORD=$test_password"
    export "${prefix}_FAULT_USER=$fault_user"
    export "${prefix}_RECEIPT_FAULT_USER=$receipt_user"
    export "${prefix}_AUDIT_FAULT_USER=$audit_user"
  done
  echo "PASS: isolated atomic fault accounts prepared"
}

cleanup_atomic_faults() {
  local remaining
  if ! db_admin <<SQL
DROP USER IF EXISTS '$fault_user'@'$test_user_host';
DROP USER IF EXISTS '$receipt_user'@'$test_user_host';
DROP USER IF EXISTS '$audit_user'@'$test_user_host';
DROP TRIGGER IF EXISTS god2_player.$receipt_trigger;
DROP TRIGGER IF EXISTS god2_player.$audit_trigger;
SQL
  then
    echo "STOP: atomic fault account/trigger cleanup failed"
    return 1
  fi

  if ! remaining="$(db_admin <<SQL
SELECT
  (SELECT COUNT(*) FROM mysql.user
   WHERE User IN ('$fault_user','$receipt_user','$audit_user'))
  + (SELECT COUNT(*) FROM information_schema.TRIGGERS
     WHERE TRIGGER_SCHEMA='god2_player'
       AND TRIGGER_NAME IN ('$receipt_trigger','$audit_trigger'));
SQL
  )"; then
    return 1
  fi
  test "$remaining" = "0" || return 1
  echo "PASS: all atomic fault users and triggers absent"
}
