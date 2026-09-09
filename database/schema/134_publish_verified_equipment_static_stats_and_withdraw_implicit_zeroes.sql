-- Publish exact ordinary-EQU stat values only where the current-client text
-- explicitly contains a labelled +N value, and withdraw old implicit zeroes.
--
-- Exact current-client gamedata.csvZ SHA-256:
-- c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f
-- Decoded staged payload source SHA-256:
-- 526c49b1b8302413636fbfadf5cb1baab4ad9b315a649175b2c6eccae7612da6
--
-- The old Phase2 mapper used GetValueOrDefault and therefore serialized an
-- absent label as numeric zero. Absence is not official zero. For all 731 EQU
-- rows the exact source explicitly contains PhysicalDefense; 729 contain
-- MagicDefense; 117 contain Speed; none contain PhysicalAttack, MagicAttack,
-- HP or MP and none contains an explicit zero. This migration restores NULL
-- for every absent field and records 1,577 field-level static evidence rows.
-- Equip activation and stat mutation remain evidence-blocked.

CREATE TABLE IF NOT EXISTS `god2_game`.`equipment_static_stat_evidence` (
    `item_id` bigint NOT NULL,
    `client_item_id` int NOT NULL,
    `stat_type` varchar(50) NOT NULL,
    `displayed_flat_value` int NOT NULL,
    `source_effect_text` text NOT NULL,
    `exact_client_source_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `evidence_status` varchar(40) NOT NULL,
    `activation_evidence_status` varchar(40) NOT NULL,
    `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0,
    `source_reference` varchar(700) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`item_id`,`stat_type`),
    UNIQUE KEY `ux_equipment_static_stat_client_type` (`client_item_id`,`stat_type`),
    KEY `ix_equipment_static_stat_gate` (`stat_type`,`runtime_eligible`),
    CONSTRAINT `fk_equipment_static_stat_registry` FOREIGN KEY (`item_id`)
        REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_equipment_static_stat_type` CHECK
        (`stat_type` IN ('PhysicalDefense','MagicDefense','Speed')),
    CONSTRAINT `ck_equipment_static_stat_value` CHECK (`displayed_flat_value` > 0),
    CONSTRAINT `ck_equipment_static_stat_status` CHECK
        (`evidence_status`='VerifiedOfficialClientText' AND
         `activation_evidence_status`='EvidenceBlocked' AND
         `runtime_eligible`=0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Exact labelled ordinary-EQU static stats; activation remains evidence-blocked';

CREATE TEMPORARY TABLE `tmp_equipment_static_text` AS
SELECT source_row.`Id` AS `item_id`,
       CAST(JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.clientItemId')) AS UNSIGNED) AS `client_item_id`,
       JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceRecordIndex')) AS `source_record_index`,
       JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.sourceSha256')) AS `payload_source_sha256`,
       profile_row.`SourceHash` AS `profile_source_sha256`,
       profile_row.`PhysicalAttackBonus` AS `profile_physical_attack`,
       profile_row.`MagicAttackBonus` AS `profile_magic_attack`,
       profile_row.`PhysicalDefenseBonus` AS `profile_physical_defense`,
       profile_row.`MagicDefenseBonus` AS `profile_magic_defense`,
       profile_row.`HpBonus` AS `profile_hp`,profile_row.`MpBonus` AS `profile_mp`,
       profile_row.`SpeedBonus` AS `profile_speed`,
       CONCAT_WS(' ',
           JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[7]')),
           JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[8]')),
           JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[9]')),
           JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[10]')),
           JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[11]')),
           JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`,'$.rawFields[12]'))) AS `effect_text`
FROM `god2`.`items` source_row
JOIN `god2`.`item_content_profiles` profile_row
  ON profile_row.`ItemId`=source_row.`Id` AND profile_row.`SourceSection`='EQU'
WHERE source_row.`ItemType`='EQU';

CREATE TEMPORARY TABLE `tmp_equipment_explicit_stat_seed` AS
SELECT text_row.`item_id`,text_row.`client_item_id`,'PhysicalDefense' AS `stat_type`,
       CAST(REGEXP_REPLACE(REGEXP_SUBSTR(text_row.`effect_text`,'物防[+＋]-?[0-9]+'),'^物防[+＋]','') AS SIGNED) AS `stat_value`,
       text_row.`effect_text`,text_row.`source_record_index`
FROM `tmp_equipment_static_text` text_row
WHERE text_row.`effect_text` REGEXP '物防[+＋]-?[0-9]+'
UNION ALL
SELECT text_row.`item_id`,text_row.`client_item_id`,'MagicDefense',
       CAST(REGEXP_REPLACE(REGEXP_SUBSTR(text_row.`effect_text`,'魔防[+＋]-?[0-9]+'),'^魔防[+＋]','') AS SIGNED),
       text_row.`effect_text`,text_row.`source_record_index`
FROM `tmp_equipment_static_text` text_row
WHERE text_row.`effect_text` REGEXP '魔防[+＋]-?[0-9]+'
UNION ALL
SELECT text_row.`item_id`,text_row.`client_item_id`,'Speed',
       CAST(REGEXP_REPLACE(REGEXP_SUBSTR(text_row.`effect_text`,'速度[+＋]-?[0-9]+'),'^速度[+＋]','') AS SIGNED),
       text_row.`effect_text`,text_row.`source_record_index`
FROM `tmp_equipment_static_text` text_row
WHERE text_row.`effect_text` REGEXP '速度[+＋]-?[0-9]+';

-- MariaDB 11 may assign the connection default uca1400 collation to a
-- CREATE ... AS expression even though the formal god2_game schema uses
-- utf8mb4_unicode_ci.  Pin the temporary evidence key to the formal key's
-- collation before the fail-closed conflict join below.
ALTER TABLE `tmp_equipment_explicit_stat_seed`
    MODIFY `stat_type` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;

CREATE TEMPORARY TABLE `tmp_equipment_explicit_stat_pivot` AS
SELECT seed_row.`item_id`,
       MAX(CASE WHEN seed_row.`stat_type`='PhysicalDefense' THEN seed_row.`stat_value` END) AS `physical_defense`,
       MAX(CASE WHEN seed_row.`stat_type`='MagicDefense' THEN seed_row.`stat_value` END) AS `magic_defense`,
       MAX(CASE WHEN seed_row.`stat_type`='Speed' THEN seed_row.`stat_value` END) AS `speed`
FROM `tmp_equipment_explicit_stat_seed` seed_row
GROUP BY seed_row.`item_id`;

CREATE TEMPORARY TABLE `tmp_equipment_static_stat_gate` (
    `ok` tinyint NOT NULL,
    CONSTRAINT `ck_tmp_equipment_static_stat_gate` CHECK (`ok`=1)
);

INSERT INTO `tmp_equipment_static_stat_gate` (`ok`)
SELECT IF(
    (SELECT COUNT(*) FROM `tmp_equipment_static_text`)=731
    AND (SELECT COUNT(DISTINCT `item_id`) FROM `tmp_equipment_static_text`)=731
    AND (SELECT COUNT(*) FROM `tmp_equipment_explicit_stat_seed`)=1577
    AND (SELECT COUNT(*) FROM `tmp_equipment_explicit_stat_seed` WHERE `stat_type`='PhysicalDefense')=731
    AND (SELECT COUNT(*) FROM `tmp_equipment_explicit_stat_seed` WHERE `stat_type`='MagicDefense')=729
    AND (SELECT COUNT(*) FROM `tmp_equipment_explicit_stat_seed` WHERE `stat_type`='Speed')=117
    AND (SELECT COUNT(*) FROM `tmp_equipment_explicit_stat_seed` WHERE `stat_value`<=0)=0
    AND (SELECT MIN(`stat_value`) FROM `tmp_equipment_explicit_stat_seed`)=2
    AND (SELECT MAX(`stat_value`) FROM `tmp_equipment_explicit_stat_seed`)=200
    AND (SELECT COUNT(*) FROM `tmp_equipment_static_text`
         WHERE `effect_text` REGEXP '(物攻|魔攻|HP|MP)[+＋]-?[0-9]+')=0
    AND (SELECT COUNT(*) FROM `tmp_equipment_static_text`
         WHERE `effect_text` REGEXP '(物防|魔防|速度)[+＋]0([^0-9]|$)')=0
    AND SUM(text_row.`payload_source_sha256`='526c49b1b8302413636fbfadf5cb1baab4ad9b315a649175b2c6eccae7612da6')=731
    AND SUM(text_row.`profile_source_sha256`='c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f')=731
    AND COUNT(registry_row.`item_id`)=731
    AND COUNT(equipment_row.`item_id`)=731
    AND SUM(equipment_row.`source_item_type`='EQU')=731
    AND SUM(equipment_row.`physical_attack_bonus` IS NULL OR equipment_row.`physical_attack_bonus`=0)=731
    AND SUM(equipment_row.`magic_attack_bonus` IS NULL OR equipment_row.`magic_attack_bonus`=0)=731
    AND SUM(equipment_row.`hp_bonus` IS NULL OR equipment_row.`hp_bonus`=0)=731
    AND SUM(equipment_row.`mp_bonus` IS NULL OR equipment_row.`mp_bonus`=0)=731
    AND SUM(equipment_row.`physical_defense_bonus`=text_row.`profile_physical_defense`)=731
    AND SUM((text_row.`effect_text` REGEXP '魔防[+＋]-?[0-9]+' AND equipment_row.`magic_defense_bonus`=text_row.`profile_magic_defense`)
         OR (NOT text_row.`effect_text` REGEXP '魔防[+＋]-?[0-9]+' AND (equipment_row.`magic_defense_bonus` IS NULL OR equipment_row.`magic_defense_bonus`=0)))=731
    AND SUM((text_row.`effect_text` REGEXP '速度[+＋]-?[0-9]+' AND equipment_row.`speed_bonus`=text_row.`profile_speed`)
         OR (NOT text_row.`effect_text` REGEXP '速度[+＋]-?[0-9]+' AND (equipment_row.`speed_bonus` IS NULL OR equipment_row.`speed_bonus`=0)))=731
    AND (SELECT COUNT(*)
         FROM `tmp_equipment_explicit_stat_seed` seed_row
         JOIN `god2_game`.`equipment_static_stat_evidence` existing_row
           ON existing_row.`item_id`=seed_row.`item_id` AND existing_row.`stat_type`=seed_row.`stat_type`
         WHERE existing_row.`displayed_flat_value`<>seed_row.`stat_value`
            OR existing_row.`runtime_eligible`<>0)=0,
    1,0)
FROM `tmp_equipment_static_text` text_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=text_row.`item_id`
JOIN `god2_game`.`equipment` equipment_row ON equipment_row.`item_id`=text_row.`item_id`;

INSERT INTO `god2_game`.`equipment_static_stat_evidence`
    (`item_id`,`client_item_id`,`stat_type`,`displayed_flat_value`,`source_effect_text`,
     `exact_client_source_sha256`,`evidence_status`,`activation_evidence_status`,
     `runtime_eligible`,`source_reference`)
SELECT seed_row.`item_id`,seed_row.`client_item_id`,seed_row.`stat_type`,seed_row.`stat_value`,
       seed_row.`effect_text`,
       'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f',
       'VerifiedOfficialClientText','EvidenceBlocked',0,
       CONCAT('Exact current-client gamedata.csvZ labelled +N text; section=EQU; clientItemId=',
              seed_row.`client_item_id`,'; sourceRecordIndex=',seed_row.`source_record_index`,
              '; stat=',seed_row.`stat_type`,'; displayedFlatValue=',seed_row.`stat_value`,
              '; equip activation/stat mutation not verified')
FROM `tmp_equipment_explicit_stat_seed` seed_row
JOIN `tmp_equipment_static_stat_gate` gate_row ON gate_row.`ok`=1
ON DUPLICATE KEY UPDATE
    `client_item_id`=VALUES(`client_item_id`),
    `displayed_flat_value`=VALUES(`displayed_flat_value`),
    `source_effect_text`=VALUES(`source_effect_text`),
    `exact_client_source_sha256`=VALUES(`exact_client_source_sha256`),
    `evidence_status`=VALUES(`evidence_status`),
    `activation_evidence_status`=VALUES(`activation_evidence_status`),
    `runtime_eligible`=0,
    `source_reference`=VALUES(`source_reference`);

UPDATE `god2_game`.`equipment` equipment_row
JOIN `tmp_equipment_static_text` text_row ON text_row.`item_id`=equipment_row.`item_id`
LEFT JOIN `tmp_equipment_explicit_stat_pivot` pivot_row ON pivot_row.`item_id`=equipment_row.`item_id`
JOIN `tmp_equipment_static_stat_gate` gate_row ON gate_row.`ok`=1
SET equipment_row.`physical_attack_bonus`=NULL,
    equipment_row.`magic_attack_bonus`=NULL,
    equipment_row.`hp_bonus`=NULL,
    equipment_row.`mp_bonus`=NULL,
    equipment_row.`physical_defense_bonus`=pivot_row.`physical_defense`,
    equipment_row.`magic_defense_bonus`=pivot_row.`magic_defense`,
    equipment_row.`speed_bonus`=pivot_row.`speed`,
    equipment_row.`admin_note`=CONCAT_WS(' | ',NULLIF(equipment_row.`admin_note`,''),
        'Exact labelled static EQU stats are field-Verified; absent labels are NULL; equip activation remains evidence-blocked.'),
    equipment_row.`updated_at_utc`=UTC_TIMESTAMP(6);

UPDATE `god2`.`item_content_profiles` profile_row
JOIN `tmp_equipment_static_text` text_row ON text_row.`item_id`=profile_row.`ItemId`
LEFT JOIN `tmp_equipment_explicit_stat_pivot` pivot_row ON pivot_row.`item_id`=profile_row.`ItemId`
JOIN `tmp_equipment_static_stat_gate` gate_row ON gate_row.`ok`=1
SET profile_row.`PhysicalAttackBonus`=NULL,
    profile_row.`MagicAttackBonus`=NULL,
    profile_row.`HpBonus`=NULL,
    profile_row.`MpBonus`=NULL,
    profile_row.`PhysicalDefenseBonus`=pivot_row.`physical_defense`,
    profile_row.`MagicDefenseBonus`=pivot_row.`magic_defense`,
    profile_row.`SpeedBonus`=pivot_row.`speed`,
    profile_row.`UpdatedAtUtc`=UTC_TIMESTAMP(6)
WHERE profile_row.`SourceSection`='EQU';

CREATE OR REPLACE VIEW `god2_game`.`vw_equipment_static_stat_evidence_readable` AS
SELECT evidence_row.`client_item_id` AS `client_item_id`,registry_row.`name_zh_tw` AS `item_name_zh_tw`,
       evidence_row.`stat_type` AS `stat_type`,evidence_row.`displayed_flat_value` AS `displayed_flat_value`,
       evidence_row.`source_effect_text` AS `source_effect_text`,evidence_row.`evidence_status` AS `evidence_status`,
       evidence_row.`activation_evidence_status` AS `activation_evidence_status`,
       evidence_row.`runtime_eligible` AS `runtime_eligible`
FROM `god2_game`.`equipment_static_stat_evidence` evidence_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=evidence_row.`item_id`;

DROP TEMPORARY TABLE `tmp_equipment_static_stat_gate`;
DROP TEMPORARY TABLE `tmp_equipment_explicit_stat_pivot`;
DROP TEMPORARY TABLE `tmp_equipment_explicit_stat_seed`;
DROP TEMPORARY TABLE `tmp_equipment_static_text`;
