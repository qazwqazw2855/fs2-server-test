ALTER TABLE `monster_drop_relationships`
    ADD COLUMN IF NOT EXISTS `EvidenceDropChance` decimal(18,9) NULL AFTER `MaximumQuantity`;

SET @copy_drop_chance_sql := IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE `TABLE_SCHEMA`=DATABASE()
          AND `TABLE_NAME`='monster_drop_relationships'
          AND `COLUMN_NAME`='OriginalDropChance'
    ),
    'UPDATE `monster_drop_relationships` SET `EvidenceDropChance`=`OriginalDropChance` WHERE `EvidenceDropChance` IS NULL',
    'SELECT 1');
PREPARE copy_drop_chance_stmt FROM @copy_drop_chance_sql;
EXECUTE copy_drop_chance_stmt;
DEALLOCATE PREPARE copy_drop_chance_stmt;

ALTER TABLE `monster_drop_relationships`
    DROP COLUMN IF EXISTS `OriginalDropChance`;

ALTER TABLE `container_item_relationships`
    ADD COLUMN IF NOT EXISTS `EvidenceProbability` decimal(18,9) NULL AFTER `Quantity`;

SET @copy_container_probability_sql := IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE `TABLE_SCHEMA`=DATABASE()
          AND `TABLE_NAME`='container_item_relationships'
          AND `COLUMN_NAME`='OriginalProbability'
    ),
    'UPDATE `container_item_relationships` SET `EvidenceProbability`=`OriginalProbability` WHERE `EvidenceProbability` IS NULL',
    'SELECT 1');
PREPARE copy_container_probability_stmt FROM @copy_container_probability_sql;
EXECUTE copy_container_probability_stmt;
DEALLOCATE PREPARE copy_container_probability_stmt;

ALTER TABLE `container_item_relationships`
    DROP COLUMN IF EXISTS `OriginalProbability`;

ALTER TABLE `pet_egg_relationships`
    ADD COLUMN IF NOT EXISTS `EvidenceProbability` decimal(18,9) NULL AFTER `HatchResultNameZhTw`;

SET @copy_pet_egg_probability_sql := IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE `TABLE_SCHEMA`=DATABASE()
          AND `TABLE_NAME`='pet_egg_relationships'
          AND `COLUMN_NAME`='OriginalProbability'
    ),
    'UPDATE `pet_egg_relationships` SET `EvidenceProbability`=`OriginalProbability` WHERE `EvidenceProbability` IS NULL',
    'SELECT 1');
PREPARE copy_pet_egg_probability_stmt FROM @copy_pet_egg_probability_sql;
EXECUTE copy_pet_egg_probability_stmt;
DEALLOCATE PREPARE copy_pet_egg_probability_stmt;

ALTER TABLE `pet_egg_relationships`
    DROP COLUMN IF EXISTS `OriginalProbability`;
