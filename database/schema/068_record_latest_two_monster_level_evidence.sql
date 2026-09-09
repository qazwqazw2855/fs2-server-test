SET @god2_sync_mode=1;

UPDATE `god2_game`.`monsters`
SET `level`=10,
    `max_hp`=NULL,
    `experience_reward`=NULL,
    `evidence_status`='Verified',
    `admin_note`='最新雙怪戰鬥再次驗證褐蝸螺為 Lv10；兩怪結算總經驗 283 無法逐怪拆分，HP、MP、能力、五行與技能等待直接狀態快照。'
WHERE `monster_id`=32 AND `name_zh_tw`='褐蝸螺';

UPDATE `god2_game`.`monsters`
SET `level`=12,
    `max_hp`=NULL,
    `experience_reward`=NULL,
    `evidence_status`='Verified',
    `admin_note`='最新雙怪戰鬥再次驗證雲鶴為 Lv12；兩怪結算總經驗 283 無法逐怪拆分，HP、MP、能力、五行與技能等待直接狀態快照。'
WHERE `monster_id`=35 AND `name_zh_tw`='雲鶴';

INSERT INTO `god2`.`verified_monster_observations`
    (`monster_id`,`name_zh_tw`,`client_monster_id`,`encounter_local_id`,`level`,
     `maximum_hp`,`experience_reward`,`source_session_id`,`official_authority_key`,
     `official_source_row`,`battle_entry_frame`,`battle_actor_frame`,
     `battle_settlement_frame`,`evidence_status`,`validation_note_zh_tw`,`observed_at_utc`)
VALUES
    (32,'褐蝸螺',1,6,10,NULL,NULL,
     'monsters-full-state-v1-20260811-114432-8d6462fb',
     'client:encounter-name/6:23505',23505,
     'injected-4120-1415','injected-4120-1430','injected-4120-1968',
     'VERIFIED_LEVEL_CAPTURE_INCOMPLETE',
     '本場再次驗證 Lv10；兩怪總經驗 283 不逐怪分配，其餘欄位等待直接狀態快照。',UTC_TIMESTAMP(6)),
    (35,'雲鶴',14,9,12,NULL,NULL,
     'monsters-full-state-v1-20260811-114432-8d6462fb',
     'client:encounter-name/9:23508',23508,
     'injected-4120-1415','injected-4120-1434','injected-4120-1968',
     'VERIFIED_LEVEL_CAPTURE_INCOMPLETE',
     '本場再次驗證 Lv12；兩怪總經驗 283 不逐怪分配，其餘欄位等待直接狀態快照。',UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    `client_monster_id`=VALUES(`client_monster_id`),
    `encounter_local_id`=VALUES(`encounter_local_id`),
    `level`=VALUES(`level`),
    `maximum_hp`=NULL,
    `experience_reward`=NULL,
    `source_session_id`=VALUES(`source_session_id`),
    `official_authority_key`=VALUES(`official_authority_key`),
    `official_source_row`=VALUES(`official_source_row`),
    `battle_entry_frame`=VALUES(`battle_entry_frame`),
    `battle_actor_frame`=VALUES(`battle_actor_frame`),
    `battle_settlement_frame`=VALUES(`battle_settlement_frame`),
    `evidence_status`=VALUES(`evidence_status`),
    `validation_note_zh_tw`=VALUES(`validation_note_zh_tw`),
    `observed_at_utc`=VALUES(`observed_at_utc`);

SET @god2_sync_mode=0;
