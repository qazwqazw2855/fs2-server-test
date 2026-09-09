UPDATE `god2`.`verified_monster_observations`
SET `source_session_id`='monsters-object-v3-20260811-120416-1eadb870',
    `battle_entry_frame`='injected-4120-367',
    `battle_actor_frame`='injected-4120-378',
    `battle_settlement_frame`='injected-4120-906',
    `maximum_hp`=NULL,
    `experience_reward`=NULL,
    `observed_at_utc`=UTC_TIMESTAMP(6),
    `validation_note_zh_tw`='最新雙怪戰鬥再次驗證褐蝸螺為 Lv10；整場總經驗 283 無法逐怪分配，HP、MP、能力、五行與技能等待建構完成後的只讀物件快照。'
WHERE `monster_id`=32 AND `name_zh_tw`='褐蝸螺';

UPDATE `god2`.`verified_monster_observations`
SET `source_session_id`='monsters-object-v3-20260811-120416-1eadb870',
    `battle_entry_frame`='injected-4120-367',
    `battle_actor_frame`='injected-4120-382',
    `battle_settlement_frame`='injected-4120-906',
    `maximum_hp`=NULL,
    `experience_reward`=NULL,
    `observed_at_utc`=UTC_TIMESTAMP(6),
    `validation_note_zh_tw`='最新雙怪戰鬥再次驗證雲鶴為 Lv12；整場總經驗 283 無法逐怪分配，HP、MP、能力、五行與技能等待建構完成後的只讀物件快照。'
WHERE `monster_id`=35 AND `name_zh_tw`='雲鶴';

SET @god2_sync_mode=1;

UPDATE `god2_game`.`monsters`
SET `level`=10,
    `max_hp`=NULL,
    `max_mp`=NULL,
    `experience_reward`=NULL,
    `evidence_status`='Verified',
    `admin_note`='最新雙怪戰鬥再次驗證褐蝸螺為 Lv10；整場總經驗 283 不逐怪分配，其餘欄位等待建構完成後的只讀物件快照。'
WHERE `monster_id`=32 AND `name_zh_tw`='褐蝸螺';

UPDATE `god2_game`.`monsters`
SET `level`=12,
    `max_hp`=NULL,
    `max_mp`=NULL,
    `experience_reward`=NULL,
    `evidence_status`='Verified',
    `admin_note`='最新雙怪戰鬥再次驗證雲鶴為 Lv12；整場總經驗 283 不逐怪分配，其餘欄位等待建構完成後的只讀物件快照。'
WHERE `monster_id`=35 AND `name_zh_tw`='雲鶴';

SET @god2_sync_mode=0;
