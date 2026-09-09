-- Withdraw the generic item-family label that was previously copied into the
-- equipment-slot column for every exact-client EQU row.
--
-- The exact current client contains 731 EQU identities distributed across
-- eleven source subtype codes. The readable rows explicitly include armor,
-- robes, headscarves, crowns, helmets, gloves, shoes, hair/accessory and
-- special/quest variants. Therefore the existing value "Armor" on all 731
-- rows is a family classification, not a verified equip-slot identity.
-- Preserve the catalog rows and stat metadata, record the withdrawal, and
-- return only this unsupported field to NULL so future equip work fails closed.

CREATE TABLE IF NOT EXISTS `god2_game`.`equipment_slot_evidence` (
    `item_id` bigint NOT NULL,
    `client_item_id` int NOT NULL,
    `equipment_subtype_code` int NOT NULL,
    `withdrawn_slot_claim` varchar(50) NOT NULL,
    `slot_evidence_status` varchar(64) NOT NULL,
    `exact_client_source_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `source_record_index` int NOT NULL,
    `exact_client_source_line` int NOT NULL,
    `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0,
    `source_reference` varchar(500) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`item_id`),
    UNIQUE KEY `ux_equipment_slot_evidence_client_identity` (`client_item_id`),
    KEY `ix_equipment_slot_evidence_status` (`slot_evidence_status`,`runtime_eligible`),
    CONSTRAINT `fk_equipment_slot_evidence_registry` FOREIGN KEY (`item_id`)
        REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_equipment_slot_subtype` CHECK (`equipment_subtype_code` BETWEEN 0 AND 10),
    CONSTRAINT `ck_equipment_slot_withdrawn_claim` CHECK (`withdrawn_slot_claim`='Armor'),
    CONSTRAINT `ck_equipment_slot_status` CHECK (`slot_evidence_status`='WithdrawnGenericFamilyMisclassifiedAsSlot'),
    CONSTRAINT `ck_equipment_slot_runtime` CHECK (`runtime_eligible`=0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Field-level evidence boundary for equipment slot identity';

CREATE TEMPORARY TABLE `tmp_equipment_slot_withdrawal_gate` (
    `ok` tinyint NOT NULL,
    CONSTRAINT `ck_tmp_equipment_slot_withdrawal_gate` CHECK (`ok`=1)
);

INSERT INTO `tmp_equipment_slot_withdrawal_gate` (`ok`)
SELECT IF(
    COUNT(*)=731
    AND COUNT(DISTINCT registry_row.`item_id`)=731
    AND COUNT(DISTINCT registry_row.`client_item_id`)=731
    AND SUM(equipment_row.`equipment_type`='Armor')=731
    AND SUM(equipment_row.`equipment_slot`='Armor')=731
    AND SUM(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceSha256'))=
        '526c49b1b8302413636fbfadf5cb1baab4ad9b315a649175b2c6eccae7612da6')=731
    AND SUM(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) REGEXP '^[0-9]+$')=731
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED) BETWEEN 0 AND 10)=731
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED)=0)=78
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED)=1)=79
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED)=2)=70
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED)=3)=79
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED)=4)=70
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED)=5)=78
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED)=6)=79
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED)=7)=101
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED)=8)=70
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED)=9)=6
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED)=10)=21
    AND COUNT(requirement_row.`item_id`)=731
    AND SUM(requirement_row.`source_item_type`='EQU'
        AND requirement_row.`evidence_status`='Verified'
        AND requirement_row.`enabled`=1
        AND requirement_row.`exact_client_source_sha256`=
            'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f')=731,
    1, 0)
FROM `god2`.`items` source_row
JOIN `god2_game`.`item_registry` registry_row
  ON registry_row.`source_item_type`='EQU'
 AND registry_row.`client_item_id`=
     CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId')) AS UNSIGNED)
JOIN `god2_game`.`equipment` equipment_row ON equipment_row.`item_id`=registry_row.`item_id`
JOIN `god2_game`.`item_requirement_evidence` requirement_row ON requirement_row.`item_id`=registry_row.`item_id`
WHERE source_row.`ItemType`='EQU';

INSERT INTO `god2_game`.`equipment_slot_evidence`
    (`item_id`,`client_item_id`,`equipment_subtype_code`,`withdrawn_slot_claim`,
     `slot_evidence_status`,`exact_client_source_sha256`,`source_record_index`,
     `exact_client_source_line`,`runtime_eligible`,`source_reference`)
SELECT registry_row.`item_id`,registry_row.`client_item_id`,
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[1]')) AS UNSIGNED),
       'Armor','WithdrawnGenericFamilyMisclassifiedAsSlot',
       'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f',
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceRecordIndex')) AS UNSIGNED),
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceRecordIndex')) AS UNSIGNED)+2,
       0,
       CONCAT('Exact XJZ2 EQU subtype diversity proves Armor is a family label, not a slot; clientItemId=',
              registry_row.`client_item_id`,
              '; sourceSha256=c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f')
FROM `god2`.`items` source_row
JOIN `god2_game`.`item_registry` registry_row
  ON registry_row.`source_item_type`='EQU'
 AND registry_row.`client_item_id`=
     CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId')) AS UNSIGNED)
JOIN `tmp_equipment_slot_withdrawal_gate` gate_row ON gate_row.`ok`=1
WHERE source_row.`ItemType`='EQU'
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `equipment_subtype_code`=VALUES(`equipment_subtype_code`),
    `withdrawn_slot_claim`=VALUES(`withdrawn_slot_claim`),
    `slot_evidence_status`=VALUES(`slot_evidence_status`),
    `exact_client_source_sha256`=VALUES(`exact_client_source_sha256`),
    `source_record_index`=VALUES(`source_record_index`),
    `exact_client_source_line`=VALUES(`exact_client_source_line`),
    `runtime_eligible`=VALUES(`runtime_eligible`),
    `source_reference`=VALUES(`source_reference`);

UPDATE `god2_game`.`equipment` equipment_row
JOIN `god2_game`.`equipment_slot_evidence` evidence_row
  ON evidence_row.`item_id`=equipment_row.`item_id`
 AND evidence_row.`slot_evidence_status`='WithdrawnGenericFamilyMisclassifiedAsSlot'
 AND evidence_row.`runtime_eligible`=0
SET equipment_row.`equipment_slot`=NULL,
    equipment_row.`admin_note`=CASE
        WHEN COALESCE(equipment_row.`admin_note`,'') LIKE '%generic Armor slot withdrawn%'
            THEN equipment_row.`admin_note`
        ELSE CONCAT_WS(' | ',NULLIF(equipment_row.`admin_note`,''),
            'Exact-client EQU generic Armor slot withdrawn; exact equip-slot wire/state mapping remains evidence-blocked.')
    END,
    equipment_row.`updated_at_utc`=UTC_TIMESTAMP(6)
WHERE equipment_row.`source_item_type`='EQU'
  AND equipment_row.`equipment_type`='Armor'
  AND equipment_row.`equipment_slot`='Armor';

CREATE OR REPLACE VIEW `god2_game`.`vw_equipment_slot_evidence_readable` AS
SELECT evidence_row.`client_item_id` AS `client_item_id`,
       registry_row.`name_zh_tw` AS `equipment_name_zh_tw`,
       evidence_row.`equipment_subtype_code` AS `equipment_subtype_code`,
       equipment_row.`equipment_type` AS `equipment_family`,
       equipment_row.`equipment_slot` AS `verified_equipment_slot`,
       evidence_row.`withdrawn_slot_claim` AS `withdrawn_slot_claim`,
       evidence_row.`slot_evidence_status` AS `slot_evidence_status`,
       evidence_row.`runtime_eligible` AS `runtime_eligible`,
       evidence_row.`exact_client_source_sha256` AS `exact_client_source_sha256`
FROM `god2_game`.`equipment_slot_evidence` evidence_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=evidence_row.`item_id`
JOIN `god2_game`.`equipment` equipment_row ON equipment_row.`item_id`=evidence_row.`item_id`;

DROP TEMPORARY TABLE `tmp_equipment_slot_withdrawal_gate`;
