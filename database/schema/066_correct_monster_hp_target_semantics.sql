ALTER TABLE `god2`.`verified_monster_observations`
    MODIFY COLUMN `maximum_hp` bigint NULL,
    MODIFY COLUMN `experience_reward` bigint NULL,
    MODIFY COLUMN `battle_settlement_frame` varchar(128) NULL,
    ADD COLUMN IF NOT EXISTS `validation_note_zh_tw` varchar(500) NULL
        COMMENT '可讀的驗證方式與限制' AFTER `evidence_status`;

SET @god2_sync_mode=1;

UPDATE `god2_game`.`monsters`
SET `max_hp`=NULL,
    `evidence_status`='Verified',
    `admin_note`='單怪戰鬥已驗證等級與經驗；舊版 HP 曾誤用角色受傷位置，已撤回並等待怪物位置完整終結鏈。'
WHERE `monster_id`=30
  AND `name_zh_tw`='仙狐'
  AND `level`=6
  AND `max_hp`=126
  AND `experience_reward`=73;

UPDATE `god2`.`verified_monster_observations`
SET `maximum_hp`=NULL,
    `evidence_status`='VERIFIED_LEVEL_AND_SINGLE_MONSTER_EXP',
    `validation_note_zh_tw`='等級取自戰鬥角色描述，經驗取自單怪結算；舊 HP 誤用了角色受傷位置，已撤回。'
WHERE `monster_id`=30
  AND `name_zh_tw`='仙狐'
  AND `maximum_hp`=126;

SET @god2_sync_mode=0;
