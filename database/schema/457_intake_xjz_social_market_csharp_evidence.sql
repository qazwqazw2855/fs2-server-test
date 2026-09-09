-- 457_intake_xjz_social_market_csharp_evidence.sql
-- Purpose: ingest C# restored social market service entries, capture targets, playbook routes, and catalog summary.
-- Source pack: XJZ-csharp-evidence-capture-pack-20260818.zip

DROP TABLE IF EXISTS god2_research._xjz_stage_social_market_client_entry_template;
DROP TABLE IF EXISTS god2_research._xjz_stage_social_market_flow_capture_target;
DROP TABLE IF EXISTS god2_research._xjz_stage_social_market_flow_playbook_route;
DROP TABLE IF EXISTS god2_research._xjz_stage_social_market_service_catalog_template;

CREATE TABLE god2_research._xjz_stage_social_market_client_entry_template (
  entry_id VARCHAR(96) PRIMARY KEY,
  entry_kind VARCHAR(64) NOT NULL,
  feature_key VARCHAR(96) NOT NULL,
  feature_name_zh_tw VARCHAR(128) NOT NULL,
  resource_kind VARCHAR(64) NOT NULL,
  resource_id INT NULL,
  resource_path VARCHAR(255) NULL,
  enum_symbol VARCHAR(96) NULL,
  client_command VARCHAR(128) NULL,
  runtime_table VARCHAR(128) NULL,
  sample_table VARCHAR(128) NULL,
  related_warehouse_kind VARCHAR(64) NULL,
  raw_source VARCHAR(255) NOT NULL,
  raw_line INT NULL,
  evidence_status VARCHAR(96) NOT NULL,
  packet_evidence_boundary INT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE god2_research._xjz_stage_social_market_flow_capture_target (
  target_id INT PRIMARY KEY,
  feature_key VARCHAR(96) NOT NULL,
  feature_name_zh_tw VARCHAR(128) NOT NULL,
  flow_kind VARCHAR(96) NOT NULL,
  target_runtime_table VARCHAR(160) NOT NULL,
  target_sample_table VARCHAR(128) NOT NULL,
  related_entry_id VARCHAR(96) NULL,
  related_message_ids_json LONGTEXT NOT NULL,
  capture_priority VARCHAR(32) NOT NULL,
  capture_context VARCHAR(255) NOT NULL,
  wanted_fields_json LONGTEXT NOT NULL,
  request_hint_zh_tw TEXT NOT NULL,
  current_client_source TEXT NOT NULL,
  raw_source VARCHAR(255) NOT NULL,
  raw_line INT NULL,
  evidence_status VARCHAR(96) NOT NULL,
  packet_evidence_boundary INT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE god2_research._xjz_stage_social_market_flow_playbook_route (
  route_binding_id INT PRIMARY KEY,
  target_id INT NOT NULL,
  step_id INT NOT NULL,
  action_key VARCHAR(96) NOT NULL,
  feature_key VARCHAR(96) NOT NULL,
  flow_kind VARCHAR(96) NOT NULL,
  target_runtime_table VARCHAR(160) NOT NULL,
  target_sample_table VARCHAR(128) NOT NULL,
  route_id INT NOT NULL,
  query_id INT NOT NULL,
  required_run_key VARCHAR(96) NOT NULL,
  sample_count_sql TEXT NOT NULL,
  last_sample_id_sql TEXT NULL,
  presence_sql TEXT NOT NULL,
  route_state VARCHAR(64) NOT NULL,
  evidence_status VARCHAR(96) NOT NULL,
  packet_evidence_boundary INT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE god2_research._xjz_stage_social_market_service_catalog_template (
  service_id INT PRIMARY KEY,
  catalog_key VARCHAR(96) NOT NULL,
  client_entry_count INT NOT NULL,
  flow_capture_target_count INT NOT NULL,
  flow_playbook_route_count INT NOT NULL,
  runtime_static_row_count INT NOT NULL,
  entries_json LONGTEXT NOT NULL,
  flow_targets_json LONGTEXT NOT NULL,
  playbook_routes_json LONGTEXT NOT NULL,
  runtime_tables_json LONGTEXT NOT NULL,
  raw_source VARCHAR(255) NOT NULL,
  evidence_status VARCHAR(96) NOT NULL,
  packet_evidence_boundary INT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO god2_research._xjz_stage_social_market_client_entry_template VALUES('auction_ui_rom','ui_flow','auction','拍賣','flow_rom',15,'Data2/Flow/martrom.ROM','ROM_MART','open_auction','auction_listing','auction_flow_sample',NULL,'server_implementation/data/evidence_raw/FieldComm.csv',20,'client_fieldcomm_direct',0);
INSERT INTO god2_research._xjz_stage_social_market_client_entry_template VALUES('stall_vendcar_ui_rom','ui_flow','stall','擺攤','flow_rom',38,'Data2/Flow/VendCar.rom','ROM_VENDCAR','open_stall','stall_listing','stall_flow_sample',NULL,'server_implementation/data/evidence_raw/FieldComm.csv',43,'client_fieldcomm_direct',0);
INSERT INTO god2_research._xjz_stage_social_market_client_entry_template VALUES('combat_pet_bank_ui_rom','ui_flow','combat_pet_bank','戰寵倉庫','flow_rom',40,'Data2/Flow/FightPetBank.rom','ROM_FIGHTPETBANK','open_combat_pet_bank','combat_pet_runtime','warehouse_sync_sample','combat_pet_bank','server_implementation/data/evidence_raw/FieldComm.csv',45,'client_fieldcomm_direct',0);
INSERT INTO god2_research._xjz_stage_social_market_client_entry_template VALUES('amulet_bank_ui_rom','ui_flow','amulet_bank','空間護符','flow_rom',42,'Data2/Flow/AmuletBank.rom','ROM_AMULETBANK','open_amulet_bank','character_warehouse_item','warehouse_sync_sample','amulet_bank','server_implementation/data/evidence_raw/FieldComm.csv',47,'client_fieldcomm_direct',0);
INSERT INTO god2_research._xjz_stage_social_market_client_entry_template VALUES('mail_sound','sound','mail','信箱','wav_sound',429,'Data2/Snd/mail.wav',NULL,'mail_notify','auction_mailbox','auction_mailbox_claim_sample',NULL,'server_implementation/data/evidence_raw/commsnd.csv',432,'client_sound_direct',0);
INSERT INTO god2_research._xjz_stage_social_market_client_entry_template VALUES('trade_ok_sound','sound','direct_trade','交易確認','wav_sound',672,'Data2/Snd/TradeOk.wav',NULL,'trade_confirm','trade_log','direct_trade_flow_sample',NULL,'server_implementation/data/evidence_raw/commsnd.csv',675,'client_sound_direct',0);
INSERT INTO god2_research._xjz_stage_social_market_client_entry_template VALUES('item_place_sound','sound','item_place','道具放置','wav_sound',670,'Data2/Snd/ItemPlace.wav',NULL,'item_place',NULL,NULL,NULL,'server_implementation/data/evidence_raw/commsnd.csv',673,'client_sound_direct',0);
INSERT INTO god2_research._xjz_stage_social_market_client_entry_template VALUES('direct_trade_cursor','cursor','direct_trade','直接交易','ani_cursor',NULL,'cursor/trade.ani',NULL,'direct_trade_cursor','trade_log','direct_trade_flow_sample',NULL,'server_implementation/data/extracted_csvz/god_crc.txt',35,'client_crc_direct',0);
INSERT INTO god2_research._xjz_stage_social_market_flow_capture_target VALUES(1,'auction','拍賣','auction_listing','auction_listing','auction_flow_sample','auction_ui_rom','[495,843]','high','auction_open_list_bid_cancel_close','["auction_id","seller_character_id","item_snapshot","start_price","buyout_price","fee_amount","bid_state","mailbox_result","before_after_inventory_money","raw_packet"]','操作拍賣開啟、上架、出價、取消與結束流程，記錄拍賣、出價、信箱、金錢與原始封包','FieldComm.csv ROM_MART; message 495/843; evidence_reverse_probe auction','server_implementation/data/evidence_raw/FieldComm.csv',20,'official_capture_target',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_capture_target VALUES(2,'stall','擺攤','stall_listing','stall_listing','stall_flow_sample','stall_vendcar_ui_rom','[]','high','stall_open_list_buy_cancel_close','["stall_listing_id","seller_character_id","buyer_character_id","item_id","quantity","unit_price","fee_amount","listing_state","before_after_inventory_money","raw_packet"]','操作擺攤開啟、上架、購買、取消與結束流程，記錄攤位、商品、金錢、背包變化與原始封包','FieldComm.csv ROM_VENDCAR; evidence_reverse_probe stall','server_implementation/data/evidence_raw/FieldComm.csv',43,'official_capture_target',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_capture_target VALUES(3,'direct_trade','直接交易','direct_trade','trade_log','direct_trade_flow_sample','direct_trade_cursor','[47,95,252,263,690,726,736,839]','high','trade_invite_offer_lock_confirm_cancel_complete','["trade_id","character_a_id","character_b_id","offer_a","offer_b","money_a","money_b","lock_state","confirm_state","result_code","before_after_inventory_money","raw_packet"]','操作直接交易邀請、放入物品、鎖定、確認、取消與完成流程，記錄雙方交易內容、金錢、狀態與原始封包','cursor/trade.ani; TradeOk.wav; message 47/95/252/263/690/726/736/839; evidence_reverse_probe direct_trade','server_implementation/data/extracted_csvz/god_crc.txt',35,'official_capture_target',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_capture_target VALUES(4,'mail','信箱','auction_mailbox_claim','auction_mailbox','auction_mailbox_claim_sample','mail_sound','[]','high','mail_notify_claim_item_money','["mailbox_id","character_id","auction_id","mailbox_type","claimed_item","claimed_money","before_after_inventory_money","raw_packet"]','操作信箱通知與領取流程，記錄信件、拍賣來源、領取道具、金錢、背包變化與原始封包','commsnd mail.wav; evidence_reverse_probe mail','server_implementation/data/evidence_raw/commsnd.csv',432,'official_capture_target',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_capture_target VALUES(5,'combat_pet_bank','戰寵倉庫','warehouse_sync','combat_pet_runtime','warehouse_sync_sample','combat_pet_bank_ui_rom','[]','medium','combat_pet_bank_store_retrieve_capacity','["warehouse_kind","owner_character_id","combat_pet_id","slot_index","capacity","operation","before_after_pet_state","raw_packet"]','操作戰寵倉庫存取流程，記錄倉庫種類、戰寵、格位、容量、前後狀態與原始封包','FieldComm.csv ROM_FIGHTPETBANK; evidence_reverse_probe combat_pet_bank','server_implementation/data/evidence_raw/FieldComm.csv',45,'official_capture_target',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_capture_target VALUES(6,'amulet_bank','空間護符','warehouse_sync','character_warehouse_item','warehouse_sync_sample','amulet_bank_ui_rom','[]','medium','amulet_bank_store_retrieve_capacity','["warehouse_kind","owner_character_id","item_id","slot_index","quantity","capacity","operation","before_after_item_state","raw_packet"]','操作空間護符倉庫存取流程，記錄倉庫種類、道具、格位、容量、前後狀態與原始封包','FieldComm.csv ROM_AMULETBANK; evidence_reverse_probe amulet_bank','server_implementation/data/evidence_raw/FieldComm.csv',47,'official_capture_target',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_capture_target VALUES(7,'mentor_relation','師徒','social_relation','mentor_relation','social_relation_flow_sample',NULL,'[500,501,502]','high','mentor_invite_accept_end_list','["mentor_relation_id","mentor_character_id","apprentice_character_id","event_type","requirements","reward","status","before_after_relation_state","raw_packet"]','操作師徒關係流程，記錄邀請、接受、解除、關係狀態與原始封包','message 500/501/502; mentor_relation table; social_relation_flow_sample table','server_implementation/data/evidence_raw/message.csv',2527,'official_capture_target',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_capture_target VALUES(8,'sworn_relation','結拜','social_relation','sworn_group;sworn_group_member','social_relation_flow_sample',NULL,'[516,549,550]','high','sworn_invite_accept_end_group','["sworn_group_id","member_character_ids","rank_no","title","event_type","requirements","status","before_after_relation_state","raw_packet"]','操作結拜關係流程，記錄建立、成員、解除、關係狀態與原始封包','message 516/549/550; sworn_group tables; social_relation_flow_sample table','server_implementation/data/evidence_raw/message.csv',2543,'official_capture_target',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_capture_target VALUES(9,'marriage_relation','結婚','social_relation','marriage_relation','social_relation_flow_sample',NULL,'[634]','high','marriage_create_end','["marriage_id","character_a_id","character_b_id","event_type","requirements","status","before_after_relation_state","raw_packet"]','操作結婚關係流程，記錄求婚、結婚、解除、關係狀態與原始封包','message 634; marriage_relation table; social_relation_flow_sample table','server_implementation/data/evidence_raw/message.csv',2661,'official_capture_target',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_capture_target VALUES(10,'item_trade_permission','道具放置','item_permission','item_template','item_trade_permission_sample','item_place_sound','[690]','medium','item_trade_stall_auction_permission','["item_id","operation","allowed","reason_code","item_flags","context","raw_packet"]','操作道具交易權限或放置流程，記錄道具、交易限制、結果與原始封包','ItemPlace.wav; message 690; item_template trade/stall/auction flags','server_implementation/data/evidence_raw/commsnd.csv',673,'official_capture_target',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_playbook_route VALUES(138,1,12,'market_social_flow','auction','auction_listing','auction_listing','auction_flow_sample',38,38,'capture_session_id','SELECT COUNT(*) FROM auction_flow_sample WHERE capture_session_id = ?1','SELECT MAX(sample_id) FROM auction_flow_sample WHERE capture_session_id = ?1','SELECT CASE WHEN EXISTS (SELECT 1 FROM auction_flow_sample WHERE capture_session_id = ?1) THEN 1 ELSE 0 END','enabled','server_capture_plan',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_playbook_route VALUES(237,2,12,'market_social_flow','stall','stall_listing','stall_listing','stall_flow_sample',37,37,'capture_session_id','SELECT COUNT(*) FROM stall_flow_sample WHERE capture_session_id = ?1','SELECT MAX(sample_id) FROM stall_flow_sample WHERE capture_session_id = ?1','SELECT CASE WHEN EXISTS (SELECT 1 FROM stall_flow_sample WHERE capture_session_id = ?1) THEN 1 ELSE 0 END','enabled','server_capture_plan',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_playbook_route VALUES(336,3,12,'market_social_flow','direct_trade','direct_trade','trade_log','direct_trade_flow_sample',36,36,'capture_session_id','SELECT COUNT(*) FROM direct_trade_flow_sample WHERE capture_session_id = ?1','SELECT MAX(sample_id) FROM direct_trade_flow_sample WHERE capture_session_id = ?1','SELECT CASE WHEN EXISTS (SELECT 1 FROM direct_trade_flow_sample WHERE capture_session_id = ?1) THEN 1 ELSE 0 END','enabled','server_capture_plan',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_playbook_route VALUES(449,4,12,'market_social_flow','mail','auction_mailbox_claim','auction_mailbox','auction_mailbox_claim_sample',49,49,'capture_session_id','SELECT COUNT(*) FROM auction_mailbox_claim_sample WHERE capture_session_id = ?1','SELECT MAX(sample_id) FROM auction_mailbox_claim_sample WHERE capture_session_id = ?1','SELECT CASE WHEN EXISTS (SELECT 1 FROM auction_mailbox_claim_sample WHERE capture_session_id = ?1) THEN 1 ELSE 0 END','enabled','server_capture_plan',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_playbook_route VALUES(522,5,8,'warehouse_open','combat_pet_bank','warehouse_sync','combat_pet_runtime','warehouse_sync_sample',22,22,'capture_session_id','SELECT COUNT(*) FROM warehouse_sync_sample WHERE capture_session_id = ?1','SELECT MAX(sample_id) FROM warehouse_sync_sample WHERE capture_session_id = ?1','SELECT CASE WHEN EXISTS (SELECT 1 FROM warehouse_sync_sample WHERE capture_session_id = ?1) THEN 1 ELSE 0 END','enabled','server_capture_plan',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_playbook_route VALUES(622,6,8,'warehouse_open','amulet_bank','warehouse_sync','character_warehouse_item','warehouse_sync_sample',22,22,'capture_session_id','SELECT COUNT(*) FROM warehouse_sync_sample WHERE capture_session_id = ?1','SELECT MAX(sample_id) FROM warehouse_sync_sample WHERE capture_session_id = ?1','SELECT CASE WHEN EXISTS (SELECT 1 FROM warehouse_sync_sample WHERE capture_session_id = ?1) THEN 1 ELSE 0 END','enabled','server_capture_plan',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_playbook_route VALUES(739,7,12,'market_social_flow','mentor_relation','social_relation','mentor_relation','social_relation_flow_sample',39,39,'capture_session_id','SELECT COUNT(*) FROM social_relation_flow_sample WHERE capture_session_id = ?1','SELECT MAX(sample_id) FROM social_relation_flow_sample WHERE capture_session_id = ?1','SELECT CASE WHEN EXISTS (SELECT 1 FROM social_relation_flow_sample WHERE capture_session_id = ?1) THEN 1 ELSE 0 END','enabled','server_capture_plan',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_playbook_route VALUES(839,8,12,'market_social_flow','sworn_relation','social_relation','sworn_group;sworn_group_member','social_relation_flow_sample',39,39,'capture_session_id','SELECT COUNT(*) FROM social_relation_flow_sample WHERE capture_session_id = ?1','SELECT MAX(sample_id) FROM social_relation_flow_sample WHERE capture_session_id = ?1','SELECT CASE WHEN EXISTS (SELECT 1 FROM social_relation_flow_sample WHERE capture_session_id = ?1) THEN 1 ELSE 0 END','enabled','server_capture_plan',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_playbook_route VALUES(939,9,12,'market_social_flow','marriage_relation','social_relation','marriage_relation','social_relation_flow_sample',39,39,'capture_session_id','SELECT COUNT(*) FROM social_relation_flow_sample WHERE capture_session_id = ?1','SELECT MAX(sample_id) FROM social_relation_flow_sample WHERE capture_session_id = ?1','SELECT CASE WHEN EXISTS (SELECT 1 FROM social_relation_flow_sample WHERE capture_session_id = ?1) THEN 1 ELSE 0 END','enabled','server_capture_plan',1);
INSERT INTO god2_research._xjz_stage_social_market_flow_playbook_route VALUES(1050,10,12,'market_social_flow','item_trade_permission','item_permission','item_template','item_trade_permission_sample',50,50,'capture_session_id','SELECT COUNT(*) FROM item_trade_permission_sample WHERE capture_session_id = ?1','SELECT MAX(sample_id) FROM item_trade_permission_sample WHERE capture_session_id = ?1','SELECT CASE WHEN EXISTS (SELECT 1 FROM item_trade_permission_sample WHERE capture_session_id = ?1) THEN 1 ELSE 0 END','enabled','server_capture_plan',1);
INSERT INTO god2_research._xjz_stage_social_market_service_catalog_template VALUES(1,'social_market_catalog',8,10,10,0,'[{"entry_id":"amulet_bank_ui_rom","entry_kind":"ui_flow","feature_key":"amulet_bank","feature_name_zh_tw":"空間護符","resource_kind":"flow_rom","resource_id":42,"resource_path":"Data2/Flow/AmuletBank.rom","enum_symbol":"ROM_AMULETBANK","client_command":"open_amulet_bank","runtime_table":"character_warehouse_item","sample_table":"warehouse_sync_sample","related_warehouse_kind":"amulet_bank","raw_source":"server_implementation/data/evidence_raw/FieldComm.csv","raw_line":47},{"entry_id":"auction_ui_rom","entry_kind":"ui_flow","feature_key":"auction","feature_name_zh_tw":"拍賣","resource_kind":"flow_rom","resource_id":15,"resource_path":"Data2/Flow/martrom.ROM","enum_symbol":"ROM_MART","client_command":"open_auction","runtime_table":"auction_listing","sample_table":"auction_flow_sample","related_warehouse_kind":null,"raw_source":"server_implementation/data/evidence_raw/FieldComm.csv","raw_line":20},{"entry_id":"combat_pet_bank_ui_rom","entry_kind":"ui_flow","feature_key":"combat_pet_bank","feature_name_zh_tw":"戰寵倉庫","resource_kind":"flow_rom","resource_id":40,"resource_path":"Data2/Flow/FightPetBank.rom","enum_symbol":"ROM_FIGHTPETBANK","client_command":"open_combat_pet_bank","runtime_table":"combat_pet_runtime","sample_table":"warehouse_sync_sample","related_warehouse_kind":"combat_pet_bank","raw_source":"server_implementation/data/evidence_raw/FieldComm.csv","raw_line":45},{"entry_id":"direct_trade_cursor","entry_kind":"cursor","feature_key":"direct_trade","feature_name_zh_tw":"直接交易","resource_kind":"ani_cursor","resource_id":null,"resource_path":"cursor/trade.ani","enum_symbol":null,"client_command":"direct_trade_cursor","runtime_table":"trade_log","sample_table":"direct_trade_flow_sample","related_warehouse_kind":null,"raw_source":"server_implementation/data/extracted_csvz/god_crc.txt","raw_line":35},{"entry_id":"trade_ok_sound","entry_kind":"sound","feature_key":"direct_trade","feature_name_zh_tw":"交易確認","resource_kind":"wav_sound","resource_id":672,"resource_path":"Data2/Snd/TradeOk.wav","enum_symbol":null,"client_command":"trade_confirm","runtime_table":"trade_log","sample_table":"direct_trade_flow_sample","related_warehouse_kind":null,"raw_source":"server_implementation/data/evidence_raw/commsnd.csv","raw_line":675},{"entry_id":"item_place_sound","entry_kind":"sound","feature_key":"item_place","feature_name_zh_tw":"道具放置","resource_kind":"wav_sound","resource_id":670,"resource_path":"Data2/Snd/ItemPlace.wav","enum_symbol":null,"client_command":"item_place","runtime_table":null,"sample_table":null,"related_warehouse_kind":null,"raw_source":"server_implementation/data/evidence_raw/commsnd.csv","raw_line":673},{"entry_id":"mail_sound","entry_kind":"sound","feature_key":"mail","feature_name_zh_tw":"信箱","resource_kind":"wav_sound","resource_id":429,"resource_path":"Data2/Snd/mail.wav","enum_symbol":null,"client_command":"mail_notify","runtime_table":"auction_mailbox","sample_table":"auction_mailbox_claim_sample","related_warehouse_kind":null,"raw_source":"server_implementation/data/evidence_raw/commsnd.csv","raw_line":432},{"entry_id":"stall_vendcar_ui_rom","entry_kind":"ui_flow","feature_key":"stall","feature_name_zh_tw":"擺攤","resource_kind":"flow_rom","resource_id":38,"resource_path":"Data2/Flow/VendCar.rom","enum_symbol":"ROM_VENDCAR","client_command":"open_stall","runtime_table":"stall_listing","sample_table":"stall_flow_sample","related_warehouse_kind":null,"raw_source":"server_implementation/data/evidence_raw/FieldComm.csv","raw_line":43}]','[{"target_id":1,"feature_key":"auction","feature_name_zh_tw":"拍賣","flow_kind":"auction_listing","target_runtime_table":"auction_listing","target_sample_table":"auction_flow_sample","related_entry_id":"auction_ui_rom","related_message_ids":[495,843],"capture_priority":"high","capture_context":"auction_open_list_bid_cancel_close","wanted_fields":["auction_id","seller_character_id","item_snapshot","start_price","buyout_price","fee_amount","bid_state","mailbox_result","before_after_inventory_money","raw_packet"],"request_hint_zh_tw":"操作拍賣開啟、上架、出價、取消與結束流程，記錄拍賣、出價、信箱、金錢與原始封包","raw_source":"server_implementation/data/evidence_raw/FieldComm.csv","raw_line":20},{"target_id":2,"feature_key":"stall","feature_name_zh_tw":"擺攤","flow_kind":"stall_listing","target_runtime_table":"stall_listing","target_sample_table":"stall_flow_sample","related_entry_id":"stall_vendcar_ui_rom","related_message_ids":[],"capture_priority":"high","capture_context":"stall_open_list_buy_cancel_close","wanted_fields":["stall_listing_id","seller_character_id","buyer_character_id","item_id","quantity","unit_price","fee_amount","listing_state","before_after_inventory_money","raw_packet"],"request_hint_zh_tw":"操作擺攤開啟、上架、購買、取消與結束流程，記錄攤位、商品、金錢、背包變化與原始封包","raw_source":"server_implementation/data/evidence_raw/FieldComm.csv","raw_line":43},{"target_id":3,"feature_key":"direct_trade","feature_name_zh_tw":"直接交易","flow_kind":"direct_trade","target_runtime_table":"trade_log","target_sample_table":"direct_trade_flow_sample","related_entry_id":"direct_trade_cursor","related_message_ids":[47,95,252,263,690,726,736,839],"capture_priority":"high","capture_context":"trade_invite_offer_lock_confirm_cancel_complete","wanted_fields":["trade_id","character_a_id","character_b_id","offer_a","offer_b","money_a","money_b","lock_state","confirm_state","result_code","before_after_inventory_money","raw_packet"],"request_hint_zh_tw":"操作直接交易邀請、放入物品、鎖定、確認、取消與完成流程，記錄雙方交易內容、金錢、狀態與原始封包","raw_source":"server_implementation/data/extracted_csvz/god_crc.txt","raw_line":35},{"target_id":4,"feature_key":"mail","feature_name_zh_tw":"信箱","flow_kind":"auction_mailbox_claim","target_runtime_table":"auction_mailbox","target_sample_table":"auction_mailbox_claim_sample","related_entry_id":"mail_sound","related_message_ids":[],"capture_priority":"high","capture_context":"mail_notify_claim_item_money","wanted_fields":["mailbox_id","character_id","auction_id","mailbox_type","claimed_item","claimed_money","before_after_inventory_money","raw_packet"],"request_hint_zh_tw":"操作信箱通知與領取流程，記錄信件、拍賣來源、領取道具、金錢、背包變化與原始封包","raw_source":"server_implementation/data/evidence_raw/commsnd.csv","raw_line":432},{"target_id":5,"feature_key":"combat_pet_bank","feature_name_zh_tw":"戰寵倉庫","flow_kind":"warehouse_sync","target_runtime_table":"combat_pet_runtime","target_sample_table":"warehouse_sync_sample","related_entry_id":"combat_pet_bank_ui_rom","related_message_ids":[],"capture_priority":"medium","capture_context":"combat_pet_bank_store_retrieve_capacity","wanted_fields":["warehouse_kind","owner_character_id","combat_pet_id","slot_index","capacity","operation","before_after_pet_state","raw_packet"],"request_hint_zh_tw":"操作戰寵倉庫存取流程，記錄倉庫種類、戰寵、格位、容量、前後狀態與原始封包","raw_source":"server_implementation/data/evidence_raw/FieldComm.csv","raw_line":45},{"target_id":6,"feature_key":"amulet_bank","feature_name_zh_tw":"空間護符","flow_kind":"warehouse_sync","target_runtime_table":"character_warehouse_item","target_sample_table":"warehouse_sync_sample","related_entry_id":"amulet_bank_ui_rom","related_message_ids":[],"capture_priority":"medium","capture_context":"amulet_bank_store_retrieve_capacity","wanted_fields":["warehouse_kind","owner_character_id","item_id","slot_index","quantity","capacity","operation","before_after_item_state","raw_packet"],"request_hint_zh_tw":"操作空間護符倉庫存取流程，記錄倉庫種類、道具、格位、容量、前後狀態與原始封包","raw_source":"server_implementation/data/evidence_raw/FieldComm.csv","raw_line":47},{"target_id":7,"feature_key":"mentor_relation","feature_name_zh_tw":"師徒","flow_kind":"social_relation","target_runtime_table":"mentor_relation","target_sample_table":"social_relation_flow_sample","related_entry_id":null,"related_message_ids":[500,501,502],"capture_priority":"high","capture_context":"mentor_invite_accept_end_list","wanted_fields":["mentor_relation_id","mentor_character_id","apprentice_character_id","event_type","requirements","reward","status","before_after_relation_state","raw_packet"],"request_hint_zh_tw":"操作師徒關係流程，記錄邀請、接受、解除、關係狀態與原始封包","raw_source":"server_implementation/data/evidence_raw/message.csv","raw_line":2527},{"target_id":8,"feature_key":"sworn_relation","feature_name_zh_tw":"結拜","flow_kind":"social_relation","target_runtime_table":"sworn_group;sworn_group_member","target_sample_table":"social_relation_flow_sample","related_entry_id":null,"related_message_ids":[516,549,550],"capture_priority":"high","capture_context":"sworn_invite_accept_end_group","wanted_fields":["sworn_group_id","member_character_ids","rank_no","title","event_type","requirements","status","before_after_relation_state","raw_packet"],"request_hint_zh_tw":"操作結拜關係流程，記錄建立、成員、解除、關係狀態與原始封包","raw_source":"server_implementation/data/evidence_raw/message.csv","raw_line":2543},{"target_id":9,"feature_key":"marriage_relation","feature_name_zh_tw":"結婚","flow_kind":"social_relation","target_runtime_table":"marriage_relation","target_sample_table":"social_relation_flow_sample","related_entry_id":null,"related_message_ids":[634],"capture_priority":"high","capture_context":"marriage_create_end","wanted_fields":["marriage_id","character_a_id","character_b_id","event_type","requirements","status","before_after_relation_state","raw_packet"],"request_hint_zh_tw":"操作結婚關係流程，記錄求婚、結婚、解除、關係狀態與原始封包","raw_source":"server_implementation/data/evidence_raw/message.csv","raw_line":2661},{"target_id":10,"feature_key":"item_trade_permission","feature_name_zh_tw":"道具放置","flow_kind":"item_permission","target_runtime_table":"item_template","target_sample_table":"item_trade_permission_sample","related_entry_id":"item_place_sound","related_message_ids":[690],"capture_priority":"medium","capture_context":"item_trade_stall_auction_permission","wanted_fields":["item_id","operation","allowed","reason_code","item_flags","context","raw_packet"],"request_hint_zh_tw":"操作道具交易權限或放置流程，記錄道具、交易限制、結果與原始封包","raw_source":"server_implementation/data/evidence_raw/commsnd.csv","raw_line":673}]','[{"route_binding_id":138,"target_id":1,"step_id":12,"action_key":"market_social_flow","feature_key":"auction","flow_kind":"auction_listing","target_runtime_table":"auction_listing","target_sample_table":"auction_flow_sample","route_id":38,"query_id":38,"required_run_key":"capture_session_id","route_state":"enabled"},{"route_binding_id":237,"target_id":2,"step_id":12,"action_key":"market_social_flow","feature_key":"stall","flow_kind":"stall_listing","target_runtime_table":"stall_listing","target_sample_table":"stall_flow_sample","route_id":37,"query_id":37,"required_run_key":"capture_session_id","route_state":"enabled"},{"route_binding_id":336,"target_id":3,"step_id":12,"action_key":"market_social_flow","feature_key":"direct_trade","flow_kind":"direct_trade","target_runtime_table":"trade_log","target_sample_table":"direct_trade_flow_sample","route_id":36,"query_id":36,"required_run_key":"capture_session_id","route_state":"enabled"},{"route_binding_id":449,"target_id":4,"step_id":12,"action_key":"market_social_flow","feature_key":"mail","flow_kind":"auction_mailbox_claim","target_runtime_table":"auction_mailbox","target_sample_table":"auction_mailbox_claim_sample","route_id":49,"query_id":49,"required_run_key":"capture_session_id","route_state":"enabled"},{"route_binding_id":522,"target_id":5,"step_id":8,"action_key":"warehouse_open","feature_key":"combat_pet_bank","flow_kind":"warehouse_sync","target_runtime_table":"combat_pet_runtime","target_sample_table":"warehouse_sync_sample","route_id":22,"query_id":22,"required_run_key":"capture_session_id","route_state":"enabled"},{"route_binding_id":622,"target_id":6,"step_id":8,"action_key":"warehouse_open","feature_key":"amulet_bank","flow_kind":"warehouse_sync","target_runtime_table":"character_warehouse_item","target_sample_table":"warehouse_sync_sample","route_id":22,"query_id":22,"required_run_key":"capture_session_id","route_state":"enabled"},{"route_binding_id":739,"target_id":7,"step_id":12,"action_key":"market_social_flow","feature_key":"mentor_relation","flow_kind":"social_relation","target_runtime_table":"mentor_relation","target_sample_table":"social_relation_flow_sample","route_id":39,"query_id":39,"required_run_key":"capture_session_id","route_state":"enabled"},{"route_binding_id":839,"target_id":8,"step_id":12,"action_key":"market_social_flow","feature_key":"sworn_relation","flow_kind":"social_relation","target_runtime_table":"sworn_group;sworn_group_member","target_sample_table":"social_relation_flow_sample","route_id":39,"query_id":39,"required_run_key":"capture_session_id","route_state":"enabled"},{"route_binding_id":939,"target_id":9,"step_id":12,"action_key":"market_social_flow","feature_key":"marriage_relation","flow_kind":"social_relation","target_runtime_table":"marriage_relation","target_sample_table":"social_relation_flow_sample","route_id":39,"query_id":39,"required_run_key":"capture_session_id","route_state":"enabled"},{"route_binding_id":1050,"target_id":10,"step_id":12,"action_key":"market_social_flow","feature_key":"item_trade_permission","flow_kind":"item_permission","target_runtime_table":"item_template","target_sample_table":"item_trade_permission_sample","route_id":50,"query_id":50,"required_run_key":"capture_session_id","route_state":"enabled"}]','[{"table":"direct_trade_flow_sample","row_count":0},{"table":"trade_log","row_count":0},{"table":"stall_listing","row_count":0},{"table":"stall_flow_sample","row_count":0},{"table":"auction_listing","row_count":0},{"table":"auction_bid","row_count":0},{"table":"auction_mailbox","row_count":0},{"table":"auction_flow_sample","row_count":0},{"table":"auction_mailbox_claim_sample","row_count":0},{"table":"item_trade_permission_sample","row_count":0},{"table":"social_relation_flow_sample","row_count":0},{"table":"marriage_relation","row_count":0},{"table":"mentor_relation","row_count":0},{"table":"sworn_group","row_count":0},{"table":"sworn_group_member","row_count":0}]','social_market_client_entry_template;social_market_flow_capture_target;social_market_flow_playbook_route','client_service_social_market_catalog',1);

CREATE TABLE IF NOT EXISTS god2_research.xjz_social_market_client_entries (
  entry_id VARCHAR(96) NOT NULL,
  entry_kind_zh_tw VARCHAR(96) NOT NULL,
  feature_key VARCHAR(96) NOT NULL,
  feature_name_zh_tw VARCHAR(128) NOT NULL,
  resource_kind_zh_tw VARCHAR(96) NOT NULL,
  resource_id INT NULL,
  resource_path_zh_tw VARCHAR(255) NULL,
  enum_symbol_zh_tw VARCHAR(96) NULL,
  client_command_zh_tw VARCHAR(128) NULL,
  target_runtime_table_zh_tw VARCHAR(128) NULL,
  target_sample_table_zh_tw VARCHAR(128) NULL,
  related_warehouse_kind_zh_tw VARCHAR(64) NULL,
  source_line INT NULL,
  evidence_status_zh_tw VARCHAR(96) NOT NULL,
  needs_packet_evidence INT NOT NULL,
  PRIMARY KEY (entry_id),
  KEY ix_xjz_social_market_feature (feature_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='C#證據包：交易、拍賣、擺攤、信箱、倉庫等玩家互動入口。';

CREATE TABLE IF NOT EXISTS god2_research.xjz_social_market_capture_targets (
  target_id INT NOT NULL,
  feature_key VARCHAR(96) NOT NULL,
  feature_name_zh_tw VARCHAR(128) NOT NULL,
  flow_kind_zh_tw VARCHAR(128) NOT NULL,
  target_runtime_table_zh_tw VARCHAR(160) NOT NULL,
  target_sample_table_zh_tw VARCHAR(128) NOT NULL,
  related_entry_id VARCHAR(96) NULL,
  related_message_ids_zh_tw TEXT NOT NULL,
  capture_priority_zh_tw VARCHAR(32) NOT NULL,
  capture_context_zh_tw VARCHAR(255) NOT NULL,
  wanted_fields_zh_tw TEXT NOT NULL,
  request_hint_zh_tw TEXT NOT NULL,
  current_client_source_zh_tw TEXT NOT NULL,
  source_line INT NULL,
  evidence_status_zh_tw VARCHAR(96) NOT NULL,
  needs_packet_evidence INT NOT NULL,
  PRIMARY KEY (target_id),
  KEY ix_xjz_social_market_target_feature (feature_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='C#證據包：玩家市場與社交關係抓包目標。';

CREATE TABLE IF NOT EXISTS god2_research.xjz_social_market_playbook_routes (
  route_binding_id INT NOT NULL,
  target_id INT NOT NULL,
  step_id INT NOT NULL,
  action_key_zh_tw VARCHAR(96) NOT NULL,
  feature_key VARCHAR(96) NOT NULL,
  flow_kind_zh_tw VARCHAR(128) NOT NULL,
  target_runtime_table_zh_tw VARCHAR(160) NOT NULL,
  target_sample_table_zh_tw VARCHAR(128) NOT NULL,
  route_id INT NOT NULL,
  query_id INT NOT NULL,
  required_run_key_zh_tw VARCHAR(96) NOT NULL,
  route_state_zh_tw VARCHAR(64) NOT NULL,
  evidence_status_zh_tw VARCHAR(96) NOT NULL,
  needs_packet_evidence INT NOT NULL,
  PRIMARY KEY (route_binding_id),
  UNIQUE KEY ux_xjz_social_market_route (target_id, route_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='C#證據包：玩家市場與社交功能抓包 playbook 路線。';

CREATE TABLE IF NOT EXISTS god2_research.xjz_social_market_service_catalog_summary (
  service_id INT NOT NULL,
  catalog_key VARCHAR(96) NOT NULL,
  client_entry_count INT NOT NULL,
  flow_capture_target_count INT NOT NULL,
  flow_playbook_route_count INT NOT NULL,
  runtime_static_row_count INT NOT NULL,
  runtime_tables_zh_tw TEXT NOT NULL,
  evidence_status_zh_tw VARCHAR(96) NOT NULL,
  needs_packet_evidence INT NOT NULL,
  PRIMARY KEY (service_id),
  UNIQUE KEY ux_xjz_social_market_catalog_key (catalog_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='C#證據包：玩家市場服務目錄摘要，不保留原始JSON。';

DELETE FROM god2_research.xjz_social_market_client_entries;
INSERT INTO god2_research.xjz_social_market_client_entries
SELECT
  entry_id,
  CASE entry_kind WHEN 'ui_flow' THEN '介面流程' WHEN 'cursor' THEN '游標' WHEN 'sound' THEN '音效' ELSE entry_kind END,
  feature_key,
  feature_name_zh_tw,
  CASE resource_kind WHEN 'flow_rom' THEN '流程ROM' WHEN 'ani_cursor' THEN '游標動畫' WHEN 'wav_sound' THEN '音效WAV' ELSE resource_kind END,
  resource_id,
  resource_path,
  enum_symbol,
  client_command,
  runtime_table,
  sample_table,
  related_warehouse_kind,
  raw_line,
  evidence_status,
  packet_evidence_boundary
FROM god2_research._xjz_stage_social_market_client_entry_template;

DELETE FROM god2_research.xjz_social_market_capture_targets;
INSERT INTO god2_research.xjz_social_market_capture_targets
SELECT
  target_id,
  feature_key,
  feature_name_zh_tw,
  flow_kind,
  target_runtime_table,
  target_sample_table,
  related_entry_id,
  related_message_ids_json,
  CASE capture_priority WHEN 'high' THEN '高' WHEN 'medium' THEN '中' ELSE capture_priority END,
  capture_context,
  REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(wanted_fields_json, '"raw_packet"', '原始封包'), '"before_after_inventory_money"', '前後背包金錢'), '"seller_character_id"', '賣方角色ID'), '"buyer_character_id"', '買方角色ID'), '"item_id"', '道具ID'), '"quantity"', '數量'),
  request_hint_zh_tw,
  current_client_source,
  raw_line,
  evidence_status,
  packet_evidence_boundary
FROM god2_research._xjz_stage_social_market_flow_capture_target;

DELETE FROM god2_research.xjz_social_market_playbook_routes;
INSERT INTO god2_research.xjz_social_market_playbook_routes
SELECT
  route_binding_id,
  target_id,
  step_id,
  CASE action_key WHEN 'market_social_flow' THEN '市場與社交流程' WHEN 'warehouse_open' THEN '開啟倉庫' ELSE action_key END,
  feature_key,
  flow_kind,
  target_runtime_table,
  target_sample_table,
  route_id,
  query_id,
  required_run_key,
  CASE route_state WHEN 'enabled' THEN '啟用' ELSE route_state END,
  evidence_status,
  packet_evidence_boundary
FROM god2_research._xjz_stage_social_market_flow_playbook_route;

DELETE FROM god2_research.xjz_social_market_service_catalog_summary;
INSERT INTO god2_research.xjz_social_market_service_catalog_summary
SELECT
  service_id,
  catalog_key,
  client_entry_count,
  flow_capture_target_count,
  flow_playbook_route_count,
  runtime_static_row_count,
  'direct_trade_flow_sample, trade_log, stall_listing, stall_flow_sample, auction_listing, auction_bid, auction_mailbox, auction_flow_sample, item_trade_permission_sample, social_relation_flow_sample, marriage_relation, mentor_relation, sworn_group, sworn_group_member',
  evidence_status,
  packet_evidence_boundary
FROM god2_research._xjz_stage_social_market_service_catalog_template;

CREATE OR REPLACE VIEW god2_research.vw_xjz_social_market_csharp_summary_zh_tw AS
SELECT '玩家互動入口' AS 資料類型, COUNT(*) AS 筆數, SUM(needs_packet_evidence) AS 需要封包證據, NULL AS 高優先目標, '交易、拍賣、擺攤、信箱、倉庫入口' AS 遊戲功能
FROM god2_research.xjz_social_market_client_entries
UNION ALL
SELECT '抓包目標', COUNT(*), SUM(needs_packet_evidence), SUM(CASE WHEN capture_priority_zh_tw='高' THEN 1 ELSE 0 END), '直接交易、拍賣、擺攤、信箱、師徒、結拜、結婚、道具交易限制'
FROM god2_research.xjz_social_market_capture_targets
UNION ALL
SELECT 'Playbook路線', COUNT(*), SUM(needs_packet_evidence), NULL, '正式抓包流程可查路線'
FROM god2_research.xjz_social_market_playbook_routes
UNION ALL
SELECT '服務目錄摘要', COUNT(*), SUM(needs_packet_evidence), NULL, '社交市場服務總目錄'
FROM god2_research.xjz_social_market_service_catalog_summary;

UPDATE god2_research.xjz_csharp_zip_evidence_digest
SET current_project_status_zh_tw = '已匯入社交市場入口與抓包路線',
    safe_apply_level_zh_tw = '可用於服務端市場/社交流程補齊；不偽造玩家交易資料',
    next_server_action_zh_tw = '先比對正式服務端 trade、auction、stall、mailbox、marriage、sworn、mentor 的資料表與封包入口，缺失處按抓包目標補流程。',
    blocked_reason_zh_tw = 'C#包提供入口與抓包路線，正式交易資料與費率仍需實測或設計確認。',
    related_project_object_zh_tw = 'migration 457, xjz_social_market_*'
WHERE evidence_key = 'social_market_contracts';

DROP TABLE IF EXISTS god2_research._xjz_stage_social_market_client_entry_template;
DROP TABLE IF EXISTS god2_research._xjz_stage_social_market_flow_capture_target;
DROP TABLE IF EXISTS god2_research._xjz_stage_social_market_flow_playbook_route;
DROP TABLE IF EXISTS god2_research._xjz_stage_social_market_service_catalog_template;