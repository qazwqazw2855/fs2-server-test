START TRANSACTION;

UPDATE `god2`.`verified_monster_observations`
SET `source_session_id`='monsters-object-v6-20260811-123941-0d46ca9e',
    `battle_entry_frame`='injected-4120-1288',
    `battle_actor_frame`='injected-4120-1312',
    `battle_settlement_frame`='injected-4120-1877',
    `maximum_hp`=NULL,
    `experience_reward`=NULL,
    `evidence_status`='VERIFIED_LEVEL_AND_OBJECT_SNAPSHOT_INCOMPLETE',
    `validation_note_zh_tw`='最新版物件快照與 0x1C 封包共同驗證褐蝸螺 Lv10；多怪戰鬥總經驗 283 不分配到單隻。HP、MP、能力、五行與技能尚未出現在已驗證欄位。',
    `observed_at_utc`='2026-08-11 04:41:20.049000'
WHERE `monster_id`=32 AND `name_zh_tw`='褐蝸螺';

UPDATE `god2`.`verified_monster_observations`
SET `source_session_id`='monsters-object-v6-20260811-123941-0d46ca9e',
    `battle_entry_frame`='injected-4120-872',
    `battle_actor_frame`='injected-4120-890',
    `battle_settlement_frame`='injected-4120-1136',
    `maximum_hp`=NULL,
    `experience_reward`=156,
    `evidence_status`='VERIFIED_LEVEL_SINGLE_EXP_AND_OBJECT_SNAPSHOT_INCOMPLETE',
    `validation_note_zh_tw`='最新版物件快照與 0x1C 封包共同驗證雲鶴 Lv12；單怪戰鬥 0x89 結算精準驗證經驗 156。HP、MP、能力、五行與技能尚未出現在已驗證欄位。',
    `observed_at_utc`='2026-08-11 04:40:51.411000'
WHERE `monster_id`=35 AND `name_zh_tw`='雲鶴';

SET @god2_sync_mode=1;

UPDATE `god2_game`.`monsters`
SET `level`=10,
    `max_hp`=NULL,
    `max_mp`=NULL,
    `strength`=NULL,
    `constitution`=NULL,
    `intelligence`=NULL,
    `speed`=NULL,
    `metal`=NULL,
    `wood`=NULL,
    `water`=NULL,
    `fire`=NULL,
    `earth`=NULL,
    `physical_attack`=NULL,
    `physical_defense`=NULL,
    `magic_attack`=NULL,
    `magic_defense`=NULL,
    `experience_reward`=NULL,
    `evidence_status`='Verified',
    `enabled`=0,
    `admin_note`='最新版物件快照與封包驗證褐蝸螺 Lv10；其餘能力值未驗證，不保留舊值。'
WHERE `monster_id`=32 AND `name_zh_tw`='褐蝸螺';

UPDATE `god2_game`.`monsters`
SET `level`=12,
    `max_hp`=NULL,
    `max_mp`=NULL,
    `strength`=NULL,
    `constitution`=NULL,
    `intelligence`=NULL,
    `speed`=NULL,
    `metal`=NULL,
    `wood`=NULL,
    `water`=NULL,
    `fire`=NULL,
    `earth`=NULL,
    `physical_attack`=NULL,
    `physical_defense`=NULL,
    `magic_attack`=NULL,
    `magic_defense`=NULL,
    `experience_reward`=156,
    `evidence_status`='Verified',
    `enabled`=0,
    `admin_note`='最新版物件快照與封包驗證雲鶴 Lv12、單怪經驗 156；其餘能力值未驗證，不保留舊值。'
WHERE `monster_id`=35 AND `name_zh_tw`='雲鶴';

SET @god2_sync_mode=0;

COMMIT;
