ALTER TABLE `god2_research`.`client_map_resource_evidence`
    ADD COLUMN IF NOT EXISTS `client_executable_sha256` char(64) NULL AFTER `resource_key`,
    ADD COLUMN IF NOT EXISTS `can_record_index` int NULL AFTER `client_executable_sha256`,
    ADD COLUMN IF NOT EXISTS `can_record_type` tinyint unsigned NULL AFTER `can_record_index`,
    ADD COLUMN IF NOT EXISTS `can_relative_path` varchar(512) NULL AFTER `can_record_type`,
    ADD COLUMN IF NOT EXISTS `can_sha256` char(64) NULL AFTER `can_relative_path`,
    ADD COLUMN IF NOT EXISTS `mbd_relative_path` varchar(512) NULL AFTER `can_sha256`,
    ADD COLUMN IF NOT EXISTS `mbd_sha256` char(64) NULL AFTER `mbd_relative_path`,
    ADD COLUMN IF NOT EXISTS `resource_file_relative_path` varchar(512) NULL AFTER `mbd_sha256`,
    ADD COLUMN IF NOT EXISTS `resource_file_sha256` char(64) NULL AFTER `resource_file_relative_path`,
    ADD COLUMN IF NOT EXISTS `navigation_relative_path` varchar(512) NULL AFTER `resource_file_sha256`,
    ADD COLUMN IF NOT EXISTS `navigation_sha256` char(64) NULL AFTER `navigation_relative_path`;

SET @has_client_map_resource_file_evidence := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='client_map_resources'
      AND `COLUMN_NAME`='can_sha256'
);

SET @archive_client_map_resource_file_evidence_sql := IF(
    @has_client_map_resource_file_evidence > 0,
    'UPDATE `god2_research`.`client_map_resource_evidence` evidence_row
     JOIN `god2_game`.`client_map_resources` resource_row ON resource_row.`resource_key`=evidence_row.`resource_key`
     SET evidence_row.`client_executable_sha256`=resource_row.`client_executable_sha256`,
         evidence_row.`can_record_index`=resource_row.`can_record_index`,
         evidence_row.`can_record_type`=resource_row.`can_record_type`,
         evidence_row.`can_relative_path`=resource_row.`can_relative_path`,
         evidence_row.`can_sha256`=resource_row.`can_sha256`,
         evidence_row.`mbd_relative_path`=resource_row.`mbd_relative_path`,
         evidence_row.`mbd_sha256`=resource_row.`mbd_sha256`,
         evidence_row.`resource_file_relative_path`=resource_row.`resource_file_relative_path`,
         evidence_row.`resource_file_sha256`=resource_row.`resource_file_sha256`,
         evidence_row.`navigation_relative_path`=resource_row.`navigation_relative_path`,
         evidence_row.`navigation_sha256`=resource_row.`navigation_sha256`,
         evidence_row.`moved_at_utc`=UTC_TIMESTAMP(6)',
    'SELECT 1'
);
PREPARE archive_client_map_resource_file_evidence_stmt FROM @archive_client_map_resource_file_evidence_sql;
EXECUTE archive_client_map_resource_file_evidence_stmt;
DEALLOCATE PREPARE archive_client_map_resource_file_evidence_stmt;

ALTER TABLE `god2_game`.`client_map_resources`
    DROP INDEX IF EXISTS `ux_client_map_resources_can_record`;

ALTER TABLE `god2_game`.`client_map_resources`
    DROP COLUMN IF EXISTS `client_executable_sha256`,
    DROP COLUMN IF EXISTS `can_record_index`,
    DROP COLUMN IF EXISTS `can_record_type`,
    DROP COLUMN IF EXISTS `can_relative_path`,
    DROP COLUMN IF EXISTS `can_sha256`,
    DROP COLUMN IF EXISTS `mbd_relative_path`,
    DROP COLUMN IF EXISTS `mbd_sha256`,
    DROP COLUMN IF EXISTS `resource_file_relative_path`,
    DROP COLUMN IF EXISTS `resource_file_sha256`,
    DROP COLUMN IF EXISTS `navigation_relative_path`,
    DROP COLUMN IF EXISTS `navigation_sha256`;
