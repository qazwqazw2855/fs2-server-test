-- Publish the exact-client WPN/EQU field-28 requirement domain.
--
-- Current exact XJZ2 gamedata.csvZ (SHA-256
-- c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f)
-- contains 645 WPN rows and 731 EQU rows. Field 28 is an encoded equipment
-- requirement: minimum rebirth * 1000 + minimum character level.
-- Examples converge with the adjacent official condition text:
--   1/10/20/30 => rebirth 0, level 1/10/20/30
--   1030/1060  => rebirth 1, level 30/60 ("限一转使用")
--   2030/2060  => rebirth 2, level 30/60 ("限二转使用")
-- The older cross-version client retained the same shared item identities but
-- stored only the level component in this field. It is corroboration, not the
-- authority used below; the current exact-client rows are authoritative.

CREATE TABLE IF NOT EXISTS `god2_game`.`item_requirement_evidence` (
    `item_id` bigint NOT NULL,
    `client_item_id` int NOT NULL,
    `source_item_type` varchar(32) NOT NULL,
    `encoded_requirement` int NOT NULL,
    `minimum_rebirth` int NOT NULL,
    `required_level` int NOT NULL,
    `exact_client_source_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `source_record_index` int NOT NULL,
    `exact_client_source_line` int NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `source_reference` varchar(500) NOT NULL,
    `enabled` tinyint(1) NOT NULL DEFAULT 1,
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`item_id`),
    UNIQUE KEY `ux_item_requirement_client_identity` (`source_item_type`,`client_item_id`),
    KEY `ix_item_requirement_gate` (`minimum_rebirth`,`required_level`,`enabled`),
    CONSTRAINT `fk_item_requirement_registry` FOREIGN KEY (`item_id`)
        REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_item_requirement_source_type` CHECK (`source_item_type` IN ('WPN','EQU')),
    CONSTRAINT `ck_item_requirement_encoding` CHECK (
        `encoded_requirement` = (`minimum_rebirth` * 1000) + `required_level`
        AND `minimum_rebirth` BETWEEN 0 AND 5
        AND `required_level` BETWEEN 1 AND 90),
    CONSTRAINT `ck_item_requirement_evidence` CHECK (`evidence_status`='Verified'),
    CONSTRAINT `ck_item_requirement_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Exact-client equipment rebirth and level requirement evidence';

-- Fail the migration before any catalog mutation unless the staged decoded rows
-- are the exact corpus independently compared with the current-client exports.
CREATE TEMPORARY TABLE `tmp_equipment_requirement_gate` (
    `ok` tinyint NOT NULL,
    CONSTRAINT `ck_tmp_equipment_requirement_gate` CHECK (`ok`=1)
);

INSERT INTO `tmp_equipment_requirement_gate` (`ok`)
SELECT IF(
    COUNT(*)=1376
    AND SUM(source_row.`ItemType`='WPN')=645
    AND SUM(source_row.`ItemType`='EQU')=731
    AND COUNT(DISTINCT CONCAT(source_row.`ItemType`,':',
        JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId'))))=1376
    AND SUM(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceSha256'))=
        '526c49b1b8302413636fbfadf5cb1baab4ad9b315a649175b2c6eccae7612da6')=1376
    AND SUM(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[28]')) REGEXP '^[0-9]+$')=1376
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[28]')) AS UNSIGNED) DIV 1000 BETWEEN 0 AND 5)=1376
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[28]')) AS UNSIGNED) MOD 1000 BETWEEN 1 AND 90)=1376
    AND COUNT(registry_row.`item_id`)=1376
    AND COUNT(DISTINCT registry_row.`item_id`)=1376,
    1, 0)
FROM `god2`.`items` source_row
JOIN `god2_game`.`item_registry` registry_row
  ON registry_row.`source_item_type`=source_row.`ItemType`
 AND registry_row.`client_item_id`=
     CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId')) AS UNSIGNED)
WHERE source_row.`ItemType` IN ('WPN','EQU');

INSERT INTO `god2_game`.`item_requirement_evidence`
    (`item_id`,`client_item_id`,`source_item_type`,`encoded_requirement`,
     `minimum_rebirth`,`required_level`,`exact_client_source_sha256`,
     `source_record_index`,`exact_client_source_line`,`evidence_status`,
     `source_reference`,`enabled`)
SELECT registry_row.`item_id`,registry_row.`client_item_id`,registry_row.`source_item_type`,
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[28]')) AS UNSIGNED),
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[28]')) AS UNSIGNED) DIV 1000,
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[28]')) AS UNSIGNED) MOD 1000,
       'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f',
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceRecordIndex')) AS UNSIGNED),
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceRecordIndex')) AS UNSIGNED)+2,
       'Verified',
       CONCAT('Exact XJZ2 Data2/Patch/Comm/gamedata.csvZ field[28]; section=',
              registry_row.`source_item_type`,'; clientItemId=',registry_row.`client_item_id`,
              '; sourceSha256=c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f'),
       1
FROM `god2`.`items` source_row
JOIN `god2_game`.`item_registry` registry_row
  ON registry_row.`source_item_type`=source_row.`ItemType`
 AND registry_row.`client_item_id`=
     CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId')) AS UNSIGNED)
JOIN `tmp_equipment_requirement_gate` gate_row ON gate_row.`ok`=1
WHERE source_row.`ItemType` IN ('WPN','EQU')
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `encoded_requirement`=VALUES(`encoded_requirement`),
    `minimum_rebirth`=VALUES(`minimum_rebirth`),
    `required_level`=VALUES(`required_level`),
    `exact_client_source_sha256`=VALUES(`exact_client_source_sha256`),
    `source_record_index`=VALUES(`source_record_index`),
    `exact_client_source_line`=VALUES(`exact_client_source_line`),
    `evidence_status`=VALUES(`evidence_status`),
    `source_reference`=VALUES(`source_reference`),
    `enabled`=VALUES(`enabled`);

UPDATE `god2_game`.`item_registry` registry_row
JOIN `god2_game`.`item_requirement_evidence` evidence_row
  ON evidence_row.`item_id`=registry_row.`item_id`
 AND evidence_row.`evidence_status`='Verified'
 AND evidence_row.`enabled`=1
SET registry_row.`required_level`=evidence_row.`required_level`,
    registry_row.`admin_note`=CASE
        WHEN COALESCE(registry_row.`admin_note`,'') LIKE '%exact-client field 28 requirement%'
            THEN registry_row.`admin_note`
        ELSE CONCAT_WS(' | ',NULLIF(registry_row.`admin_note`,''),
            'Verified exact-client field 28 requirement; rebirth component is recorded separately.')
    END,
    registry_row.`updated_at_utc`=UTC_TIMESTAMP(6);

UPDATE `god2_game`.`weapons` weapon_row
JOIN `god2_game`.`item_requirement_evidence` evidence_row
  ON evidence_row.`item_id`=weapon_row.`item_id`
 AND evidence_row.`source_item_type`='WPN'
 AND evidence_row.`evidence_status`='Verified'
 AND evidence_row.`enabled`=1
SET weapon_row.`required_level`=evidence_row.`required_level`,
    weapon_row.`updated_at_utc`=UTC_TIMESTAMP(6);

UPDATE `god2_game`.`equipment` equipment_row
JOIN `god2_game`.`item_requirement_evidence` evidence_row
  ON evidence_row.`item_id`=equipment_row.`item_id`
 AND evidence_row.`source_item_type`='EQU'
 AND evidence_row.`evidence_status`='Verified'
 AND evidence_row.`enabled`=1
SET equipment_row.`required_level`=evidence_row.`required_level`,
    equipment_row.`updated_at_utc`=UTC_TIMESTAMP(6);

UPDATE `god2_game`.`item_usage_rules` rule_row
JOIN `god2_game`.`item_requirement_evidence` evidence_row
  ON evidence_row.`item_id`=rule_row.`item_id`
 AND evidence_row.`evidence_status`='Verified'
 AND evidence_row.`enabled`=1
SET rule_row.`minimum_rebirth`=evidence_row.`minimum_rebirth`,
    rule_row.`source_reference_zh_tw`=CONCAT(
        'Exact-client gamedata field[28] requirement; ',evidence_row.`source_reference`),
    rule_row.`updated_at_utc`=UTC_TIMESTAMP(6);

CREATE OR REPLACE VIEW `god2_game`.`vw_item_requirements_readable` AS
SELECT evidence_row.`client_item_id` AS `client_item_id`,
       registry_row.`name_zh_tw` AS `item_name_zh_tw`,
       evidence_row.`source_item_type` AS `source_item_type`,
       evidence_row.`encoded_requirement` AS `encoded_requirement`,
       evidence_row.`minimum_rebirth` AS `minimum_rebirth`,
       evidence_row.`required_level` AS `required_level`,
       evidence_row.`evidence_status` AS `evidence_status`,
       evidence_row.`exact_client_source_sha256` AS `exact_client_source_sha256`,
       evidence_row.`exact_client_source_line` AS `exact_client_source_line`,
       evidence_row.`enabled` AS `enabled`
FROM `god2_game`.`item_requirement_evidence` evidence_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=evidence_row.`item_id`;

DROP TEMPORARY TABLE `tmp_equipment_requirement_gate`;
