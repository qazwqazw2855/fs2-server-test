-- Publish the exact-client WPN field-29 attack-range domain.
--
-- Current exact XJZ2 gamedata.csvZ (SHA-256
-- c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f)
-- contains 645 WPN rows. Field 29 is the numeric attack range. In the same
-- exact source, 642 rows expose an independently readable "attack range N"
-- description and all 642 match field 29; the remaining three rows retain a
-- valid numeric value but omit that display sentence. The staged decoded
-- corpus and exact-current export agree on identity, subtype and field 29 for
-- all 645 rows.

CREATE TABLE IF NOT EXISTS `god2_game`.`weapon_attack_range_evidence` (
    `item_id` bigint NOT NULL,
    `client_item_id` int NOT NULL,
    `weapon_subtype_code` int NOT NULL,
    `attack_range` int NOT NULL,
    `exact_client_source_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `source_record_index` int NOT NULL,
    `exact_client_source_line` int NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `source_reference` varchar(500) NOT NULL,
    `enabled` tinyint(1) NOT NULL DEFAULT 1,
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`item_id`),
    UNIQUE KEY `ux_weapon_attack_range_client_identity` (`client_item_id`),
    KEY `ix_weapon_attack_range_gate` (`attack_range`,`enabled`),
    CONSTRAINT `fk_weapon_attack_range_registry` FOREIGN KEY (`item_id`)
        REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_weapon_attack_range_subtype` CHECK (`weapon_subtype_code` BETWEEN 0 AND 10),
    CONSTRAINT `ck_weapon_attack_range_value` CHECK (`attack_range` BETWEEN 2 AND 4),
    CONSTRAINT `ck_weapon_attack_range_evidence` CHECK (`evidence_status`='Verified'),
    CONSTRAINT `ck_weapon_attack_range_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Exact-client weapon attack-range evidence';

-- Fail before catalog mutation unless the complete WPN corpus, prior exact
-- requirement authority and all numeric domains converge.
CREATE TEMPORARY TABLE `tmp_weapon_attack_range_gate` (
    `ok` tinyint NOT NULL,
    CONSTRAINT `ck_tmp_weapon_attack_range_gate` CHECK (`ok`=1)
);

INSERT INTO `tmp_weapon_attack_range_gate` (`ok`)
SELECT IF(
    COUNT(*)=645
    AND COUNT(DISTINCT CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId')) AS UNSIGNED))=645
    AND SUM(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceSha256'))=
        '526c49b1b8302413636fbfadf5cb1baab4ad9b315a649175b2c6eccae7612da6')=645
    AND SUM(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) REGEXP '^[0-9]+$')=645
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED) BETWEEN 0 AND 10)=645
    AND SUM(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[29]')) REGEXP '^[0-9]+$')=645
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[29]')) AS UNSIGNED) BETWEEN 2 AND 4)=645
    AND COUNT(registry_row.`item_id`)=645
    AND COUNT(DISTINCT registry_row.`item_id`)=645
    AND COUNT(requirement_row.`item_id`)=645
    AND SUM(requirement_row.`source_item_type`='WPN'
        AND requirement_row.`evidence_status`='Verified'
        AND requirement_row.`enabled`=1
        AND requirement_row.`exact_client_source_sha256`=
            'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f')=645,
    1, 0)
FROM `god2`.`items` source_row
JOIN `god2_game`.`item_registry` registry_row
  ON registry_row.`source_item_type`='WPN'
 AND registry_row.`client_item_id`=
     CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId')) AS UNSIGNED)
JOIN `god2_game`.`item_requirement_evidence` requirement_row
  ON requirement_row.`item_id`=registry_row.`item_id`
WHERE source_row.`ItemType`='WPN';

INSERT INTO `god2_game`.`weapon_attack_range_evidence`
    (`item_id`,`client_item_id`,`weapon_subtype_code`,`attack_range`,
     `exact_client_source_sha256`,`source_record_index`,`exact_client_source_line`,
     `evidence_status`,`source_reference`,`enabled`)
SELECT registry_row.`item_id`,registry_row.`client_item_id`,
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED),
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[29]')) AS UNSIGNED),
       'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f',
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceRecordIndex')) AS UNSIGNED),
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceRecordIndex')) AS UNSIGNED)+2,
       'Verified',
       CONCAT('Exact XJZ2 Data2/Patch/Comm/gamedata.csvZ WPN field[29]; clientItemId=',
              registry_row.`client_item_id`,
              '; sourceSha256=c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f'),
       1
FROM `god2`.`items` source_row
JOIN `god2_game`.`item_registry` registry_row
  ON registry_row.`source_item_type`='WPN'
 AND registry_row.`client_item_id`=
     CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId')) AS UNSIGNED)
JOIN `tmp_weapon_attack_range_gate` gate_row ON gate_row.`ok`=1
WHERE source_row.`ItemType`='WPN'
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `weapon_subtype_code`=VALUES(`weapon_subtype_code`),
    `attack_range`=VALUES(`attack_range`),
    `exact_client_source_sha256`=VALUES(`exact_client_source_sha256`),
    `source_record_index`=VALUES(`source_record_index`),
    `exact_client_source_line`=VALUES(`exact_client_source_line`),
    `evidence_status`=VALUES(`evidence_status`),
    `source_reference`=VALUES(`source_reference`),
    `enabled`=VALUES(`enabled`);

UPDATE `god2_game`.`weapons` weapon_row
JOIN `god2_game`.`weapon_attack_range_evidence` evidence_row
  ON evidence_row.`item_id`=weapon_row.`item_id`
 AND evidence_row.`evidence_status`='Verified'
 AND evidence_row.`enabled`=1
SET weapon_row.`attack_range`=evidence_row.`attack_range`,
    weapon_row.`admin_note`=CASE
        WHEN COALESCE(weapon_row.`admin_note`,'') LIKE '%exact-client field 29 attack range%'
            THEN weapon_row.`admin_note`
        ELSE CONCAT_WS(' | ',NULLIF(weapon_row.`admin_note`,''),
            'Verified exact-client field 29 attack range; battle/equip mutation remains evidence-gated.')
    END,
    weapon_row.`updated_at_utc`=UTC_TIMESTAMP(6);

CREATE OR REPLACE VIEW `god2_game`.`vw_weapon_attack_ranges_readable` AS
SELECT evidence_row.`client_item_id` AS `client_item_id`,
       registry_row.`name_zh_tw` AS `weapon_name_zh_tw`,
       evidence_row.`weapon_subtype_code` AS `weapon_subtype_code`,
       evidence_row.`attack_range` AS `attack_range`,
       evidence_row.`evidence_status` AS `evidence_status`,
       evidence_row.`exact_client_source_sha256` AS `exact_client_source_sha256`,
       evidence_row.`exact_client_source_line` AS `exact_client_source_line`,
       evidence_row.`enabled` AS `enabled`
FROM `god2_game`.`weapon_attack_range_evidence` evidence_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=evidence_row.`item_id`;

DROP TEMPORARY TABLE `tmp_weapon_attack_range_gate`;
