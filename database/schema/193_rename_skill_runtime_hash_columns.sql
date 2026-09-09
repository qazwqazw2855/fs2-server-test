-- Rename skill runtime replay/idempotency hashes to formal operation names.
-- These hashes protect skill execution from duplicate MP/cost/effect commits.

ALTER TABLE `skill_executions`
    ADD COLUMN IF NOT EXISTS `ExecutionFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '技能執行 SHA-256' AFTER `RecoveryState`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'skill_executions'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `skill_executions` SET `ExecutionFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `skill_executions`
    MODIFY COLUMN `ExecutionFingerprintSha256` char(64) NOT NULL COMMENT '技能執行 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;

ALTER TABLE `skill_idempotency`
    ADD COLUMN IF NOT EXISTS `OperationFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '技能操作 SHA-256' AFTER `IdempotencyKeyHash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'skill_idempotency'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `skill_idempotency` SET `OperationFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `skill_idempotency`
    MODIFY COLUMN `OperationFingerprintSha256` char(64) NOT NULL COMMENT '技能操作 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;
