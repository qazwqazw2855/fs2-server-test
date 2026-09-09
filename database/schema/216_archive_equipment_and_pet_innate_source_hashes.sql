CREATE TABLE IF NOT EXISTS `god2_research`.`content_profile_source_archive` (
    `FormalTable` varchar(128) NOT NULL,
    `RecordIdentity` varchar(128) NOT NULL,
    `RunId` varchar(64) NOT NULL DEFAULT '',
    `SourceType` varchar(64) NOT NULL DEFAULT '',
    `SourceFile` varchar(768) NOT NULL DEFAULT '',
    `SourceIdentity` varchar(768) NOT NULL DEFAULT '',
    `SourceHash` char(64) NOT NULL DEFAULT '',
    `EvidenceStatus` varchar(32) NOT NULL DEFAULT '',
    `ArchiveReasonZhTw` varchar(512) NOT NULL,
    `ArchivedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`FormalTable`,`RecordIdentity`),
    KEY `IX_content_profile_source_archive_run` (`RunId`),
    KEY `IX_content_profile_source_archive_hash` (`SourceHash`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @has_equipment_source_hash := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'equipment_set_definitions'
      AND `COLUMN_NAME` = 'SourceHash'
);
SET @archive_equipment_source_hash_sql := IF(
    @has_equipment_source_hash > 0,
    'INSERT INTO `god2_research`.`content_profile_source_archive`
        (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`EvidenceStatus`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
     SELECT ''equipment_set_definitions'', CAST(`SetId` AS char), `RunId`, ''OfficialClientCsvZ'', ''EquipSetList.csvZ'', CONCAT(''equipment-set:'', CAST(`SetId` AS char)), `SourceHash`, `SetRelationshipStatus`, ''來源 hash 已移入 research 封存；正式裝備套裝表只保留套裝件數與效果。'', UTC_TIMESTAMP(6)
     FROM `equipment_set_definitions`
     WHERE COALESCE(`SourceHash`,'''')<>''''
     ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceFile`=VALUES(`SourceFile`),`SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`EvidenceStatus`=VALUES(`EvidenceStatus`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)',
    'SELECT 1'
);
PREPARE archive_equipment_source_hash_stmt FROM @archive_equipment_source_hash_sql;
EXECUTE archive_equipment_source_hash_stmt;
DEALLOCATE PREPARE archive_equipment_source_hash_stmt;

SET @has_pet_innate_source_hash := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'pet_innate_definitions'
      AND `COLUMN_NAME` = 'SourceHash'
);
SET @archive_pet_innate_source_hash_sql := IF(
    @has_pet_innate_source_hash > 0,
    'INSERT INTO `god2_research`.`content_profile_source_archive`
        (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`EvidenceStatus`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
     SELECT ''pet_innate_definitions'', CAST(`InnateId` AS char), `RunId`, ''OfficialClientCsvZ'', ''NewCombatPet_Innate.csvZ'', CONCAT(''pet-innate:'', CAST(`InnateId` AS char)), `SourceHash`, CASE WHEN `ProductionEnabled`=1 THEN ''Verified'' ELSE ''EvidenceBlocked'' END, ''來源 hash 已移入 research 封存；正式寵物天賦表只保留天賦名稱、描述與效果。'', UTC_TIMESTAMP(6)
     FROM `pet_innate_definitions`
     WHERE COALESCE(`SourceHash`,'''')<>''''
     ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceFile`=VALUES(`SourceFile`),`SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`EvidenceStatus`=VALUES(`EvidenceStatus`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)',
    'SELECT 1'
);
PREPARE archive_pet_innate_source_hash_stmt FROM @archive_pet_innate_source_hash_sql;
EXECUTE archive_pet_innate_source_hash_stmt;
DEALLOCATE PREPARE archive_pet_innate_source_hash_stmt;

ALTER TABLE `equipment_set_definitions`
    DROP COLUMN IF EXISTS `SourceHash`;

ALTER TABLE `pet_innate_definitions`
    DROP COLUMN IF EXISTS `SourceHash`;
