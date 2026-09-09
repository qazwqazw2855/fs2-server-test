-- Pet egg relationships are formal hatch gameplay data. Source hashes are
-- evidence metadata, so archive them to research and keep the runtime table
-- readable for owner-facing database inspection.

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_pet_egg_relationship_source_hash_evidence` (
    `relationship_id` char(64) NOT NULL,
    `source_hash` char(64) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`relationship_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='封存寵物蛋孵化關係的來源 SHA-256 證據，正式孵化表不保留機器碼欄位';

SET @archive_pet_egg_source_hash_sql = IF(
    EXISTS (
        SELECT 1
        FROM `information_schema`.`COLUMNS`
        WHERE `TABLE_SCHEMA`='god2'
          AND `TABLE_NAME`='pet_egg_relationships'
          AND `COLUMN_NAME`='SourceHash'
    ),
    'INSERT INTO `god2_research`.`legacy_pet_egg_relationship_source_hash_evidence`
        (`relationship_id`,`source_hash`,`archived_at_utc`)
     SELECT `RelationshipId`,`SourceHash`,UTC_TIMESTAMP(6)
     FROM `god2`.`pet_egg_relationships`
     ON DUPLICATE KEY UPDATE
        `source_hash`=VALUES(`source_hash`),
        `archived_at_utc`=VALUES(`archived_at_utc`)',
    'DO 0'
);
PREPARE archive_pet_egg_source_hash_stmt FROM @archive_pet_egg_source_hash_sql;
EXECUTE archive_pet_egg_source_hash_stmt;
DEALLOCATE PREPARE archive_pet_egg_source_hash_stmt;

ALTER TABLE `god2`.`pet_egg_relationships`
    DROP COLUMN IF EXISTS `SourceHash`;
