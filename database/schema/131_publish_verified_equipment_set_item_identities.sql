-- Publish exact-client WPN/EQU field-30 equipment-set identities.
--
-- The exact XJZ2 gamedata.csvZ (SHA-256
-- c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f)
-- contains 31 WPN/EQU rows whose field 30 is nonzero (3 WPN, 28 EQU).
-- Exact-build client code stores field 30 at item record +0x122, dispatches the
-- referenced catalog through the literal EquipSetCount, and resolves it through
-- the 0x150-byte equipment-set table (lookup RVA 0x000287E0). Every one of the
-- 31 identities independently matches an already Verified EquipSetList member.
--
-- This migration publishes only the item-to-set identity. It deliberately does
-- not enable set bonuses, infer equipment slots, or implement equip mutations.

CREATE TABLE IF NOT EXISTS `god2_game`.`equipment_set_item_evidence` (
    `item_id` bigint NOT NULL,
    `client_item_id` int NOT NULL,
    `source_item_type` varchar(32) NOT NULL,
    `set_id` bigint NOT NULL,
    `source_field_index` int NOT NULL,
    `exact_client_gamedata_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `exact_client_binary_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `runtime_snapshot_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `source_record_index` int NOT NULL,
    `relationship_evidence_status` varchar(30) NOT NULL,
    `bonus_evidence_status` varchar(30) NOT NULL,
    `source_reference` varchar(700) NOT NULL,
    `enabled` tinyint(1) NOT NULL DEFAULT 1,
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`item_id`),
    UNIQUE KEY `ux_equipment_set_item_client_identity` (`source_item_type`,`client_item_id`),
    KEY `ix_equipment_set_item_set` (`set_id`,`enabled`),
    CONSTRAINT `fk_equipment_set_item_evidence_item` FOREIGN KEY (`item_id`)
        REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `fk_equipment_set_item_evidence_set` FOREIGN KEY (`set_id`)
        REFERENCES `god2_game`.`item_sets` (`set_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_equipment_set_item_source_type` CHECK (`source_item_type` IN ('WPN','EQU')),
    CONSTRAINT `ck_equipment_set_item_field` CHECK (`source_field_index`=30),
    CONSTRAINT `ck_equipment_set_item_relationship_status` CHECK (`relationship_evidence_status`='Verified'),
    CONSTRAINT `ck_equipment_set_item_bonus_status` CHECK (`bonus_evidence_status`='EvidenceBlocked'),
    CONSTRAINT `ck_equipment_set_item_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Exact-client WPN/EQU field-30 equipment-set identity evidence';

CREATE TEMPORARY TABLE `tmp_equipment_set_item_gate` (
    `ok` tinyint NOT NULL,
    CONSTRAINT `ck_tmp_equipment_set_item_gate` CHECK (`ok`=1)
);

INSERT INTO `tmp_equipment_set_item_gate` (`ok`)
SELECT IF(
    COUNT(*)=31
    AND SUM(source_row.`ItemType`='WPN')=3
    AND SUM(source_row.`ItemType`='EQU')=28
    AND COUNT(DISTINCT source_row.`Id`)=31
    AND COUNT(DISTINCT CONCAT(source_row.`ItemType`,':',
        JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId'))))=31
    AND COUNT(DISTINCT CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[30]')) AS UNSIGNED))=11
    AND SUM(CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[30]')) AS UNSIGNED)
        IN (1,2,3,4,5,25,42,43,44,45,46))=31
    AND SUM(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceSha256'))=
        '526c49b1b8302413636fbfadf5cb1baab4ad9b315a649175b2c6eccae7612da6')=31
    AND COUNT(formal_item.`item_id`)=31
    AND COUNT(set_row.`set_id`)=31
    AND SUM(set_row.`enabled`=1)=31
    AND COUNT(member_row.`ItemId`)=31
    AND SUM(member_row.`EvidenceStatus`='Verified')=31
    AND SUM(member_row.`ProductionRelationshipEnabled`=1)=31
    AND SUM(source_row.`ItemType`<>'EQU' OR formal_equipment.`item_id` IS NOT NULL)=31
    AND SUM(source_row.`ItemType`<>'EQU' OR formal_equipment.`set_id` IS NULL OR
        formal_equipment.`set_id`=CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[30]')) AS UNSIGNED))=31,
    1, 0)
FROM `god2`.`items` source_row
JOIN `god2_game`.`item_registry` formal_item ON formal_item.`item_id`=source_row.`Id`
JOIN `god2_game`.`item_sets` set_row
  ON set_row.`set_id`=CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[30]')) AS UNSIGNED)
JOIN `god2`.`equipment_set_members` member_row
  ON member_row.`SetId`=set_row.`set_id`
 AND member_row.`ItemId`=source_row.`Id`
LEFT JOIN `god2_game`.`equipment` formal_equipment ON formal_equipment.`item_id`=source_row.`Id`
WHERE source_row.`ItemType` IN ('WPN','EQU')
  AND JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[30]')) REGEXP '^[1-9][0-9]*$';

INSERT INTO `god2_game`.`equipment_set_item_evidence`
    (`item_id`,`client_item_id`,`source_item_type`,`set_id`,`source_field_index`,
     `exact_client_gamedata_sha256`,`exact_client_binary_sha256`,`runtime_snapshot_sha256`,
     `source_record_index`,`relationship_evidence_status`,`bonus_evidence_status`,
     `source_reference`,`enabled`)
SELECT source_row.`Id`,
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId')) AS UNSIGNED),
       source_row.`ItemType`,
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[30]')) AS UNSIGNED),
       30,
       'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f',
       '6b127086e0c00014de26137b4ec482801e06e0724c5c05c64561d7f9ff32bd9b',
       '054996eb6c036114306b10f25af3b8eb120f90dd2b383121d4640a26ff9c08db',
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceRecordIndex')) AS UNSIGNED),
       'Verified','EvidenceBlocked',
       CONCAT('Exact XJZ2 gamedata field[30] -> item record +0x122 -> EquipSetCount; ',
              'lookup RVA 0x000287E0; matching Verified EquipSetList member; type=',source_row.`ItemType`,
              '; clientItemId=',JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId')),
              '; setId=',JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[30]'))),
       1
FROM `god2`.`items` source_row
JOIN `tmp_equipment_set_item_gate` gate_row ON gate_row.`ok`=1
WHERE source_row.`ItemType` IN ('WPN','EQU')
  AND JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[30]')) REGEXP '^[1-9][0-9]*$'
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `source_item_type`=VALUES(`source_item_type`),
    `set_id`=VALUES(`set_id`),
    `source_field_index`=VALUES(`source_field_index`),
    `exact_client_gamedata_sha256`=VALUES(`exact_client_gamedata_sha256`),
    `exact_client_binary_sha256`=VALUES(`exact_client_binary_sha256`),
    `runtime_snapshot_sha256`=VALUES(`runtime_snapshot_sha256`),
    `source_record_index`=VALUES(`source_record_index`),
    `relationship_evidence_status`=VALUES(`relationship_evidence_status`),
    `bonus_evidence_status`=VALUES(`bonus_evidence_status`),
    `source_reference`=VALUES(`source_reference`),
    `enabled`=VALUES(`enabled`);

UPDATE `god2_game`.`equipment` equipment_row
JOIN `god2_game`.`equipment_set_item_evidence` evidence_row
  ON evidence_row.`item_id`=equipment_row.`item_id`
 AND evidence_row.`source_item_type`='EQU'
 AND evidence_row.`relationship_evidence_status`='Verified'
 AND evidence_row.`enabled`=1
SET equipment_row.`set_id`=evidence_row.`set_id`,
    equipment_row.`admin_note`=CASE
        WHEN COALESCE(equipment_row.`admin_note`,'') LIKE '%exact-client field 30 equipment-set identity%'
            THEN equipment_row.`admin_note`
        ELSE CONCAT_WS(' | ',NULLIF(equipment_row.`admin_note`,''),
            'Verified exact-client field 30 equipment-set identity; set bonus remains evidence-blocked.')
    END,
    equipment_row.`updated_at_utc`=UTC_TIMESTAMP(6);

CREATE OR REPLACE VIEW `god2_game`.`vw_equipment_set_item_evidence_readable` AS
SELECT evidence_row.`client_item_id` AS `client_item_id`,
       formal_item.`name_zh_tw` AS `item_name_zh_tw`,
       evidence_row.`source_item_type` AS `source_item_type`,
       evidence_row.`set_id` AS `set_id`,
       set_row.`name_zh_tw` AS `set_name_zh_tw`,
       evidence_row.`relationship_evidence_status` AS `relationship_evidence_status`,
       evidence_row.`bonus_evidence_status` AS `bonus_evidence_status`,
       evidence_row.`exact_client_gamedata_sha256` AS `exact_client_gamedata_sha256`,
       evidence_row.`exact_client_binary_sha256` AS `exact_client_binary_sha256`,
       evidence_row.`source_record_index` AS `source_record_index`,
       evidence_row.`enabled` AS `enabled`
FROM `god2_game`.`equipment_set_item_evidence` evidence_row
JOIN `god2_game`.`item_registry` formal_item ON formal_item.`item_id`=evidence_row.`item_id`
JOIN `god2_game`.`item_sets` set_row ON set_row.`set_id`=evidence_row.`set_id`;

DROP TEMPORARY TABLE `tmp_equipment_set_item_gate`;
