CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`class_stat_growth_evidence` (
    `class_id` bigint NOT NULL,
    `stat_name` varchar(50) NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`class_id`,`stat_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`class_stat_growth_evidence`
    (`class_id`,`stat_name`,`evidence_status`,`admin_note`)
SELECT `class_id`,`stat_name`,`evidence_status`,`admin_note`
FROM `god2_game`.`class_stat_growth`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

CREATE TABLE IF NOT EXISTS `god2_research`.`immortal_rank_evidence` (
    `rank_id` bigint NOT NULL,
    `exact_client_source_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `source_record_index` int NULL,
    `source_line` int NULL,
    `evidence_status` varchar(40) NOT NULL,
    `runtime_eligible` tinyint(1) NOT NULL,
    `admin_note` varchar(500) NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`rank_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`immortal_rank_evidence`
    (`rank_id`,`exact_client_source_sha256`,`source_record_index`,`source_line`,`evidence_status`,`runtime_eligible`,`admin_note`)
SELECT `rank_id`,`exact_client_source_sha256`,`source_record_index`,`source_line`,`evidence_status`,`runtime_eligible`,`admin_note`
FROM `god2_game`.`immortal_ranks`
ON DUPLICATE KEY UPDATE
    `exact_client_source_sha256`=VALUES(`exact_client_source_sha256`),
    `source_record_index`=VALUES(`source_record_index`),
    `source_line`=VALUES(`source_line`),
    `evidence_status`=VALUES(`evidence_status`),
    `runtime_eligible`=VALUES(`runtime_eligible`),
    `admin_note`=VALUES(`admin_note`);

CREATE TABLE IF NOT EXISTS `god2_research`.`pet_growth_archetype_evidence` (
    `growth_archetype_id` smallint NOT NULL,
    `source_article_sn` int NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`growth_archetype_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`pet_growth_archetype_evidence`
    (`growth_archetype_id`,`source_article_sn`,`evidence_status`)
SELECT `growth_archetype_id`,`source_article_sn`,`evidence_status`
FROM `god2_game`.`pet_growth_archetypes`
ON DUPLICATE KEY UPDATE
    `source_article_sn`=VALUES(`source_article_sn`),
    `evidence_status`=VALUES(`evidence_status`);

DROP VIEW IF EXISTS `god2_game`.`vw_official_character_attribute_growth`;
DROP VIEW IF EXISTS `god2_game`.`vw_immortal_ranks_readable`;

ALTER TABLE `god2_game`.`class_stat_growth`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`immortal_ranks`
    DROP INDEX `ix_immortal_ranks_evidence`,
    DROP COLUMN IF EXISTS `exact_client_source_sha256`,
    DROP COLUMN IF EXISTS `source_record_index`,
    DROP COLUMN IF EXISTS `source_line`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `runtime_eligible`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`pet_growth_archetypes`
    DROP CONSTRAINT `ck_pet_growth_archetypes_source`,
    DROP COLUMN IF EXISTS `source_article_sn`,
    DROP COLUMN IF EXISTS `evidence_status`;

CREATE OR REPLACE VIEW `god2_game`.`vw_official_character_attribute_growth` AS
SELECT class_row.`class_id` AS `職業ID`,
       class_row.`name_zh_tw` AS `職業`,
       MAX(CASE WHEN growth.`stat_name`='Constitution' THEN growth.`growth_value` END) AS `每級自動體力`,
       MAX(CASE WHEN growth.`stat_name`='Strength' THEN growth.`growth_value` END) AS `每級自動腕力`,
       MAX(CASE WHEN growth.`stat_name`='Intelligence' THEN growth.`growth_value` END) AS `每級自動智力`,
       MAX(CASE WHEN growth.`stat_name`='Speed' THEN growth.`growth_value` END) AS `每級自動速度`,
       MAX(CASE WHEN growth.`stat_name`='UnspentAttributePoints' THEN growth.`growth_value` END) AS `每級自由配點`
FROM `god2_game`.`character_classes` class_row
JOIN `god2_game`.`class_stat_growth` growth ON growth.`class_id`=class_row.`class_id`
WHERE class_row.`class_id` IN (1,2,3,4)
  AND growth.`enabled`=1
GROUP BY class_row.`class_id`,class_row.`name_zh_tw`;

CREATE OR REPLACE VIEW `god2_game`.`vw_immortal_ranks_readable` AS
SELECT rank_row.`rank_id` AS `client_rank_id`,
       rank_row.`name_zh_tw` AS `rank_name_zh_tw`,
       rank_row.`name_original` AS `rank_name_original`,
       rank_row.`display_order` AS `display_order`,
       rank_row.`required_level` AS `required_level`,
       rank_row.`required_experience` AS `required_experience`,
       rank_row.`enabled` AS `enabled`
FROM `god2_game`.`immortal_ranks` rank_row;

GRANT SELECT ON `god2_game`.`vw_official_character_attribute_growth` TO `god2_runtime_role`;
GRANT SELECT ON `god2_game`.`vw_immortal_ranks_readable` TO `god2_runtime_role`;

GRANT SELECT ON `god2_research`.`class_stat_growth_evidence` TO 'god2_server'@'localhost';
GRANT SELECT ON `god2_research`.`class_stat_growth_evidence` TO 'god2_server'@'127.0.0.1';
GRANT SELECT ON `god2_research`.`class_stat_growth_evidence` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT ON `god2_research`.`class_stat_growth_evidence` TO 'god2_catalog_builder'@'127.0.0.1';

GRANT SELECT ON `god2_research`.`immortal_rank_evidence` TO 'god2_server'@'localhost';
GRANT SELECT ON `god2_research`.`immortal_rank_evidence` TO 'god2_server'@'127.0.0.1';
GRANT SELECT ON `god2_research`.`immortal_rank_evidence` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT ON `god2_research`.`immortal_rank_evidence` TO 'god2_catalog_builder'@'127.0.0.1';

GRANT SELECT ON `god2_research`.`pet_growth_archetype_evidence` TO 'god2_server'@'localhost';
GRANT SELECT ON `god2_research`.`pet_growth_archetype_evidence` TO 'god2_server'@'127.0.0.1';
GRANT SELECT ON `god2_research`.`pet_growth_archetype_evidence` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT ON `god2_research`.`pet_growth_archetype_evidence` TO 'god2_catalog_builder'@'127.0.0.1';

FLUSH PRIVILEGES;
