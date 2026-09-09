ALTER TABLE `monster_combat_runtime_state`
    ADD COLUMN IF NOT EXISTS `RuntimeMetadataJson` longtext NULL AFTER `ContentVersion`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'monster_combat_runtime_state'
          AND COLUMN_NAME = 'RawMetadata'
    ),
    'UPDATE `monster_combat_runtime_state` SET `RuntimeMetadataJson`=`RawMetadata` WHERE `RuntimeMetadataJson` IS NULL',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

UPDATE `monster_combat_runtime_state`
SET `RuntimeMetadataJson` = '{}'
WHERE `RuntimeMetadataJson` IS NULL OR `RuntimeMetadataJson` = '';

ALTER TABLE `monster_combat_runtime_state`
    MODIFY COLUMN `RuntimeMetadataJson` longtext NOT NULL,
    DROP COLUMN IF EXISTS `RawMetadata`;
