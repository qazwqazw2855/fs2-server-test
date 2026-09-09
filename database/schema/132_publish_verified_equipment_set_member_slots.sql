-- Publish exact EquipSetList slot identities for the 32 ordinary EQU members.
--
-- Exact current-client Data2/Patch/Comm/EquipSetList.csvZ (SHA-256
-- 5696e3f6492d7f9b4c9205ad775fb30b14d612941a64706ca0bced324411c858)
-- exposes fixed member columns whose meanings are independently locked by the
-- Phase2 parser/tests: Armor, Helmet, Gloves, Shoes and Weapon. The current
-- official corpus contains 32 unique EQU member rows with exactly one slot
-- each: Armor 10, Helmet 10, Gloves 6, Shoes 6. No item has conflicting slots.
--
-- This migration restores only those 32 exact slots after migration 130
-- withdrew the false row-wide generic Armor label. It does not extrapolate a
-- subtype mapping to the other 699 EQU rows and does not enable equip mutation.

CREATE TABLE IF NOT EXISTS `god2_game`.`equipment_slot_verified_evidence` (
    `item_id` bigint NOT NULL,
    `client_item_id` int NOT NULL,
    `source_item_type` varchar(32) NOT NULL,
    `equipment_slot` varchar(50) NOT NULL,
    `set_id` bigint NOT NULL,
    `exact_client_source_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `source_run_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `slot_evidence_status` varchar(30) NOT NULL,
    `runtime_eligible` tinyint(1) NOT NULL DEFAULT 1,
    `source_reference` varchar(700) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`item_id`),
    UNIQUE KEY `ux_equipment_slot_verified_client_identity` (`source_item_type`,`client_item_id`),
    KEY `ix_equipment_slot_verified_slot` (`equipment_slot`,`runtime_eligible`),
    KEY `ix_equipment_slot_verified_set` (`set_id`),
    CONSTRAINT `fk_equipment_slot_verified_item` FOREIGN KEY (`item_id`)
        REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `fk_equipment_slot_verified_set` FOREIGN KEY (`set_id`)
        REFERENCES `god2_game`.`item_sets` (`set_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_equipment_slot_verified_source_type` CHECK (`source_item_type`='EQU'),
    CONSTRAINT `ck_equipment_slot_verified_value` CHECK (`equipment_slot` IN ('Armor','Helmet','Gloves','Shoes')),
    CONSTRAINT `ck_equipment_slot_verified_status` CHECK (`slot_evidence_status`='Verified'),
    CONSTRAINT `ck_equipment_slot_verified_runtime` CHECK (`runtime_eligible` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Exact EquipSetList member-slot evidence for ordinary EQU items';

CREATE TEMPORARY TABLE `tmp_equipment_slot_verified_gate` (
    `ok` tinyint NOT NULL,
    CONSTRAINT `ck_tmp_equipment_slot_verified_gate` CHECK (`ok`=1)
);

INSERT INTO `tmp_equipment_slot_verified_gate` (`ok`)
SELECT IF(
    COUNT(*)=32
    AND COUNT(DISTINCT source_item.`Id`)=32
    AND COUNT(DISTINCT CONCAT(source_item.`ItemType`,':',
        JSON_UNQUOTE(JSON_EXTRACT(source_item.`PayloadJson`,'$.clientItemId'))))=32
    AND SUM(member_row.`SlotName`='Armor')=10
    AND SUM(member_row.`SlotName`='Helmet')=10
    AND SUM(member_row.`SlotName`='Gloves')=6
    AND SUM(member_row.`SlotName`='Shoes')=6
    AND SUM(member_row.`EvidenceStatus`='Verified')=32
    AND SUM(member_row.`ProductionRelationshipEnabled`=1)=32
    AND SUM(set_source.`SetRelationshipStatus`='Verified')=32
    AND SUM(set_source.`ProductionRelationshipEnabled`=1)=32
    AND SUM(set_source.`SourceHash`='5696e3f6492d7f9b4c9205ad775fb30b14d612941a64706ca0bced324411c858')=32
    AND COUNT(registry_row.`item_id`)=32
    AND SUM(registry_row.`source_item_type`='EQU')=32
    AND COUNT(equipment_row.`item_id`)=32
    AND SUM(equipment_row.`equipment_slot` IS NULL OR equipment_row.`equipment_slot`=member_row.`SlotName`)=32
    AND COUNT(field30_evidence.`item_id`)=28
    AND SUM(field30_evidence.`item_id` IS NOT NULL OR
        (member_row.`SetId`=26 AND
         CAST(JSON_UNQUOTE(JSON_EXTRACT(source_item.`PayloadJson`,'$.rawFields[30]')) AS UNSIGNED)=0))=32,
    1, 0)
FROM `god2`.`equipment_set_members` member_row
JOIN `god2`.`equipment_set_definitions` set_source
  ON set_source.`SetId`=member_row.`SetId`
JOIN `god2`.`items` source_item
  ON source_item.`Id`=member_row.`ItemId`
 AND source_item.`ItemType`='EQU'
JOIN `god2_game`.`item_registry` registry_row
  ON registry_row.`item_id`=source_item.`Id`
JOIN `god2_game`.`equipment` equipment_row
  ON equipment_row.`item_id`=source_item.`Id`
LEFT JOIN `god2_game`.`equipment_set_item_evidence` field30_evidence
  ON field30_evidence.`item_id`=source_item.`Id`
 AND field30_evidence.`set_id`=member_row.`SetId`
 AND field30_evidence.`relationship_evidence_status`='Verified'
 AND field30_evidence.`enabled`=1
WHERE member_row.`EvidenceStatus`='Verified'
  AND member_row.`ProductionRelationshipEnabled`=1;

INSERT INTO `god2_game`.`equipment_slot_verified_evidence`
    (`item_id`,`client_item_id`,`source_item_type`,`equipment_slot`,`set_id`,
     `exact_client_source_sha256`,`source_run_id`,`slot_evidence_status`,
     `runtime_eligible`,`source_reference`)
SELECT source_item.`Id`,
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_item.`PayloadJson`,'$.clientItemId')) AS UNSIGNED),
       'EQU',member_row.`SlotName`,member_row.`SetId`,
       '5696e3f6492d7f9b4c9205ad775fb30b14d612941a64706ca0bced324411c858',
       member_row.`RunId`,'Verified',1,
       CONCAT('Exact XJZ2 Data2/Patch/Comm/EquipSetList.csvZ fixed member column; ',
              'clientItemId=',JSON_UNQUOTE(JSON_EXTRACT(source_item.`PayloadJson`,'$.clientItemId')),
              '; setId=',member_row.`SetId`,'; slot=',member_row.`SlotName`,
              '; sourceSha256=5696e3f6492d7f9b4c9205ad775fb30b14d612941a64706ca0bced324411c858')
FROM `god2`.`equipment_set_members` member_row
JOIN `god2`.`equipment_set_definitions` set_source
  ON set_source.`SetId`=member_row.`SetId`
JOIN `god2`.`items` source_item
  ON source_item.`Id`=member_row.`ItemId`
 AND source_item.`ItemType`='EQU'
JOIN `tmp_equipment_slot_verified_gate` gate_row ON gate_row.`ok`=1
WHERE member_row.`EvidenceStatus`='Verified'
  AND member_row.`ProductionRelationshipEnabled`=1
  AND set_source.`SourceHash`='5696e3f6492d7f9b4c9205ad775fb30b14d612941a64706ca0bced324411c858'
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `equipment_slot`=VALUES(`equipment_slot`),
    `set_id`=VALUES(`set_id`),
    `exact_client_source_sha256`=VALUES(`exact_client_source_sha256`),
    `source_run_id`=VALUES(`source_run_id`),
    `slot_evidence_status`=VALUES(`slot_evidence_status`),
    `runtime_eligible`=VALUES(`runtime_eligible`),
    `source_reference`=VALUES(`source_reference`);

UPDATE `god2_game`.`equipment` equipment_row
JOIN `god2_game`.`equipment_slot_verified_evidence` evidence_row
  ON evidence_row.`item_id`=equipment_row.`item_id`
 AND evidence_row.`slot_evidence_status`='Verified'
 AND evidence_row.`runtime_eligible`=1
SET equipment_row.`equipment_slot`=evidence_row.`equipment_slot`,
    equipment_row.`admin_note`=CASE
        WHEN COALESCE(equipment_row.`admin_note`,'') LIKE '%exact EquipSetList member slot%'
            THEN equipment_row.`admin_note`
        ELSE CONCAT_WS(' | ',NULLIF(equipment_row.`admin_note`,''),
            'Verified exact EquipSetList member slot; equip mutation remains evidence-blocked.')
    END,
    equipment_row.`updated_at_utc`=UTC_TIMESTAMP(6);

CREATE OR REPLACE VIEW `god2_game`.`vw_equipment_slot_verified_evidence_readable` AS
SELECT evidence_row.`client_item_id` AS `client_item_id`,
       registry_row.`name_zh_tw` AS `item_name_zh_tw`,
       evidence_row.`equipment_slot` AS `equipment_slot`,
       evidence_row.`set_id` AS `set_id`,
       set_row.`name_zh_tw` AS `set_name_zh_tw`,
       evidence_row.`slot_evidence_status` AS `slot_evidence_status`,
       evidence_row.`runtime_eligible` AS `runtime_eligible`,
       evidence_row.`exact_client_source_sha256` AS `exact_client_source_sha256`
FROM `god2_game`.`equipment_slot_verified_evidence` evidence_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=evidence_row.`item_id`
JOIN `god2_game`.`item_sets` set_row ON set_row.`set_id`=evidence_row.`set_id`;

DROP TEMPORARY TABLE `tmp_equipment_slot_verified_gate`;
