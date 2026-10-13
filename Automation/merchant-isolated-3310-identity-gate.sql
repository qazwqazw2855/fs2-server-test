-- STEP 90Q-23T
-- Isolated 3310 fixture identity gate.
-- Caller must set @fixture_name and @merchant_id.
-- Execute after identity locks in the same active transaction.
-- On any SQL exception: ROLLBACK and propagate the error.
-- No DELETE, COMMIT or routine DDL in this file.

DELIMITER //
BEGIN NOT ATOMIC
  DECLARE EXIT HANDLER FOR SQLEXCEPTION
  BEGIN
    ROLLBACK;
    RESIGNAL;
  END;

  IF @@in_transaction <> 1 THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'MERCHANT_FIXTURE_TRANSACTION_REQUIRED';
  END IF;

SELECT
  (SELECT COUNT(*)
   FROM god2_player.accounts
   WHERE username=@fixture_name
     AND status='Disabled'),

  (SELECT COUNT(*)
   FROM god2_player.characters c
   JOIN god2_player.accounts a
     ON a.account_id=c.account_id
   WHERE a.username=@fixture_name
     AND a.status='Disabled'
     AND c.name=@fixture_name
     AND c.enabled=0
     AND c.admin_note='MerchantPurchaseFixture'),

  (SELECT COUNT(*)
   FROM god2_game.merchants
   WHERE merchant_id=@merchant_id
     AND admin_note=@fixture_name)
INTO
  @fixture_account_count,
  @fixture_character_count,
  @fixture_merchant_count;

  IF NOT (
    @fixture_account_count <=> 1
    AND @fixture_character_count <=> 1
    AND @fixture_merchant_count <=> 1
  ) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'MERCHANT_FIXTURE_IDENTITY_REJECTED';
  END IF;
END//
DELIMITER ;
