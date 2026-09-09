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

SET @has_item_source_hash := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'item_content_profiles'
      AND `COLUMN_NAME` = 'SourceHash'
);
SET @has_item_source_section := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'item_content_profiles'
      AND `COLUMN_NAME` = 'SourceSection'
);
SET @archive_item_profile_source_sql := IF(
    @has_item_source_hash > 0,
    CONCAT(
        'INSERT INTO `god2_research`.`content_profile_source_archive`
            (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`EvidenceStatus`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
         SELECT ''item_content_profiles'', CAST(`ItemId` AS char), `RunId`, ''OfficialClientProfile'', ',
        IF(@has_item_source_section > 0, 'COALESCE(`SourceSection`,'''')', ''''''),
        ', CONCAT(''item-profile:'', CAST(`ItemId` AS char), '':'', CAST(`ClientItemId` AS char)), `SourceHash`, ''Derived'', ''來源區段與來源 hash 已移入 research 封存；正式物品 profile 只保留物品屬性與服務端規則。'', UTC_TIMESTAMP(6)
         FROM `item_content_profiles`
         WHERE COALESCE(`SourceHash`,'''')<>''''
         ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceFile`=VALUES(`SourceFile`),`SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`EvidenceStatus`=VALUES(`EvidenceStatus`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)'
    ),
    'SELECT 1'
);
PREPARE archive_item_profile_source_stmt FROM @archive_item_profile_source_sql;
EXECUTE archive_item_profile_source_stmt;
DEALLOCATE PREPARE archive_item_profile_source_stmt;

SET @has_pet_profile_source_hash := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'pet_content_profiles'
      AND `COLUMN_NAME` = 'SourceHash'
);
SET @archive_pet_profile_source_sql := IF(
    @has_pet_profile_source_hash > 0,
    'INSERT INTO `god2_research`.`content_profile_source_archive`
        (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`EvidenceStatus`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
     SELECT ''pet_content_profiles'', `ProfileId`, `RunId`, ''OfficialClientProfile'', ''pet_content_profiles'', CONCAT(''pet-profile:'', CAST(`ClientPetId` AS char)), `SourceHash`, CASE WHEN `ItemId` IS NULL THEN ''EvidenceBlocked'' ELSE ''Derived'' END, ''來源 hash 已移入 research 封存；正式寵物 profile 只保留寵物資料與服務端規則。'', UTC_TIMESTAMP(6)
     FROM `pet_content_profiles`
     WHERE COALESCE(`SourceHash`,'''')<>''''
     ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceFile`=VALUES(`SourceFile`),`SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`EvidenceStatus`=VALUES(`EvidenceStatus`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)',
    'SELECT 1'
);
PREPARE archive_pet_profile_source_stmt FROM @archive_pet_profile_source_sql;
EXECUTE archive_pet_profile_source_stmt;
DEALLOCATE PREPARE archive_pet_profile_source_stmt;

SET @has_quest_profile_source_hash := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'quest_content_profiles'
      AND `COLUMN_NAME` = 'SourceHash'
);
SET @archive_quest_profile_source_sql := IF(
    @has_quest_profile_source_hash > 0,
    'INSERT INTO `god2_research`.`content_profile_source_archive`
        (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`EvidenceStatus`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
     SELECT ''quest_content_profiles'', `ProfileId`, `RunId`, ''OfficialClientProfile'', ''quest_content_profiles'', CONCAT(''quest-profile:'', CAST(`ClientQuestId` AS char)), `SourceHash`, CASE WHEN `ProductionProfileEnabled`=1 THEN ''Derived'' ELSE ''EvidenceBlocked'' END, ''來源 hash 已移入 research 封存；正式任務 profile 只保留任務文字、步驟、NPC 與獎勵。'', UTC_TIMESTAMP(6)
     FROM `quest_content_profiles`
     WHERE COALESCE(`SourceHash`,'''')<>''''
     ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceFile`=VALUES(`SourceFile`),`SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`EvidenceStatus`=VALUES(`EvidenceStatus`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)',
    'SELECT 1'
);
PREPARE archive_quest_profile_source_stmt FROM @archive_quest_profile_source_sql;
EXECUTE archive_quest_profile_source_stmt;
DEALLOCATE PREPARE archive_quest_profile_source_stmt;

SET @has_item_client_source_index := (
    SELECT COUNT(*)
    FROM `information_schema`.`STATISTICS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'item_content_profiles'
      AND `INDEX_NAME` = 'UX_ItemContentProfiles_Client'
);
SET @drop_item_client_source_index_sql := IF(
    @has_item_client_source_index > 0,
    'ALTER TABLE `item_content_profiles` DROP INDEX `UX_ItemContentProfiles_Client`',
    'SELECT 1'
);
PREPARE drop_item_client_source_index_stmt FROM @drop_item_client_source_index_sql;
EXECUTE drop_item_client_source_index_stmt;
DEALLOCATE PREPARE drop_item_client_source_index_stmt;

SET @drop_item_source_section_sql := IF(
    @has_item_source_section > 0,
    'ALTER TABLE `item_content_profiles` DROP COLUMN `SourceSection`',
    'SELECT 1'
);
PREPARE drop_item_source_section_stmt FROM @drop_item_source_section_sql;
EXECUTE drop_item_source_section_stmt;
DEALLOCATE PREPARE drop_item_source_section_stmt;

SET @drop_item_source_hash_sql := IF(
    @has_item_source_hash > 0,
    'ALTER TABLE `item_content_profiles` DROP COLUMN `SourceHash`',
    'SELECT 1'
);
PREPARE drop_item_source_hash_stmt FROM @drop_item_source_hash_sql;
EXECUTE drop_item_source_hash_stmt;
DEALLOCATE PREPARE drop_item_source_hash_stmt;

CREATE INDEX IF NOT EXISTS `IX_ItemContentProfiles_ClientItemId`
    ON `item_content_profiles` (`ClientItemId`);

ALTER TABLE `pet_content_profiles`
    DROP COLUMN IF EXISTS `SourceHash`;

ALTER TABLE `quest_content_profiles`
    DROP COLUMN IF EXISTS `SourceHash`;
