-- Rename the remaining runtime idempotency/replay hashes to formal operation
-- names. These are gameplay safety fields that prevent duplicate battle
-- actions, combat commits, and inventory transactions.

ALTER TABLE `battle_actions`
    ADD COLUMN IF NOT EXISTS `ActionFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '戰鬥行動 SHA-256' AFTER `ResolutionIndex`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'battle_actions'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `battle_actions` SET `ActionFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `battle_actions`
    MODIFY COLUMN `ActionFingerprintSha256` char(64) NOT NULL COMMENT '戰鬥行動 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;

ALTER TABLE `battle_idempotency`
    ADD COLUMN IF NOT EXISTS `OperationFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '戰鬥操作 SHA-256' AFTER `IdempotencyKeyHash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'battle_idempotency'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `battle_idempotency` SET `OperationFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `battle_idempotency`
    MODIFY COLUMN `OperationFingerprintSha256` char(64) NOT NULL COMMENT '戰鬥操作 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;

ALTER TABLE `combat_idempotency`
    ADD COLUMN IF NOT EXISTS `CombatFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '戰鬥結算 SHA-256' AFTER `CharacterId`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'combat_idempotency'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `combat_idempotency` SET `CombatFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `combat_idempotency`
    MODIFY COLUMN `CombatFingerprintSha256` char(64) NOT NULL COMMENT '戰鬥結算 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;

ALTER TABLE `inventory_transaction_idempotency`
    ADD COLUMN IF NOT EXISTS `TransactionFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '背包交易 SHA-256' AFTER `IdempotencyKeyHash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'inventory_transaction_idempotency'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `inventory_transaction_idempotency` SET `TransactionFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `inventory_transaction_idempotency`
    MODIFY COLUMN `TransactionFingerprintSha256` char(64) NOT NULL COMMENT '背包交易 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;

ALTER TABLE `god2_player`.`inventory_transaction_idempotency`
    ADD COLUMN IF NOT EXISTS `TransactionFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '背包交易 SHA-256' AFTER `IdempotencyKeyHash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = 'god2_player'
          AND TABLE_NAME = 'inventory_transaction_idempotency'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `god2_player`.`inventory_transaction_idempotency` SET `TransactionFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `god2_player`.`inventory_transaction_idempotency`
    MODIFY COLUMN `TransactionFingerprintSha256` char(64) NOT NULL COMMENT '背包交易 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;
