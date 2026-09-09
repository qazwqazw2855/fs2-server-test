-- Rename Battle Architecture V2 durability columns to describe their runtime
-- purpose. These hashes and schema versions are replay/idempotency controls,
-- not packet evidence. Preserve existing rows and keep C# runtime contracts
-- stable by changing only the database column names.

ALTER TABLE `battle_command_journal`
    DROP CONSTRAINT IF EXISTS `CK_BattleJournal_Sequence`;

ALTER TABLE `battle_command_journal`
    ADD COLUMN IF NOT EXISTS `CommandSchemaVersion` int NOT NULL DEFAULT 1 AFTER `SafeIdempotencyHash`,
    ADD COLUMN IF NOT EXISTS `CanonicalCommandJson` longtext NULL AFTER `CommandSchemaVersion`,
    ADD COLUMN IF NOT EXISTS `CommandFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' AFTER `CanonicalCommandJson`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'battle_command_journal'
          AND COLUMN_NAME = 'PayloadVersion'
    ),
    'UPDATE `battle_command_journal` SET `CommandSchemaVersion`=`PayloadVersion`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'battle_command_journal'
          AND COLUMN_NAME = 'CanonicalPayload'
    ),
    'UPDATE `battle_command_journal` SET `CanonicalCommandJson`=`CanonicalPayload` WHERE `CanonicalCommandJson` IS NULL',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'battle_command_journal'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `battle_command_journal` SET `CommandFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

UPDATE `battle_command_journal`
SET `CanonicalCommandJson` = ''
WHERE `CanonicalCommandJson` IS NULL;

ALTER TABLE `battle_command_journal`
    MODIFY COLUMN `CanonicalCommandJson` longtext NOT NULL,
    MODIFY COLUMN `CommandFingerprintSha256` char(64) NOT NULL,
    DROP COLUMN IF EXISTS `PayloadVersion`,
    DROP COLUMN IF EXISTS `CanonicalPayload`,
    DROP COLUMN IF EXISTS `PayloadHash`,
    ADD CONSTRAINT `CK_BattleJournal_Sequence`
        CHECK (`JournalSequence` >= 1 AND `BattleVersion` >= 0 AND `RoundNumber` >= 1
               AND `CommandWindowVersion` >= 0 AND `CommandSchemaVersion` >= 1);

ALTER TABLE `battle_round_checkpoints`
    DROP CONSTRAINT IF EXISTS `CK_BattleCheckpoint_Version`;

ALTER TABLE `battle_round_checkpoints`
    ADD COLUMN IF NOT EXISTS `SnapshotSchemaVersion` int NOT NULL DEFAULT 1 AFTER `CheckpointVersion`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'battle_round_checkpoints'
          AND COLUMN_NAME = 'PayloadVersion'
    ),
    'UPDATE `battle_round_checkpoints` SET `SnapshotSchemaVersion`=`PayloadVersion`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `battle_round_checkpoints`
    DROP COLUMN IF EXISTS `PayloadVersion`,
    ADD CONSTRAINT `CK_BattleCheckpoint_Version`
        CHECK (`CheckpointVersion` >= 1 AND `SnapshotSchemaVersion` >= 1);

ALTER TABLE `battle_event_outbox`
    DROP CONSTRAINT IF EXISTS `CK_BattleOutbox_Sequence`;

ALTER TABLE `battle_event_outbox`
    ADD COLUMN IF NOT EXISTS `EventSchemaVersion` int NOT NULL DEFAULT 1 AFTER `ActionExecutionId`,
    ADD COLUMN IF NOT EXISTS `EventFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' AFTER `EventSchemaVersion`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'battle_event_outbox'
          AND COLUMN_NAME = 'PayloadVersion'
    ),
    'UPDATE `battle_event_outbox` SET `EventSchemaVersion`=`PayloadVersion`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'battle_event_outbox'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `battle_event_outbox` SET `EventFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `battle_event_outbox`
    MODIFY COLUMN `EventFingerprintSha256` char(64) NOT NULL,
    DROP COLUMN IF EXISTS `PayloadVersion`,
    DROP COLUMN IF EXISTS `PayloadHash`,
    ADD CONSTRAINT `CK_BattleOutbox_Sequence`
        CHECK (`EventSequence` >= 1 AND `RoundNumber` >= 1 AND `EventSchemaVersion` >= 1
               AND `RetryCount` >= 0);

ALTER TABLE `battle_finalizations`
    ADD COLUMN IF NOT EXISTS `FinalizationFingerprintSha256` char(64) NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' AFTER `SafeIdempotencyHash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'battle_finalizations'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `battle_finalizations` SET `FinalizationFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `battle_finalizations`
    MODIFY COLUMN `FinalizationFingerprintSha256` char(64) NOT NULL,
    DROP COLUMN IF EXISTS `PayloadHash`;
