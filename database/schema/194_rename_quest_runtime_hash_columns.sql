-- Rename quest runtime replay/idempotency hashes to formal operation names.
-- These hashes prevent duplicate quest accepts/abandons, duplicate objective
-- progress mutations, and duplicate reward finalization.

ALTER TABLE `quest_operation_idempotency`
    ADD COLUMN IF NOT EXISTS `OperationFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '任務操作 SHA-256' AFTER `IdempotencyKeyHash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'quest_operation_idempotency'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `quest_operation_idempotency` SET `OperationFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `quest_operation_idempotency`
    MODIFY COLUMN `OperationFingerprintSha256` char(64) NOT NULL COMMENT '任務操作 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;

ALTER TABLE `quest_progress_mutations`
    ADD COLUMN IF NOT EXISTS `ProgressFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '任務進度異動 SHA-256' AFTER `IdempotencyKeyHash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'quest_progress_mutations'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `quest_progress_mutations` SET `ProgressFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `quest_progress_mutations`
    MODIFY COLUMN `ProgressFingerprintSha256` char(64) NOT NULL COMMENT '任務進度異動 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;

ALTER TABLE `quest_reward_finalization`
    ADD COLUMN IF NOT EXISTS `RewardFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '任務獎勵結算 SHA-256' AFTER `IdempotencyKeyHash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'quest_reward_finalization'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `quest_reward_finalization` SET `RewardFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `quest_reward_finalization`
    MODIFY COLUMN `RewardFingerprintSha256` char(64) NOT NULL COMMENT '任務獎勵結算 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;
