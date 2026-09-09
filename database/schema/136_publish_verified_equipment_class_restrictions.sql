-- Publish only class restrictions stated directly by the exact current client.
-- This is static catalog evidence. It does not enable equip/unequip, stat
-- projection, inventory mutation or any player-owned equipment row.

CREATE TABLE IF NOT EXISTS `god2_game`.`item_class_restriction_evidence` (
    `item_id` bigint NOT NULL,
    `client_item_id` int NOT NULL,
    `source_item_type` varchar(32) NOT NULL,
    `class_restriction_zh_tw` varchar(200) NOT NULL,
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
    UNIQUE KEY `ux_item_class_restriction_client_identity` (`source_item_type`,`client_item_id`),
    KEY `ix_item_class_restriction_gate` (`evidence_status`,`activation_evidence_status`,`runtime_eligible`,`enabled`),
    CONSTRAINT `fk_item_class_restriction_registry` FOREIGN KEY (`item_id`)
        REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_item_class_restriction_source_type` CHECK (`source_item_type` IN ('WPN','EQU')),
    CONSTRAINT `ck_item_class_restriction_evidence` CHECK (`evidence_status`='VerifiedOfficialClientText'),
    CONSTRAINT `ck_item_class_restriction_activation` CHECK (`activation_evidence_status`='EvidenceBlocked'),
    CONSTRAINT `ck_item_class_restriction_runtime` CHECK (`runtime_eligible`=0),
    CONSTRAINT `ck_item_class_restriction_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Exact-client WPN/EQU class restriction text; activation remains blocked';

CREATE TEMPORARY TABLE `tmp_item_class_restriction_seed` AS
SELECT registry_row.`item_id`,registry_row.`client_item_id`,registry_row.`source_item_type`,
       rule_row.`class_restriction_zh_tw`,source_row.`DescriptionZhTw` AS `condition_text_zh_tw`,
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceRecordIndex')) AS UNSIGNED) AS `source_record_index`
FROM `god2_game`.`item_usage_rules` rule_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=rule_row.`item_id`
JOIN `god2`.`items` source_row
  ON source_row.`ItemType`=registry_row.`source_item_type`
 AND CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId')) AS UNSIGNED)=registry_row.`client_item_id`
WHERE registry_row.`source_item_type` IN ('WPN','EQU')
  AND rule_row.`class_restriction_zh_tw` IS NOT NULL;

CREATE TEMPORARY TABLE `tmp_item_class_restriction_gate` (
    `ok` tinyint NOT NULL,
    CONSTRAINT `ck_tmp_item_class_restriction_gate` CHECK (`ok`=1)
);

INSERT INTO `tmp_item_class_restriction_gate` (`ok`)
SELECT IF(
    COUNT(*)=259
    AND SUM(seed.`source_item_type`='WPN')=147
    AND SUM(seed.`source_item_type`='EQU')=112
    AND COUNT(DISTINCT seed.`item_id`)=259
    AND COUNT(DISTINCT CONCAT(seed.`source_item_type`,':',seed.`client_item_id`))=259
    AND SUM(seed.`class_restriction_zh_tw` REGEXP
        '^(劍客|仙道|藥師|謀士)(、(劍客|仙道|藥師|謀士)){0,3}$')=259
    AND SUM(seed.`condition_text_zh_tw` REGEXP
        '^限([一二三四五]轉後)?(劍客|仙道|藥師|謀士)(，(劍客|仙道|藥師|謀士)){0,3}使用')=259
    AND SUM(
        (seed.`class_restriction_zh_tw` LIKE '%劍客%')=(SUBSTRING_INDEX(seed.`condition_text_zh_tw`,'使用',1) LIKE '%劍客%')
        AND (seed.`class_restriction_zh_tw` LIKE '%仙道%')=(SUBSTRING_INDEX(seed.`condition_text_zh_tw`,'使用',1) LIKE '%仙道%')
        AND (seed.`class_restriction_zh_tw` LIKE '%藥師%')=(SUBSTRING_INDEX(seed.`condition_text_zh_tw`,'使用',1) LIKE '%藥師%')
        AND (seed.`class_restriction_zh_tw` LIKE '%謀士%')=(SUBSTRING_INDEX(seed.`condition_text_zh_tw`,'使用',1) LIKE '%謀士%')
    )=259,
    1,0)
FROM `tmp_item_class_restriction_seed` seed;

INSERT INTO `god2_game`.`item_class_restriction_evidence`
    (`item_id`,`client_item_id`,`source_item_type`,`class_restriction_zh_tw`,
     `condition_text_zh_tw`,`exact_client_source_sha256`,`source_record_index`,
     `evidence_status`,`activation_evidence_status`,`runtime_eligible`,
     `source_reference`,`enabled`)
SELECT seed.`item_id`,seed.`client_item_id`,seed.`source_item_type`,
       seed.`class_restriction_zh_tw`,seed.`condition_text_zh_tw`,
       'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f',
       seed.`source_record_index`,'VerifiedOfficialClientText','EvidenceBlocked',0,
       CONCAT('Exact XJZ2 Data2/Patch/Comm/gamedata.csvZ field[7]; section=',
              seed.`source_item_type`,'; clientItemId=',seed.`client_item_id`,
              '; sourceSha256=c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f'),
       1
FROM `tmp_item_class_restriction_seed` seed
JOIN `tmp_item_class_restriction_gate` gate_row ON gate_row.`ok`=1
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `class_restriction_zh_tw`=VALUES(`class_restriction_zh_tw`),
    `condition_text_zh_tw`=VALUES(`condition_text_zh_tw`),
    `exact_client_source_sha256`=VALUES(`exact_client_source_sha256`),
    `source_record_index`=VALUES(`source_record_index`),
    `evidence_status`=VALUES(`evidence_status`),
    `activation_evidence_status`=VALUES(`activation_evidence_status`),
    `runtime_eligible`=VALUES(`runtime_eligible`),
    `source_reference`=VALUES(`source_reference`),
    `enabled`=VALUES(`enabled`);

UPDATE `god2_game`.`item_usage_rules` rule_row
JOIN `god2_game`.`item_class_restriction_evidence` evidence_row
  ON evidence_row.`item_id`=rule_row.`item_id`
 AND evidence_row.`evidence_status`='VerifiedOfficialClientText'
 AND evidence_row.`activation_evidence_status`='EvidenceBlocked'
 AND evidence_row.`runtime_eligible`=0
 AND evidence_row.`enabled`=1
SET rule_row.`class_restriction_zh_tw`=evidence_row.`class_restriction_zh_tw`,
    rule_row.`condition_text_zh_tw`=evidence_row.`condition_text_zh_tw`,
    rule_row.`text_rule_evidence_status`='Verified',
    rule_row.`source_reference_zh_tw`=evidence_row.`source_reference`,
    rule_row.`updated_at_utc`=UTC_TIMESTAMP(6);

UPDATE `god2_game`.`weapons` weapon_row
JOIN `god2_game`.`item_class_restriction_evidence` evidence_row
  ON evidence_row.`item_id`=weapon_row.`item_id`
 AND evidence_row.`source_item_type`='WPN'
 AND evidence_row.`evidence_status`='VerifiedOfficialClientText'
 AND evidence_row.`runtime_eligible`=0
 AND evidence_row.`enabled`=1
SET weapon_row.`class_restriction_zh_tw`=evidence_row.`class_restriction_zh_tw`,
    weapon_row.`updated_at_utc`=UTC_TIMESTAMP(6);

CREATE OR REPLACE VIEW `god2_game`.`vw_item_class_restrictions_readable` AS
SELECT evidence_row.`client_item_id` AS `client_item_id`,
       registry_row.`name_zh_tw` AS `item_name_zh_tw`,
       evidence_row.`source_item_type` AS `source_item_type`,
       evidence_row.`class_restriction_zh_tw` AS `class_restriction_zh_tw`,
       evidence_row.`condition_text_zh_tw` AS `condition_text_zh_tw`,
       evidence_row.`evidence_status` AS `evidence_status`,
       evidence_row.`activation_evidence_status` AS `activation_evidence_status`,
       evidence_row.`runtime_eligible` AS `runtime_eligible`,
       evidence_row.`exact_client_source_sha256` AS `exact_client_source_sha256`,
       evidence_row.`source_record_index` AS `source_record_index`,
       evidence_row.`enabled` AS `enabled`
FROM `god2_game`.`item_class_restriction_evidence` evidence_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=evidence_row.`item_id`;

DROP TEMPORARY TABLE `tmp_item_class_restriction_gate`;
DROP TEMPORARY TABLE `tmp_item_class_restriction_seed`;
