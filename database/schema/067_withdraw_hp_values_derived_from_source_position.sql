ALTER TABLE `god2`.`verified_monster_observations`
    ADD COLUMN IF NOT EXISTS `validation_note_zh_tw` varchar(500) NULL
        COMMENT '可讀的驗證方式與限制' AFTER `evidence_status`;

SET @god2_sync_mode=1;

UPDATE `god2_game`.`monsters`
SET `max_hp`=NULL,
    `evidence_status`='Verified',
    `admin_note`='已由戰鬥角色描述驗證等級；舊 HP 34 誤將 0x83 動作來源位置視為受擊目標，現已撤回。其餘屬性等待客戶端模板或直接狀態快照。'
WHERE `monster_id`=32
  AND `name_zh_tw`='褐蝸螺';

UPDATE `god2`.`verified_monster_observations`
SET `maximum_hp`=NULL,
    `evidence_status`='VERIFIED_LEVEL_CAPTURE_INCOMPLETE',
    `validation_note_zh_tw`='等級取自戰鬥角色描述；0x83 的位置欄已證實是動作來源，舊 HP 34 因目標關聯錯誤而撤回。'
WHERE `monster_id`=32
  AND `name_zh_tw`='褐蝸螺';

UPDATE `god2_game`.`monsters`
SET `max_hp`=NULL,
    `experience_reward`=73,
    `evidence_status`='Verified',
    `admin_note`='兩隻同模板 Lv6 仙狐戰鬥總經驗 146，驗證每隻經驗 73；HP 尚無直接狀態快照，不寫入推測值。'
WHERE `monster_id`=30
  AND `name_zh_tw`='仙狐';

UPDATE `god2`.`verified_monster_observations`
SET `maximum_hp`=NULL,
    `experience_reward`=73,
    `source_session_id`='monsters-semantic-v2-20260811-112856-3ed58e2d',
    `evidence_status`='VERIFIED_LEVEL_AND_HOMOGENEOUS_ENCOUNTER_EXP',
    `validation_note_zh_tw`='兩隻相同 Lv6 仙狐的結算總經驗為 146，故每隻經驗為 73；傷害總和因末擊可能溢出，不作為最大 HP。'
WHERE `monster_id`=30
  AND `name_zh_tw`='仙狐';

SET @god2_sync_mode=0;
