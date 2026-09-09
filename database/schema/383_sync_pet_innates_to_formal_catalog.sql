CREATE TABLE IF NOT EXISTS `god2_game`.`pet_innate_definitions`
(
    `innate_id` int NOT NULL,
    `name_zh_tw` varchar(150) NOT NULL,
    `artifact_name_zh_tw` varchar(150) NULL,
    `description_zh_tw` text NULL,
    `effect_reference` varchar(150) NULL,
    `effect_value` int NULL,
    `effect_evidence_status` varchar(32) NOT NULL,
    `enabled` tinyint(1) NOT NULL DEFAULT 0,
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`innate_id`),
    KEY `ix_pet_innate_definitions_enabled` (`enabled`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_game`.`pet_innate_definitions`
    (`innate_id`,`name_zh_tw`,`artifact_name_zh_tw`,`description_zh_tw`,`effect_reference`,`effect_value`,`effect_evidence_status`,`enabled`)
SELECT
    innate_row.`InnateId`,
    LEFT(innate_row.`NameZhTw`, 150),
    LEFT(NULLIF(innate_row.`ArtifactNameZhTw`, ''), 150),
    NULLIF(innate_row.`DescriptionZhTw`, ''),
    LEFT(NULLIF(innate_row.`EffectReference`, ''), 150),
    innate_row.`EffectValue`,
    CASE
        WHEN innate_row.`EffectReference` IS NULL AND innate_row.`EffectValue` IS NULL THEN 'NotApplicable'
        WHEN innate_row.`EffectReference` IS NOT NULL AND innate_row.`EffectValue` IS NOT NULL THEN 'Verified'
        ELSE 'EvidenceBlocked'
    END,
    innate_row.`ProductionEnabled`
FROM `god2`.`pet_innate_definitions` innate_row
WHERE innate_row.`NameZhTw` IS NOT NULL
  AND innate_row.`NameZhTw` <> ''
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `artifact_name_zh_tw`=VALUES(`artifact_name_zh_tw`),
    `description_zh_tw`=VALUES(`description_zh_tw`),
    `effect_reference`=VALUES(`effect_reference`),
    `effect_value`=VALUES(`effect_value`),
    `effect_evidence_status`=VALUES(`effect_evidence_status`),
    `enabled`=VALUES(`enabled`),
    `updated_at_utc`=UTC_TIMESTAMP(6);
