-- Rename game meta synchronization fields to formal, operator-readable names.
-- These fields are audit/control data for catalog synchronization, not runtime
-- gameplay values. Preserve the data and remove the old evidence/candidate names.

ALTER TABLE `god2_game_meta`.`sync_runs`
    ADD COLUMN IF NOT EXISTS `blocked_source_value_count` bigint NOT NULL DEFAULT 0 COMMENT '被阻擋來源值數' AFTER `mapped_value_count`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = 'god2_game_meta'
          AND TABLE_NAME = 'sync_runs'
          AND COLUMN_NAME = 'blocked_candidate_count'
    ),
    'UPDATE `god2_game_meta`.`sync_runs` SET `blocked_source_value_count`=`blocked_candidate_count`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `god2_game_meta`.`sync_runs`
    DROP COLUMN IF EXISTS `blocked_candidate_count`;

ALTER TABLE `god2_game_meta`.`sync_conflicts`
    ADD COLUMN IF NOT EXISTS `proposed_source_value` longtext NULL COMMENT '提議來源值' AFTER `existing_value`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = 'god2_game_meta'
          AND TABLE_NAME = 'sync_conflicts'
          AND COLUMN_NAME = 'candidate_value'
    ),
    'UPDATE `god2_game_meta`.`sync_conflicts` SET `proposed_source_value`=`candidate_value` WHERE `proposed_source_value` IS NULL',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `god2_game_meta`.`sync_conflicts`
    DROP COLUMN IF EXISTS `candidate_value`;

ALTER TABLE `god2_game_meta`.`combatant_stat_observations`
    ADD COLUMN IF NOT EXISTS `observation_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT '觀測狀態' AFTER `magic_defense_observed`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = 'god2_game_meta'
          AND TABLE_NAME = 'combatant_stat_observations'
          AND COLUMN_NAME = 'evidence_level'
    ),
    'UPDATE `god2_game_meta`.`combatant_stat_observations` SET `observation_status`=`evidence_level`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `god2_game_meta`.`combatant_stat_observations`
    DROP COLUMN IF EXISTS `evidence_level`;
