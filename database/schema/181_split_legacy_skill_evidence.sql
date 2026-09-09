CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_skill_catalog_evidence` (
    `skill_id` int NOT NULL,
    `evidence_status` varchar(32) NULL,
    `skill_family_evidence_status` varchar(32) NULL,
    `target_policy_evidence_status` varchar(32) NULL,
    `mp_cost_evidence_status` varchar(32) NULL,
    `payload_json` longtext NULL,
    `payload_sha256` char(64) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`skill_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_skill_content_profile_evidence` (
    `profile_id` char(64) NOT NULL,
    `skill_family_evidence_status` varchar(32) NOT NULL,
    `target_policy_evidence_status` varchar(32) NOT NULL,
    `mp_cost_evidence_status` varchar(32) NOT NULL,
    `evidence_status` varchar(32) NOT NULL,
    `source_type` varchar(64) NOT NULL,
    `source_file` varchar(768) NOT NULL,
    `source_identity` varchar(512) NOT NULL,
    `source_hash` char(64) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`profile_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_skill_semantic_profile_evidence` (
    `run_id` char(36) NOT NULL,
    `skill_id` int NOT NULL,
    `identity_evidence_status` varchar(32) NOT NULL,
    `family_evidence_status` varchar(32) NOT NULL,
    `target_evidence_status` varchar(32) NOT NULL,
    `mp_evidence_status` varchar(32) NOT NULL,
    `effect_evidence_status` varchar(32) NOT NULL,
    `semantic_evidence_status` varchar(32) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`run_id`,`skill_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`legacy_skill_catalog_evidence`
    (`skill_id`,`evidence_status`,`skill_family_evidence_status`,`target_policy_evidence_status`,
     `mp_cost_evidence_status`,`payload_json`,`payload_sha256`)
SELECT `Id`,`EvidenceStatus`,`SkillFamilyEvidenceStatus`,`TargetPolicyEvidenceStatus`,
       `MpCostEvidenceStatus`,`PayloadJson`,`PayloadSha256`
FROM `skills`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `skill_family_evidence_status`=VALUES(`skill_family_evidence_status`),
    `target_policy_evidence_status`=VALUES(`target_policy_evidence_status`),
    `mp_cost_evidence_status`=VALUES(`mp_cost_evidence_status`),
    `payload_json`=VALUES(`payload_json`),
    `payload_sha256`=VALUES(`payload_sha256`);

INSERT INTO `god2_research`.`legacy_skill_content_profile_evidence`
    (`profile_id`,`skill_family_evidence_status`,`target_policy_evidence_status`,`mp_cost_evidence_status`,
     `evidence_status`,`source_type`,`source_file`,`source_identity`,`source_hash`)
SELECT `ProfileId`,`SkillFamilyEvidenceStatus`,`TargetPolicyEvidenceStatus`,`MpCostEvidenceStatus`,
       `EvidenceStatus`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`
FROM `skill_content_profiles`
ON DUPLICATE KEY UPDATE
    `skill_family_evidence_status`=VALUES(`skill_family_evidence_status`),
    `target_policy_evidence_status`=VALUES(`target_policy_evidence_status`),
    `mp_cost_evidence_status`=VALUES(`mp_cost_evidence_status`),
    `evidence_status`=VALUES(`evidence_status`),
    `source_type`=VALUES(`source_type`),
    `source_file`=VALUES(`source_file`),
    `source_identity`=VALUES(`source_identity`),
    `source_hash`=VALUES(`source_hash`);

INSERT INTO `god2_research`.`legacy_skill_semantic_profile_evidence`
    (`run_id`,`skill_id`,`identity_evidence_status`,`family_evidence_status`,`target_evidence_status`,
     `mp_evidence_status`,`effect_evidence_status`,`semantic_evidence_status`)
SELECT `RunId`,`SkillId`,`IdentityEvidenceStatus`,`FamilyEvidenceStatus`,`TargetEvidenceStatus`,
       `MpEvidenceStatus`,`EffectEvidenceStatus`,`SemanticEvidenceStatus`
FROM `skill_semantic_profiles`
ON DUPLICATE KEY UPDATE
    `identity_evidence_status`=VALUES(`identity_evidence_status`),
    `family_evidence_status`=VALUES(`family_evidence_status`),
    `target_evidence_status`=VALUES(`target_evidence_status`),
    `mp_evidence_status`=VALUES(`mp_evidence_status`),
    `effect_evidence_status`=VALUES(`effect_evidence_status`),
    `semantic_evidence_status`=VALUES(`semantic_evidence_status`);

ALTER TABLE `skill_content_profiles`
    DROP INDEX IF EXISTS `IX_SkillContentProfiles_Family`;

ALTER TABLE `skill_semantic_profiles`
    DROP INDEX IF EXISTS `IX_SkillSemanticProfiles_Identity`;

ALTER TABLE `skill_semantic_profiles`
    DROP INDEX IF EXISTS `IX_SkillSemanticProfiles_Production`;

ALTER TABLE `skills`
    DROP COLUMN IF EXISTS `EvidenceStatus`,
    DROP COLUMN IF EXISTS `SkillFamilyEvidenceStatus`,
    DROP COLUMN IF EXISTS `TargetPolicyEvidenceStatus`,
    DROP COLUMN IF EXISTS `MpCostEvidenceStatus`,
    DROP COLUMN IF EXISTS `PayloadJson`,
    DROP COLUMN IF EXISTS `PayloadSha256`;

ALTER TABLE `skill_content_profiles`
    DROP COLUMN IF EXISTS `SkillFamilyEvidenceStatus`,
    DROP COLUMN IF EXISTS `TargetPolicyEvidenceStatus`,
    DROP COLUMN IF EXISTS `MpCostEvidenceStatus`,
    DROP COLUMN IF EXISTS `EvidenceStatus`,
    DROP COLUMN IF EXISTS `SourceType`,
    DROP COLUMN IF EXISTS `SourceFile`,
    DROP COLUMN IF EXISTS `SourceIdentity`,
    DROP COLUMN IF EXISTS `SourceHash`,
    ADD INDEX `IX_SkillContentProfiles_Family` (`SkillFamily`);

ALTER TABLE `skill_semantic_profiles`
    DROP COLUMN IF EXISTS `IdentityEvidenceStatus`,
    DROP COLUMN IF EXISTS `FamilyEvidenceStatus`,
    DROP COLUMN IF EXISTS `TargetEvidenceStatus`,
    DROP COLUMN IF EXISTS `MpEvidenceStatus`,
    DROP COLUMN IF EXISTS `EffectEvidenceStatus`,
    DROP COLUMN IF EXISTS `SemanticEvidenceStatus`,
    ADD INDEX `IX_SkillSemanticProfiles_Production` (`RunId`,`ProductionEnabled`);
