CREATE SCHEMA IF NOT EXISTS `god2_research`;

CREATE TABLE IF NOT EXISTS `god2_research`.`relationship_source_archive` (
    `FormalTable` varchar(128) NOT NULL,
    `RelationshipId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `SourceType` varchar(64) NOT NULL DEFAULT '',
    `SourceFile` varchar(768) NOT NULL DEFAULT '',
    `SourceIdentity` varchar(512) NOT NULL DEFAULT '',
    `SourceHash` char(64) NOT NULL DEFAULT '',
    `Confidence` varchar(32) NOT NULL DEFAULT '',
    `ArchiveReasonZhTw` varchar(255) NOT NULL DEFAULT '關係來源資料移入 research；正式 runtime 關係表只保留遊戲規則與狀態。',
    `ArchivedAtUtc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`FormalTable`, `RelationshipId`),
    KEY `IX_RelationshipSourceArchive_Run` (`RunId`),
    KEY `IX_RelationshipSourceArchive_SourceHash` (`SourceHash`)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

SET @has_monster_drop_source := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE `TABLE_SCHEMA`=DATABASE()
      AND `TABLE_NAME`='monster_drop_relationships'
      AND `COLUMN_NAME`='SourceHash'
);

SET @copy_monster_drop_source_sql := IF(
    @has_monster_drop_source > 0,
    'INSERT INTO `god2_research`.`relationship_source_archive`
        (`FormalTable`,`RelationshipId`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`Confidence`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
     SELECT ''monster_drop_relationships'',`RelationshipId`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`Confidence`,
            ''怪物掉落關係來源資料移入 research；正式掉落表只保留怪物、物品、數量、機率與啟用狀態。'',UTC_TIMESTAMP(6)
     FROM `monster_drop_relationships`
     ON DUPLICATE KEY UPDATE
        `RunId`=VALUES(`RunId`),
        `SourceType`=VALUES(`SourceType`),
        `SourceFile`=VALUES(`SourceFile`),
        `SourceIdentity`=VALUES(`SourceIdentity`),
        `SourceHash`=VALUES(`SourceHash`),
        `Confidence`=VALUES(`Confidence`),
        `ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),
        `ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)',
    'SELECT 1');
PREPARE copy_monster_drop_source_stmt FROM @copy_monster_drop_source_sql;
EXECUTE copy_monster_drop_source_stmt;
DEALLOCATE PREPARE copy_monster_drop_source_stmt;

ALTER TABLE `monster_drop_relationships`
    DROP COLUMN IF EXISTS `SourceType`,
    DROP COLUMN IF EXISTS `SourceFile`,
    DROP COLUMN IF EXISTS `SourceIdentity`,
    DROP COLUMN IF EXISTS `SourceHash`,
    DROP COLUMN IF EXISTS `Confidence`;

SET @has_container_source := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE `TABLE_SCHEMA`=DATABASE()
      AND `TABLE_NAME`='container_item_relationships'
      AND `COLUMN_NAME`='SourceHash'
);

SET @copy_container_source_sql := IF(
    @has_container_source > 0,
    'INSERT INTO `god2_research`.`relationship_source_archive`
        (`FormalTable`,`RelationshipId`,`RunId`,`SourceIdentity`,`SourceHash`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
     SELECT ''container_item_relationships'',`RelationshipId`,`RunId`,`SourceIdentity`,`SourceHash`,
            ''容器/福袋/合成關係來源資料移入 research；正式關係表只保留物品、數量、機率與啟用狀態。'',UTC_TIMESTAMP(6)
     FROM `container_item_relationships`
     ON DUPLICATE KEY UPDATE
        `RunId`=VALUES(`RunId`),
        `SourceIdentity`=VALUES(`SourceIdentity`),
        `SourceHash`=VALUES(`SourceHash`),
        `ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),
        `ArchivedAtUtc`=VALUES(`ArchivedAtUtc`)',
    'SELECT 1');
PREPARE copy_container_source_stmt FROM @copy_container_source_sql;
EXECUTE copy_container_source_stmt;
DEALLOCATE PREPARE copy_container_source_stmt;

ALTER TABLE `container_item_relationships`
    DROP COLUMN IF EXISTS `SourceHash`,
    DROP COLUMN IF EXISTS `SourceIdentity`;
