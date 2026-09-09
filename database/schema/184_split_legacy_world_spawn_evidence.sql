CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_client_map_identity_evidence` (
    `map_id` int NOT NULL,
    `client_build_id` varchar(64) NOT NULL,
    `identity_evidence_status` varchar(32) NOT NULL,
    `coordinate_evidence_status` varchar(32) NOT NULL,
    `evidence_reference` varchar(512) NOT NULL,
    `field_evidence_json` json NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`map_id`,`client_build_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_npc_client_identity_evidence` (
    `npc_id` int NOT NULL,
    `client_build_id` varchar(64) NOT NULL,
    `identity_evidence_status` varchar(32) NOT NULL,
    `evidence_reference` varchar(512) NOT NULL,
    `field_evidence_json` json NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`npc_id`,`client_build_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_npc_spawn_evidence` (
    `spawn_id` int NOT NULL,
    `wire_evidence_status` varchar(32) NULL,
    `identity_evidence_status` varchar(32) NULL,
    `coordinate_evidence_status` varchar(32) NULL,
    `service_evidence_status` varchar(32) NULL,
    `application_message_sha256` char(64) NULL,
    `opaque_template_sha256` char(64) NULL,
    `evidence_reference` varchar(512) NULL,
    `field_evidence_json` json NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`spawn_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_monster_spawn_evidence` (
    `spawn_id` bigint NOT NULL,
    `evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`spawn_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_monster_spawn_semantic_evidence` (
    `run_id` char(36) NOT NULL,
    `monster_id` int NOT NULL,
    `coordinate_evidence_status` varchar(32) NOT NULL,
    `evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`run_id`,`monster_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='client_map_identities' AND COLUMN_NAME='IdentityEvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_client_map_identity_evidence` (`map_id`,`client_build_id`,`identity_evidence_status`,`coordinate_evidence_status`,`evidence_reference`,`field_evidence_json`) SELECT `MapId`,`ClientBuildId`,`IdentityEvidenceStatus`,`CoordinateEvidenceStatus`,`EvidenceReference`,`FieldEvidenceJson` FROM `client_map_identities` ON DUPLICATE KEY UPDATE `identity_evidence_status`=VALUES(`identity_evidence_status`),`coordinate_evidence_status`=VALUES(`coordinate_evidence_status`),`evidence_reference`=VALUES(`evidence_reference`),`field_evidence_json`=VALUES(`field_evidence_json`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='npc_client_identities' AND COLUMN_NAME='IdentityEvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_npc_client_identity_evidence` (`npc_id`,`client_build_id`,`identity_evidence_status`,`evidence_reference`,`field_evidence_json`) SELECT `NpcId`,`ClientBuildId`,`IdentityEvidenceStatus`,`EvidenceReference`,`FieldEvidenceJson` FROM `npc_client_identities` ON DUPLICATE KEY UPDATE `identity_evidence_status`=VALUES(`identity_evidence_status`),`evidence_reference`=VALUES(`evidence_reference`),`field_evidence_json`=VALUES(`field_evidence_json`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='npc_spawns' AND COLUMN_NAME='IdentityEvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_npc_spawn_evidence` (`spawn_id`,`wire_evidence_status`,`identity_evidence_status`,`coordinate_evidence_status`,`service_evidence_status`,`application_message_sha256`,`opaque_template_sha256`,`evidence_reference`,`field_evidence_json`) SELECT `Id`,`WireEvidenceStatus`,`IdentityEvidenceStatus`,`CoordinateEvidenceStatus`,`ServiceEvidenceStatus`,`ApplicationMessageSha256`,`OpaqueTemplateSha256`,`EvidenceReference`,`FieldEvidenceJson` FROM `npc_spawns` ON DUPLICATE KEY UPDATE `wire_evidence_status`=VALUES(`wire_evidence_status`),`identity_evidence_status`=VALUES(`identity_evidence_status`),`coordinate_evidence_status`=VALUES(`coordinate_evidence_status`),`service_evidence_status`=VALUES(`service_evidence_status`),`application_message_sha256`=VALUES(`application_message_sha256`),`opaque_template_sha256`=VALUES(`opaque_template_sha256`),`evidence_reference`=VALUES(`evidence_reference`),`field_evidence_json`=VALUES(`field_evidence_json`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='spawns' AND COLUMN_NAME='EvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_monster_spawn_evidence` (`spawn_id`,`evidence_status`) SELECT `Id`,`EvidenceStatus` FROM `spawns` ON DUPLICATE KEY UPDATE `evidence_status`=VALUES(`evidence_status`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

SET @god2_sql = IF(
    EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='monster_spawn_semantics' AND COLUMN_NAME='EvidenceStatus'),
    'INSERT INTO `god2_research`.`legacy_monster_spawn_semantic_evidence` (`run_id`,`monster_id`,`coordinate_evidence_status`,`evidence_status`) SELECT `RunId`,`MonsterId`,`CoordinateEvidenceStatus`,`EvidenceStatus` FROM `monster_spawn_semantics` ON DUPLICATE KEY UPDATE `coordinate_evidence_status`=VALUES(`coordinate_evidence_status`),`evidence_status`=VALUES(`evidence_status`)',
    'SELECT 1');
PREPARE god2_statement FROM @god2_sql;
EXECUTE god2_statement;
DEALLOCATE PREPARE god2_statement;

ALTER TABLE `client_map_identities`
    DROP CONSTRAINT IF EXISTS `CK_ClientMapIdentities_ProductionGate`,
    DROP CONSTRAINT IF EXISTS `CK_ClientMapIdentities_IdentityEvidence`,
    DROP CONSTRAINT IF EXISTS `CK_ClientMapIdentities_CoordinateEvidence`;

ALTER TABLE `client_map_identities`
    DROP COLUMN IF EXISTS `IdentityEvidenceStatus`,
    DROP COLUMN IF EXISTS `CoordinateEvidenceStatus`,
    DROP COLUMN IF EXISTS `EvidenceReference`,
    DROP COLUMN IF EXISTS `FieldEvidenceJson`,
    ADD CONSTRAINT `CK_ClientMapIdentities_RuntimeGate` CHECK (
        `ProductionEnabled` = 0 OR (
            `CoordinateScaleX` > 0 AND
            `CoordinateScaleY` > 0 AND
            CHAR_LENGTH(`ResourceIdentity`) > 0 AND
            CHAR_LENGTH(`SourceHash`) = 64
        )
    );

ALTER TABLE `npc_client_identities`
    DROP CONSTRAINT IF EXISTS `CK_NpcClientIdentities_State`;

ALTER TABLE `npc_client_identities`
    DROP COLUMN IF EXISTS `IdentityEvidenceStatus`,
    DROP COLUMN IF EXISTS `EvidenceReference`,
    DROP COLUMN IF EXISTS `FieldEvidenceJson`;

ALTER TABLE `npc_spawns`
    DROP CONSTRAINT IF EXISTS `CK_NpcSpawns_IdentityState`,
    DROP CONSTRAINT IF EXISTS `CK_NpcSpawns_CoordinateState`,
    DROP CONSTRAINT IF EXISTS `CK_NpcSpawns_ServiceState`,
    DROP CONSTRAINT IF EXISTS `CK_NpcSpawns_WireEvidenceStatus`;

ALTER TABLE `npc_spawns`
    DROP COLUMN IF EXISTS `WireEvidenceStatus`,
    DROP COLUMN IF EXISTS `IdentityEvidenceStatus`,
    DROP COLUMN IF EXISTS `CoordinateEvidenceStatus`,
    DROP COLUMN IF EXISTS `ServiceEvidenceStatus`,
    DROP COLUMN IF EXISTS `ApplicationMessageSha256`,
    DROP COLUMN IF EXISTS `OpaqueTemplateSha256`,
    DROP COLUMN IF EXISTS `EvidenceReference`,
    DROP COLUMN IF EXISTS `FieldEvidenceJson`;

ALTER TABLE `spawns`
    DROP COLUMN IF EXISTS `EvidenceStatus`;

ALTER TABLE `monster_spawn_semantics`
    DROP COLUMN IF EXISTS `CoordinateEvidenceStatus`,
    DROP COLUMN IF EXISTS `EvidenceStatus`;
