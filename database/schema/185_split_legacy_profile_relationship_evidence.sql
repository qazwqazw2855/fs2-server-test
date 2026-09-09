CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_item_content_profile_evidence` (
    `item_id` int NOT NULL,
    `run_id` char(36) NOT NULL,
    `maximum_stack_evidence_status` varchar(32) NOT NULL,
    `evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`item_id`,`run_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_monster_drop_relationship_evidence` (
    `relationship_id` char(64) NOT NULL,
    `chance_evidence_status` varchar(32) NOT NULL,
    `quantity_evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`relationship_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_container_item_relationship_evidence` (
    `relationship_id` char(64) NOT NULL,
    `probability_evidence_status` varchar(32) NOT NULL,
    `quantity_evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`relationship_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_equipment_set_evidence` (
    `set_id` int NOT NULL,
    `run_id` char(36) NOT NULL,
    `evidence_status` varchar(32) NOT NULL,
    `set_bonus_evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`set_id`,`run_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_equipment_set_member_evidence` (
    `set_id` int NOT NULL,
    `item_id` int NOT NULL,
    `run_id` char(36) NOT NULL,
    `evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`set_id`,`item_id`,`run_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_quest_content_profile_evidence` (
    `profile_id` char(64) NOT NULL,
    `objective_evidence_status` varchar(32) NOT NULL,
    `reward_evidence_status` varchar(32) NOT NULL,
    `evidence_status` varchar(32) NOT NULL,
    `identity_evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`profile_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_pet_content_profile_evidence` (
    `profile_id` char(64) NOT NULL,
    `evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`profile_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_pet_egg_relationship_evidence` (
    `relationship_id` char(64) NOT NULL,
    `probability_evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`relationship_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_pet_innate_evidence` (
    `innate_id` int NOT NULL,
    `run_id` char(36) NOT NULL,
    `evidence_status` varchar(32) NOT NULL,
    `effect_evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`innate_id`,`run_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_skill_content_profile_identity_evidence` (
    `profile_id` char(64) NOT NULL,
    `identity_evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`profile_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='item_content_profiles' AND COLUMN_NAME='EvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_item_content_profile_evidence` (`item_id`,`run_id`,`maximum_stack_evidence_status`,`evidence_status`) SELECT `ItemId`,`RunId`,`MaximumStackEvidenceStatus`,`EvidenceStatus` FROM `item_content_profiles` ON DUPLICATE KEY UPDATE `maximum_stack_evidence_status`=VALUES(`maximum_stack_evidence_status`),`evidence_status`=VALUES(`evidence_status`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='monster_drop_relationships' AND COLUMN_NAME='ChanceEvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_monster_drop_relationship_evidence` (`relationship_id`,`chance_evidence_status`,`quantity_evidence_status`) SELECT `RelationshipId`,`ChanceEvidenceStatus`,`QuantityEvidenceStatus` FROM `monster_drop_relationships` ON DUPLICATE KEY UPDATE `chance_evidence_status`=VALUES(`chance_evidence_status`),`quantity_evidence_status`=VALUES(`quantity_evidence_status`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='container_item_relationships' AND COLUMN_NAME='ProbabilityEvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_container_item_relationship_evidence` (`relationship_id`,`probability_evidence_status`,`quantity_evidence_status`) SELECT `RelationshipId`,`ProbabilityEvidenceStatus`,`QuantityEvidenceStatus` FROM `container_item_relationships` ON DUPLICATE KEY UPDATE `probability_evidence_status`=VALUES(`probability_evidence_status`),`quantity_evidence_status`=VALUES(`quantity_evidence_status`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='equipment_set_definitions' AND COLUMN_NAME='EvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_equipment_set_evidence` (`set_id`,`run_id`,`evidence_status`,`set_bonus_evidence_status`) SELECT `SetId`,`RunId`,`EvidenceStatus`,`SetBonusEvidenceStatus` FROM `equipment_set_definitions` ON DUPLICATE KEY UPDATE `evidence_status`=VALUES(`evidence_status`),`set_bonus_evidence_status`=VALUES(`set_bonus_evidence_status`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='equipment_set_members' AND COLUMN_NAME='EvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_equipment_set_member_evidence` (`set_id`,`item_id`,`run_id`,`evidence_status`) SELECT `SetId`,`ItemId`,`RunId`,`EvidenceStatus` FROM `equipment_set_members` ON DUPLICATE KEY UPDATE `evidence_status`=VALUES(`evidence_status`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='quest_content_profiles' AND COLUMN_NAME='EvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_quest_content_profile_evidence` (`profile_id`,`objective_evidence_status`,`reward_evidence_status`,`evidence_status`,`identity_evidence_status`) SELECT `ProfileId`,`ObjectiveEvidenceStatus`,`RewardEvidenceStatus`,`EvidenceStatus`,`IdentityEvidenceStatus` FROM `quest_content_profiles` ON DUPLICATE KEY UPDATE `objective_evidence_status`=VALUES(`objective_evidence_status`),`reward_evidence_status`=VALUES(`reward_evidence_status`),`evidence_status`=VALUES(`evidence_status`),`identity_evidence_status`=VALUES(`identity_evidence_status`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='pet_content_profiles' AND COLUMN_NAME='EvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_pet_content_profile_evidence` (`profile_id`,`evidence_status`) SELECT `ProfileId`,`EvidenceStatus` FROM `pet_content_profiles` ON DUPLICATE KEY UPDATE `evidence_status`=VALUES(`evidence_status`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='pet_egg_relationships' AND COLUMN_NAME='ProbabilityEvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_pet_egg_relationship_evidence` (`relationship_id`,`probability_evidence_status`) SELECT `RelationshipId`,`ProbabilityEvidenceStatus` FROM `pet_egg_relationships` ON DUPLICATE KEY UPDATE `probability_evidence_status`=VALUES(`probability_evidence_status`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='pet_innate_definitions' AND COLUMN_NAME='EvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_pet_innate_evidence` (`innate_id`,`run_id`,`evidence_status`,`effect_evidence_status`) SELECT `InnateId`,`RunId`,`EvidenceStatus`,`EffectEvidenceStatus` FROM `pet_innate_definitions` ON DUPLICATE KEY UPDATE `evidence_status`=VALUES(`evidence_status`),`effect_evidence_status`=VALUES(`effect_evidence_status`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='skill_content_profiles' AND COLUMN_NAME='IdentityEvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_skill_content_profile_identity_evidence` (`profile_id`,`identity_evidence_status`) SELECT `ProfileId`,`IdentityEvidenceStatus` FROM `skill_content_profiles` ON DUPLICATE KEY UPDATE `identity_evidence_status`=VALUES(`identity_evidence_status`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

ALTER TABLE `monster_drop_relationships`
    DROP INDEX IF EXISTS `IX_MonsterDropRelationships_State`;

ALTER TABLE `item_content_profiles`
    DROP COLUMN IF EXISTS `MaximumStackEvidenceStatus`,
    DROP COLUMN IF EXISTS `EvidenceStatus`;

ALTER TABLE `monster_drop_relationships`
    DROP COLUMN IF EXISTS `ChanceEvidenceStatus`,
    DROP COLUMN IF EXISTS `QuantityEvidenceStatus`;

ALTER TABLE `container_item_relationships`
    DROP COLUMN IF EXISTS `ProbabilityEvidenceStatus`,
    DROP COLUMN IF EXISTS `QuantityEvidenceStatus`;

ALTER TABLE `equipment_set_definitions`
    DROP COLUMN IF EXISTS `EvidenceStatus`,
    DROP COLUMN IF EXISTS `SetBonusEvidenceStatus`;

ALTER TABLE `equipment_set_members`
    DROP COLUMN IF EXISTS `EvidenceStatus`;

ALTER TABLE `quest_content_profiles`
    DROP COLUMN IF EXISTS `ObjectiveEvidenceStatus`,
    DROP COLUMN IF EXISTS `RewardEvidenceStatus`,
    DROP COLUMN IF EXISTS `EvidenceStatus`,
    DROP COLUMN IF EXISTS `IdentityEvidenceStatus`;

ALTER TABLE `pet_content_profiles`
    DROP COLUMN IF EXISTS `EvidenceStatus`;

ALTER TABLE `pet_egg_relationships`
    DROP COLUMN IF EXISTS `ProbabilityEvidenceStatus`;

ALTER TABLE `pet_innate_definitions`
    DROP COLUMN IF EXISTS `EvidenceStatus`,
    DROP COLUMN IF EXISTS `EffectEvidenceStatus`;

ALTER TABLE `skill_content_profiles`
    DROP COLUMN IF EXISTS `IdentityEvidenceStatus`;
