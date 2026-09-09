-- Publish only gender restrictions stated directly by the exact current client.
-- This is field-level static catalog evidence. It deliberately does not change
-- the row-wide item_usage_rules evidence gate or enable equip/item activation.

CREATE TABLE IF NOT EXISTS `god2_game`.`item_gender_restriction_evidence` (
    `item_id` bigint NOT NULL,
    `client_item_id` int NOT NULL,
    `source_item_type` varchar(32) NOT NULL,
    `gender_restriction_zh_tw` varchar(20) NOT NULL,
    `condition_text_zh_tw` text NOT NULL,
    `exact_client_source_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `source_record_index` int NOT NULL,
    `evidence_status` varchar(40) NOT NULL,
    `activation_evidence_status` varchar(40) NOT NULL,
    `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0,
    `source_reference` varchar(500) NOT NULL,
    `enabled` tinyint(1) NOT NULL DEFAULT 1,
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`item_id`),
    UNIQUE KEY `ux_item_gender_restriction_client_identity` (`source_item_type`,`client_item_id`),
    KEY `ix_item_gender_restriction_gate` (`evidence_status`,`activation_evidence_status`,`runtime_eligible`,`enabled`),
    CONSTRAINT `fk_item_gender_restriction_registry` FOREIGN KEY (`item_id`)
        REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_item_gender_restriction_source_type` CHECK (`source_item_type` IN ('WPN','EQU')),
    CONSTRAINT `ck_item_gender_restriction_value` CHECK (`gender_restriction_zh_tw` IN ('男性','女性')),
    CONSTRAINT `ck_item_gender_restriction_evidence` CHECK (`evidence_status`='VerifiedOfficialClientText'),
    CONSTRAINT `ck_item_gender_restriction_activation` CHECK (`activation_evidence_status`='EvidenceBlocked'),
    CONSTRAINT `ck_item_gender_restriction_runtime` CHECK (`runtime_eligible`=0),
    CONSTRAINT `ck_item_gender_restriction_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Exact-client WPN/EQU gender restriction text; activation remains blocked';

CREATE TEMPORARY TABLE `tmp_item_gender_restriction_seed` AS
SELECT registry_row.`item_id`,registry_row.`client_item_id`,registry_row.`source_item_type`,
       rule_row.`gender_restriction_zh_tw`,source_row.`DescriptionZhTw` AS `condition_text_zh_tw`,
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceRecordIndex')) AS UNSIGNED) AS `source_record_index`
FROM `god2_game`.`item_usage_rules` rule_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=rule_row.`item_id`
JOIN `god2`.`items` source_row
  ON source_row.`ItemType`=registry_row.`source_item_type`
 AND CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId')) AS UNSIGNED)=registry_row.`client_item_id`
WHERE registry_row.`source_item_type` IN ('WPN','EQU')
  AND rule_row.`gender_restriction_zh_tw` IS NOT NULL;

CREATE TEMPORARY TABLE `tmp_item_gender_restriction_gate` (
    `ok` tinyint NOT NULL,
    CONSTRAINT `ck_tmp_item_gender_restriction_gate` CHECK (`ok`=1)
);

INSERT INTO `tmp_item_gender_restriction_gate` (`ok`)
SELECT IF(
    COUNT(*)=44
    AND SUM(seed.`source_item_type`='WPN')=10
    AND SUM(seed.`source_item_type`='EQU')=34
    AND SUM(seed.`gender_restriction_zh_tw`='男性')=22
    AND SUM(seed.`gender_restriction_zh_tw`='女性')=22
    AND COUNT(DISTINCT seed.`item_id`)=44
    AND COUNT(DISTINCT CONCAT(seed.`source_item_type`,':',seed.`client_item_id`))=44
    AND SUM(seed.`gender_restriction_zh_tw` IN ('男性','女性'))=44
    AND SUM(seed.`condition_text_zh_tw` LIKE CONCAT('%',seed.`gender_restriction_zh_tw`,'%'))=44
    AND SUM(seed.`condition_text_zh_tw` LIKE '%裝備%')=44,
    1,0)
FROM `tmp_item_gender_restriction_seed` seed;

INSERT INTO `god2_game`.`item_gender_restriction_evidence`
    (`item_id`,`client_item_id`,`source_item_type`,`gender_restriction_zh_tw`,
     `condition_text_zh_tw`,`exact_client_source_sha256`,`source_record_index`,
     `evidence_status`,`activation_evidence_status`,`runtime_eligible`,
     `source_reference`,`enabled`)
SELECT seed.`item_id`,seed.`client_item_id`,seed.`source_item_type`,
       seed.`gender_restriction_zh_tw`,seed.`condition_text_zh_tw`,
       'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f',
       seed.`source_record_index`,'VerifiedOfficialClientText','EvidenceBlocked',0,
       CONCAT('Exact XJZ2 Data2/Patch/Comm/gamedata.csvZ field[7]; section=',
              seed.`source_item_type`,'; clientItemId=',seed.`client_item_id`,
              '; sourceSha256=c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f'),
       1
FROM `tmp_item_gender_restriction_seed` seed
JOIN `tmp_item_gender_restriction_gate` gate_row ON gate_row.`ok`=1
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `gender_restriction_zh_tw`=VALUES(`gender_restriction_zh_tw`),
    `condition_text_zh_tw`=VALUES(`condition_text_zh_tw`),
    `exact_client_source_sha256`=VALUES(`exact_client_source_sha256`),
    `source_record_index`=VALUES(`source_record_index`),
    `evidence_status`=VALUES(`evidence_status`),
    `activation_evidence_status`=VALUES(`activation_evidence_status`),
    `runtime_eligible`=VALUES(`runtime_eligible`),
    `source_reference`=VALUES(`source_reference`),
    `enabled`=VALUES(`enabled`);

CREATE OR REPLACE VIEW `god2_game`.`vw_item_gender_restrictions_readable` AS
SELECT evidence_row.`client_item_id` AS `client_item_id`,
       registry_row.`name_zh_tw` AS `item_name_zh_tw`,
       evidence_row.`source_item_type` AS `source_item_type`,
       evidence_row.`gender_restriction_zh_tw` AS `gender_restriction_zh_tw`,
       evidence_row.`condition_text_zh_tw` AS `condition_text_zh_tw`,
       evidence_row.`evidence_status` AS `evidence_status`,
       evidence_row.`activation_evidence_status` AS `activation_evidence_status`,
       evidence_row.`runtime_eligible` AS `runtime_eligible`,
       evidence_row.`exact_client_source_sha256` AS `exact_client_source_sha256`,
       evidence_row.`source_record_index` AS `source_record_index`,
       evidence_row.`enabled` AS `enabled`
FROM `god2_game`.`item_gender_restriction_evidence` evidence_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=evidence_row.`item_id`;

DROP TEMPORARY TABLE `tmp_item_gender_restriction_gate`;
DROP TEMPORARY TABLE `tmp_item_gender_restriction_seed`;
