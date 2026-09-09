CREATE TABLE IF NOT EXISTS `god2_research`.`semantic_profile_source_run_archive` (
    `FormalTable` varchar(128) NOT NULL,
    `RecordIdentity` varchar(128) NOT NULL,
    `RunId` char(36) NOT NULL,
    `SourceRunId` char(36) NULL,
    `ResourceKey` varchar(256) NULL,
    `ArchiveReasonZhTw` varchar(512) NOT NULL,
    `ArchivedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`FormalTable`,`RunId`,`RecordIdentity`),
    KEY `IX_semantic_profile_source_run_archive_source_run` (`SourceRunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @has_monster_profile_source_run := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE()
      AND `TABLE_NAME`='monster_semantic_profiles'
      AND `COLUMN_NAME`='SourceRunId'
);
SET @archive_monster_profile_source_run_sql := IF(
    @has_monster_profile_source_run > 0,
    'INSERT INTO `god2_research`.`semantic_profile_source_run_archive`
        (`FormalTable`,`RecordIdentity`,`RunId`,`SourceRunId`,`ResourceKey`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
     SELECT ''monster_semantic_profiles'', CAST(`MonsterId` AS char), `RunId`, `SourceRunId`, NULL,
            ''怪物 HP/MP、攻防、獎勵與掉落語意的 Phase2 來源 RunId 已移入 research；正式表只保留服務端執行需要的怪物語意欄位。'',
            UTC_TIMESTAMP(6)
     FROM `monster_semantic_profiles`
     ON DUPLICATE KEY UPDATE `SourceRunId`=VALUES(`SourceRunId`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)',
    'SELECT 1'
);
PREPARE archive_monster_profile_source_run_stmt FROM @archive_monster_profile_source_run_sql;
EXECUTE archive_monster_profile_source_run_stmt;
DEALLOCATE PREPARE archive_monster_profile_source_run_stmt;

SET @has_monster_spawn_source_run := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE()
      AND `TABLE_NAME`='monster_spawn_semantics'
      AND `COLUMN_NAME`='SourceRunId'
);
SET @archive_monster_spawn_source_run_sql := IF(
    @has_monster_spawn_source_run > 0,
    'INSERT INTO `god2_research`.`semantic_profile_source_run_archive`
        (`FormalTable`,`RecordIdentity`,`RunId`,`SourceRunId`,`ResourceKey`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
     SELECT ''monster_spawn_semantics'', CAST(`MonsterId` AS char), `RunId`, `SourceRunId`, NULL,
            ''怪物地圖出生點、座標、重生與生產啟用語意的 Phase2 來源 RunId 已移入 research；正式表只保留服務端刷新怪物需要的語意欄位。'',
            UTC_TIMESTAMP(6)
     FROM `monster_spawn_semantics`
     ON DUPLICATE KEY UPDATE `SourceRunId`=VALUES(`SourceRunId`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)',
    'SELECT 1'
);
PREPARE archive_monster_spawn_source_run_stmt FROM @archive_monster_spawn_source_run_sql;
EXECUTE archive_monster_spawn_source_run_stmt;
DEALLOCATE PREPARE archive_monster_spawn_source_run_stmt;

SET @has_skill_profile_source_run := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE()
      AND `TABLE_NAME`='skill_semantic_profiles'
      AND `COLUMN_NAME`='SourceRunId'
);
SET @has_skill_profile_resource_key := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`=DATABASE()
      AND `TABLE_NAME`='skill_semantic_profiles'
      AND `COLUMN_NAME`='ResourceKey'
);
SET @archive_skill_profile_source_run_sql := IF(
    @has_skill_profile_source_run > 0 OR @has_skill_profile_resource_key > 0,
    CONCAT(
        'INSERT INTO `god2_research`.`semantic_profile_source_run_archive`
            (`FormalTable`,`RecordIdentity`,`RunId`,`SourceRunId`,`ResourceKey`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
         SELECT ''skill_semantic_profiles'', CAST(`SkillId` AS char), `RunId`, ',
        IF(@has_skill_profile_source_run > 0, '`SourceRunId`', 'NULL'),
        ', ',
        IF(@has_skill_profile_resource_key > 0, 'NULLIF(`ResourceKey`,'''')', 'NULL'),
        ',
                ''技能 MP 消耗、目標、效果與狀態語意的 Phase2 來源 RunId/ResourceKey 已移入 research；正式表只保留服務端施放技能需要的語意欄位。'',
                UTC_TIMESTAMP(6)
         FROM `skill_semantic_profiles`
         ON DUPLICATE KEY UPDATE `SourceRunId`=VALUES(`SourceRunId`),`ResourceKey`=VALUES(`ResourceKey`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)'
    ),
    'SELECT 1'
);
PREPARE archive_skill_profile_source_run_stmt FROM @archive_skill_profile_source_run_sql;
EXECUTE archive_skill_profile_source_run_stmt;
DEALLOCATE PREPARE archive_skill_profile_source_run_stmt;

SET @has_monster_profile_source_fk := (
    SELECT COUNT(*)
    FROM `information_schema`.`REFERENTIAL_CONSTRAINTS`
    WHERE `CONSTRAINT_SCHEMA`=DATABASE()
      AND `TABLE_NAME`='monster_semantic_profiles'
      AND `CONSTRAINT_NAME`='FK_MonsterSemanticProfiles_SourceRun'
);
SET @drop_monster_profile_source_fk_sql := IF(
    @has_monster_profile_source_fk > 0,
    'ALTER TABLE `monster_semantic_profiles` DROP FOREIGN KEY `FK_MonsterSemanticProfiles_SourceRun`',
    'SELECT 1'
);
PREPARE drop_monster_profile_source_fk_stmt FROM @drop_monster_profile_source_fk_sql;
EXECUTE drop_monster_profile_source_fk_stmt;
DEALLOCATE PREPARE drop_monster_profile_source_fk_stmt;

SET @has_monster_spawn_source_fk := (
    SELECT COUNT(*)
    FROM `information_schema`.`REFERENTIAL_CONSTRAINTS`
    WHERE `CONSTRAINT_SCHEMA`=DATABASE()
      AND `TABLE_NAME`='monster_spawn_semantics'
      AND `CONSTRAINT_NAME`='FK_MonsterSpawnSemantics_SourceRun'
);
SET @drop_monster_spawn_source_fk_sql := IF(
    @has_monster_spawn_source_fk > 0,
    'ALTER TABLE `monster_spawn_semantics` DROP FOREIGN KEY `FK_MonsterSpawnSemantics_SourceRun`',
    'SELECT 1'
);
PREPARE drop_monster_spawn_source_fk_stmt FROM @drop_monster_spawn_source_fk_sql;
EXECUTE drop_monster_spawn_source_fk_stmt;
DEALLOCATE PREPARE drop_monster_spawn_source_fk_stmt;

SET @has_skill_profile_source_fk := (
    SELECT COUNT(*)
    FROM `information_schema`.`REFERENTIAL_CONSTRAINTS`
    WHERE `CONSTRAINT_SCHEMA`=DATABASE()
      AND `TABLE_NAME`='skill_semantic_profiles'
      AND `CONSTRAINT_NAME`='FK_SkillSemanticProfiles_SourceRun'
);
SET @drop_skill_profile_source_fk_sql := IF(
    @has_skill_profile_source_fk > 0,
    'ALTER TABLE `skill_semantic_profiles` DROP FOREIGN KEY `FK_SkillSemanticProfiles_SourceRun`',
    'SELECT 1'
);
PREPARE drop_skill_profile_source_fk_stmt FROM @drop_skill_profile_source_fk_sql;
EXECUTE drop_skill_profile_source_fk_stmt;
DEALLOCATE PREPARE drop_skill_profile_source_fk_stmt;

ALTER TABLE `monster_semantic_profiles`
    DROP INDEX IF EXISTS `FK_MonsterSemanticProfiles_SourceRun`,
    DROP COLUMN IF EXISTS `SourceRunId`;

ALTER TABLE `monster_spawn_semantics`
    DROP INDEX IF EXISTS `FK_MonsterSpawnSemantics_SourceRun`,
    DROP COLUMN IF EXISTS `SourceRunId`;

ALTER TABLE `skill_semantic_profiles`
    DROP INDEX IF EXISTS `FK_SkillSemanticProfiles_SourceRun`,
    DROP COLUMN IF EXISTS `SourceRunId`,
    DROP COLUMN IF EXISTS `ResourceKey`;
