CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_official_runtime_payload_evidence` (
    `formal_table` varchar(64) NOT NULL,
    `record_identity` varchar(160) NOT NULL,
    `source_reference` varchar(512) NULL,
    `recovery_status` varchar(32) NULL,
    `evidence_status` varchar(32) NULL,
    `secondary_evidence_status` varchar(32) NULL,
    `raw_metadata` longtext NULL,
    `payload_json` longtext NULL,
    `payload_sha256` char(64) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`formal_table`,`record_identity`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`legacy_official_runtime_payload_evidence`
    (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`evidence_status`,`payload_json`,`payload_sha256`)
SELECT 'maps',CAST(`Id` AS char),`SourceReference`,`RecoveryStatus`,`EvidenceStatus`,`PayloadJson`,`PayloadSha256`
FROM `maps`
ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`evidence_status`=VALUES(`evidence_status`),`payload_json`=VALUES(`payload_json`),`payload_sha256`=VALUES(`payload_sha256`);

INSERT INTO `god2_research`.`legacy_official_runtime_payload_evidence`
    (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`evidence_status`,`raw_metadata`,`payload_json`,`payload_sha256`)
SELECT 'items',CAST(`Id` AS char),`SourceReference`,`RecoveryStatus`,`EvidenceStatus`,`RawMetadata`,`PayloadJson`,`PayloadSha256`
FROM `items`
ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`evidence_status`=VALUES(`evidence_status`),`raw_metadata`=VALUES(`raw_metadata`),`payload_json`=VALUES(`payload_json`),`payload_sha256`=VALUES(`payload_sha256`);

INSERT INTO `god2_research`.`legacy_official_runtime_payload_evidence`
    (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`evidence_status`,`secondary_evidence_status`,`payload_json`,`payload_sha256`)
SELECT 'npcs',CAST(`Id` AS char),`SourceReference`,`RecoveryStatus`,`EvidenceStatus`,`CoordinateEvidenceStatus`,`PayloadJson`,`PayloadSha256`
FROM `npcs`
ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`evidence_status`=VALUES(`evidence_status`),`secondary_evidence_status`=VALUES(`secondary_evidence_status`),`payload_json`=VALUES(`payload_json`),`payload_sha256`=VALUES(`payload_sha256`);

INSERT INTO `god2_research`.`legacy_official_runtime_payload_evidence`
    (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`evidence_status`,`payload_json`,`payload_sha256`)
SELECT 'monsters',CAST(`Id` AS char),`SourceReference`,`RecoveryStatus`,`EvidenceStatus`,`PayloadJson`,`PayloadSha256`
FROM `monsters`
ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`evidence_status`=VALUES(`evidence_status`),`payload_json`=VALUES(`payload_json`),`payload_sha256`=VALUES(`payload_sha256`);

INSERT INTO `god2_research`.`legacy_official_runtime_payload_evidence`
    (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`evidence_status`,`payload_json`,`payload_sha256`)
SELECT 'quests',CAST(`Id` AS char),`SourceReference`,`RecoveryStatus`,`EvidenceStatus`,`PayloadJson`,`PayloadSha256`
FROM `quests`
ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`evidence_status`=VALUES(`evidence_status`),`payload_json`=VALUES(`payload_json`),`payload_sha256`=VALUES(`payload_sha256`);

INSERT INTO `god2_research`.`legacy_official_runtime_payload_evidence`
    (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`evidence_status`,`payload_json`,`payload_sha256`)
SELECT 'merchants',CAST(`Id` AS char),`SourceReference`,`RecoveryStatus`,`EvidenceStatus`,`PayloadJson`,`PayloadSha256`
FROM `merchants`
ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`evidence_status`=VALUES(`evidence_status`),`payload_json`=VALUES(`payload_json`),`payload_sha256`=VALUES(`payload_sha256`);

INSERT INTO `god2_research`.`legacy_official_runtime_payload_evidence`
    (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`payload_json`,`payload_sha256`)
SELECT 'portals',CAST(`Id` AS char),`SourceReference`,`RecoveryStatus`,`PayloadJson`,`PayloadSha256`
FROM `portals`
ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`payload_json`=VALUES(`payload_json`),`payload_sha256`=VALUES(`payload_sha256`);

INSERT INTO `god2_research`.`legacy_official_runtime_payload_evidence`
    (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`payload_json`,`payload_sha256`)
SELECT 'drop_tables',CAST(`Id` AS char),`SourceReference`,`RecoveryStatus`,`PayloadJson`,`PayloadSha256`
FROM `drop_tables`
ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`payload_json`=VALUES(`payload_json`),`payload_sha256`=VALUES(`payload_sha256`);

INSERT INTO `god2_research`.`legacy_official_runtime_payload_evidence`
    (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`payload_json`,`payload_sha256`)
SELECT 'dialogs',CAST(`Id` AS char),`SourceReference`,`RecoveryStatus`,`PayloadJson`,`PayloadSha256`
FROM `dialogs`
ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`payload_json`=VALUES(`payload_json`),`payload_sha256`=VALUES(`payload_sha256`);

INSERT INTO `god2_research`.`legacy_official_runtime_payload_evidence`
    (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`payload_json`,`payload_sha256`)
SELECT 'localization_entries',CONCAT(`Language`,':',`TextKey`),`SourceReference`,`RecoveryStatus`,`PayloadJson`,`PayloadSha256`
FROM `localization_entries`
ON DUPLICATE KEY UPDATE `source_reference`=VALUES(`source_reference`),`recovery_status`=VALUES(`recovery_status`),`payload_json`=VALUES(`payload_json`),`payload_sha256`=VALUES(`payload_sha256`);

ALTER TABLE `maps` DROP COLUMN IF EXISTS `EvidenceStatus`, DROP COLUMN IF EXISTS `PayloadJson`, DROP COLUMN IF EXISTS `PayloadSha256`;
ALTER TABLE `items` DROP COLUMN IF EXISTS `EvidenceStatus`, DROP COLUMN IF EXISTS `RawMetadata`, DROP COLUMN IF EXISTS `PayloadJson`, DROP COLUMN IF EXISTS `PayloadSha256`;
ALTER TABLE `npcs` DROP COLUMN IF EXISTS `EvidenceStatus`, DROP COLUMN IF EXISTS `CoordinateEvidenceStatus`, DROP COLUMN IF EXISTS `PayloadJson`, DROP COLUMN IF EXISTS `PayloadSha256`;
ALTER TABLE `monsters` DROP COLUMN IF EXISTS `EvidenceStatus`, DROP COLUMN IF EXISTS `PayloadJson`, DROP COLUMN IF EXISTS `PayloadSha256`;
ALTER TABLE `quests` DROP COLUMN IF EXISTS `EvidenceStatus`, DROP COLUMN IF EXISTS `PayloadJson`, DROP COLUMN IF EXISTS `PayloadSha256`;
ALTER TABLE `merchants` DROP COLUMN IF EXISTS `EvidenceStatus`, DROP COLUMN IF EXISTS `PayloadJson`, DROP COLUMN IF EXISTS `PayloadSha256`;
ALTER TABLE `portals` DROP COLUMN IF EXISTS `PayloadJson`, DROP COLUMN IF EXISTS `PayloadSha256`;
ALTER TABLE `drop_tables` DROP COLUMN IF EXISTS `PayloadJson`, DROP COLUMN IF EXISTS `PayloadSha256`;
ALTER TABLE `dialogs` DROP COLUMN IF EXISTS `PayloadJson`, DROP COLUMN IF EXISTS `PayloadSha256`;
ALTER TABLE `localization_entries` DROP COLUMN IF EXISTS `PayloadJson`, DROP COLUMN IF EXISTS `PayloadSha256`;
