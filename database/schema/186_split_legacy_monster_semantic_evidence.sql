CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_monster_semantic_profile_evidence` (
    `run_id` char(36) NOT NULL,
    `monster_id` int NOT NULL,
    `level_evidence_status` varchar(32) NOT NULL,
    `stat_evidence_status` varchar(32) NOT NULL,
    `reward_evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`run_id`, `monster_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'monster_semantic_profiles'
          AND COLUMN_NAME = 'LevelEvidenceStatus'
    ),
    'INSERT INTO `god2_research`.`legacy_monster_semantic_profile_evidence` (`run_id`,`monster_id`,`level_evidence_status`,`stat_evidence_status`,`reward_evidence_status`) SELECT `RunId`,`MonsterId`,`LevelEvidenceStatus`,`StatEvidenceStatus`,`RewardEvidenceStatus` FROM `monster_semantic_profiles` ON DUPLICATE KEY UPDATE `level_evidence_status`=VALUES(`level_evidence_status`),`stat_evidence_status`=VALUES(`stat_evidence_status`),`reward_evidence_status`=VALUES(`reward_evidence_status`)',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `monster_semantic_profiles`
    DROP COLUMN IF EXISTS `LevelEvidenceStatus`,
    DROP COLUMN IF EXISTS `StatEvidenceStatus`,
    DROP COLUMN IF EXISTS `RewardEvidenceStatus`;
