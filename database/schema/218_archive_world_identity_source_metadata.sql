CREATE TABLE IF NOT EXISTS `god2_research`.`world_identity_source_archive` (
    `FormalTable` varchar(128) NOT NULL,
    `RecordIdentity` varchar(128) NOT NULL,
    `RunId` varchar(64) NOT NULL DEFAULT '',
    `SourceType` varchar(64) NOT NULL DEFAULT '',
    `SourceIdentity` varchar(768) NOT NULL DEFAULT '',
    `SourceHash` char(64) NOT NULL DEFAULT '',
    `ProvenanceJson` longtext NULL,
    `ArchiveReasonZhTw` varchar(512) NOT NULL,
    `ArchivedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`FormalTable`,`RecordIdentity`),
    KEY `IX_world_identity_source_archive_hash` (`SourceHash`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @has_client_map_source_hash := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'client_map_identities'
      AND `COLUMN_NAME` = 'SourceHash'
);
SET @has_client_map_source_type := (
    SELECT COUNT(*) FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE() AND `TABLE_NAME`='client_map_identities' AND `COLUMN_NAME`='SourceType'
);
SET @has_client_map_source_identity := (
    SELECT COUNT(*) FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE() AND `TABLE_NAME`='client_map_identities' AND `COLUMN_NAME`='SourceIdentity'
);
SET @has_client_map_provenance := (
    SELECT COUNT(*) FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE() AND `TABLE_NAME`='client_map_identities' AND `COLUMN_NAME`='ProvenanceJson'
);
SET @archive_client_map_source_sql := IF(
    @has_client_map_source_hash > 0,
    CONCAT(
        'INSERT INTO `god2_research`.`world_identity_source_archive`
            (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceIdentity`,`SourceHash`,`ProvenanceJson`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
         SELECT ''client_map_identities'', CONCAT(CAST(`MapId` AS char), '':'', `ClientBuildId`), '''', ',
        IF(@has_client_map_source_type > 0, '`SourceType`', ''''''),
        ', ',
        IF(@has_client_map_source_identity > 0, '`SourceIdentity`', ''''''),
        ', `SourceHash`, ',
        IF(@has_client_map_provenance > 0, '`ProvenanceJson`', 'NULL'),
        ', ''地圖客戶端來源證據與 provenance JSON 已移入 research；正式表只保留地圖與客戶端資源對應。'', UTC_TIMESTAMP(6)
         FROM `client_map_identities`
         WHERE COALESCE(`SourceHash`,'''')<>''''
         ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`ProvenanceJson`=VALUES(`ProvenanceJson`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)'
    ),
    'SELECT 1'
);
PREPARE archive_client_map_source_stmt FROM @archive_client_map_source_sql;
EXECUTE archive_client_map_source_stmt;
DEALLOCATE PREPARE archive_client_map_source_stmt;

SET @has_npc_identity_source_hash := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'npc_client_identities'
      AND `COLUMN_NAME` = 'SourceHash'
);
SET @has_npc_identity_source_type := (
    SELECT COUNT(*) FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE() AND `TABLE_NAME`='npc_client_identities' AND `COLUMN_NAME`='SourceType'
);
SET @has_npc_identity_source_identity := (
    SELECT COUNT(*) FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE() AND `TABLE_NAME`='npc_client_identities' AND `COLUMN_NAME`='SourceIdentity'
);
SET @has_npc_identity_provenance := (
    SELECT COUNT(*) FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE() AND `TABLE_NAME`='npc_client_identities' AND `COLUMN_NAME`='ProvenanceJson'
);
SET @archive_npc_identity_source_sql := IF(
    @has_npc_identity_source_hash > 0,
    CONCAT(
        'INSERT INTO `god2_research`.`world_identity_source_archive`
            (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceIdentity`,`SourceHash`,`ProvenanceJson`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
         SELECT ''npc_client_identities'', CONCAT(CAST(`NpcId` AS char), '':'', `ClientBuildId`), '''', ',
        IF(@has_npc_identity_source_type > 0, '`SourceType`', ''''''),
        ', ',
        IF(@has_npc_identity_source_identity > 0, '`SourceIdentity`', ''''''),
        ', `SourceHash`, ',
        IF(@has_npc_identity_provenance > 0, '`ProvenanceJson`', 'NULL'),
        ', ''NPC 客戶端來源證據與 provenance JSON 已移入 research；正式表只保留 NPC 與客戶端外觀資源對應。'', UTC_TIMESTAMP(6)
         FROM `npc_client_identities`
         WHERE COALESCE(`SourceHash`,'''')<>''''
         ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`ProvenanceJson`=VALUES(`ProvenanceJson`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)'
    ),
    'SELECT 1'
);
PREPARE archive_npc_identity_source_stmt FROM @archive_npc_identity_source_sql;
EXECUTE archive_npc_identity_source_stmt;
DEALLOCATE PREPARE archive_npc_identity_source_stmt;

SET @has_npc_spawn_source_hash := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'npc_spawns'
      AND `COLUMN_NAME` = 'SourceHash'
);
SET @has_npc_spawn_source_type := (
    SELECT COUNT(*) FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE() AND `TABLE_NAME`='npc_spawns' AND `COLUMN_NAME`='SourceType'
);
SET @has_npc_spawn_source_identity := (
    SELECT COUNT(*) FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE() AND `TABLE_NAME`='npc_spawns' AND `COLUMN_NAME`='SourceIdentity'
);
SET @has_npc_spawn_provenance := (
    SELECT COUNT(*) FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE() AND `TABLE_NAME`='npc_spawns' AND `COLUMN_NAME`='ProvenanceJson'
);
SET @archive_npc_spawn_source_sql := IF(
    @has_npc_spawn_source_hash > 0,
    CONCAT(
        'INSERT INTO `god2_research`.`world_identity_source_archive`
            (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceIdentity`,`SourceHash`,`ProvenanceJson`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
         SELECT ''npc_spawns'', CAST(`Id` AS char), '''', ',
        IF(@has_npc_spawn_source_type > 0, '`SourceType`', ''''''),
        ', ',
        IF(@has_npc_spawn_source_identity > 0, '`SourceIdentity`', ''''''),
        ', `SourceHash`, ',
        IF(@has_npc_spawn_provenance > 0, '`ProvenanceJson`', 'NULL'),
        ', ''NPC 出生點來源證據與 provenance JSON 已移入 research；正式表只保留 NPC、地圖、座標與客戶端觀測代號。'', UTC_TIMESTAMP(6)
         FROM `npc_spawns`
         WHERE COALESCE(`SourceHash`,'''')<>''''
         ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`ProvenanceJson`=VALUES(`ProvenanceJson`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)'
    ),
    'SELECT 1'
);
PREPARE archive_npc_spawn_source_stmt FROM @archive_npc_spawn_source_sql;
EXECUTE archive_npc_spawn_source_stmt;
DEALLOCATE PREPARE archive_npc_spawn_source_stmt;

SET @has_npc_spawn_observed_source_index := (
    SELECT COUNT(*)
    FROM `information_schema`.`STATISTICS`
    WHERE `TABLE_SCHEMA` = DATABASE()
      AND `TABLE_NAME` = 'npc_spawns'
      AND `INDEX_NAME` = 'UX_NpcSpawns_ObservedEntity'
);
SET @drop_npc_spawn_observed_source_index_sql := IF(
    @has_npc_spawn_observed_source_index > 0,
    'ALTER TABLE `npc_spawns` DROP INDEX `UX_NpcSpawns_ObservedEntity`',
    'SELECT 1'
);
PREPARE drop_npc_spawn_observed_source_index_stmt FROM @drop_npc_spawn_observed_source_index_sql;
EXECUTE drop_npc_spawn_observed_source_index_stmt;
DEALLOCATE PREPARE drop_npc_spawn_observed_source_index_stmt;

SET @has_client_map_runtime_gate := (
    SELECT COUNT(*) FROM `information_schema`.`CHECK_CONSTRAINTS`
    WHERE `CONSTRAINT_SCHEMA`=DATABASE() AND `TABLE_NAME`='client_map_identities' AND `CONSTRAINT_NAME`='CK_ClientMapIdentities_RuntimeGate'
);
SET @drop_client_map_runtime_gate_sql := IF(@has_client_map_runtime_gate > 0, 'ALTER TABLE `client_map_identities` DROP CONSTRAINT `CK_ClientMapIdentities_RuntimeGate`', 'SELECT 1');
PREPARE drop_client_map_runtime_gate_stmt FROM @drop_client_map_runtime_gate_sql;
EXECUTE drop_client_map_runtime_gate_stmt;
DEALLOCATE PREPARE drop_client_map_runtime_gate_stmt;

SET @has_client_map_provenance_check := (
    SELECT COUNT(*) FROM `information_schema`.`CHECK_CONSTRAINTS`
    WHERE `CONSTRAINT_SCHEMA`=DATABASE() AND `TABLE_NAME`='client_map_identities' AND `CONSTRAINT_NAME`='ProvenanceJson'
);
SET @drop_client_map_provenance_check_sql := 'SELECT 1';
PREPARE drop_client_map_provenance_check_stmt FROM @drop_client_map_provenance_check_sql;
EXECUTE drop_client_map_provenance_check_stmt;
DEALLOCATE PREPARE drop_client_map_provenance_check_stmt;

SET @has_npc_identity_provenance_check := (
    SELECT COUNT(*) FROM `information_schema`.`CHECK_CONSTRAINTS`
    WHERE `CONSTRAINT_SCHEMA`=DATABASE() AND `TABLE_NAME`='npc_client_identities' AND `CONSTRAINT_NAME`='ProvenanceJson'
);
SET @drop_npc_identity_provenance_check_sql := 'SELECT 1';
PREPARE drop_npc_identity_provenance_check_stmt FROM @drop_npc_identity_provenance_check_sql;
EXECUTE drop_npc_identity_provenance_check_stmt;
DEALLOCATE PREPARE drop_npc_identity_provenance_check_stmt;

SET @has_npc_spawn_provenance_check := (
    SELECT COUNT(*) FROM `information_schema`.`CHECK_CONSTRAINTS`
    WHERE `CONSTRAINT_SCHEMA`=DATABASE() AND `TABLE_NAME`='npc_spawns' AND `CONSTRAINT_NAME`='ProvenanceJson'
);
SET @drop_npc_spawn_provenance_check_sql := 'SELECT 1';
PREPARE drop_npc_spawn_provenance_check_stmt FROM @drop_npc_spawn_provenance_check_sql;
EXECUTE drop_npc_spawn_provenance_check_stmt;
DEALLOCATE PREPARE drop_npc_spawn_provenance_check_stmt;

SET @drop_client_map_source_type_sql := IF(@has_client_map_source_type > 0, 'ALTER TABLE `client_map_identities` DROP COLUMN `SourceType`', 'SELECT 1');
PREPARE drop_client_map_source_type_stmt FROM @drop_client_map_source_type_sql;
EXECUTE drop_client_map_source_type_stmt;
DEALLOCATE PREPARE drop_client_map_source_type_stmt;

SET @drop_client_map_source_identity_sql := IF(@has_client_map_source_identity > 0, 'ALTER TABLE `client_map_identities` DROP COLUMN `SourceIdentity`', 'SELECT 1');
PREPARE drop_client_map_source_identity_stmt FROM @drop_client_map_source_identity_sql;
EXECUTE drop_client_map_source_identity_stmt;
DEALLOCATE PREPARE drop_client_map_source_identity_stmt;

SET @drop_client_map_source_hash_sql := IF(@has_client_map_source_hash > 0, 'ALTER TABLE `client_map_identities` DROP COLUMN `SourceHash`', 'SELECT 1');
PREPARE drop_client_map_source_hash_stmt FROM @drop_client_map_source_hash_sql;
EXECUTE drop_client_map_source_hash_stmt;
DEALLOCATE PREPARE drop_client_map_source_hash_stmt;

SET @drop_client_map_provenance_sql := IF(@has_client_map_provenance > 0, 'ALTER TABLE `client_map_identities` DROP COLUMN `ProvenanceJson`', 'SELECT 1');
PREPARE drop_client_map_provenance_stmt FROM @drop_client_map_provenance_sql;
EXECUTE drop_client_map_provenance_stmt;
DEALLOCATE PREPARE drop_client_map_provenance_stmt;

SET @drop_npc_identity_source_type_sql := IF(@has_npc_identity_source_type > 0, 'ALTER TABLE `npc_client_identities` DROP COLUMN `SourceType`', 'SELECT 1');
PREPARE drop_npc_identity_source_type_stmt FROM @drop_npc_identity_source_type_sql;
EXECUTE drop_npc_identity_source_type_stmt;
DEALLOCATE PREPARE drop_npc_identity_source_type_stmt;

SET @drop_npc_identity_source_identity_sql := IF(@has_npc_identity_source_identity > 0, 'ALTER TABLE `npc_client_identities` DROP COLUMN `SourceIdentity`', 'SELECT 1');
PREPARE drop_npc_identity_source_identity_stmt FROM @drop_npc_identity_source_identity_sql;
EXECUTE drop_npc_identity_source_identity_stmt;
DEALLOCATE PREPARE drop_npc_identity_source_identity_stmt;

SET @drop_npc_identity_source_hash_sql := IF(@has_npc_identity_source_hash > 0, 'ALTER TABLE `npc_client_identities` DROP COLUMN `SourceHash`', 'SELECT 1');
PREPARE drop_npc_identity_source_hash_stmt FROM @drop_npc_identity_source_hash_sql;
EXECUTE drop_npc_identity_source_hash_stmt;
DEALLOCATE PREPARE drop_npc_identity_source_hash_stmt;

SET @drop_npc_identity_provenance_sql := IF(@has_npc_identity_provenance > 0, 'ALTER TABLE `npc_client_identities` DROP COLUMN `ProvenanceJson`', 'SELECT 1');
PREPARE drop_npc_identity_provenance_stmt FROM @drop_npc_identity_provenance_sql;
EXECUTE drop_npc_identity_provenance_stmt;
DEALLOCATE PREPARE drop_npc_identity_provenance_stmt;

SET @drop_npc_spawn_source_type_sql := IF(@has_npc_spawn_source_type > 0, 'ALTER TABLE `npc_spawns` DROP COLUMN `SourceType`', 'SELECT 1');
PREPARE drop_npc_spawn_source_type_stmt FROM @drop_npc_spawn_source_type_sql;
EXECUTE drop_npc_spawn_source_type_stmt;
DEALLOCATE PREPARE drop_npc_spawn_source_type_stmt;

SET @drop_npc_spawn_source_identity_sql := IF(@has_npc_spawn_source_identity > 0, 'ALTER TABLE `npc_spawns` DROP COLUMN `SourceIdentity`', 'SELECT 1');
PREPARE drop_npc_spawn_source_identity_stmt FROM @drop_npc_spawn_source_identity_sql;
EXECUTE drop_npc_spawn_source_identity_stmt;
DEALLOCATE PREPARE drop_npc_spawn_source_identity_stmt;

SET @drop_npc_spawn_source_hash_sql := IF(@has_npc_spawn_source_hash > 0, 'ALTER TABLE `npc_spawns` DROP COLUMN `SourceHash`', 'SELECT 1');
PREPARE drop_npc_spawn_source_hash_stmt FROM @drop_npc_spawn_source_hash_sql;
EXECUTE drop_npc_spawn_source_hash_stmt;
DEALLOCATE PREPARE drop_npc_spawn_source_hash_stmt;

SET @drop_npc_spawn_provenance_sql := IF(@has_npc_spawn_provenance > 0, 'ALTER TABLE `npc_spawns` DROP COLUMN `ProvenanceJson`', 'SELECT 1');
PREPARE drop_npc_spawn_provenance_stmt FROM @drop_npc_spawn_provenance_sql;
EXECUTE drop_npc_spawn_provenance_stmt;
DEALLOCATE PREPARE drop_npc_spawn_provenance_stmt;

SET @has_client_map_runtime_gate_after_drop := (
    SELECT COUNT(*) FROM `information_schema`.`CHECK_CONSTRAINTS`
    WHERE `CONSTRAINT_SCHEMA`=DATABASE() AND `TABLE_NAME`='client_map_identities' AND `CONSTRAINT_NAME`='CK_ClientMapIdentities_RuntimeGate'
);
SET @add_client_map_runtime_gate_sql := IF(
    @has_client_map_runtime_gate_after_drop = 0,
    'ALTER TABLE `client_map_identities` ADD CONSTRAINT `CK_ClientMapIdentities_RuntimeGate` CHECK (`ProductionEnabled` = 0 OR (`CoordinateScaleX` > 0 AND `CoordinateScaleY` > 0 AND CHAR_LENGTH(`ResourceIdentity`) > 0))',
    'SELECT 1'
);
PREPARE add_client_map_runtime_gate_stmt FROM @add_client_map_runtime_gate_sql;
EXECUTE add_client_map_runtime_gate_stmt;
DEALLOCATE PREPARE add_client_map_runtime_gate_stmt;

CREATE INDEX IF NOT EXISTS `IX_NpcSpawns_ObservedEntity`
    ON `npc_spawns` (`ClientBuildId`,`ObservedClientEntityHandle`);
