UPDATE `god2`.`verified_monster_observations`
SET `source_session_id`='monsters-object-v2-20260811-115745-72fba55c',
    `battle_entry_frame`='injected-4120-905',
    `battle_actor_frame`='injected-4120-920',
    `battle_settlement_frame`='injected-4120-1428',
    `observed_at_utc`=UTC_TIMESTAMP(6),
    `validation_note_zh_tw`='最新雙怪戰鬥再次驗證褐蝸螺為 Lv10；兩怪總經驗 283 不逐怪分配，其餘欄位等待直接狀態快照。'
WHERE `monster_id`=32 AND `name_zh_tw`='褐蝸螺';

UPDATE `god2`.`verified_monster_observations`
SET `source_session_id`='monsters-object-v2-20260811-115745-72fba55c',
    `battle_entry_frame`='injected-4120-905',
    `battle_actor_frame`='injected-4120-924',
    `battle_settlement_frame`='injected-4120-1428',
    `observed_at_utc`=UTC_TIMESTAMP(6),
    `validation_note_zh_tw`='最新雙怪戰鬥再次驗證雲鶴為 Lv12；兩怪總經驗 283 不逐怪分配，其餘欄位等待直接狀態快照。'
WHERE `monster_id`=35 AND `name_zh_tw`='雲鶴';
