START TRANSACTION;

UPDATE `god2_game`.`monsters`
SET `level`=32,
    `max_hp`=699,
    `max_mp`=307,
    `strength`=190,
    `constitution`=108,
    `intelligence`=190,
    `speed`=77,
    `metal`=0,
    `wood`=200,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=107,
    `physical_defense`=32,
    `magic_attack`=107,
    `magic_defense`=36,
    `experience_reward`=561,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=1
  AND `name_zh_tw`='三碑鴨';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=6;source_row_index=101;keys=1:0:1;0:2:212',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=1;

UPDATE `god2_game`.`monsters`
SET `level`=32,
    `max_hp`=810,
    `max_mp`=230,
    `strength`=276,
    `constitution`=121,
    `intelligence`=78,
    `speed`=90,
    `metal`=0,
    `wood`=200,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=130,
    `physical_defense`=38,
    `magic_attack`=77,
    `magic_defense`=26,
    `experience_reward`=561,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=2
  AND `name_zh_tw`='長耳兔';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=6;source_row_index=102;keys=1:0:2',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=2;

UPDATE `god2_game`.`monsters`
SET `level`=32,
    `max_hp`=699,
    `max_mp`=307,
    `strength`=190,
    `constitution`=108,
    `intelligence`=190,
    `speed`=77,
    `metal`=0,
    `wood`=200,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=107,
    `physical_defense`=32,
    `magic_attack`=107,
    `magic_defense`=36,
    `experience_reward`=561,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=3
  AND `name_zh_tw`='小野菇';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=6;source_row_index=103;keys=1:0:3',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=3;

UPDATE `god2_game`.`monsters`
SET `level`=34,
    `max_hp`=756,
    `max_mp`=319,
    `strength`=190,
    `constitution`=117,
    `intelligence`=190,
    `speed`=98,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=108,
    `physical_defense`=34,
    `magic_attack`=108,
    `magic_defense`=38,
    `experience_reward`=631,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=4
  AND `name_zh_tw`='大蜻蛉';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=6;source_row_index=104;keys=1:0:4;0:2:30',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=4;

UPDATE `god2_game`.`monsters`
SET `level`=36,
    `max_hp`=894,
    `max_mp`=254,
    `strength`=308,
    `constitution`=133,
    `intelligence`=86,
    `speed`=98,
    `metal`=0,
    `wood`=200,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=141,
    `physical_defense`=42,
    `magic_attack`=81,
    `magic_defense`=28,
    `experience_reward`=707,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=5
  AND `name_zh_tw`='野狐';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=6;source_row_index=105;keys=1:0:5;0:2:222',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=5;

UPDATE `god2_game`.`monsters`
SET `level`=36,
    `max_hp`=794,
    `max_mp`=235,
    `strength`=350,
    `constitution`=125,
    `intelligence`=60,
    `speed`=90,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=167,
    `physical_defense`=43,
    `magic_attack`=59,
    `magic_defense`=23,
    `experience_reward`=707,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=6
  AND `name_zh_tw`='狼';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=6;source_row_index=106;keys=1:0:6',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=6;

UPDATE `god2_game`.`monsters`
SET `level`=38,
    `max_hp`=837,
    `max_mp`=244,
    `strength`=363,
    `constitution`=131,
    `intelligence`=57,
    `speed`=104,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=171,
    `physical_defense`=45,
    `magic_attack`=59,
    `magic_defense`=23,
    `experience_reward`=790,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=7
  AND `name_zh_tw`='豹';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=6;source_row_index=107;keys=1:0:7',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=7;

UPDATE `god2_game`.`monsters`
SET `level`=38,
    `max_hp`=813,
    `max_mp`=356,
    `strength`=220,
    `constitution`=126,
    `intelligence`=220,
    `speed`=89,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=118,
    `physical_defense`=36,
    `magic_attack`=118,
    `magic_defense`=41,
    `experience_reward`=790,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=8
  AND `name_zh_tw`='馬賊';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=6;source_row_index=108;keys=1:0:8;1:0:17;0:0:281;0:0:282;0:0:283;0:0:284;0:0:285',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=8;

UPDATE `god2_game`.`monsters`
SET `level`=1,
    `max_hp`=38,
    `max_mp`=40,
    `strength`=25,
    `constitution`=10,
    `intelligence`=25,
    `speed`=10,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=50,
    `physical_attack`=29,
    `physical_defense`=3,
    `magic_attack`=29,
    `magic_defense`=4,
    `experience_reward`=10,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=27
  AND `name_zh_tw`='絨毛鴨';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=1;source_row_index=0;keys=2:0:1;0:2:512',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=27;

UPDATE `god2_game`.`monsters`
SET `level`=2,
    `max_hp`=45,
    `max_mp`=44,
    `strength`=32,
    `constitution`=12,
    `intelligence`=27,
    `speed`=11,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=31,
    `physical_defense`=3,
    `magic_attack`=30,
    `magic_defense`=4,
    `experience_reward`=20,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=28
  AND `name_zh_tw`='小野兔';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=1;source_row_index=1;keys=2:0:2;0:2:25',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=28;

UPDATE `god2_game`.`monsters`
SET `level`=4,
    `max_hp`=72,
    `max_mp`=54,
    `strength`=46,
    `constitution`=16,
    `intelligence`=31,
    `speed`=13,
    `metal`=50,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=36,
    `physical_defense`=5,
    `magic_attack`=32,
    `magic_defense`=5,
    `experience_reward`=40,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=29
  AND `name_zh_tw`='仙兔';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=1;source_row_index=2;keys=2:0:3;0:2:125',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=29;

UPDATE `god2_game`.`monsters`
SET `level`=6,
    `max_hp`=90,
    `max_mp`=64,
    `strength`=60,
    `constitution`=20,
    `intelligence`=35,
    `speed`=15,
    `metal`=50,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=40,
    `physical_defense`=6,
    `magic_attack`=33,
    `magic_defense`=5,
    `experience_reward`=61,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=30
  AND `name_zh_tw`='仙狐';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=1;source_row_index=3;keys=2:0:4;0:2:122',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=30;

UPDATE `god2_game`.`monsters`
SET `level`=8,
    `max_hp`=126,
    `max_mp`=74,
    `strength`=74,
    `constitution`=24,
    `intelligence`=39,
    `speed`=17,
    `metal`=0,
    `wood`=50,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=45,
    `physical_defense`=7,
    `magic_attack`=35,
    `magic_defense`=6,
    `experience_reward`=83,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=31
  AND `name_zh_tw`='靈草菇';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=1;source_row_index=4;keys=2:0:5;0:2:202',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=31;

UPDATE `god2_game`.`monsters`
SET `level`=10,
    `max_hp`=147,
    `max_mp`=84,
    `strength`=88,
    `constitution`=28,
    `intelligence`=43,
    `speed`=19,
    `metal`=0,
    `wood`=50,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=49,
    `physical_defense`=8,
    `magic_attack`=37,
    `magic_defense`=7,
    `experience_reward`=106,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=32
  AND `name_zh_tw`='褐蝸螺';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=1;source_row_index=5;keys=2:0:6;8:4:1;0:2:201;0:2:277;0:2:278;0:2:279;0:2:280;0:2:281;0:2:282;0:2:283;0:2:284;0:2:285;0:2:286;0:2:287;0:2:288;0:2:289;0:2:290;0:2:291;0:2:292;0:2:293;0:2:294',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=32;

UPDATE `god2_game`.`monsters`
SET `level`=4,
    `max_hp`=200,
    `max_mp`=80,
    `strength`=50,
    `constitution`=24,
    `intelligence`=50,
    `speed`=21,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=50,
    `earth`=0,
    `physical_attack`=43,
    `physical_defense`=11,
    `magic_attack`=43,
    `magic_defense`=12,
    `experience_reward`=40,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=33
  AND `name_zh_tw`='強化妖兔';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=1;source_row_index=6;keys=2:0:7',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=33;

UPDATE `god2_game`.`monsters`
SET `level`=12,
    `max_hp`=770,
    `max_mp`=90,
    `strength`=106,
    `constitution`=48,
    `intelligence`=30,
    `speed`=38,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=61,
    `physical_defense`=17,
    `magic_attack`=40,
    `magic_defense`=12,
    `experience_reward`=105,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=34
  AND `name_zh_tw`='千年樹妖';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=1;source_row_index=15;keys=2:0:8;2:0:9;2:0:10;2:0:11;2:0:12;2:0:13;2:0:14;2:0:15;2:0:16',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=34;

UPDATE `god2_game`.`monsters`
SET `level`=10,
    `max_hp`=169,
    `max_mp`=129,
    `strength`=80,
    `constitution`=42,
    `intelligence`=80,
    `speed`=33,
    `metal`=50,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=82,
    `physical_defense`=21,
    `magic_attack`=82,
    `magic_defense`=23,
    `experience_reward`=85,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=36
  AND `name_zh_tw`='強化迷你鴨';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=1;source_row_index=17;keys=2:0:18',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=36;

UPDATE `god2_game`.`monsters`
SET `level`=8,
    `max_hp`=157,
    `max_mp`=112,
    `strength`=26,
    `constitution`=30,
    `intelligence`=78,
    `speed`=40,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=50,
    `physical_attack`=38,
    `physical_defense`=11,
    `magic_attack`=52,
    `magic_defense`=16,
    `experience_reward`=66,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=47
  AND `name_zh_tw`='土爪草';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=57;keys=2:2:1;8:8:2;0:2:503',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=47;

UPDATE `god2_game`.`monsters`
SET `level`=8,
    `max_hp`=204,
    `max_mp`=71,
    `strength`=76,
    `constitution`=38,
    `intelligence`=22,
    `speed`=38,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=50,
    `earth`=0,
    `physical_attack`=51,
    `physical_defense`=15,
    `magic_attack`=37,
    `magic_defense`=11,
    `experience_reward`=66,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=48
  AND `name_zh_tw`='火魅';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=58;keys=2:2:2;8:8:5;0:2:413',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=48;

UPDATE `god2_game`.`monsters`
SET `level`=8,
    `max_hp`=206,
    `max_mp`=72,
    `strength`=78,
    `constitution`=40,
    `intelligence`=26,
    `speed`=30,
    `metal`=0,
    `wood`=50,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=51,
    `physical_defense`=14,
    `magic_attack`=37,
    `magic_defense`=11,
    `experience_reward`=66,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=49
  AND `name_zh_tw`='黑木怪';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=59;keys=2:2:3;0:2:207',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=49;

UPDATE `god2_game`.`monsters`
SET `level`=16,
    `max_hp`=235,
    `max_mp`=181,
    `strength`=34,
    `constitution`=46,
    `intelligence`=134,
    `speed`=56,
    `metal`=36,
    `wood`=36,
    `water`=36,
    `fire`=36,
    `earth`=36,
    `physical_attack`=44,
    `physical_defense`=14,
    `magic_attack`=71,
    `magic_defense`=23,
    `experience_reward`=150,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=51
  AND `name_zh_tw`='幽魂';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=64;keys=2:32:1;0:0:192;0:2:58',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=51;

UPDATE `god2_game`.`monsters`
SET `level`=16,
    `max_hp`=232,
    `max_mp`=184,
    `strength`=34,
    `constitution`=46,
    `intelligence`=139,
    `speed`=51,
    `metal`=40,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=80,
    `physical_attack`=38,
    `physical_defense`=9,
    `magic_attack`=66,
    `magic_defense`=19,
    `experience_reward`=150,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=52
  AND `name_zh_tw`='幻法師';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=65;keys=2:32:2;12:3:11;0:2:521',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=52;

UPDATE `god2_game`.`monsters`
SET `level`=16,
    `max_hp`=870,
    `max_mp`=184,
    `strength`=34,
    `constitution`=46,
    `intelligence`=139,
    `speed`=51,
    `metal`=50,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=44,
    `physical_defense`=14,
    `magic_attack`=72,
    `magic_defense`=24,
    `experience_reward`=150,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=53
  AND `name_zh_tw`='附瘟妖靈師';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=66;keys=2:32:3;8:2:10',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=53;

UPDATE `god2_game`.`monsters`
SET `level`=72,
    `max_hp`=1487,
    `max_mp`=426,
    `strength`=674,
    `constitution`=233,
    `intelligence`=96,
    `speed`=162,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=396,
    `physical_defense`=96,
    `magic_attack`=138,
    `magic_defense`=49,
    `experience_reward`=3619,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=55
  AND `name_zh_tw`='幽冥衛士';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=6;source_row_index=93;keys=2:43:1',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=55;

UPDATE `god2_game`.`monsters`
SET `level`=72,
    `max_hp`=1478,
    `max_mp`=470,
    `strength`=596,
    `constitution`=241,
    `intelligence`=158,
    `speed`=170,
    `metal`=300,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=375,
    `physical_defense`=93,
    `magic_attack`=155,
    `magic_defense`=56,
    `experience_reward`=3619,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=57
  AND `name_zh_tw`='黑金骷髏';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=6;source_row_index=95;keys=2:43:3;0:2:150',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=57;

UPDATE `god2_game`.`monsters`
SET `level`=74,
    `max_hp`=972,
    `max_mp`=800,
    `strength`=166,
    `constitution`=239,
    `intelligence`=614,
    `speed`=176,
    `metal`=0,
    `wood`=0,
    `water`=300,
    `fire`=0,
    `earth`=0,
    `physical_attack`=158,
    `physical_defense`=219,
    `magic_attack`=279,
    `magic_defense`=158,
    `experience_reward`=3891,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=60
  AND `name_zh_tw`='獵魂鬼';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=6;source_row_index=98;keys=2:43:6;0:2:358',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=60;

UPDATE `god2_game`.`monsters`
SET `level`=76,
    `max_hp`=1564,
    `max_mp`=447,
    `strength`=710,
    `constitution`=245,
    `intelligence`=100,
    `speed`=170,
    `metal`=300,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=407,
    `physical_defense`=99,
    `magic_attack`=141,
    `magic_defense`=51,
    `experience_reward`=4177,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=61
  AND `name_zh_tw`='怪刀武士';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=6;source_row_index=99;keys=2:43:7;0:2:115',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=61;

UPDATE `god2_game`.`monsters`
SET `level`=12,
    `max_hp`=254,
    `max_mp`=89,
    `strength`=104,
    `constitution`=46,
    `intelligence`=26,
    `speed`=46,
    `metal`=50,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=61,
    `physical_defense`=17,
    `magic_attack`=40,
    `magic_defense`=12,
    `experience_reward`=105,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=62
  AND `name_zh_tw`='金魅';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=61;keys=2:108:1;0:2:113',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=62;

UPDATE `god2_game`.`monsters`
SET `level`=12,
    `max_hp`=233,
    `max_mp`=93,
    `strength`=106,
    `constitution`=38,
    `intelligence`=30,
    `speed`=48,
    `metal`=50,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=62,
    `physical_defense`=17,
    `magic_attack`=41,
    `magic_defense`=12,
    `experience_reward`=105,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=63
  AND `name_zh_tw`='負刀半妖';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=62;keys=2:108:2;0:2:118',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=63;

UPDATE `god2_game`.`monsters`
SET `level`=12,
    `max_hp`=257,
    `max_mp`=90,
    `strength`=106,
    `constitution`=48,
    `intelligence`=30,
    `speed`=38,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=50,
    `earth`=0,
    `physical_attack`=62,
    `physical_defense`=17,
    `magic_attack`=41,
    `magic_defense`=12,
    `experience_reward`=105,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=64
  AND `name_zh_tw`='火甲兵';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=63;keys=2:108:3;0:2:405',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=64;

UPDATE `god2_game`.`monsters`
SET `level`=14,
    `max_hp`=210,
    `max_mp`=98,
    `strength`=118,
    `constitution`=50,
    `intelligence`=28,
    `speed`=50,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=66,
    `physical_defense`=18,
    `magic_attack`=41,
    `magic_defense`=13,
    `experience_reward`=158,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=65
  AND `name_zh_tw`='大灰狐';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=2;source_row_index=20;keys=3:0:1;0:0:11;0:2:22',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=65;

UPDATE `god2_game`.`monsters`
SET `level`=16,
    `max_hp`=191,
    `max_mp`=147,
    `strength`=84,
    `constitution`=44,
    `intelligence`=84,
    `speed`=58,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=100,
    `physical_attack`=58,
    `physical_defense`=17,
    `magic_attack`=58,
    `magic_defense`=19,
    `experience_reward`=187,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=66
  AND `name_zh_tw`='大黃蜂';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=2;source_row_index=21;keys=3:0:2;0:2:529',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=66;

UPDATE `god2_game`.`monsters`
SET `level`=16,
    `max_hp`=1008,
    `max_mp`=205,
    `strength`=50,
    `constitution`=75,
    `intelligence`=150,
    `speed`=50,
    `metal`=100,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=50,
    `physical_defense`=17,
    `magic_attack`=76,
    `magic_defense`=26,
    `experience_reward`=94,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=71
  AND `name_zh_tw`='金蚌精';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=2;source_row_index=33;keys=3:0:7;3:0:8;3:0:9;3:0:10;3:0:11;3:0:12;3:0:13;3:0:14;0:2:160',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=71;

UPDATE `god2_game`.`monsters`
SET `level`=16,
    `max_hp`=4140,
    `max_mp`=174,
    `strength`=100,
    `constitution`=63,
    `intelligence`=100,
    `speed`=62,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=90,
    `physical_defense`=25,
    `magic_attack`=90,
    `magic_defense`=27,
    `experience_reward`=280,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=72
  AND `name_zh_tw`='巨化大蒼蜂';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=2;source_row_index=34;keys=3:0:15',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=72;

UPDATE `god2_game`.`monsters`
SET `level`=16,
    `max_hp`=4780,
    `max_mp`=134,
    `strength`=148,
    `constitution`=73,
    `intelligence`=46,
    `speed`=58,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=103,
    `physical_defense`=29,
    `magic_attack`=76,
    `magic_defense`=22,
    `experience_reward`=280,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=73
  AND `name_zh_tw`='強化大灰狐';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=2;source_row_index=35;keys=3:0:16',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=73;

UPDATE `god2_game`.`monsters`
SET `level`=24,
    `max_hp`=384,
    `max_mp`=147,
    `strength`=190,
    `constitution`=62,
    `intelligence`=42,
    `speed`=72,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=100,
    `physical_attack`=93,
    `physical_defense`=25,
    `magic_attack`=53,
    `magic_defense`=17,
    `experience_reward`=271,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=81
  AND `name_zh_tw`='食土蚤';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=5;source_row_index=74;keys=3:2:1;0:2:524',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=81;

UPDATE `god2_game`.`monsters`
SET `level`=24,
    `max_hp`=406,
    `max_mp`=143,
    `strength`=188,
    `constitution`=70,
    `intelligence`=38,
    `speed`=70,
    `metal`=0,
    `wood`=0,
    `water`=100,
    `fire`=0,
    `earth`=0,
    `physical_attack`=92,
    `physical_defense`=26,
    `magic_attack`=52,
    `magic_defense`=17,
    `experience_reward`=271,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=82
  AND `name_zh_tw`='藍犀獸';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=5;source_row_index=75;keys=3:2:2;12:3:2;0:2:323',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=82;

UPDATE `god2_game`.`monsters`
SET `level`=24,
    `max_hp`=772,
    `max_mp`=216,
    `strength`=116,
    `constitution`=197,
    `intelligence`=111,
    `speed`=62,
    `metal`=0,
    `wood`=100,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=63,
    `physical_defense`=29,
    `magic_attack`=62,
    `magic_defense`=24,
    `experience_reward`=271,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=83
  AND `name_zh_tw`='綠水蛭';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=5;source_row_index=76;keys=3:2:3;8:11:21;0:2:247',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=83;

UPDATE `god2_game`.`monsters`
SET `level`=18,
    `max_hp`=309,
    `max_mp`=120,
    `strength`=148,
    `constitution`=50,
    `intelligence`=36,
    `speed`=60,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=100,
    `physical_attack`=75,
    `physical_defense`=20,
    `magic_attack`=45,
    `magic_defense`=14,
    `experience_reward`=176,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=85
  AND `name_zh_tw`='石猴';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=67;keys=3:5:1;0:2:527',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=85;

UPDATE `god2_game`.`monsters`
SET `level`=16,
    `max_hp`=254,
    `max_mp`=147,
    `strength`=84,
    `constitution`=44,
    `intelligence`=84,
    `speed`=58,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=100,
    `physical_attack`=58,
    `physical_defense`=17,
    `magic_attack`=58,
    `magic_defense`=19,
    `experience_reward`=150,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=86
  AND `name_zh_tw`='長尾蛉';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=68;keys=3:5:2;0:2:530',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=86;

UPDATE `god2_game`.`monsters`
SET `level`=21,
    `max_hp`=369,
    `max_mp`=130,
    `strength`=167,
    `constitution`=64,
    `intelligence`=35,
    `speed`=64,
    `metal`=0,
    `wood`=100,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=86,
    `physical_defense`=24,
    `magic_attack`=50,
    `magic_defense`=16,
    `experience_reward`=220,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=89
  AND `name_zh_tw`='腐木骷髏';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=71;keys=3:7:1;0:2:250',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=89;

UPDATE `god2_game`.`monsters`
SET `level`=21,
    `max_hp`=284,
    `max_mp`=225,
    `strength`=39,
    `constitution`=56,
    `intelligence`=169,
    `speed`=66,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=100,
    `earth`=0,
    `physical_attack`=51,
    `physical_defense`=17,
    `magic_attack`=86,
    `magic_defense`=29,
    `experience_reward`=220,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=91
  AND `name_zh_tw`='攝魂鬼';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=73;keys=3:7:3;0:2:458',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=91;

UPDATE `god2_game`.`monsters`
SET `level`=21,
    `max_hp`=584,
    `max_mp`=165,
    `strength`=190,
    `constitution`=90,
    `intelligence`=60,
    `speed`=60,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=94,
    `physical_defense`=28,
    `magic_attack`=58,
    `magic_defense`=20,
    `experience_reward`=275,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=93
  AND `name_zh_tw`='樹妖';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=3;source_row_index=38;keys=4:0:1;0:2:59',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=93;

UPDATE `god2_game`.`monsters`
SET `level`=24,
    `max_hp`=547,
    `max_mp`=242,
    `strength`=150,
    `constitution`=84,
    `intelligence`=150,
    `speed`=61,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=100,
    `earth`=0,
    `physical_attack`=84,
    `physical_defense`=25,
    `magic_attack`=84,
    `magic_defense`=28,
    `experience_reward`=339,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=94
  AND `name_zh_tw`='紅頂鶴';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=3;source_row_index=39;keys=4:0:2;0:0:193;0:2:411',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=94;

UPDATE `god2_game`.`monsters`
SET `level`=26,
    `max_hp`=458,
    `max_mp`=213,
    `strength`=124,
    `constitution`=64,
    `intelligence`=124,
    `speed`=78,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=100,
    `earth`=0,
    `physical_attack`=76,
    `physical_defense`=22,
    `magic_attack`=76,
    `magic_defense`=26,
    `experience_reward`=387,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=95
  AND `name_zh_tw`='紅花蝶';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=3;source_row_index=40;keys=4:0:3;8:6:8;0:0:23;0:2:428',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=95;

UPDATE `god2_game`.`monsters`
SET `level`=25,
    `max_hp`=667,
    `max_mp`=189,
    `strength`=222,
    `constitution`=102,
    `intelligence`=68,
    `speed`=68,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=100,
    `physical_attack`=104,
    `physical_defense`=31,
    `magic_attack`=62,
    `magic_defense`=21,
    `experience_reward`=362,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=96
  AND `name_zh_tw`='樹精';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=3;source_row_index=41;keys=4:0:4',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=96;

UPDATE `god2_game`.`monsters`
SET `level`=29,
    `max_hp`=587,
    `max_mp`=166,
    `strength`=223,
    `constitution`=80,
    `intelligence`=43,
    `speed`=80,
    `metal`=0,
    `wood`=100,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=104,
    `physical_defense`=29,
    `magic_attack`=55,
    `magic_defense`=18,
    `experience_reward`=468,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=97
  AND `name_zh_tw`='森林矮人';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=3;source_row_index=42;keys=4:0:5;0:2:217',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=97;

UPDATE `god2_game`.`monsters`
SET `level`=24,
    `max_hp`=1575,
    `max_mp`=170,
    `strength`=237,
    `constitution`=89,
    `intelligence`=43,
    `speed`=76,
    `metal`=0,
    `wood`=0,
    `water`=100,
    `fire`=0,
    `earth`=0,
    `physical_attack`=108,
    `physical_defense`=31,
    `magic_attack`=56,
    `magic_defense`=19,
    `experience_reward`=271,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=98
  AND `name_zh_tw`='猛大水鷲';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=3;source_row_index=51;keys=4:0:6;4:0:7;4:0:8;4:0:9;4:0:10;4:0:11;4:0:12;4:0:13;4:0:14',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=98;

UPDATE `god2_game`.`monsters`
SET `level`=25,
    `max_hp`=1170,
    `max_mp`=246,
    `strength`=145,
    `constitution`=90,
    `intelligence`=145,
    `speed`=80,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=115,
    `physical_defense`=32,
    `magic_attack`=115,
    `magic_defense`=35,
    `experience_reward`=434,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=99
  AND `name_zh_tw`='大蝴蝶';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=3;source_row_index=52;keys=4:0:15;13:0:5;0:2:28',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=99;

UPDATE `god2_game`.`monsters`
SET `level`=32,
    `max_hp`=489,
    `max_mp`=321,
    `strength`=50,
    `constitution`=78,
    `intelligence`=246,
    `speed`=88,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=100,
    `earth`=0,
    `physical_attack`=66,
    `physical_defense`=23,
    `magic_attack`=119,
    `magic_defense`=41,
    `experience_reward`=449,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=103
  AND `name_zh_tw`='火燒蚤';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=5;source_row_index=82;keys=4:2:1;0:2:424',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=103;

UPDATE `god2_game`.`monsters`
SET `level`=32,
    `max_hp`=634,
    `max_mp`=179,
    `strength`=244,
    `constitution`=86,
    `intelligence`=46,
    `speed`=86,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=100,
    `physical_attack`=119,
    `physical_defense`=33,
    `magic_attack`=65,
    `magic_defense`=21,
    `experience_reward`=449,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=104
  AND `name_zh_tw`='土螃蟹';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=5;source_row_index=83;keys=4:2:2;0:2:526',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=104;

UPDATE `god2_game`.`monsters`
SET `level`=32,
    `max_hp`=607,
    `max_mp`=183,
    `strength`=246,
    `constitution`=78,
    `intelligence`=50,
    `speed`=88,
    `metal`=0,
    `wood`=100,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=119,
    `physical_defense`=32,
    `magic_attack`=66,
    `magic_defense`=21,
    `experience_reward`=449,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=105
  AND `name_zh_tw`='野猩猩';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=5;source_row_index=84;keys=4:2:3;8:12:10;0:2:227',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=105;

UPDATE `god2_game`.`monsters`
SET `level`=28,
    `max_hp`=440,
    `max_mp`=286,
    `strength`=46,
    `constitution`=70,
    `intelligence`=218,
    `speed`=80,
    `metal`=0,
    `wood`=0,
    `water`=100,
    `fire`=0,
    `earth`=0,
    `physical_attack`=56,
    `physical_defense`=19,
    `magic_attack`=102,
    `magic_defense`=35,
    `experience_reward`=351,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=107
  AND `name_zh_tw`='水蚌精';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=5;source_row_index=78;keys=4:11:1;0:0:191;0:2:360',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=107;

UPDATE `god2_game`.`monsters`
SET `level`=28,
    `max_hp`=539,
    `max_mp`=167,
    `strength`=223,
    `constitution`=70,
    `intelligence`=51,
    `speed`=70,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=100,
    `physical_attack`=103,
    `physical_defense`=28,
    `magic_attack`=57,
    `magic_defense`=18,
    `experience_reward`=351,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=108
  AND `name_zh_tw`='銀霜武士';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=5;source_row_index=79;keys=4:11:2;0:2:515',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=108;

UPDATE `god2_game`.`monsters`
SET `level`=28,
    `max_hp`=541,
    `max_mp`=219,
    `strength`=132,
    `constitution`=85,
    `intelligence`=127,
    `speed`=70,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=79,
    `physical_defense`=25,
    `magic_attack`=77,
    `magic_defense`=26,
    `experience_reward`=351,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=109
  AND `name_zh_tw`='鯨龍';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=5;source_row_index=80;keys=4:11:3;12:1:4',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=109;

UPDATE `god2_game`.`monsters`
SET `level`=26,
    `max_hp`=585,
    `max_mp`=258,
    `strength`=160,
    `constitution`=90,
    `intelligence`=160,
    `speed`=65,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=88,
    `physical_defense`=26,
    `magic_attack`=88,
    `magic_defense`=30,
    `experience_reward`=310,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=111
  AND `name_zh_tw`='小蘑菇';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=54;keys=4:12:1;0:2:2',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=111;

UPDATE `god2_game`.`monsters`
SET `level`=26,
    `max_hp`=512,
    `max_mp`=156,
    `strength`=204,
    `constitution`=66,
    `intelligence`=44,
    `speed`=76,
    `metal`=0,
    `wood`=100,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=98,
    `physical_defense`=27,
    `magic_attack`=54,
    `magic_defense`=18,
    `experience_reward`=310,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=113
  AND `name_zh_tw`='拂塵草';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=4;source_row_index=56;keys=4:12:3;0:0:14;0:2:203',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=113;

UPDATE `god2_game`.`monsters`
SET `level`=36,
    `max_hp`=588,
    `max_mp`=271,
    `strength`=164,
    `constitution`=101,
    `intelligence`=159,
    `speed`=86,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=100,
    `physical_attack`=83,
    `physical_defense`=76,
    `magic_attack`=82,
    `magic_defense`=48,
    `experience_reward`=566,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=115
  AND `name_zh_tw`='大紅花';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=5;source_row_index=87;keys=4:13:2;0:2:508',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=115;

UPDATE `god2_game`.`monsters`
SET `level`=36,
    `max_hp`=1940,
    `max_mp`=303,
    `strength`=164,
    `constitution`=261,
    `intelligence`=159,
    `speed`=86,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=100,
    `earth`=0,
    `physical_attack`=86,
    `physical_defense`=39,
    `magic_attack`=85,
    `magic_defense`=33,
    `experience_reward`=566,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=116
  AND `name_zh_tw`='紅水蛭';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=5;source_row_index=88;keys=4:13:3;8:11:23;0:2:447',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=116;

UPDATE `god2_game`.`monsters`
SET `level`=44,
    `max_hp`=946,
    `max_mp`=400,
    `strength`=240,
    `constitution`=147,
    `intelligence`=240,
    `speed`=118,
    `metal`=0,
    `wood`=200,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=138,
    `physical_defense`=43,
    `magic_attack`=138,
    `magic_defense`=47,
    `experience_reward`=1084,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=119
  AND `name_zh_tw`='食人蜂';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=7;source_row_index=119;keys=5:0:2',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=119;

UPDATE `god2_game`.`monsters`
SET `level`=46,
    `max_hp`=1096,
    `max_mp`=288,
    `strength`=440,
    `constitution`=155,
    `intelligence`=70,
    `speed`=110,
    `metal`=0,
    `wood`=200,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=193,
    `physical_defense`=53,
    `magic_attack`=93,
    `magic_defense`=30,
    `experience_reward`=1198,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=120
  AND `name_zh_tw`='戰魂武士';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=7;source_row_index=120;keys=5:0:3;8:8:7;0:2:215',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=120;

UPDATE `god2_game`.`monsters`
SET `level`=46,
    `max_hp`=1104,
    `max_mp`=315,
    `strength`=390,
    `constitution`=165,
    `intelligence`=110,
    `speed`=110,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=179,
    `physical_defense`=52,
    `magic_attack`=104,
    `magic_defense`=35,
    `experience_reward`=1198,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=121
  AND `name_zh_tw`='棕熊';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=7;source_row_index=121;keys=5:0:4;0:2:41',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=121;

UPDATE `god2_game`.`monsters`
SET `level`=46,
    `max_hp`=11020,
    `max_mp`=314,
    `strength`=388,
    `constitution`=163,
    `intelligence`=106,
    `speed`=118,
    `metal`=200,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=229,
    `physical_defense`=62,
    `magic_attack`=153,
    `magic_defense`=45,
    `experience_reward`=9584,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=123
  AND `name_zh_tw`='黃金熊';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=7;source_row_index=126;keys=5:0:9;0:2:141',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=123;

UPDATE `god2_game`.`monsters`
SET `level`=68,
    `max_hp`=1410,
    `max_mp`=405,
    `strength`=638,
    `constitution`=221,
    `intelligence`=92,
    `speed`=154,
    `metal`=0,
    `wood`=0,
    `water`=300,
    `fire`=0,
    `earth`=0,
    `physical_attack`=337,
    `physical_defense`=84,
    `magic_attack`=115,
    `magic_defense`=43,
    `experience_reward`=3116,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=132
  AND `name_zh_tw`='水靈武士';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=8;source_row_index=166;keys=5:44:3;6:2:23;0:2:315',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=132;

UPDATE `god2_game`.`monsters`
SET `level`=50,
    `max_hp`=1187,
    `max_mp`=339,
    `strength`=422,
    `constitution`=177,
    `intelligence`=118,
    `speed`=118,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=200,
    `physical_attack`=208,
    `physical_defense`=59,
    `magic_attack`=126,
    `magic_defense`=41,
    `experience_reward`=1453,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=135
  AND `name_zh_tw`='流氓熊';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=7;source_row_index=128;keys=6:0:1;0:2:541',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=135;

UPDATE `god2_game`.`monsters`
SET `level`=58,
    `max_hp`=1028,
    `max_mp`=633,
    `strength`=134,
    `constitution`=201,
    `intelligence`=486,
    `speed`=134,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=200,
    `physical_attack`=107,
    `physical_defense`=45,
    `magic_attack`=257,
    `magic_defense`=81,
    `experience_reward`=2080,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=137
  AND `name_zh_tw`='穿山甲';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=7;source_row_index=132;keys=6:0:5;6:12:3;0:0:381;0:0:390',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=137;

UPDATE `god2_game`.`monsters`
SET `level`=62,
    `max_hp`=1063,
    `max_mp`=677,
    `strength`=142,
    `constitution`=203,
    `intelligence`=518,
    `speed`=152,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=126,
    `physical_defense`=50,
    `magic_attack`=302,
    `magic_defense`=92,
    `experience_reward`=2459,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=148
  AND `name_zh_tw`='黑泥怪';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=8;source_row_index=160;keys=6:2:17',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=148;

UPDATE `god2_game`.`monsters`
SET `level`=64,
    `max_hp`=784,
    `max_mp`=566,
    `strength`=350,
    `constitution`=204,
    `intelligence`=350,
    `speed`=141,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=300,
    `earth`=0,
    `physical_attack`=183,
    `physical_defense`=176,
    `magic_attack`=183,
    `magic_defense`=108,
    `experience_reward`=2665,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=149
  AND `name_zh_tw`='食焰蠅';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=8;source_row_index=161;keys=6:2:18',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=149;

UPDATE `god2_game`.`monsters`
SET `level`=64,
    `max_hp`=1094,
    `max_mp`=698,
    `strength`=146,
    `constitution`=209,
    `intelligence`=534,
    `speed`=156,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=300,
    `physical_attack`=128,
    `physical_defense`=51,
    `magic_attack`=308,
    `magic_defense`=94,
    `experience_reward`=2665,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=150
  AND `name_zh_tw`='黃靈蝶';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=8;source_row_index=162;keys=6:2:19;8:6:9;0:2:528',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=150;

UPDATE `god2_game`.`monsters`
SET `level`=66,
    `max_hp`=1366,
    `max_mp`=434,
    `strength`=548,
    `constitution`=223,
    `intelligence`=146,
    `speed`=158,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=312,
    `physical_defense`=80,
    `magic_attack`=129,
    `magic_defense`=49,
    `experience_reward`=2884,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=151
  AND `name_zh_tw`='青魅';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=8;source_row_index=163;keys=6:2:20;0:2:13',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=151;

UPDATE `god2_game`.`monsters`
SET `level`=66,
    `max_hp`=1368,
    `max_mp`=435,
    `strength`=550,
    `constitution`=225,
    `intelligence`=150,
    `speed`=150,
    `metal`=300,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=313,
    `physical_defense`=80,
    `magic_attack`=130,
    `magic_defense`=49,
    `experience_reward`=2884,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=152
  AND `name_zh_tw`='鋼牙怪';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=8;source_row_index=164;keys=6:2:21;0:2:107',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=152;

UPDATE `god2_game`.`monsters`
SET `level`=66,
    `max_hp`=1529,
    `max_mp`=392,
    `strength`=615,
    `constitution`=215,
    `intelligence`=85,
    `speed`=160,
    `metal`=0,
    `wood`=0,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=293,
    `physical_defense`=79,
    `magic_attack`=150,
    `magic_defense`=46,
    `experience_reward`=2884,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=153
  AND `name_zh_tw`='怨靈妖';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=8;source_row_index=165;keys=6:2:22;0:0:393;0:2:6',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=153;

UPDATE `god2_game`.`monsters`
SET `level`=68,
    `max_hp`=1406,
    `max_mp`=447,
    `strength`=566,
    `constitution`=231,
    `intelligence`=154,
    `speed`=154,
    `metal`=0,
    `wood`=300,
    `water`=0,
    `fire`=0,
    `earth`=0,
    `physical_attack`=318,
    `physical_defense`=81,
    `magic_attack`=132,
    `magic_defense`=50,
    `experience_reward`=3116,
    `enabled`=1,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `monster_id`=154
  AND `name_zh_tw`='千森山叉';

UPDATE `god2_research`.`monster_catalog_evidence`
SET `evidence_status`='Derived',
    `admin_note`='17173 公開怪物屬性表與 current client EnyName 名稱對照唯一命中；非官方封包直出，作為相容服務端可執行屬性。page=8;source_row_index=167;keys=6:2:24;0:2:261',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `catalog_table`='monsters' AND `catalog_row_id`=154;

COMMIT;
