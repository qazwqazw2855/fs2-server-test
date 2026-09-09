-- Rename the runtime verification fixture hash to describe its purpose.
-- This is infrastructure verification state, not packet evidence or gameplay
-- payload data. Preserve existing rows and remove the older payload name.

ALTER TABLE `god2`.`runtime_verification_transactions`
    ADD COLUMN IF NOT EXISTS `verification_case_sha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '驗證案例 SHA-256' AFTER `state_version`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = 'god2'
          AND TABLE_NAME = 'runtime_verification_transactions'
          AND COLUMN_NAME = 'payload_sha256'
    ),
    'UPDATE `god2`.`runtime_verification_transactions` SET `verification_case_sha256`=`payload_sha256`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `god2`.`runtime_verification_transactions`
    MODIFY COLUMN `verification_case_sha256` char(64) NOT NULL COMMENT '驗證案例 SHA-256',
    DROP COLUMN IF EXISTS `payload_sha256`;
