-- Exact labelled WPN values only. Static text is not equip/combat authority.
CREATE TABLE IF NOT EXISTS `god2_game`.`weapon_static_stat_evidence` (
 `item_id` bigint NOT NULL, `client_item_id` int NOT NULL,
 `stat_type` varchar(50) NOT NULL, `displayed_flat_value` int NOT NULL,
 `source_effect_text` text NOT NULL,
 `exact_client_source_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 `evidence_status` varchar(40) NOT NULL, `activation_evidence_status` varchar(40) NOT NULL,
 `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0, `source_reference` varchar(700) NOT NULL,
 `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
 PRIMARY KEY (`item_id`,`stat_type`), UNIQUE KEY `ux_weapon_static_stat_client_type` (`client_item_id`,`stat_type`),
 CONSTRAINT `fk_weapon_static_stat_registry` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`) ON DELETE CASCADE,
 CONSTRAINT `ck_weapon_static_stat_type` CHECK (`stat_type` IN ('PhysicalAttack','MagicAttack','Speed')),
 CONSTRAINT `ck_weapon_static_stat_value` CHECK (`displayed_flat_value`>0),
 CONSTRAINT `ck_weapon_static_stat_status` CHECK (`evidence_status`='VerifiedOfficialClientText' AND `activation_evidence_status`='EvidenceBlocked' AND `runtime_eligible`=0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TEMPORARY TABLE `tmp_weapon_static_text` AS
SELECT s.`Id` `item_id`,CAST(JSON_UNQUOTE(JSON_EXTRACT(s.`PayloadJson`,'$.clientItemId')) AS UNSIGNED) `client_item_id`,
 JSON_UNQUOTE(JSON_EXTRACT(s.`PayloadJson`,'$.sourceRecordIndex')) `source_record_index`,
 JSON_UNQUOTE(JSON_EXTRACT(s.`PayloadJson`,'$.sourceSha256')) `payload_source_sha256`,p.`SourceHash` `profile_source_sha256`,
 p.`PhysicalAttackBonus` `profile_physical_attack`,p.`MagicAttackBonus` `profile_magic_attack`,
 p.`PhysicalDefenseBonus` `profile_physical_defense`,p.`MagicDefenseBonus` `profile_magic_defense`,
 p.`HpBonus` `profile_hp`,p.`MpBonus` `profile_mp`,p.`SpeedBonus` `profile_speed`,
 CONCAT_WS(' ',JSON_UNQUOTE(JSON_EXTRACT(s.`PayloadJson`,'$.rawFields[7]')),JSON_UNQUOTE(JSON_EXTRACT(s.`PayloadJson`,'$.rawFields[8]')),
 JSON_UNQUOTE(JSON_EXTRACT(s.`PayloadJson`,'$.rawFields[9]')),JSON_UNQUOTE(JSON_EXTRACT(s.`PayloadJson`,'$.rawFields[10]')),
 JSON_UNQUOTE(JSON_EXTRACT(s.`PayloadJson`,'$.rawFields[11]')),JSON_UNQUOTE(JSON_EXTRACT(s.`PayloadJson`,'$.rawFields[12]'))) `effect_text`
FROM `god2`.`items` s JOIN `god2`.`item_content_profiles` p ON p.`ItemId`=s.`Id` AND p.`SourceSection`='WPN'
WHERE s.`ItemType`='WPN';

CREATE TEMPORARY TABLE `tmp_weapon_explicit_stat_seed` AS
SELECT t.`item_id`,t.`client_item_id`,'PhysicalAttack' `stat_type`,
 CAST(REGEXP_REPLACE(REGEXP_SUBSTR(t.`effect_text`,'物攻[[:space:]]*[+＋][[:space:]]*-?[0-9]+'),'^物攻[[:space:]]*[+＋][[:space:]]*','') AS SIGNED) `stat_value`,t.`effect_text`,t.`source_record_index`
FROM `tmp_weapon_static_text` t WHERE t.`effect_text` REGEXP '物攻[[:space:]]*[+＋][[:space:]]*-?[0-9]+'
UNION ALL SELECT t.`item_id`,t.`client_item_id`,'MagicAttack',
 CAST(REGEXP_REPLACE(REGEXP_SUBSTR(t.`effect_text`,'魔攻[[:space:]]*[+＋][[:space:]]*-?[0-9]+'),'^魔攻[[:space:]]*[+＋][[:space:]]*','') AS SIGNED),t.`effect_text`,t.`source_record_index`
FROM `tmp_weapon_static_text` t WHERE t.`effect_text` REGEXP '魔攻[[:space:]]*[+＋][[:space:]]*-?[0-9]+'
UNION ALL SELECT t.`item_id`,t.`client_item_id`,'Speed',
 CAST(REGEXP_REPLACE(REGEXP_SUBSTR(t.`effect_text`,'速度[[:space:]]*[+＋][[:space:]]*-?[0-9]+'),'^速度[[:space:]]*[+＋][[:space:]]*','') AS SIGNED),t.`effect_text`,t.`source_record_index`
FROM `tmp_weapon_static_text` t WHERE t.`effect_text` REGEXP '速度[[:space:]]*[+＋][[:space:]]*-?[0-9]+';
ALTER TABLE `tmp_weapon_explicit_stat_seed` MODIFY `stat_type` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;

CREATE TEMPORARY TABLE `tmp_weapon_explicit_stat_pivot` AS
SELECT `item_id`,MAX(CASE WHEN `stat_type`='PhysicalAttack' THEN `stat_value` END) `physical_attack`,
 MAX(CASE WHEN `stat_type`='MagicAttack' THEN `stat_value` END) `magic_attack`,
 MAX(CASE WHEN `stat_type`='Speed' THEN `stat_value` END) `speed`
FROM `tmp_weapon_explicit_stat_seed` GROUP BY `item_id`;
CREATE TEMPORARY TABLE `tmp_weapon_static_stat_gate` (`ok` tinyint NOT NULL,PRIMARY KEY(`ok`));

INSERT INTO `tmp_weapon_static_stat_gate`
SELECT IF((SELECT COUNT(*) FROM `tmp_weapon_static_text`)=645
 AND (SELECT COUNT(DISTINCT `item_id`) FROM `tmp_weapon_static_text`)=645
 AND (SELECT COUNT(*) FROM `tmp_weapon_explicit_stat_seed`)=1541
 AND (SELECT COUNT(*) FROM `tmp_weapon_explicit_stat_seed` WHERE `stat_type`='PhysicalAttack')=645
 AND (SELECT COUNT(*) FROM `tmp_weapon_explicit_stat_seed` WHERE `stat_type`='MagicAttack')=645
 AND (SELECT COUNT(*) FROM `tmp_weapon_explicit_stat_seed` WHERE `stat_type`='Speed')=251
 AND (SELECT COUNT(*) FROM `tmp_weapon_explicit_stat_seed` WHERE `stat_value`<=0)=0
 AND (SELECT MIN(`stat_value`) FROM `tmp_weapon_explicit_stat_seed`)=2
 AND (SELECT MAX(`stat_value`) FROM `tmp_weapon_explicit_stat_seed`)=550
 AND (SELECT COUNT(*) FROM `tmp_weapon_static_text` WHERE `effect_text` REGEXP '(物防|魔防|HP|MP)[[:space:]]*[+＋][[:space:]]*-?[0-9]+')=0
 AND SUM(t.`payload_source_sha256`='526c49b1b8302413636fbfadf5cb1baab4ad9b315a649175b2c6eccae7612da6')=645
 AND SUM(t.`profile_source_sha256`='c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f')=645
 AND COUNT(w.`item_id`)=645 AND SUM(w.`source_item_type`='WPN')=645
 AND SUM(w.`physical_attack_bonus`=t.`profile_physical_attack`)=645 AND SUM(w.`magic_attack_bonus`=t.`profile_magic_attack`)=645
 AND SUM(t.`profile_physical_defense` IS NULL OR t.`profile_physical_defense`=0)=645
 AND SUM(t.`profile_magic_defense` IS NULL OR t.`profile_magic_defense`=0)=645
 AND SUM(t.`profile_hp` IS NULL OR t.`profile_hp`=0)=645 AND SUM(t.`profile_mp` IS NULL OR t.`profile_mp`=0)=645
 AND SUM(t.`profile_physical_attack`=p.`physical_attack`)=645 AND SUM(t.`profile_magic_attack`=p.`magic_attack`)=645
 AND SUM((p.`speed` IS NOT NULL AND t.`profile_speed`=p.`speed`) OR (p.`speed` IS NULL AND (t.`profile_speed` IS NULL OR t.`profile_speed`=0)))=645
 AND (SELECT COUNT(*) FROM `tmp_weapon_explicit_stat_seed` s JOIN `god2_game`.`weapon_static_stat_evidence` e
      ON e.`item_id`=s.`item_id` AND e.`stat_type`=s.`stat_type`
      WHERE e.`displayed_flat_value`<>s.`stat_value` OR e.`runtime_eligible`<>0)=0,1,0)
FROM `tmp_weapon_static_text` t JOIN `god2_game`.`weapons` w ON w.`item_id`=t.`item_id`
JOIN `tmp_weapon_explicit_stat_pivot` p ON p.`item_id`=t.`item_id`;

INSERT INTO `god2_game`.`weapon_static_stat_evidence`
 (`item_id`,`client_item_id`,`stat_type`,`displayed_flat_value`,`source_effect_text`,`exact_client_source_sha256`,
  `evidence_status`,`activation_evidence_status`,`runtime_eligible`,`source_reference`)
SELECT s.`item_id`,s.`client_item_id`,s.`stat_type`,s.`stat_value`,s.`effect_text`,
 'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','VerifiedOfficialClientText','EvidenceBlocked',0,
 CONCAT('Exact current-client gamedata.csvZ labelled +N text; section=WPN; clientItemId=',s.`client_item_id`,
 '; sourceRecordIndex=',s.`source_record_index`,'; stat=',s.`stat_type`,'; displayedFlatValue=',s.`stat_value`,
 '; equip/combat activation not verified')
FROM `tmp_weapon_explicit_stat_seed` s JOIN `tmp_weapon_static_stat_gate` g ON g.`ok`=1
ON DUPLICATE KEY UPDATE `client_item_id`=VALUES(`client_item_id`),`displayed_flat_value`=VALUES(`displayed_flat_value`),
 `source_effect_text`=VALUES(`source_effect_text`),`exact_client_source_sha256`=VALUES(`exact_client_source_sha256`),
 `evidence_status`=VALUES(`evidence_status`),`activation_evidence_status`=VALUES(`activation_evidence_status`),
 `runtime_eligible`=0,`source_reference`=VALUES(`source_reference`);

UPDATE `god2_game`.`weapons` w JOIN `tmp_weapon_explicit_stat_pivot` p ON p.`item_id`=w.`item_id`
JOIN `tmp_weapon_static_stat_gate` g ON g.`ok`=1
SET w.`physical_attack_bonus`=p.`physical_attack`,w.`magic_attack_bonus`=p.`magic_attack`,
 w.`admin_note`=CONCAT_WS(' | ',NULLIF(w.`admin_note`,''),'Exact labelled WPN stats field-Verified; activation evidence-blocked.'),
 w.`updated_at_utc`=UTC_TIMESTAMP(6) WHERE w.`source_item_type`='WPN';

UPDATE `god2`.`item_content_profiles` r JOIN `tmp_weapon_explicit_stat_pivot` p ON p.`item_id`=r.`ItemId`
JOIN `tmp_weapon_static_stat_gate` g ON g.`ok`=1
SET r.`PhysicalAttackBonus`=p.`physical_attack`,r.`MagicAttackBonus`=p.`magic_attack`,
 r.`PhysicalDefenseBonus`=NULL,r.`MagicDefenseBonus`=NULL,r.`HpBonus`=NULL,r.`MpBonus`=NULL,r.`SpeedBonus`=p.`speed`,
 r.`UpdatedAtUtc`=UTC_TIMESTAMP(6) WHERE r.`SourceSection`='WPN';

CREATE OR REPLACE VIEW `god2_game`.`vw_weapon_static_stat_evidence_readable` AS
SELECT e.`client_item_id`,r.`name_zh_tw` `item_name_zh_tw`,e.`stat_type`,e.`displayed_flat_value`,e.`source_effect_text`,
 e.`evidence_status`,e.`activation_evidence_status`,e.`runtime_eligible`
FROM `god2_game`.`weapon_static_stat_evidence` e JOIN `god2_game`.`item_registry` r ON r.`item_id`=e.`item_id`;

DROP TEMPORARY TABLE `tmp_weapon_static_stat_gate`;
DROP TEMPORARY TABLE `tmp_weapon_explicit_stat_pivot`;
DROP TEMPORARY TABLE `tmp_weapon_explicit_stat_seed`;
DROP TEMPORARY TABLE `tmp_weapon_static_text`;
