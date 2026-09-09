-- Rename status-effect runtime replay/idempotency hashes to formal operation
-- names. These are not packet evidence; they protect buff/debuff application
-- and removal from duplicate execution.

ALTER TABLE `battle_status_applications`
    ADD COLUMN IF NOT EXISTS `ApplicationFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '狀態套用操作 SHA-256' AFTER `IdempotencyKeyHash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'battle_status_applications'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `battle_status_applications` SET `ApplicationFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `battle_status_applications`
    MODIFY COLUMN `ApplicationFingerprintSha256` char(64) NOT NULL COMMENT '狀態套用操作 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;

ALTER TABLE `battle_status_removals`
    ADD COLUMN IF NOT EXISTS `RemovalFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '狀態移除操作 SHA-256' AFTER `IdempotencyKeyHash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'battle_status_removals'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `battle_status_removals` SET `RemovalFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `battle_status_removals`
    MODIFY COLUMN `RemovalFingerprintSha256` char(64) NOT NULL COMMENT '狀態移除操作 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;

ALTER TABLE `battle_status_idempotency`
    ADD COLUMN IF NOT EXISTS `OperationFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '狀態效果操作 SHA-256' AFTER `IdempotencyKeyHash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'battle_status_idempotency'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `battle_status_idempotency` SET `OperationFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `battle_status_idempotency`
    MODIFY COLUMN `OperationFingerprintSha256` char(64) NOT NULL COMMENT '狀態效果操作 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;
