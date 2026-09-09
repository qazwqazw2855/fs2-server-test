-- Publish the four rank identities stated directly by the exact current client
-- GodLevel section. Rank thresholds, template-to-rank assignment and progression
-- are not present in this source and remain blocked/NULL.

ALTER TABLE `god2_game`.`immortal_ranks`
    ADD COLUMN IF NOT EXISTS `exact_client_source_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `required_experience`,
    ADD COLUMN IF NOT EXISTS `source_record_index` int NULL AFTER `exact_client_source_sha256`,
    ADD COLUMN IF NOT EXISTS `source_line` int NULL AFTER `source_record_index`,
    ADD COLUMN IF NOT EXISTS `evidence_status` varchar(40) NOT NULL DEFAULT 'Unknown' AFTER `source_line`,
    ADD COLUMN IF NOT EXISTS `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0 AFTER `evidence_status`,
    ADD KEY IF NOT EXISTS `ix_immortal_ranks_evidence` (`evidence_status`,`runtime_eligible`,`enabled`);

CREATE TEMPORARY TABLE `tmp_immortal_rank_seed` (
    `rank_id` bigint NOT NULL,
    `name_zh_tw` varchar(100) NOT NULL,
    `name_original` varchar(100) NOT NULL,
    `display_order` int NOT NULL,
    `source_record_index` int NOT NULL,
    `source_line` int NOT NULL,
    PRIMARY KEY (`rank_id`)
);

INSERT INTO `tmp_immortal_rank_seed`
    (`rank_id`,`name_zh_tw`,`name_original`,`display_order`,`source_record_index`,`source_line`)
VALUES
    (1,'小仙位','小仙位',1,0,22872),
    (2,'正仙位','正仙位',2,1,22873),
    (3,'強仙位','强仙位',3,2,22874),
    (4,'齊仙位','齐仙位',4,3,22875);

CREATE TEMPORARY TABLE `tmp_immortal_rank_gate` (
    `ok` tinyint NOT NULL,
    CONSTRAINT `ck_tmp_immortal_rank_gate` CHECK (`ok`=1)
);

INSERT INTO `tmp_immortal_rank_gate` (`ok`)
SELECT IF(
    (SELECT COUNT(*) FROM `tmp_immortal_rank_seed`)=4
    AND (SELECT COUNT(DISTINCT `rank_id`) FROM `tmp_immortal_rank_seed`)=4
    AND (SELECT COUNT(DISTINCT `display_order`) FROM `tmp_immortal_rank_seed`)=4
    AND NOT EXISTS (
        SELECT 1
        FROM `god2_game`.`immortal_ranks` existing_row
        JOIN `tmp_immortal_rank_seed` seed ON seed.`rank_id`=existing_row.`rank_id`
        WHERE existing_row.`name_zh_tw`<>seed.`name_zh_tw`
           OR existing_row.`name_original`<>seed.`name_original`
           OR existing_row.`display_order`<>seed.`display_order`
           OR existing_row.`required_level` IS NOT NULL
           OR existing_row.`required_experience` IS NOT NULL
    ),1,0);

INSERT INTO `god2_game`.`immortal_ranks`
    (`rank_id`,`name_zh_tw`,`name_original`,`display_order`,
     `required_level`,`required_experience`,`exact_client_source_sha256`,
     `source_record_index`,`source_line`,`evidence_status`,`runtime_eligible`,
     `enabled`,`admin_note`)
SELECT seed.`rank_id`,seed.`name_zh_tw`,seed.`name_original`,seed.`display_order`,
       NULL,NULL,
       'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f',
       seed.`source_record_index`,seed.`source_line`,
       'VerifiedOfficialClientStatic',0,1,
       'Exact current-client GodLevel rank identity only; thresholds, template assignment and rank progression remain evidence-blocked.'
FROM `tmp_immortal_rank_seed` seed
JOIN `tmp_immortal_rank_gate` gate_row ON gate_row.`ok`=1
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `name_original`=VALUES(`name_original`),
    `display_order`=VALUES(`display_order`),
    `required_level`=VALUES(`required_level`),
    `required_experience`=VALUES(`required_experience`),
    `exact_client_source_sha256`=VALUES(`exact_client_source_sha256`),
    `source_record_index`=VALUES(`source_record_index`),
    `source_line`=VALUES(`source_line`),
    `evidence_status`=VALUES(`evidence_status`),
    `runtime_eligible`=VALUES(`runtime_eligible`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

CREATE OR REPLACE VIEW `god2_game`.`vw_immortal_ranks_readable` AS
SELECT rank_row.`rank_id` AS `client_rank_id`,
       rank_row.`name_zh_tw` AS `rank_name_zh_tw`,
       rank_row.`name_original` AS `rank_name_original`,
       rank_row.`display_order` AS `display_order`,
       rank_row.`required_level` AS `required_level`,
       rank_row.`required_experience` AS `required_experience`,
       rank_row.`evidence_status` AS `evidence_status`,
       rank_row.`runtime_eligible` AS `runtime_eligible`,
       rank_row.`exact_client_source_sha256` AS `exact_client_source_sha256`,
       rank_row.`source_record_index` AS `source_record_index`,
       rank_row.`source_line` AS `source_line`,
       rank_row.`enabled` AS `enabled`
FROM `god2_game`.`immortal_ranks` rank_row;

DROP TEMPORARY TABLE `tmp_immortal_rank_gate`;
DROP TEMPORARY TABLE `tmp_immortal_rank_seed`;
