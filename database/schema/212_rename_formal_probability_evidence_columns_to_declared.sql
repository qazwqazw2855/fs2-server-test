ALTER TABLE `monster_drop_relationships`
    ADD COLUMN IF NOT EXISTS `DeclaredDropChance` decimal(18,9) NULL AFTER `MaximumQuantity`;

SET @copy_declared_drop_chance_sql := IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE `TABLE_SCHEMA`=DATABASE()
          AND `TABLE_NAME`='monster_drop_relationships'
          AND `COLUMN_NAME`='EvidenceDropChance'
    ),
    'UPDATE `monster_drop_relationships` SET `DeclaredDropChance`=`EvidenceDropChance` WHERE `DeclaredDropChance` IS NULL',
    'SELECT 1');
PREPARE copy_declared_drop_chance_stmt FROM @copy_declared_drop_chance_sql;
EXECUTE copy_declared_drop_chance_stmt;
DEALLOCATE PREPARE copy_declared_drop_chance_stmt;

ALTER TABLE `monster_drop_relationships`
    DROP COLUMN IF EXISTS `EvidenceDropChance`;

ALTER TABLE `container_item_relationships`
    ADD COLUMN IF NOT EXISTS `DeclaredProbability` decimal(18,9) NULL AFTER `Quantity`;

SET @copy_declared_container_probability_sql := IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE `TABLE_SCHEMA`=DATABASE()
          AND `TABLE_NAME`='container_item_relationships'
          AND `COLUMN_NAME`='EvidenceProbability'
    ),
    'UPDATE `container_item_relationships` SET `DeclaredProbability`=`EvidenceProbability` WHERE `DeclaredProbability` IS NULL',
    'SELECT 1');
PREPARE copy_declared_container_probability_stmt FROM @copy_declared_container_probability_sql;
EXECUTE copy_declared_container_probability_stmt;
DEALLOCATE PREPARE copy_declared_container_probability_stmt;

ALTER TABLE `container_item_relationships`
    DROP COLUMN IF EXISTS `EvidenceProbability`;

ALTER TABLE `pet_egg_relationships`
    ADD COLUMN IF NOT EXISTS `DeclaredProbability` decimal(18,9) NULL AFTER `HatchResultNameZhTw`;

SET @copy_declared_pet_egg_probability_sql := IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE `TABLE_SCHEMA`=DATABASE()
          AND `TABLE_NAME`='pet_egg_relationships'
          AND `COLUMN_NAME`='EvidenceProbability'
    ),
    'UPDATE `pet_egg_relationships` SET `DeclaredProbability`=`EvidenceProbability` WHERE `DeclaredProbability` IS NULL',
    'SELECT 1');
PREPARE copy_declared_pet_egg_probability_stmt FROM @copy_declared_pet_egg_probability_sql;
EXECUTE copy_declared_pet_egg_probability_stmt;
DEALLOCATE PREPARE copy_declared_pet_egg_probability_stmt;

ALTER TABLE `pet_egg_relationships`
    DROP COLUMN IF EXISTS `EvidenceProbability`;
