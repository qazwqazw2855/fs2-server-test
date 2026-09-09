-- Publish the exact flat values displayed by the current-client EquipSetList.
--
-- Source: Data2/Patch/Comm/EquipSetList.csvZ
-- SHA-256: 5696e3f6492d7f9b4c9205ad775fb30b14d612941a64706ca0bced324411c858
--
-- These are Verified static client-text values only. The exact runtime
-- activation rule, aggregate-stat mutation and ordered client result are not
-- verified. All operational bonus rows therefore remain disabled and every
-- evidence row is explicitly runtime-ineligible.

CREATE TABLE IF NOT EXISTS `god2_game`.`equipment_set_bonus_static_evidence` (
    `set_id` bigint NOT NULL,
    `required_pieces` int NOT NULL,
    `bonus_type` varchar(50) NOT NULL,
    `displayed_flat_value` int NOT NULL,
    `source_effect_text` varchar(500) NOT NULL,
    `exact_client_source_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `evidence_status` varchar(40) NOT NULL,
    `activation_evidence_status` varchar(40) NOT NULL,
    `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0,
    `source_reference` varchar(700) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`set_id`,`required_pieces`,`bonus_type`),
    KEY `ix_equipment_set_bonus_static_type` (`bonus_type`,`runtime_eligible`),
    CONSTRAINT `fk_equipment_set_bonus_static_set` FOREIGN KEY (`set_id`)
        REFERENCES `god2_game`.`item_sets` (`set_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_equipment_set_bonus_static_type` CHECK (`bonus_type` IN
        ('Strength','Constitution','Intelligence','Speed','PhysicalAttack','MagicAttack',
         'PhysicalDefense','MagicDefense','MaximumHp','MaximumMp')),
    CONSTRAINT `ck_equipment_set_bonus_static_value` CHECK (`displayed_flat_value` > 0),
    CONSTRAINT `ck_equipment_set_bonus_static_status` CHECK
        (`evidence_status`='VerifiedOfficialClientText' AND
         `activation_evidence_status`='EvidenceBlocked' AND
         `runtime_eligible`=0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Exact EquipSetList displayed bonus values; activation remains evidence-blocked';

CREATE TEMPORARY TABLE `tmp_equipment_set_expected_effect` (
    `set_id` int NOT NULL PRIMARY KEY,
    `required_pieces` int NOT NULL,
    `effect_group` char(1) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `effect_text` varchar(500) NOT NULL
);

INSERT INTO `tmp_equipment_set_expected_effect`
    (`set_id`,`required_pieces`,`effect_group`,`effect_text`) VALUES
    (1,2,'A','物理防禦+20 魔法防禦+20'),
    (2,2,'B','物理攻擊+20 魔法攻擊+20'),(3,2,'B','物理攻擊+20 魔法攻擊+20'),
    (4,2,'B','物理攻擊+20 魔法攻擊+20'),(5,2,'B','物理攻擊+20 魔法攻擊+20'),
    (6,2,'C','體智力速+30 物魔攻+15 物防+15 魔防+25 HP+150 MP+50'),
    (7,2,'C','體智力速+30 物魔攻+15 物防+15 魔防+25 HP+150 MP+50'),
    (8,2,'D','體智力速+20 物魔攻+10 物防+10 魔防+20 HP+100 MP+50'),
    (9,2,'E','體智力速+30 物魔攻+15 物防+25 魔防+15 HP+150 MP+50'),
    (10,2,'E','體智力速+30 物魔攻+15 物防+25 魔防+15 HP+150 MP+50'),
    (11,2,'F','體智力速+20 物魔攻+10 物防+20 魔防+10 HP+100 MP+50'),
    (12,2,'G','體智力速+100 物魔攻+50 物魔防+50 HP+300 MP+150'),
    (13,2,'G','體智力速+100 物魔攻+50 物魔防+50 HP+300 MP+150'),
    (14,2,'H','體智力速+50 物魔攻+30 物魔防+30 HP+250 MP+125'),
    (15,2,'G','體智力速+100 物魔攻+50 物魔防+50 HP+300 MP+150'),
    (16,2,'G','體智力速+100 物魔攻+50 物魔防+50 HP+300 MP+150'),
    (17,2,'G','體智力速+100 物魔攻+50 物魔防+50 HP+300 MP+150'),
    (18,2,'G','體智力速+100 物魔攻+50 物魔防+50 HP+300 MP+150'),
    (19,2,'G','體智力速+100 物魔攻+50 物魔防+50 HP+300 MP+150'),
    (20,2,'G','體智力速+100 物魔攻+50 物魔防+50 HP+300 MP+150'),
    (21,2,'H','體智力速+50 物魔攻+30 物魔防+30 HP+250 MP+125'),
    (22,2,'H','體智力速+50 物魔攻+30 物魔防+30 HP+250 MP+125'),
    (23,2,'H','體智力速+50 物魔攻+30 物魔防+30 HP+250 MP+125'),
    (24,2,'H','體智力速+50 物魔攻+30 物魔防+30 HP+250 MP+125'),
    (25,5,'I','力智+150 物魔攻+150 HP+2000'),
    (26,5,'J','四維+200 雙抗雙攻各+150、 HP MP各1000'),
    (27,2,'K','體智力速+100 物魔攻+75 物魔防+75 HP+300 MP+200'),
    (28,2,'K','體智力速+100 物魔攻+75 物魔防+75 HP+300 MP+200'),
    (29,2,'K','體智力速+100 物魔攻+75 物魔防+75 HP+300 MP+200'),
    (30,2,'K','體智力速+100 物魔攻+75 物魔防+75 HP+300 MP+200'),
    (31,2,'L','體智力速+100 物魔攻+75 物魔防+75 HP+300 MP+150'),
    (32,2,'L','體智力速+100 物魔攻+75 物魔防+75 HP+300 MP+150'),
    (33,2,'M','體智+200 雙抗+150、 HP MP各500'),
    (34,2,'M','體智+200 雙抗+150、 HP MP各500'),
    (35,2,'N','力速+200 雙攻各+150、 HP MP各500'),
    (36,2,'O','體智力速+100 物魔攻+100 物魔防+100  HP+1000'),
    (37,2,'O','體智力速+100 物魔攻+100 物魔防+100  HP+1000'),
    (38,2,'O','體智力速+100 物魔攻+100 物魔防+100  HP+1000'),
    (39,2,'O','體智力速+100 物魔攻+100 物魔防+100  HP+1000'),
    (40,2,'P','體智力速+100 物魔攻+100 物魔防+100  MP+1000'),
    (41,2,'P','體智力速+100 物魔攻+100 物魔防+100  MP+1000'),
    (42,2,'Q','物理攻擊+50 魔法攻擊+50'),(43,2,'Q','物理攻擊+50 魔法攻擊+50'),
    (44,2,'R','物理防禦+50 魔法防禦+50'),
    (45,5,'S','四基+200 雙抗雙攻各+200 HP+2000'),
    (46,5,'S','四基+200 雙抗雙攻各+200 HP+2000'),
    (47,2,'T','四基+150 物理防禦+150 魔法防禦+150 HP+1500'),
    (48,2,'T','四基+150 物理防禦+150 魔法防禦+150 HP+1500');

CREATE TEMPORARY TABLE `tmp_equipment_set_bonus_group_value` (
    `effect_group` char(1) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `bonus_type` varchar(50) NOT NULL,
    `bonus_value` int NOT NULL,
    PRIMARY KEY (`effect_group`,`bonus_type`)
);

INSERT INTO `tmp_equipment_set_bonus_group_value`
    (`effect_group`,`bonus_type`,`bonus_value`) VALUES
    ('A','PhysicalDefense',20),('A','MagicDefense',20),
    ('B','PhysicalAttack',20),('B','MagicAttack',20),
    ('C','Strength',30),('C','Constitution',30),('C','Intelligence',30),('C','Speed',30),
    ('C','PhysicalAttack',15),('C','MagicAttack',15),('C','PhysicalDefense',15),('C','MagicDefense',25),('C','MaximumHp',150),('C','MaximumMp',50),
    ('D','Strength',20),('D','Constitution',20),('D','Intelligence',20),('D','Speed',20),
    ('D','PhysicalAttack',10),('D','MagicAttack',10),('D','PhysicalDefense',10),('D','MagicDefense',20),('D','MaximumHp',100),('D','MaximumMp',50),
    ('E','Strength',30),('E','Constitution',30),('E','Intelligence',30),('E','Speed',30),
    ('E','PhysicalAttack',15),('E','MagicAttack',15),('E','PhysicalDefense',25),('E','MagicDefense',15),('E','MaximumHp',150),('E','MaximumMp',50),
    ('F','Strength',20),('F','Constitution',20),('F','Intelligence',20),('F','Speed',20),
    ('F','PhysicalAttack',10),('F','MagicAttack',10),('F','PhysicalDefense',20),('F','MagicDefense',10),('F','MaximumHp',100),('F','MaximumMp',50),
    ('G','Strength',100),('G','Constitution',100),('G','Intelligence',100),('G','Speed',100),
    ('G','PhysicalAttack',50),('G','MagicAttack',50),('G','PhysicalDefense',50),('G','MagicDefense',50),('G','MaximumHp',300),('G','MaximumMp',150),
    ('H','Strength',50),('H','Constitution',50),('H','Intelligence',50),('H','Speed',50),
    ('H','PhysicalAttack',30),('H','MagicAttack',30),('H','PhysicalDefense',30),('H','MagicDefense',30),('H','MaximumHp',250),('H','MaximumMp',125),
    ('I','Strength',150),('I','Intelligence',150),('I','PhysicalAttack',150),('I','MagicAttack',150),('I','MaximumHp',2000),
    ('J','Strength',200),('J','Constitution',200),('J','Intelligence',200),('J','Speed',200),
    ('J','PhysicalAttack',150),('J','MagicAttack',150),('J','PhysicalDefense',150),('J','MagicDefense',150),('J','MaximumHp',1000),('J','MaximumMp',1000),
    ('K','Strength',100),('K','Constitution',100),('K','Intelligence',100),('K','Speed',100),
    ('K','PhysicalAttack',75),('K','MagicAttack',75),('K','PhysicalDefense',75),('K','MagicDefense',75),('K','MaximumHp',300),('K','MaximumMp',200),
    ('L','Strength',100),('L','Constitution',100),('L','Intelligence',100),('L','Speed',100),
    ('L','PhysicalAttack',75),('L','MagicAttack',75),('L','PhysicalDefense',75),('L','MagicDefense',75),('L','MaximumHp',300),('L','MaximumMp',150),
    ('M','Constitution',200),('M','Intelligence',200),('M','PhysicalDefense',150),('M','MagicDefense',150),('M','MaximumHp',500),('M','MaximumMp',500),
    ('N','Strength',200),('N','Speed',200),('N','PhysicalAttack',150),('N','MagicAttack',150),('N','MaximumHp',500),('N','MaximumMp',500),
    ('O','Strength',100),('O','Constitution',100),('O','Intelligence',100),('O','Speed',100),
    ('O','PhysicalAttack',100),('O','MagicAttack',100),('O','PhysicalDefense',100),('O','MagicDefense',100),('O','MaximumHp',1000),
    ('P','Strength',100),('P','Constitution',100),('P','Intelligence',100),('P','Speed',100),
    ('P','PhysicalAttack',100),('P','MagicAttack',100),('P','PhysicalDefense',100),('P','MagicDefense',100),('P','MaximumMp',1000),
    ('Q','PhysicalAttack',50),('Q','MagicAttack',50),
    ('R','PhysicalDefense',50),('R','MagicDefense',50),
    ('S','Strength',200),('S','Constitution',200),('S','Intelligence',200),('S','Speed',200),
    ('S','PhysicalAttack',200),('S','MagicAttack',200),('S','PhysicalDefense',200),('S','MagicDefense',200),('S','MaximumHp',2000),
    ('T','Strength',150),('T','Constitution',150),('T','Intelligence',150),('T','Speed',150),
    ('T','PhysicalDefense',150),('T','MagicDefense',150),('T','MaximumHp',1500);

CREATE TEMPORARY TABLE `tmp_equipment_set_bonus_seed` AS
SELECT expected_row.`set_id`,expected_row.`required_pieces`,value_row.`bonus_type`,
       value_row.`bonus_value`,expected_row.`effect_text`
FROM `tmp_equipment_set_expected_effect` expected_row
JOIN `tmp_equipment_set_bonus_group_value` value_row
  ON value_row.`effect_group`=expected_row.`effect_group`;

CREATE TEMPORARY TABLE `tmp_equipment_set_bonus_gate` (
    `ok` tinyint NOT NULL,
    CONSTRAINT `ck_tmp_equipment_set_bonus_gate` CHECK (`ok`=1)
);

INSERT INTO `tmp_equipment_set_bonus_gate` (`ok`)
SELECT IF(
    (SELECT COUNT(*) FROM `tmp_equipment_set_expected_effect`)=48
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_group_value`)=149
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed`)=385
    AND (SELECT COUNT(DISTINCT `set_id`) FROM `tmp_equipment_set_bonus_seed`)=48
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `required_pieces`=2)=352
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `required_pieces`=5)=33
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `bonus_value`<=0)=0
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `bonus_type`='Strength')=38
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `bonus_type`='Constitution')=38
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `bonus_type`='Intelligence')=39
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `bonus_type`='Speed')=37
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `bonus_type`='PhysicalAttack')=42
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `bonus_type`='MagicAttack')=42
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `bonus_type`='PhysicalDefense')=40
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `bonus_type`='MagicDefense')=40
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `bonus_type`='MaximumHp')=38
    AND (SELECT COUNT(*) FROM `tmp_equipment_set_bonus_seed` WHERE `bonus_type`='MaximumMp')=31
    AND COUNT(source_row.`SetId`)=48
    AND COUNT(set_row.`set_id`)=48
    AND SUM(source_row.`RequiredPieces`=expected_row.`required_pieces`)=48
    AND SUM(BINARY source_row.`EffectsZhTw`=BINARY expected_row.`effect_text`)=48
    AND SUM(source_row.`EvidenceStatus`='Verified')=48
    AND SUM(source_row.`SetRelationshipStatus`='Verified')=48
    AND SUM(source_row.`SetBonusEvidenceStatus` IN ('Candidate','VerifiedStaticTextOnly'))=48
    AND SUM(source_row.`ProductionRelationshipEnabled`=1)=48
    AND SUM(source_row.`ProductionBonusEnabled`=0)=48
    AND SUM(source_row.`SourceHash`='5696e3f6492d7f9b4c9205ad775fb30b14d612941a64706ca0bced324411c858')=48
    AND (SELECT COUNT(*)
         FROM `tmp_equipment_set_bonus_seed` seed_row
         JOIN `god2_game`.`item_set_bonuses` existing_row
           ON existing_row.`set_id`=seed_row.`set_id`
          AND existing_row.`required_pieces`=seed_row.`required_pieces`
          AND existing_row.`bonus_type`=seed_row.`bonus_type`
         WHERE existing_row.`bonus_value`<>seed_row.`bonus_value`
            OR existing_row.`status_effect_id` IS NOT NULL
            OR existing_row.`enabled`<>0)=0,
    1, 0)
FROM `tmp_equipment_set_expected_effect` expected_row
JOIN `god2`.`equipment_set_definitions` source_row ON source_row.`SetId`=expected_row.`set_id`
JOIN `god2_game`.`item_sets` set_row ON set_row.`set_id`=expected_row.`set_id`;

INSERT INTO `god2_game`.`equipment_set_bonus_static_evidence`
    (`set_id`,`required_pieces`,`bonus_type`,`displayed_flat_value`,`source_effect_text`,
     `exact_client_source_sha256`,`evidence_status`,`activation_evidence_status`,
     `runtime_eligible`,`source_reference`)
SELECT seed_row.`set_id`,seed_row.`required_pieces`,seed_row.`bonus_type`,seed_row.`bonus_value`,
       seed_row.`effect_text`,
       '5696e3f6492d7f9b4c9205ad775fb30b14d612941a64706ca0bced324411c858',
       'VerifiedOfficialClientText','EvidenceBlocked',0,
       CONCAT('Exact current-client EquipSetList.csvZ display text; setId=',seed_row.`set_id`,
              '; requiredPieces=',seed_row.`required_pieces`,'; type=',seed_row.`bonus_type`,
              '; displayedFlatValue=',seed_row.`bonus_value`,
              '; activation/stat mutation/ordered result not verified')
FROM `tmp_equipment_set_bonus_seed` seed_row
JOIN `tmp_equipment_set_bonus_gate` gate_row ON gate_row.`ok`=1
ON DUPLICATE KEY UPDATE
    `displayed_flat_value`=VALUES(`displayed_flat_value`),
    `source_effect_text`=VALUES(`source_effect_text`),
    `exact_client_source_sha256`=VALUES(`exact_client_source_sha256`),
    `evidence_status`=VALUES(`evidence_status`),
    `activation_evidence_status`=VALUES(`activation_evidence_status`),
    `runtime_eligible`=0,
    `source_reference`=VALUES(`source_reference`);

INSERT INTO `god2_game`.`item_set_bonuses`
    (`set_id`,`required_pieces`,`bonus_type`,`bonus_value`,`status_effect_id`,`enabled`,`admin_note`)
SELECT seed_row.`set_id`,seed_row.`required_pieces`,seed_row.`bonus_type`,seed_row.`bonus_value`,
       NULL,0,'Verified exact current-client display value only; activation and stat mutation remain evidence-blocked.'
FROM `tmp_equipment_set_bonus_seed` seed_row
JOIN `tmp_equipment_set_bonus_gate` gate_row ON gate_row.`ok`=1
ON DUPLICATE KEY UPDATE
    `bonus_value`=VALUES(`bonus_value`),
    `status_effect_id`=NULL,
    `enabled`=0,
    `admin_note`=VALUES(`admin_note`);

UPDATE `god2`.`equipment_set_definitions` source_row
JOIN `tmp_equipment_set_expected_effect` expected_row ON expected_row.`set_id`=source_row.`SetId`
JOIN `tmp_equipment_set_bonus_gate` gate_row ON gate_row.`ok`=1
SET source_row.`SetBonusEvidenceStatus`='VerifiedStaticTextOnly',
    source_row.`ProductionBonusEnabled`=0;

UPDATE `god2_game`.`item_sets` set_row
JOIN `tmp_equipment_set_expected_effect` expected_row ON expected_row.`set_id`=set_row.`set_id`
JOIN `tmp_equipment_set_bonus_gate` gate_row ON gate_row.`ok`=1
SET set_row.`admin_note`='Set relationship and exact displayed bonus values are verified; activation and stat mutation remain evidence-blocked.',
    set_row.`updated_at_utc`=UTC_TIMESTAMP(6);

CREATE OR REPLACE VIEW `god2_game`.`vw_equipment_set_bonus_static_evidence_readable` AS
SELECT evidence_row.`set_id` AS `set_id`,set_row.`name_zh_tw` AS `set_name_zh_tw`,
       evidence_row.`required_pieces` AS `required_pieces`,
       evidence_row.`bonus_type` AS `bonus_type`,
       evidence_row.`displayed_flat_value` AS `displayed_flat_value`,
       evidence_row.`source_effect_text` AS `source_effect_text`,
       evidence_row.`evidence_status` AS `evidence_status`,
       evidence_row.`activation_evidence_status` AS `activation_evidence_status`,
       evidence_row.`runtime_eligible` AS `runtime_eligible`
FROM `god2_game`.`equipment_set_bonus_static_evidence` evidence_row
JOIN `god2_game`.`item_sets` set_row ON set_row.`set_id`=evidence_row.`set_id`;

DROP TEMPORARY TABLE `tmp_equipment_set_bonus_gate`;
DROP TEMPORARY TABLE `tmp_equipment_set_bonus_seed`;
DROP TEMPORARY TABLE `tmp_equipment_set_bonus_group_value`;
DROP TEMPORARY TABLE `tmp_equipment_set_expected_effect`;
