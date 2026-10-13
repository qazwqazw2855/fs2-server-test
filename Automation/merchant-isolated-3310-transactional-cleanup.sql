-- STEP 90Q-23Z
-- Isolated 3310 only; caller must verify Docker identity and port mapping.
-- Caller supplies @fixture_name and @merchant_id.
-- Caller starts the transaction; this block never commits.
-- Successful caller must explicitly COMMIT or ROLLBACK.
-- Any SQL exception rolls back the complete transaction.
-- No account DDL, catalog deletion or foreign-key disabling.

DELIMITER //
BEGIN NOT ATOMIC
  DECLARE blocked_rows BIGINT DEFAULT 0;
  DECLARE expected_rows BIGINT DEFAULT 0;
  DECLARE deleted_rows BIGINT DEFAULT 0;
  DECLARE EXIT HANDLER FOR SQLEXCEPTION
  BEGIN
    ROLLBACK;
    RESIGNAL;
  END;

  IF @@in_transaction <> 1 THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_TRANSACTION_REQUIRED';
  END IF;

  IF @fixture_name IS NULL
     OR @fixture_name NOT REGEXP '^shopfixture_[0-9a-f]{32}$'
     OR @merchant_id IS NULL
     OR @merchant_id < 4000000000
     OR @merchant_id >= 5000000000
  THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_ARGUMENTS_INVALID';
  END IF;

  SET @cleanup_account_id=NULL, @cleanup_character_id=NULL;
  SET @cleanup_locked_merchant_id=NULL;

  SELECT account_id INTO @cleanup_account_id
  FROM god2_player.accounts
  WHERE username=@fixture_name AND status='Disabled'
  FOR UPDATE;

  SELECT c.character_id INTO @cleanup_character_id
  FROM god2_player.characters c
  JOIN god2_player.accounts a ON a.account_id=c.account_id
  WHERE a.username=@fixture_name AND a.status='Disabled'
    AND c.name=@fixture_name AND c.enabled=0
    AND c.admin_note='MerchantPurchaseFixture'
  FOR UPDATE;

  SELECT merchant_id INTO @cleanup_locked_merchant_id
  FROM god2_game.merchants
  WHERE merchant_id=@merchant_id AND admin_note=@fixture_name
  FOR UPDATE;


BEGIN
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
END;

  IF @cleanup_account_id IS NULL OR @cleanup_character_id IS NULL
     OR NOT (@cleanup_locked_merchant_id <=> @merchant_id)
  THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_LOCKED_IDS_INVALID';
  END IF;

  SELECT COALESCE(SUM(row_count),0) INTO blocked_rows
  FROM (
SELECT 'character_equipment.character_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`character_equipment` d
JOIN god2_player.characters c
  ON d.`character_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'character_immortals.owner_character_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`character_immortals` d
JOIN god2_player.characters c
  ON d.`owner_character_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'character_inventory.character_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`character_inventory` d
JOIN god2_player.characters c
  ON d.`character_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'character_learned_recipes.character_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`character_learned_recipes` d
JOIN god2_player.characters c
  ON d.`character_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'character_life_skills.character_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`character_life_skills` d
JOIN god2_player.characters c
  ON d.`character_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'character_pets.owner_character_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`character_pets` d
JOIN god2_player.characters c
  ON d.`owner_character_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'player_relationships.character_id_high' AS dependency, COUNT(*) AS row_count
FROM god2_player.`player_relationships` d
JOIN god2_player.characters c
  ON d.`character_id_high` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'player_relationships.character_id_low' AS dependency, COUNT(*) AS row_count
FROM god2_player.`player_relationships` d
JOIN god2_player.characters c
  ON d.`character_id_low` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'player_social_blocks.blocked_character_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`player_social_blocks` d
JOIN god2_player.characters c
  ON d.`blocked_character_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'player_social_blocks.blocker_character_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`player_social_blocks` d
JOIN god2_player.characters c
  ON d.`blocker_character_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'player_social_invitations.requester_character_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`player_social_invitations` d
JOIN god2_player.characters c
  ON d.`requester_character_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'player_social_invitations.response_actor_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`player_social_invitations` d
JOIN god2_player.characters c
  ON d.`response_actor_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'player_social_invitations.target_character_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`player_social_invitations` d
JOIN god2_player.characters c
  ON d.`target_character_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'player_social_operations.actor_character_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`player_social_operations` d
JOIN god2_player.characters c
  ON d.`actor_character_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'player_social_operations.related_character_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`player_social_operations` d
JOIN god2_player.characters c
  ON d.`related_character_id` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'v2_quest_instances.CharacterId' AS dependency, COUNT(*) AS row_count
FROM god2_player.`v2_quest_instances` d
JOIN god2_player.characters c
  ON d.`CharacterId` = c.character_id
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
UNION ALL

SELECT 'character_equipment.inventory_id' AS dependency, COUNT(*) AS row_count
FROM god2_player.`character_equipment` d
JOIN god2_player.characters c
  ON d.`inventory_id` IN (
    SELECT i.inventory_id
    FROM god2_player.character_inventory i
    WHERE i.character_id = c.character_id
  )
JOIN god2_player.accounts a
  ON a.account_id = c.account_id
WHERE a.username = @fixture_name
  AND c.name = @fixture_name
  AND c.enabled = 0
  AND c.admin_note = 'MerchantPurchaseFixture'
  ) dependencies
  WHERE dependency<>'character_inventory.character_id';

  IF blocked_rows <> 0 THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_RESTRICT_DEPENDENCIES';
  END IF;

  SELECT
(SELECT COUNT(*) FROM god2_player.`character_item_element_bonuses` WHERE `character_id`=@cleanup_character_id)
    + (SELECT COUNT(*) FROM god2_player.`character_item_experience_bonuses` WHERE `character_id`=@cleanup_character_id)
    + (SELECT COUNT(*) FROM god2_player.`character_item_timed_effects` WHERE `character_id`=@cleanup_character_id)
    + (SELECT COUNT(*) FROM god2_player.`equipment_enhancement_idempotency` WHERE `character_id`=@cleanup_character_id)
    + (SELECT COUNT(*) FROM god2_player.`equipment_instances` WHERE `character_id`=@cleanup_character_id)
    + (SELECT COUNT(*) FROM god2_player.`item_use_idempotency` WHERE `character_id`=@cleanup_character_id)
    + (SELECT COUNT(*) FROM god2_player.`pet_operation_audit` WHERE `character_id`=@cleanup_character_id)
    + (SELECT COUNT(*) FROM god2_player.`pet_operation_idempotency` WHERE `character_id`=@cleanup_character_id)
    + (SELECT COUNT(*) FROM god2_player.`world_interaction_audit` WHERE `CharacterId`=@cleanup_character_id)
    + (SELECT COUNT(*) FROM god2_player.`world_interaction_idempotency` WHERE `CharacterId`=@cleanup_character_id)
    + (SELECT COUNT(*) FROM god2_player.equipment_instances e
        JOIN god2_player.character_inventory i
          ON i.inventory_id=e.inventory_id
        WHERE i.character_id=@cleanup_character_id)
    + (SELECT COUNT(*) FROM god2_player.character_lifecycle_idempotency
        WHERE account_id=@cleanup_account_id)
    + (SELECT COUNT(*) FROM god2_player.characters
        WHERE account_id=@cleanup_account_id
          AND character_id<>@cleanup_character_id)
    + (SELECT COUNT(*) FROM god2_game.npcs
        WHERE merchant_id=@merchant_id)
    + (SELECT COUNT(*) FROM god2_game.merchant_client_catalog_identities x
        JOIN god2_game.merchant_inventory mi
          ON mi.merchant_inventory_id=x.merchant_inventory_id
        WHERE mi.merchant_id=@merchant_id)
    + (SELECT COUNT(*) FROM god2_game.merchant_inventory
        WHERE (merchant_id=@merchant_id OR merchant_inventory_id=@merchant_id)
          AND NOT (
            merchant_id=@merchant_id
            AND merchant_inventory_id=@merchant_id
            AND item_id=253231541
          ))
  INTO blocked_rows;

  IF blocked_rows <> 0 THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_UNEXPECTED_DEPENDENCIES';
  END IF;

  -- Refuse sequence removal if another character references the same ID.
  SELECT COUNT(*) INTO blocked_rows
  FROM (

SELECT inventory_id AS owned_id
FROM god2_player.character_inventory
WHERE character_id=@cleanup_character_id
UNION
SELECT InventoryItemId AS owned_id
FROM god2_player.inventory_audit_ledger
WHERE CharacterId=@cleanup_character_id
  AND InventoryItemId IS NOT NULL

  ) owned
  WHERE EXISTS (
    SELECT 1 FROM god2_player.character_inventory i
    WHERE i.inventory_id=owned.owned_id
      AND i.character_id<>@cleanup_character_id
  ) OR EXISTS (
    SELECT 1 FROM god2_player.inventory_audit_ledger a
    WHERE a.InventoryItemId=owned.owned_id
      AND a.CharacterId<>@cleanup_character_id
  );

  IF blocked_rows <> 0 THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_SHARED_ITEM_IDENTITY';
  END IF;

  SELECT COUNT(*) INTO expected_rows
  FROM god2_player.inventory_item_identity_sequence s
  JOIN (

SELECT inventory_id AS owned_id
FROM god2_player.character_inventory
WHERE character_id=@cleanup_character_id
UNION
SELECT InventoryItemId AS owned_id
FROM god2_player.inventory_audit_ledger
WHERE CharacterId=@cleanup_character_id
  AND InventoryItemId IS NOT NULL

  ) owned ON owned.owned_id=s.PersistentInventoryItemId;

  DELETE s
  FROM god2_player.inventory_item_identity_sequence s
  JOIN (

SELECT inventory_id AS owned_id
FROM god2_player.character_inventory
WHERE character_id=@cleanup_character_id
UNION
SELECT InventoryItemId AS owned_id
FROM god2_player.inventory_audit_ledger
WHERE CharacterId=@cleanup_character_id
  AND InventoryItemId IS NOT NULL

  ) owned ON owned.owned_id=s.PersistentInventoryItemId;
  SET deleted_rows=ROW_COUNT();

  IF deleted_rows<>expected_rows THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_SEQUENCE_COUNT_MISMATCH';
  END IF;

  SELECT COUNT(*) INTO expected_rows
  FROM god2_player.character_inventory
  WHERE character_id=@cleanup_character_id;

  DELETE FROM god2_player.character_inventory
  WHERE character_id=@cleanup_character_id;
  SET deleted_rows=ROW_COUNT();

  IF deleted_rows<>expected_rows THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_INVENTORY_COUNT_MISMATCH';
  END IF;

  -- Only reviewed merchant journal/economy tables may contain cascade data.
  DELETE FROM god2_player.characters
  WHERE character_id=@cleanup_character_id
    AND account_id=@cleanup_account_id
    AND name=@fixture_name AND enabled=0
    AND admin_note='MerchantPurchaseFixture';
  SET deleted_rows=ROW_COUNT();

  IF deleted_rows<>1 THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_CHARACTER_COUNT_MISMATCH';
  END IF;

  DELETE FROM god2_player.accounts
  WHERE account_id=@cleanup_account_id
    AND username=@fixture_name AND status='Disabled';
  SET deleted_rows=ROW_COUNT();

  IF deleted_rows<>1 THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_ACCOUNT_COUNT_MISMATCH';
  END IF;

  SELECT COUNT(*) INTO expected_rows
  FROM god2_game.merchant_inventory
  WHERE merchant_id=@merchant_id
    AND merchant_inventory_id=@merchant_id
    AND item_id=253231541;

  DELETE FROM god2_game.merchant_inventory
  WHERE merchant_id=@merchant_id
    AND merchant_inventory_id=@merchant_id
    AND item_id=253231541;
  SET deleted_rows=ROW_COUNT();

  IF deleted_rows<>expected_rows THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_MERCHANT_INVENTORY_COUNT_MISMATCH';
  END IF;

  DELETE FROM god2_game.merchants
  WHERE merchant_id=@merchant_id AND admin_note=@fixture_name;
  SET deleted_rows=ROW_COUNT();

  IF deleted_rows<>1 THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_MERCHANT_COUNT_MISMATCH';
  END IF;

  SELECT
    (SELECT COUNT(*) FROM god2_player.accounts
     WHERE account_id=@cleanup_account_id OR username=@fixture_name)
    + (SELECT COUNT(*) FROM god2_player.characters
       WHERE character_id=@cleanup_character_id
          OR account_id=@cleanup_account_id OR name=@fixture_name)
    + (SELECT COUNT(*) FROM god2_game.merchants
       WHERE merchant_id=@merchant_id OR admin_note=@fixture_name)
    + (SELECT COUNT(*) FROM god2_game.merchant_inventory
       WHERE merchant_id=@merchant_id OR merchant_inventory_id=@merchant_id)
  INTO blocked_rows;

  IF blocked_rows<>0 OR @@in_transaction<>1 THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT='MERCHANT_CLEANUP_FINAL_STATE_INVALID';
  END IF;

  SELECT 'PASS: scoped cleanup complete; transaction remains active';
END//
DELIMITER ;
