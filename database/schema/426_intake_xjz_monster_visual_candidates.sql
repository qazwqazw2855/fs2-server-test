USE god2_game;

CREATE TABLE IF NOT EXISTS god2_research.xjz_monster_visual_candidates (
    source_monster_visual_id INT NOT NULL,
    monster_code INT NOT NULL,
    tentative_formal_monster_id BIGINT NULL,
    tentative_formal_monster_code VARCHAR(100) NULL,
    tentative_formal_monster_name_zh_tw VARCHAR(150) NULL,
    action_code VARCHAR(32) NOT NULL,
    palette_id INT NULL,
    file_identifier INT NULL,
    field_rom_path VARCHAR(256) NULL,
    battle_rom_path VARCHAR(256) NULL,
    ally_battle_rom_path VARCHAR(256) NULL,
    action_id INT NULL,
    pos_x INT NULL,
    pos_y INT NULL,
    reserved_flag INT NULL,
    shadow_index INT NULL,
    raw_source VARCHAR(256) NOT NULL,
    raw_line INT NULL,
    evidence_status VARCHAR(64) NOT NULL,
    packet_evidence_boundary TINYINT(1) NOT NULL,
    source_pack_id VARCHAR(96) NOT NULL,
    promoted_to_formal_monster TINYINT(1) NOT NULL DEFAULT 0,
    blocked_reason_zh_tw VARCHAR(512) NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (source_pack_id, monster_code),
    KEY idx_xjz_monster_visual_formal_monster (tentative_formal_monster_id),
    KEY idx_xjz_monster_visual_file_identifier (file_identifier)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS god2_research.xjz_monster_visual_candidate_summary (
    source_pack_id VARCHAR(96) NOT NULL,
    evidence_status VARCHAR(64) NOT NULL,
    visual_count INT NOT NULL,
    linked_formal_monster_count INT NOT NULL,
    field_rom_count INT NOT NULL,
    battle_rom_count INT NOT NULL,
    blocked_reason_zh_tw VARCHAR(512) NOT NULL,
    PRIMARY KEY (source_pack_id, evidence_status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TEMPORARY TABLE xjz_monster_visual_stage (
    monster_visual_id INT NOT NULL PRIMARY KEY,
    monster_code INT NOT NULL,
    action_code VARCHAR(32) NOT NULL,
    palette_id INT NULL,
    file_identifier INT NULL,
    field_rom_path VARCHAR(256) NULL,
    battle_rom_path VARCHAR(256) NULL,
    ally_battle_rom_path VARCHAR(256) NULL,
    action_id INT NULL,
    pos_x INT NULL,
    pos_y INT NULL,
    reserved_flag INT NULL,
    shadow_index INT NULL,
    raw_source VARCHAR(256) NOT NULL,
    raw_line INT NULL,
    evidence_status VARCHAR(64) NOT NULL,
    packet_evidence_boundary INT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO xjz_monster_visual_stage VALUES(1,1,'fi',6,6001,'data2\rom\eny\eny3001.ROM','data2\fight\eny\eny5001.rom','data2\fight\eny1\eny1001.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',5,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(2,2,'fi',6,6002,'data2\rom\eny\eny3002.ROM','data2\fight\eny\eny5002.rom','data2\fight\eny1\eny1002.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',6,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(3,3,'fi',6,6003,'data2\rom\eny\eny3003.ROM','data2\fight\eny\eny5003.rom','data2\fight\eny1\eny1003.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',7,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(4,4,'fi',6,6004,'data2\rom\eny\eny3004.ROM','data2\fight\eny\eny5004.rom','data2\fight\eny1\eny1004.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',8,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(5,5,'fi',6,6005,'data2\rom\eny\eny3005.ROM','data2\fight\eny\eny5005.rom','data2\fight\eny1\eny1005.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',9,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(6,6,'fi',6,6006,'data2\rom\eny\eny3006.ROM','data2\fight\eny\eny5006.rom','data2\fight\eny1\eny1006.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',10,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(7,7,'fi',6,6007,'data2\rom\eny\eny3007.ROM','data2\fight\eny\eny5007.rom','data2\fight\eny1\eny1007.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',11,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(8,8,'fi',6,6008,'data2\rom\eny\eny3008.ROM','data2\fight\eny\eny5008.rom','data2\fight\eny1\eny1008.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',12,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(9,9,'fi',6,6009,'data2\rom\eny\eny3009.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',13,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(10,10,'fi',6,6010,'data2\rom\eny\eny3010.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',14,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(11,11,'fi',6,6011,'data2\rom\eny\eny3011.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',15,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(12,12,'fi',6,6012,'data2\rom\eny\eny3012.ROM','data2\fight\eny\eny5012.rom','data2\fight\eny1\eny1012.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',16,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(13,13,'fi',6,6013,'data2\rom\eny\eny3013.ROM','data2\fight\eny\eny5013.rom','data2\fight\eny1\eny1013.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',17,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(14,14,'fi',6,6014,'data2\rom\eny\eny3014.ROM','data2\fight\eny\eny5014.rom','data2\fight\eny1\eny1014.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',18,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(15,15,'fi',6,6015,'data2\rom\eny\eny3015.ROM','data2\fight\eny\eny5015.rom','data2\fight\eny1\eny1015.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',19,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(16,16,'fi',6,6016,'data2\rom\eny\eny3016.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',20,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(17,17,'fi',6,6017,'data2\rom\eny\eny3017.ROM','data2\fight\eny\eny5017.rom','data2\fight\eny1\eny1017.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',21,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(18,18,'fi',6,6018,'data2\rom\eny\eny3018.ROM','data2\fight\eny\eny5018.rom','data2\fight\eny1\eny1018.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',22,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(19,19,'fi',6,6019,'data2\rom\eny\eny3019.ROM','data2\fight\eny\eny5019.rom','data2\fight\eny1\eny1019.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',23,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(20,20,'fi',6,6020,'data2\rom\eny\eny3020.ROM','data2\fight\eny\eny5020.rom','data2\fight\eny1\eny1020.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',24,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(21,21,'fi',6,6021,'data2\rom\eny\eny3021.ROM','data2\fight\eny\eny5021.rom','data2\fight\eny1\eny1021.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',25,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(22,22,'fi',6,6022,'data2\rom\eny\eny3022.ROM','data2\fight\eny\eny5022.rom','data2\fight\eny1\eny1022.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',26,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(23,23,'fi',6,6023,'data2\rom\eny\eny3023.ROM','data2\fight\eny\eny5023.rom','data2\fight\eny1\eny1023.rom',16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',27,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(24,24,'fi',6,6024,'data2\rom\eny\eny3024.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',28,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(25,25,'fi',6,6025,'data2\rom\eny\eny3025.ROM','data2\fight\eny\eny5025.rom','data2\fight\eny1\eny1025.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',29,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(26,26,'fi',6,6026,'data2\rom\eny\eny3026.ROM','data2\fight\eny\eny5026.rom','data2\fight\eny1\eny1026.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',30,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(27,27,'fi',6,6027,'data2\rom\eny\eny3027.ROM','data2\fight\eny\eny5027.rom','data2\fight\eny1\eny1027.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',31,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(28,28,'fi',6,6028,'data2\rom\eny\eny3028.ROM','data2\fight\eny\eny5028.rom','data2\fight\eny1\eny1028.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',32,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(29,29,'fi',6,6029,'data2\rom\eny\eny3029.ROM','data2\fight\eny\eny5029.rom','data2\fight\eny1\eny1029.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',33,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(30,30,'fi',6,6030,'data2\rom\eny\eny3030.ROM','data2\fight\eny\eny5030.rom','data2\fight\eny1\eny1030.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',34,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(31,31,'fi',6,6031,'data2\rom\eny\eny3031.ROM','data2\fight\eny\eny5031.rom','data2\fight\eny1\eny1031.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',35,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(32,32,'fi',6,6032,'data2\rom\eny\eny3032.ROM','data2\fight\eny\eny5032.rom','data2\fight\eny1\eny1032.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',36,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(33,33,'fi',6,6033,'data2\rom\eny\eny3033.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',37,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(34,34,'fi',6,6034,'data2\rom\eny\eny3034.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',38,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(35,35,'fi',6,6035,'data2\rom\eny\eny3035.ROM','data2\fight\eny\eny5035.rom','data2\fight\eny1\eny1035.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',39,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(36,36,'fi',6,6036,'data2\rom\eny\eny3036.ROM','data2\fight\eny\eny5036.rom','data2\fight\eny1\eny1036.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',40,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(37,37,'fi',6,6037,'data2\rom\eny\eny3037.ROM','data2\fight\eny\eny5037.rom','data2\fight\eny1\eny1037.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',41,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(38,38,'fi',6,6038,'data2\rom\eny\eny3038.ROM','data2\fight\eny\eny5038.rom','data2\fight\eny1\eny1038.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',42,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(39,39,'fi',6,6039,'data2\rom\eny\eny3039.ROM','data2\fight\eny\eny5039.rom','data2\fight\eny1\eny1039.rom',16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',43,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(40,40,'fi',6,6040,'data2\rom\eny\eny3040.ROM','data2\fight\eny\eny5040.rom','data2\fight\eny1\eny1040.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',44,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(41,41,'fi',6,6041,'data2\rom\eny\eny3041.ROM','data2\fight\eny\eny5041.rom','data2\fight\eny1\eny1041.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',45,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(42,42,'fi',6,6042,'data2\rom\eny\eny3042.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',46,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(43,43,'fi',6,6043,'data2\rom\eny\eny3043.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',47,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(44,44,'fi',6,6044,'data2\rom\eny\eny3044.ROM','data2\fight\eny\eny5044.rom','data2\fight\eny1\eny1044.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',48,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(45,45,'fi',6,6045,'data2\rom\eny\eny3045.ROM','data2\fight\eny\eny5045.rom','data2\fight\eny1\eny1045.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',49,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(46,46,'fi',6,6046,'data2\rom\eny\eny3046.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',50,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(47,47,'fi',6,6047,'data2\rom\eny\eny3047.ROM','data2\fight\eny\eny5047.rom','data2\fight\eny1\eny1047.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',51,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(48,48,'fi',6,6048,'data2\rom\eny\eny3048.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',52,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(49,49,'fi',6,6049,'data2\rom\eny\eny3049.ROM','data2\fight\eny\eny5049.rom','data2\fight\eny1\eny1049.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',53,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(50,50,'fi',6,6050,'data2\rom\eny\eny3050.ROM','data2\fight\eny\eny5050.rom','data2\fight\eny1\eny1050.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',54,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(51,51,'fi',6,6051,'data2\rom\eny\eny3051.ROM','data2\fight\eny\eny5051.rom','data2\fight\eny1\eny1051.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',55,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(52,52,'fi',6,6052,'data2\rom\eny\eny3052.ROM','data2\fight\eny\eny5052.rom','data2\fight\eny1\eny1052.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',56,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(53,53,'fi',6,6053,'data2\rom\eny\eny3053.ROM','data2\fight\eny\eny5053.rom','data2\fight\eny1\eny1053.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',57,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(54,54,'fi',6,6054,'data2\rom\eny\eny3054.ROM','data2\fight\eny\eny5054.rom','data2\fight\eny1\eny1054.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',58,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(55,55,'fi',6,6055,'data2\rom\eny\eny3055.ROM','data2\fight\eny\eny5055.rom','data2\fight\eny1\eny1055.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',59,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(56,56,'fi',6,6056,'data2\rom\eny\eny3056.ROM','data2\fight\eny\eny5056.rom','data2\fight\eny1\eny1056.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',60,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(57,57,'fi',6,6057,'data2\rom\eny\eny3057.ROM','data2\fight\eny\eny5057.rom','data2\fight\eny1\eny1057.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',61,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(58,58,'fi',6,6058,'data2\rom\eny\eny3058.ROM','data2\fight\eny\eny5058.rom','data2\fight\eny1\eny1058.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',62,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(59,59,'fi',6,6059,'data2\rom\eny\eny3059.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',63,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(60,60,'fi',6,6060,'data2\rom\eny\eny3060.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',64,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(61,61,'fi',6,6061,'data2\rom\eny\eny3061.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',65,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(62,62,'fi',6,6062,'data2\rom\eny\eny3062.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',66,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(63,63,'fi',6,6063,'data2\rom\eny\eny3063.ROM',NULL,NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',67,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(64,64,'fi',6,6064,NULL,'data\fight\eny\eny6064.rom',NULL,16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',68,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(65,65,'fi',6,6065,NULL,'data\fight\eny\eny6065.rom',NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',69,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(66,66,'fi',6,6066,NULL,'data\fight\eny\eny6066.rom',NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',70,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(67,67,'fi',6,6067,NULL,'data\fight\eny\eny6067.rom',NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',71,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(68,68,'fi',6,6068,NULL,'data\fight\eny\eny6068.rom',NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',72,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(69,69,'fi',6,6069,NULL,'data\fight\eny\eny6069.rom',NULL,16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',73,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(70,70,'fi',6,6070,NULL,'data\fight\eny\eny6070.rom',NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',74,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(71,71,'fi',6,6071,'data2\rom\eny\eny3071.ROM','data2\fight\eny\eny5071.rom','data2\fight\eny1\eny1071.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',75,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(72,72,'fi',6,6072,'data2\rom\eny\eny3072.ROM','data2\fight\eny\eny5072.rom','data2\fight\eny1\eny1072.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',76,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(73,73,'fi',6,6073,NULL,'data\fight\eny\eny6073.rom',NULL,16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',77,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(74,74,'fi',6,6074,NULL,'data\fight\eny\eny6074.rom',NULL,16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',78,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(75,75,'fi',6,6075,NULL,'data\fight\eny\eny6075.rom',NULL,16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',79,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(76,76,'fi',6,6076,'data2\rom\eny\eny3076.ROM','data2\fight\eny\eny5076.rom','data2\fight\eny1\eny1076.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',80,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(77,77,'fi',6,6077,'data2\rom\eny\eny3077.ROM','data2\fight\eny\eny5077.rom','data2\fight\eny1\eny1077.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',81,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(78,78,'fi',6,6078,'data2\rom\eny\eny3078.ROM','data2\fight\eny\eny5078.rom','data2\fight\eny1\eny1078.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',82,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(79,79,'fi',6,6079,'data2\rom\eny\eny3079.ROM','data2\fight\eny\eny5079.rom','data2\fight\eny1\eny1079.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',83,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(80,80,'fi',6,6080,'data2\rom\eny\eny3080.ROM','data2\fight\eny\eny5080.rom','data2\fight\eny1\eny1080.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',84,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(81,81,'fi',6,6081,'data2\rom\eny\eny3081.ROM','data2\fight\eny\eny5081.rom','data2\fight\eny1\eny1081.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',85,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(82,82,'fi',6,6082,'data2\rom\eny\eny3082.ROM','data2\fight\eny\eny5082.rom','data2\fight\eny1\eny1082.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',86,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(83,83,'fi',6,6083,'data2\rom\eny\eny3083.ROM','data2\fight\eny\eny5083.rom','data2\fight\eny1\eny1083.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',87,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(84,84,'fi',6,6084,'data2\rom\eny\eny3084.ROM','data2\fight\eny\eny6084.rom',NULL,16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',88,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(85,85,'fi',6,6085,NULL,'data\fight\eny\eny6085.rom',NULL,16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',89,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(86,86,'fi',6,6086,NULL,'data\fight\eny\eny6086.rom',NULL,16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',90,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(87,87,'fi',6,6087,'data2\rom\eny\eny3087.ROM','data2\fight\eny\eny5087.rom','data2\fight\eny1\eny1087.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',91,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(88,88,'fi',6,6088,'data2\rom\eny\eny3088.ROM','data2\fight\eny\eny5088.rom','data2\fight\eny1\eny1088.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',92,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(89,89,'fi',6,6089,'data2\rom\eny\eny3089.ROM','data2\fight\eny\eny5089.rom','data2\fight\eny1\eny1089.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',93,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(90,90,'fi',6,6090,'data2\rom\eny\eny3090.ROM','data2\fight\eny\eny5090.rom','data2\fight\eny1\eny1090.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',94,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(91,91,'fi',6,6091,'data2\rom\eny\eny3091.ROM','data2\fight\eny\eny5091.rom','data2\fight\eny1\eny1091.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',95,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(92,92,'fi',6,6092,'data2\rom\eny\eny3092.ROM','data2\fight\eny\eny5092.rom','data2\fight\eny1\eny1092.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',96,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(93,93,'fi',6,6093,'data2\rom\eny\eny3093.ROM','data2\fight\eny\eny5093.rom','data2\fight\eny1\eny1093.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',97,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(94,94,'fi',6,6094,'data2\rom\eny\eny3094.ROM','data2\fight\eny\eny5094.rom','data2\fight\eny1\eny1094.rom',16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',98,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(95,95,'fi',6,6095,NULL,'data2\fight\eny\eny6095.rom','data2\fight\eny1\eny1095.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',99,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(96,96,'fi',6,6096,'data2\rom\eny\eny3096.ROM','data2\fight\eny\eny6096.rom','data2\fight\eny1\eny1096.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',100,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(97,97,'fi',6,6097,'data2\rom\eny\eny3097.ROM','data2\fight\eny\eny6097.rom','data2\fight\eny1\eny1097.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',101,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(98,98,'fi',6,6098,'data2\rom\eny\eny3098.ROM','data2\fight\eny\eny5098.rom','data2\fight\eny1\eny1098.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',102,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(99,99,'fi',6,6099,NULL,'data2\fight\eny\eny6099.rom','data2\fight\eny1\eny1099.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',103,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(100,100,'fi',6,6100,NULL,'data2\fight\eny\eny6100.rom','data2\fight\eny1\eny1100.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',104,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(101,101,'fi',6,6101,NULL,'data2\fight\eny\eny6101.rom','data2\fight\eny1\eny1101.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',105,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(102,102,'fi',6,6102,NULL,'data2\fight\eny\eny6102.rom','data2\fight\eny1\eny1102.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',106,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(103,103,'fi',6,6103,'data2\rom\eny\eny3103.ROM','data2\fight\eny\eny5103.rom','data2\fight\eny1\eny1103.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',107,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(104,104,'fi',6,6104,NULL,'data2\fight\eny\eny6104.rom','data2\fight\eny1\eny1104.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',108,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(105,105,'fi',6,6105,NULL,'data2\fight\eny\eny6105.rom','data2\fight\eny1\eny1105.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',109,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(106,106,'fi',6,6106,'data2\rom\eny\eny3106.ROM','data2\fight\eny\eny5106.rom','data2\fight\eny1\eny1106.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',110,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(107,107,'fi',6,6107,'data2\rom\eny\eny3107.ROM','data2\fight\eny\eny5107.rom','data2\fight\eny1\eny1107.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',111,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(108,108,'fi',6,6108,NULL,'data2\fight\eny\eny6108.rom','data2\fight\eny1\eny1108.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',112,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(109,109,'fi',6,6109,NULL,'data2\fight\eny\eny6109.rom','data2\fight\eny1\eny1109.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',113,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(110,110,'fi',6,6110,'data2\rom\eny\eny3110.ROM','data2\fight\eny\eny5110.rom','data2\fight\eny1\eny1110.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',114,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(111,111,'fi',6,6111,'data2\rom\eny\eny3111.ROM','data2\fight\eny\eny6111.rom','data2\fight\eny1\eny1111.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',115,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(112,112,'fi',6,6112,'data2\rom\eny\eny3112.ROM','data2\fight\eny\eny5112.rom','data2\fight\eny1\eny1112.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',116,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(113,113,'fi',6,6113,NULL,'data2\fight\eny\eny6109.rom','data2\fight\eny1\eny1109.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',117,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(114,114,'fi',6,6114,NULL,'data2\fight\eny\eny6109.rom','data2\fight\eny1\eny1109.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',118,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(115,115,'fi',6,6115,NULL,'data2\fight\eny\eny6115.rom','data2\fight\eny1\eny1115.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',119,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(116,116,'fi',6,6116,'data2\rom\eny\eny3116.ROM','data2\fight\eny\eny6116.rom','data2\fight\eny1\eny1116.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',120,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(117,117,'fi',6,6117,'data2\rom\eny\eny3117.ROM','data2\fight\eny\eny5117.rom','data2\fight\eny1\eny1117.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',121,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(118,118,'fi',6,6118,'data2\rom\eny\eny3118.ROM','data2\fight\eny\eny5118.rom','data2\fight\eny1\eny1118.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',122,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(119,119,'fi',6,6119,NULL,'data2\fight\eny\eny6109.rom','data2\fight\eny1\eny1109.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',123,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(120,120,'fi',6,6120,NULL,'data2\fight\eny\eny6109.rom','data2\fight\eny1\eny1109.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',124,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(121,121,'fi',6,6121,'data2\rom\eny\eny3121.ROM','data2\fight\eny\eny5121.rom','data2\fight\eny1\eny1121.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',125,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(122,122,'fi',6,6122,NULL,'data2\fight\eny\eny5122.rom','data2\fight\eny1\eny1109.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',126,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(123,123,'fi',6,6123,NULL,'data2\fight\eny\eny6109.rom','data2\fight\eny1\eny1109.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',127,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(124,124,'fi',6,6124,NULL,'data2\fight\eny\eny6124.rom','data2\fight\eny1\eny1124.rom',16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',128,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(125,125,'fi',6,6125,'data2\rom\eny\eny3125.ROM','data2\fight\eny\eny6125.rom','data2\fight\eny1\eny1125.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',129,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(126,126,'fi',6,6126,'data2\rom\eny\eny3126.ROM','data2\fight\eny\eny5126.rom',NULL,16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',130,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(127,127,'fi',6,6127,NULL,NULL,NULL,16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',131,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(128,128,'fi',6,6128,NULL,NULL,NULL,16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',132,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(129,129,'fi',6,6129,NULL,NULL,NULL,16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',133,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(130,130,'fi',6,6130,NULL,NULL,NULL,16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',134,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(131,131,'fi',6,6131,'data2\rom\eny\eny3131.ROM','data2\fight\eny\eny5131.rom','data2\fight\eny1\eny1131.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',135,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(132,132,'fi',6,6132,NULL,NULL,NULL,16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',136,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(133,133,'fi',6,6133,NULL,NULL,NULL,16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',137,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(134,134,'fi',6,6134,NULL,NULL,NULL,16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',138,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(135,135,'fi',6,6135,'data2\rom\eyn\eny3135.ROM','data2\fight\ey5n\eny5135.rom','data2\fight\ey1n\eny1135.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',139,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(136,136,'fi',6,6135,'data2\rom\eyn2\eny3136.ROM','data2\fight\ey5n2\eny5136.rom',NULL,16,0,0,0,5,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',140,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(137,137,'fi',6,6137,'data2\rom\eyn\eny3137.ROM','data2\fight\ey5n\eny5137.rom','data2\fight\ey1n\eny1137.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',141,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(138,138,'fi',6,6138,'data2\rom\eyn\eny3138.ROM','data2\fight\ey5n\eny5138.rom','data2\fight\ey1n\eny1138.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',142,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(139,139,'fi',6,6139,'data2\rom\eyn\eny3139.ROM','data2\fight\ey5n\eny5139.rom','data2\fight\ey1n\eny1139.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',143,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(140,140,'fi',6,6140,'data2\rom\eyn\eny3140.ROM','data2\fight\ey5n\eny5140.rom','data2\fight\ey1n\eny1140.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',144,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(141,141,'fi',6,6141,'data2\rom\eyn\eny3141.ROM','data2\fight\ey5n\eny5141.rom','data2\fight\ey1n\eny1141.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',145,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(142,142,'fi',6,6142,'data2\rom\eyn\eny3142.ROM','data2\fight\ey5n\eny5142.rom','data2\fight\ey1n\eny1142.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',146,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(143,143,'fi',6,6143,'data2\rom\eyn2\eny3143.ROM','data2\fight\ey5n2\eny5143.rom','data2\fight\ey1n2\eny1143.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',147,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(144,144,'fi',6,6144,'data2\rom\eyn2\eny3144.ROM','data2\fight\ey5n2\eny5144.rom','data2\fight\ey1n2\eny1144.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',148,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(145,145,'fi',6,6145,'data2\rom\eyn2\eny3145.ROM','data2\fight\ey5n2\eny5145.rom','data2\fight\ey1n2\eny1145.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',149,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(146,146,'fi',6,6146,'data2\rom\eyn2\eny3146.ROM','data2\fight\ey5n2\eny5146.rom','data2\fight\ey1n2\eny1146.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',150,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(147,147,'fi',6,6147,'data2\rom\eyn2\eny3147.ROM','data2\fight\ey5n2\eny5147.rom','data2\fight\ey1n2\eny1147.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',151,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(148,148,'fi',6,6148,'data2\rom\eyn2\eny3148.ROM','data2\fight\ey5n2\eny5148.rom','data2\fight\ey1n2\eny1148.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',152,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(149,149,'fi',6,6149,'data2\rom\eny\eny3149.ROM','data2\fight\eny\eny5149.rom','data2\fight\eny1\eny1149.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',153,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(150,150,'fi',6,6150,'data2\rom\eyjiuwe\eny3150.ROM','data2\fight\Ey5jiuwe\eny5150.rom','data2\fight\Ey1jiuwe\eny1150.rom',16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',154,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(151,151,'fi',6,6151,'data2\rom\eyjiuwe\eny3151.ROM','data2\fight\Ey5jiuwe\eny5151.rom','data2\fight\Ey1jiuwe\eny1151.rom',16,0,0,0,3,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',155,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(152,152,'fi',6,6152,'data2\rom\eyjiu\eny3152.ROM','data2\fight\ey5jiu\eny5152.rom','data2\fight\ey1jiu\eny1152.rom',16,0,0,0,1,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',156,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(153,153,'fi',6,6153,'data2\rom\eyjiu\eny3153.ROM','data2\fight\ey5jiu\eny5153.rom','data2\fight\ey1jiu\eny1153.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',157,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(154,154,'fi',6,6154,'data2\rom\eyjiu\eny3154.ROM','data2\fight\ey5jiu\eny5154.rom','data2\fight\ey1jiu\eny1154.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',158,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(155,155,'fi',6,6155,'data2\rom\eyjiu\eny3155.ROM','data2\fight\ey5jiu\eny5155.rom','data2\fight\ey1jiu\eny1155.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',159,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(156,156,'fi',6,6156,'data2\rom\eyjiu\eny3156.ROM','data2\fight\ey5jiu\eny5156.rom','data2\fight\ey1jiu\eny1156.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',160,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(157,157,'fi',6,6157,'data2\rom\eyjiuwe\eny3157.ROM','data2\fight\Ey5jiuwe\eny5157.rom','data2\fight\Ey1jiuwe\eny1157.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',161,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(158,158,'fi',6,6158,'data2\rom\eyjiuwe\eny3158.ROM','data2\fight\Ey5jiuwe\eny5158.rom','data2\fight\Ey1jiuwe\eny1158.rom',16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',162,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(159,159,'fi',6,6159,'data2\rom\eyjiu\eny3159.ROM','data2\fight\ey5jiu\eny5159.rom','data2\fight\ey1jiu\eny1159.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',163,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(160,160,'fi',6,6160,'data2\rom\eyjiu\eny3160.ROM','data2\fight\ey5jiu\eny5160.rom','data2\fight\ey1jiu\eny1160.rom',16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',164,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(161,161,'fi',6,6161,'data2\rom\eyjiu\eny3161.ROM','data2\fight\ey5jiu\eny5161.rom','data2\fight\ey1jiu\eny11611.rom',16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',165,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(162,162,'fi',6,6162,'data2\rom\eyjiu\eny3162.ROM','data2\fight\ey5jiu\eny5162.rom','data2\fight\ey1jiu\eny1162.rom',16,0,0,0,4,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',166,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(163,163,'fi',6,6163,'data2\rom\eyjiu\eny3163.ROM','data2\fight\ey5jiu\eny5163.rom','data2\fight\ey1jiu\eny1163.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',167,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(164,164,'fi',6,6164,'data2\rom\eyjiu\eny3164.ROM','data2\fight\ey5jiu\eny5164.rom','data2\fight\ey1jiu\eny1164.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',168,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(165,165,'fi',6,6165,'data2\rom\eyjiuwe\eny3165.ROM','data2\fight\Ey5jiuwe\eny5165.rom','data2\fight\Ey1jiuwe\eny1165.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',169,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(166,166,'fi',6,6166,'data2\rom\eyjiuwe\eny3166.ROM','data2\fight\Ey5jiuwe\eny5166.rom','data2\fight\Ey1jiuwe\eny1166.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',170,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(167,167,'fi',6,6167,'data2\rom\eyjiuwe\eny3167.ROM','data2\fight\Ey5jiuwe\eny5167.rom','data2\fight\Ey1jiuwe\eny1167.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',171,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(168,168,'fi',6,6168,'data2\rom\eyjiuwe\eny3168.ROM','data2\fight\Ey5jiuwe\eny5168.rom','data2\fight\Ey1jiuwe\eny1168.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',172,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(169,169,'fi',6,6169,'data2\rom\eyjiuwe\eny3169.ROM','data2\fight\Ey5jiuwe\eny5169.rom','data2\fight\Ey1jiuwe\eny1169.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',173,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(170,170,'fi',6,6170,'data2\rom\eyjiuwe\eny3170.ROM','data2\fight\Ey5jiuwe\eny5170.rom','data2\fight\Ey1jiuwe\eny1170.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',174,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(171,171,'fi',6,6171,'data2\rom\eyjiuwe\eny3171.ROM','data2\fight\Ey5jiuwe\eny5171.rom','data2\fight\Ey1jiuwe\eny1171.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',175,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(172,172,'fi',6,6172,'data2\rom\eyjiuwe\eny3172.ROM','data2\fight\Ey5jiuwe\eny5172.rom','data2\fight\Ey1jiuwe\eny1172.rom',16,0,0,0,0,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',176,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(173,173,'fi',6,6173,'data2\rom\Eny\eny3173.ROM','data2\fight\Eny\eny5173.rom','data2\fight\Eny1\eny1173.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',177,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(174,174,'fi',6,6174,'data2\rom\Eny\eny3174.ROM','data2\fight\Eny\eny5174.rom','data2\fight\Eny1\eny1174.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',178,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(175,175,'fi',6,6175,'data2\rom\Eny\eny3175.ROM','data2\fight\Eny\eny5175.rom','data2\fight\Eny1\eny1175.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',179,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(176,176,'fi',6,6176,'data2\rom\Eny\eny3176.ROM','data2\fight\Eny\eny5176.rom','data2\fight\Eny1\eny1176.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',180,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(177,177,'fi',6,6177,'data2\rom\Eny\eny3177.ROM','data2\fight\Eny\eny5177.rom','data2\fight\Eny1\eny1177.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',181,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(178,178,'fi',6,6178,'data2\rom\Eny\eny3178.ROM','data2\fight\Eny\eny5178.rom','data2\fight\Eny1\eny1178.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',182,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(179,179,'fi',6,6179,'data2\rom\Eny\eny3179.ROM','data2\fight\Eny\eny5179.rom','data2\fight\Eny1\eny1179.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',183,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(180,180,'fi',6,6180,'data2\rom\Eny\eny3180.ROM','data2\fight\Eny\eny5180.rom',NULL,16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',184,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(181,181,'fi',6,6181,'data2\rom\Eny\eny3181.ROM','data2\fight\Eny\eny5181.rom','data2\fight\Eny1\eny1181.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',185,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(182,182,'fi',6,6182,'data2\rom\Eny\eny3182.ROM','data2\fight\Eny\eny5182.rom','data2\fight\Eny1\eny1182.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',186,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(183,183,'fi',6,6183,'data2\rom\Eny\eny3183.ROM','data2\fight\Eny\eny5183.rom','data2\fight\Eny1\eny1183.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',187,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(184,184,'fi',6,6184,'data2\rom\Eny\eny3184.ROM','data2\fight\Eny\eny5184.rom','data2\fight\Eny1\eny1184.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',188,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(185,185,'fi',6,6185,'data2\rom\Eny\eny3185.ROM','data2\fight\Eny\eny5185.rom','data2\fight\Eny1\eny1185.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',189,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(186,186,'fi',6,6186,'data2\rom\Eny\eny3186.ROM','data2\fight\Eny\eny5186.rom','data2\fight\Eny1\eny1186.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',190,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(187,187,'fi',6,6187,'data2\rom\Eny\eny3187.ROM','data2\fight\Eny\eny5187.rom','data2\fight\Eny1\eny1187.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',191,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(188,188,'fi',6,6188,'data2\rom\Eny\eny3188.ROM','data2\fight\Eny\eny5188.rom','data2\fight\Eny1\eny1188.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',192,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(189,189,'fi',6,6189,'data2\rom\Eny\eny3189.ROM','data2\fight\Eny\eny5189.rom','data2\fight\Eny1\eny1189.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',193,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(190,190,'fi',6,6190,'data2\rom\Eny\eny3190.ROM','data2\fight\Eny\eny5190.rom','data2\fight\Eny1\eny1190.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',194,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(191,191,'fi',6,6191,'data2\rom\Eny\eny3191.ROM','data2\fight\Eny\eny5191.rom','data2\fight\Eny1\eny1191.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',195,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(192,192,'fi',6,6192,'data2\rom\Eny\eny3192.ROM','data2\fight\Eny\eny5192.rom','data2\fight\Eny1\eny1192.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',196,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(193,193,'fi',6,6193,'data2\rom\Eny\eny3193.ROM','data2\fight\Eny\eny5193.rom','data2\fight\Eny1\eny1193.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',197,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(194,194,'fi',6,6194,'data2\rom\Eny\eny3194.ROM','data2\fight\Eny\eny5194.rom','data2\fight\Eny1\eny1194.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',198,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(195,195,'fi',6,6195,'data2\rom\Eny\eny3195.ROM','data2\fight\Eny\eny5195.rom','data2\fight\Eny1\eny1195.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',199,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(196,196,'fi',6,6196,'data2\rom\Eny\eny3196.ROM','data2\fight\Eny\eny5196.rom','data2\fight\Eny1\eny1196.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',200,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(197,197,'fi',6,6197,'data2\rom\Eny\eny3197.ROM','data2\fight\Eny\eny5197.rom','data2\fight\Eny1\eny1197.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',201,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(198,198,'fi',6,6198,'data2\rom\Eny\eny3198.ROM','data2\fight\Eny\eny5198.rom','data2\fight\Eny1\eny1198.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',202,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(199,199,'fi',6,6199,'data2\rom\Eny\eny3199.ROM','data2\fight\Eny\eny5199.rom','data2\fight\Eny1\eny1199.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',203,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(200,200,'fi',6,6200,'data2\rom\Eny\eny3200.ROM','data2\fight\Eny\eny5200.rom','data2\fight\Eny1\eny1200.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',204,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(201,201,'fi',6,6201,'data2\rom\Eny\eny3201.ROM','data2\fight\Eny\eny5201.rom','data2\fight\Eny1\eny1201.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',205,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(202,202,'fi',6,6202,'data2\rom\Eny\eny3202.ROM','data2\fight\Eny\eny5202.rom','data2\fight\Eny1\eny1202.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',206,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(203,203,'fi',6,6203,'data2\rom\Eny\eny3203.ROM','data2\fight\Eny\eny5203.rom','data2\fight\Eny1\eny1203.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',207,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(204,204,'fi',6,6204,'data2\rom\Eny\eny3204.ROM','data2\fight\Eny\eny5204.rom','data2\fight\Eny1\eny1204.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',208,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(205,205,'fi',6,6205,'data2\rom\Eny\eny3205.ROM','data2\fight\Eny\eny5205.rom','data2\fight\Eny1\eny1205.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',209,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(206,206,'fi',6,6206,'data2\rom\Eny\eny3206.ROM','data2\fight\Eny\eny5206.rom','data2\fight\Eny1\eny1206.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',210,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(207,207,'fi',6,6207,'data2\rom\Eny\eny3207.ROM','data2\fight\Eny\eny5207.rom','data2\fight\Eny1\eny1207.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',211,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(208,208,'fi',6,6208,'data2\rom\Eny\eny3208.ROM','data2\fight\Eny\eny5208.rom','data2\fight\Eny1\eny1208.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',212,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(209,209,'fi',6,6209,'data2\rom\Eny\eny3209.ROM','data2\fight\Eny\eny5209.rom','data2\fight\Eny1\eny1209.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',213,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(210,210,'fi',6,6210,'data2\rom\Eny\eny3210.ROM','data2\fight\Eny\eny5210.rom','data2\fight\Eny1\eny1210.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',214,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(211,211,'fi',6,6211,'data2\rom\Eny\eny3211.ROM','data2\fight\Eny\eny5211.rom','data2\fight\Eny1\eny1211.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',215,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(212,212,'fi',6,6212,'data2\rom\Eny\eny3212.ROM','data2\fight\Eny\eny5212.rom','data2\fight\Eny1\eny1212.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',216,'client_text_nls_traditional',0);
INSERT INTO xjz_monster_visual_stage VALUES(213,213,'fi',6,6213,'data2\rom\Eny\eny3213.ROM','data2\fight\Eny\eny5213.rom','data2\fight\Eny1\eny1213.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',217,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(214,214,'fi',6,6214,'data2\rom\Eny\eny3214.ROM','data2\fight\Eny\eny5214.rom','data2\fight\Eny1\eny1214.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',218,'client_csv',0);
INSERT INTO xjz_monster_visual_stage VALUES(215,215,'fi',6,6215,'data2\rom\Eny\eny3215.ROM','data2\fight\Eny\eny5215.rom','data2\fight\Eny1\eny1215.rom',16,0,0,0,2,'server_implementation/data/evidence_raw/eny.csv|server_implementation/data/evidence_raw/FightEny.csv',219,'client_text_nls_traditional',0);

DELETE FROM god2_research.xjz_monster_visual_candidates
WHERE source_pack_id = 'XJZ-csharp-evidence-capture-pack-20260818';

INSERT INTO god2_research.xjz_monster_visual_candidates (
    source_monster_visual_id,
    monster_code,
    tentative_formal_monster_id,
    tentative_formal_monster_code,
    tentative_formal_monster_name_zh_tw,
    action_code,
    palette_id,
    file_identifier,
    field_rom_path,
    battle_rom_path,
    ally_battle_rom_path,
    action_id,
    pos_x,
    pos_y,
    reserved_flag,
    shadow_index,
    raw_source,
    raw_line,
    evidence_status,
    packet_evidence_boundary,
    source_pack_id,
    promoted_to_formal_monster,
    blocked_reason_zh_tw
)
SELECT
    s.monster_visual_id,
    s.monster_code,
    m.monster_id,
    m.code,
    m.name_zh_tw,
    s.action_code,
    s.palette_id,
    s.file_identifier,
    s.field_rom_path,
    s.battle_rom_path,
    s.ally_battle_rom_path,
    s.action_id,
    s.pos_x,
    s.pos_y,
    s.reserved_flag,
    s.shadow_index,
    s.raw_source,
    s.raw_line,
    s.evidence_status,
    s.packet_evidence_boundary,
    'XJZ-csharp-evidence-capture-pack-20260818',
    0,
    '客戶端外觀資源可用；正式怪物名稱、HP、MP、攻防與生成綁定不在本證據內，需官方封包或黑箱測試。'
FROM xjz_monster_visual_stage s
LEFT JOIN god2_game.monsters m
    ON m.code = CONCAT('monster_', s.monster_code);

DELETE FROM god2_research.xjz_monster_visual_candidate_summary
WHERE source_pack_id = 'XJZ-csharp-evidence-capture-pack-20260818';

INSERT INTO god2_research.xjz_monster_visual_candidate_summary (
    source_pack_id,
    evidence_status,
    visual_count,
    linked_formal_monster_count,
    field_rom_count,
    battle_rom_count,
    blocked_reason_zh_tw
)
SELECT
    'XJZ-csharp-evidence-capture-pack-20260818',
    evidence_status,
    COUNT(*) AS visual_count,
    COUNT(tentative_formal_monster_id) AS linked_formal_monster_count,
    SUM(field_rom_path IS NOT NULL) AS field_rom_count,
    SUM(battle_rom_path IS NOT NULL) AS battle_rom_count,
    '外觀候選已整理；不覆蓋正式怪物數值。'
FROM god2_research.xjz_monster_visual_candidates
WHERE source_pack_id = 'XJZ-csharp-evidence-capture-pack-20260818'
GROUP BY evidence_status;

UPDATE god2_research.xjz_csharp_apply_queue
SET
    safe_apply_level_zh_tw = CONCAT(
        '已匯入怪物外觀候選 ',
        (SELECT COUNT(*) FROM god2_research.xjz_monster_visual_candidates WHERE source_pack_id = 'XJZ-csharp-evidence-capture-pack-20260818'),
        ' 筆，暫定對到正式怪物 ',
        (SELECT COUNT(*) FROM god2_research.xjz_monster_visual_candidates WHERE source_pack_id = 'XJZ-csharp-evidence-capture-pack-20260818' AND tentative_formal_monster_id IS NOT NULL),
        ' 筆'
    ),
    next_server_action_zh_tw = '可用於怪物外觀與抓包目標；HP/MP/攻防/AI 不從此表硬套，待官方封包或黑箱測試。'
WHERE queue_key = 'monster_visual_and_targets';
