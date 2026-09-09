-- Catalog sync mapping and run ledgers are builder/research metadata, not
-- owner-facing formal server metadata. Move the full sync helper group to
-- god2_research so god2_game_meta only keeps administration and release state.

CREATE TABLE IF NOT EXISTS `god2_research`.`field_mappings` (
    `mapping_id` bigint NOT NULL AUTO_INCREMENT,
    `source_schema` varchar(64) NOT NULL,
    `source_table` varchar(128) NOT NULL,
    `source_key_column` varchar(128) NOT NULL,
    `source_value_column` varchar(128) NOT NULL,
    `source_status_column` varchar(128) NULL,
    `source_gate_column` varchar(128) NULL,
    `target_schema` varchar(64) NOT NULL,
    `target_table` varchar(128) NOT NULL,
    `target_key_column` varchar(128) NOT NULL,
    `target_field` varchar(128) NOT NULL,
    `value_type` varchar(30) NOT NULL,
    `fill_null_only` tinyint(1) NOT NULL DEFAULT 1,
    `enabled` tinyint(1) NOT NULL DEFAULT 0,
    `admin_note` varchar(500) NULL,
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`mapping_id`),
    UNIQUE KEY `ux_field_mappings_target` (`source_schema`,`source_table`,`source_value_column`,`target_schema`,`target_table`,`target_field`),
    CONSTRAINT `ck_research_field_mappings_flags` CHECK (`fill_null_only` IN (0,1) AND `enabled` IN (0,1)),
    CONSTRAINT `ck_research_field_mappings_target_schema` CHECK (`target_schema` IN ('god2_game','god2_player'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Catalog Builder 欄位同步映射設定；非正式遊戲 runtime 資料';

CREATE TABLE IF NOT EXISTS `god2_research`.`sync_runs` (
    `sync_run_id` char(36) NOT NULL,
    `started_at_utc` datetime(6) NOT NULL,
    `completed_at_utc` datetime(6) NULL,
    `status` varchar(30) NOT NULL,
    `source_watermark` varchar(500) NULL,
    `mapped_value_count` bigint NOT NULL DEFAULT 0,
    `blocked_source_value_count` bigint NOT NULL DEFAULT 0,
    `conflict_count` bigint NOT NULL DEFAULT 0,
    `unmapped_count` bigint NOT NULL DEFAULT 0,
    `error_message` text NULL,
    PRIMARY KEY (`sync_run_id`),
    KEY `ix_sync_runs_started` (`started_at_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Catalog Builder 同步執行紀錄；非正式遊戲 runtime 資料';

CREATE TABLE IF NOT EXISTS `god2_research`.`field_provenance` (
    `provenance_id` bigint NOT NULL AUTO_INCREMENT,
    `entity_schema` varchar(64) NOT NULL,
    `entity_table` varchar(128) NOT NULL,
    `entity_id` varchar(512) NOT NULL,
    `field_name` varchar(128) NOT NULL,
    `source_schema` varchar(64) NOT NULL,
    `source_table` varchar(128) NOT NULL,
    `source_identity` varchar(512) NOT NULL,
    `source_status` varchar(30) NOT NULL,
    `source_hash` char(64) NOT NULL,
    `sync_run_id` char(36) NOT NULL,
    `recorded_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`provenance_id`),
    UNIQUE KEY `ux_field_provenance_current` (`entity_schema`,`entity_table`,`entity_id`,`field_name`),
    KEY `ix_field_provenance_sync_run` (`sync_run_id`),
    CONSTRAINT `fk_research_field_provenance_sync_run`
        FOREIGN KEY (`sync_run_id`) REFERENCES `god2_research`.`sync_runs` (`sync_run_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Catalog Builder 欄位來源對照；非正式遊戲 runtime 資料';

CREATE TABLE IF NOT EXISTS `god2_research`.`sync_conflicts` (
    `conflict_id` bigint NOT NULL AUTO_INCREMENT,
    `sync_run_id` char(36) NOT NULL,
    `entity_schema` varchar(64) NOT NULL,
    `entity_table` varchar(128) NOT NULL,
    `entity_id` varchar(512) NOT NULL,
    `field_name` varchar(128) NOT NULL,
    `existing_value` longtext NULL,
    `proposed_source_value` longtext NULL,
    `conflict_type` varchar(50) NOT NULL,
    `source_status` varchar(30) NOT NULL,
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    `resolved_at_utc` datetime(6) NULL,
    `resolution_note` varchar(500) NULL,
    PRIMARY KEY (`conflict_id`),
    KEY `ix_sync_conflicts_run` (`sync_run_id`),
    CONSTRAINT `fk_research_sync_conflicts_run`
        FOREIGN KEY (`sync_run_id`) REFERENCES `god2_research`.`sync_runs` (`sync_run_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Catalog Builder 同步衝突紀錄；非正式遊戲 runtime 資料';

CREATE TABLE IF NOT EXISTS `god2_research`.`unmapped_fields` (
    `unmapped_id` bigint NOT NULL AUTO_INCREMENT,
    `sync_run_id` char(36) NOT NULL,
    `source_domain` varchar(128) NOT NULL,
    `source_field` varchar(128) NOT NULL,
    `observed_count` bigint NOT NULL,
    `first_source_identity` varchar(512) NULL,
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`unmapped_id`),
    UNIQUE KEY `ux_unmapped_fields_run_field` (`sync_run_id`,`source_domain`,`source_field`),
    CONSTRAINT `fk_research_unmapped_fields_run`
        FOREIGN KEY (`sync_run_id`) REFERENCES `god2_research`.`sync_runs` (`sync_run_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Catalog Builder 尚未映射欄位摘要；非正式遊戲 runtime 資料';

SET @copy_field_mappings_sql = IF(
    EXISTS (SELECT 1 FROM `information_schema`.`TABLES` WHERE `TABLE_SCHEMA`='god2_game_meta' AND `TABLE_NAME`='field_mappings'),
    'INSERT INTO `god2_research`.`field_mappings`
        (`mapping_id`,`source_schema`,`source_table`,`source_key_column`,`source_value_column`,
         `source_status_column`,`source_gate_column`,`target_schema`,`target_table`,`target_key_column`,
         `target_field`,`value_type`,`fill_null_only`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`)
     SELECT `mapping_id`,`source_schema`,`source_table`,`source_key_column`,`source_value_column`,
            `source_status_column`,`source_gate_column`,`target_schema`,`target_table`,`target_key_column`,
            `target_field`,`value_type`,`fill_null_only`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`
     FROM `god2_game_meta`.`field_mappings`
     ON DUPLICATE KEY UPDATE
        `source_schema`=VALUES(`source_schema`),`source_table`=VALUES(`source_table`),
        `source_key_column`=VALUES(`source_key_column`),`source_value_column`=VALUES(`source_value_column`),
        `source_status_column`=VALUES(`source_status_column`),`source_gate_column`=VALUES(`source_gate_column`),
        `target_schema`=VALUES(`target_schema`),`target_table`=VALUES(`target_table`),
        `target_key_column`=VALUES(`target_key_column`),`target_field`=VALUES(`target_field`),
        `value_type`=VALUES(`value_type`),`fill_null_only`=VALUES(`fill_null_only`),
        `enabled`=VALUES(`enabled`),`admin_note`=VALUES(`admin_note`),
        `created_at_utc`=VALUES(`created_at_utc`),`updated_at_utc`=VALUES(`updated_at_utc`)',
    'DO 0'
);
PREPARE copy_field_mappings_stmt FROM @copy_field_mappings_sql;
EXECUTE copy_field_mappings_stmt;
DEALLOCATE PREPARE copy_field_mappings_stmt;

SET @copy_sync_runs_sql = IF(
    EXISTS (SELECT 1 FROM `information_schema`.`TABLES` WHERE `TABLE_SCHEMA`='god2_game_meta' AND `TABLE_NAME`='sync_runs'),
    'INSERT INTO `god2_research`.`sync_runs`
        (`sync_run_id`,`started_at_utc`,`completed_at_utc`,`status`,`source_watermark`,
         `mapped_value_count`,`blocked_source_value_count`,`conflict_count`,`unmapped_count`,`error_message`)
     SELECT `sync_run_id`,`started_at_utc`,`completed_at_utc`,`status`,`source_watermark`,
            `mapped_value_count`,`blocked_source_value_count`,`conflict_count`,`unmapped_count`,`error_message`
     FROM `god2_game_meta`.`sync_runs`
     ON DUPLICATE KEY UPDATE
        `started_at_utc`=VALUES(`started_at_utc`),`completed_at_utc`=VALUES(`completed_at_utc`),
        `status`=VALUES(`status`),`source_watermark`=VALUES(`source_watermark`),
        `mapped_value_count`=VALUES(`mapped_value_count`),
        `blocked_source_value_count`=VALUES(`blocked_source_value_count`),
        `conflict_count`=VALUES(`conflict_count`),`unmapped_count`=VALUES(`unmapped_count`),
        `error_message`=VALUES(`error_message`)',
    'DO 0'
);
PREPARE copy_sync_runs_stmt FROM @copy_sync_runs_sql;
EXECUTE copy_sync_runs_stmt;
DEALLOCATE PREPARE copy_sync_runs_stmt;

SET @copy_field_provenance_sql = IF(
    EXISTS (SELECT 1 FROM `information_schema`.`TABLES` WHERE `TABLE_SCHEMA`='god2_game_meta' AND `TABLE_NAME`='field_provenance'),
    'INSERT INTO `god2_research`.`field_provenance`
        (`provenance_id`,`entity_schema`,`entity_table`,`entity_id`,`field_name`,`source_schema`,
         `source_table`,`source_identity`,`source_status`,`source_hash`,`sync_run_id`,`recorded_at_utc`)
     SELECT `provenance_id`,`entity_schema`,`entity_table`,`entity_id`,`field_name`,`source_schema`,
            `source_table`,`source_identity`,`source_status`,`source_hash`,`sync_run_id`,`recorded_at_utc`
     FROM `god2_game_meta`.`field_provenance`
     ON DUPLICATE KEY UPDATE
        `source_schema`=VALUES(`source_schema`),`source_table`=VALUES(`source_table`),
        `source_identity`=VALUES(`source_identity`),`source_status`=VALUES(`source_status`),
        `source_hash`=VALUES(`source_hash`),`sync_run_id`=VALUES(`sync_run_id`),
        `recorded_at_utc`=VALUES(`recorded_at_utc`)',
    'DO 0'
);
PREPARE copy_field_provenance_stmt FROM @copy_field_provenance_sql;
EXECUTE copy_field_provenance_stmt;
DEALLOCATE PREPARE copy_field_provenance_stmt;

SET @copy_sync_conflicts_sql = IF(
    EXISTS (SELECT 1 FROM `information_schema`.`TABLES` WHERE `TABLE_SCHEMA`='god2_game_meta' AND `TABLE_NAME`='sync_conflicts'),
    'INSERT INTO `god2_research`.`sync_conflicts`
        (`conflict_id`,`sync_run_id`,`entity_schema`,`entity_table`,`entity_id`,`field_name`,
         `existing_value`,`proposed_source_value`,`conflict_type`,`source_status`,
         `created_at_utc`,`resolved_at_utc`,`resolution_note`)
     SELECT `conflict_id`,`sync_run_id`,`entity_schema`,`entity_table`,`entity_id`,`field_name`,
            `existing_value`,`proposed_source_value`,`conflict_type`,`source_status`,
            `created_at_utc`,`resolved_at_utc`,`resolution_note`
     FROM `god2_game_meta`.`sync_conflicts`
     ON DUPLICATE KEY UPDATE
        `existing_value`=VALUES(`existing_value`),
        `proposed_source_value`=VALUES(`proposed_source_value`),
        `conflict_type`=VALUES(`conflict_type`),`source_status`=VALUES(`source_status`),
        `created_at_utc`=VALUES(`created_at_utc`),`resolved_at_utc`=VALUES(`resolved_at_utc`),
        `resolution_note`=VALUES(`resolution_note`)',
    'DO 0'
);
PREPARE copy_sync_conflicts_stmt FROM @copy_sync_conflicts_sql;
EXECUTE copy_sync_conflicts_stmt;
DEALLOCATE PREPARE copy_sync_conflicts_stmt;

SET @copy_unmapped_fields_sql = IF(
    EXISTS (SELECT 1 FROM `information_schema`.`TABLES` WHERE `TABLE_SCHEMA`='god2_game_meta' AND `TABLE_NAME`='unmapped_fields'),
    'INSERT INTO `god2_research`.`unmapped_fields`
        (`unmapped_id`,`sync_run_id`,`source_domain`,`source_field`,`observed_count`,`first_source_identity`,`created_at_utc`)
     SELECT `unmapped_id`,`sync_run_id`,`source_domain`,`source_field`,`observed_count`,`first_source_identity`,`created_at_utc`
     FROM `god2_game_meta`.`unmapped_fields`
     ON DUPLICATE KEY UPDATE
        `observed_count`=VALUES(`observed_count`),
        `first_source_identity`=VALUES(`first_source_identity`),
        `created_at_utc`=VALUES(`created_at_utc`)',
    'DO 0'
);
PREPARE copy_unmapped_fields_stmt FROM @copy_unmapped_fields_sql;
EXECUTE copy_unmapped_fields_stmt;
DEALLOCATE PREPARE copy_unmapped_fields_stmt;

DROP TABLE IF EXISTS `god2_game_meta`.`field_provenance`;
DROP TABLE IF EXISTS `god2_game_meta`.`sync_conflicts`;
DROP TABLE IF EXISTS `god2_game_meta`.`unmapped_fields`;
DROP TABLE IF EXISTS `god2_game_meta`.`sync_runs`;
DROP TABLE IF EXISTS `god2_game_meta`.`field_mappings`;
