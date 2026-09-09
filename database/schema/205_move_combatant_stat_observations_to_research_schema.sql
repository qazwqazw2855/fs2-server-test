-- Combatant stat observations are research evidence for formula recovery, not
-- formal gameplay or owner-facing metadata. Keep the structure available for
-- future HP/MP and damage formula evidence, but move it out of god2_game_meta.

CREATE TABLE IF NOT EXISTS `god2_research`.`combatant_stat_observations` (
    `observation_id` bigint NOT NULL AUTO_INCREMENT COMMENT '觀測流水號',
    `session_id` varchar(128) NOT NULL COMMENT '觀測工作階段',
    `entity_type` varchar(30) NOT NULL COMMENT '角色、怪物、寵物、仙道等戰鬥單位類型',
    `entity_id` bigint NOT NULL COMMENT '戰鬥單位識別碼',
    `observed_at_utc` datetime(6) NOT NULL COMMENT '觀測時間（UTC）',
    `level_observed` int NULL COMMENT '觀測等級',
    `current_hp_observed` bigint NULL COMMENT '目前 HP',
    `max_hp_observed` bigint NULL COMMENT '最大 HP',
    `current_mp_observed` bigint NULL COMMENT '目前 MP',
    `max_mp_observed` bigint NULL COMMENT '最大 MP',
    `strength_base_observed` int NULL COMMENT '力量基礎值',
    `strength_bonus_observed` int NULL COMMENT '力量加成值',
    `constitution_base_observed` int NULL COMMENT '體魄基礎值',
    `constitution_bonus_observed` int NULL COMMENT '體魄加成值',
    `intelligence_base_observed` int NULL COMMENT '智慧基礎值',
    `intelligence_bonus_observed` int NULL COMMENT '智慧加成值',
    `speed_base_observed` int NULL COMMENT '身法基礎值',
    `speed_bonus_observed` int NULL COMMENT '身法加成值',
    `metal_observed` int NULL COMMENT '金屬性觀測值',
    `wood_observed` int NULL COMMENT '木屬性觀測值',
    `water_observed` int NULL COMMENT '水屬性觀測值',
    `fire_observed` int NULL COMMENT '火屬性觀測值',
    `earth_observed` int NULL COMMENT '土屬性觀測值',
    `physical_attack_observed` int NULL COMMENT '物理攻擊觀測值',
    `physical_defense_observed` int NULL COMMENT '物理防禦觀測值',
    `magic_attack_observed` int NULL COMMENT '法術攻擊觀測值',
    `magic_defense_observed` int NULL COMMENT '法術防禦觀測值',
    `observation_status` varchar(30) NOT NULL COMMENT '觀測狀態',
    `confidence` decimal(9,6) NULL COMMENT '可信度',
    `source_session_id` varchar(128) NULL COMMENT '來源實機工作階段',
    `source_frame_ids` text NULL COMMENT '來源封包或畫面影格識別',
    `source_hash` char(64) NOT NULL COMMENT '來源 SHA-256',
    PRIMARY KEY (`observation_id`),
    KEY `ix_combatant_stat_observations_entity` (`entity_type`,`entity_id`,`observed_at_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='戰鬥單位 HP、MP、攻防、五行等公式還原觀測證據；非正式遊戲資料';

SET @copy_combatant_stat_observations_sql = IF(
    EXISTS (
        SELECT 1
        FROM `information_schema`.`TABLES`
        WHERE `TABLE_SCHEMA`='god2_game_meta'
          AND `TABLE_NAME`='combatant_stat_observations'
    ),
    'INSERT IGNORE INTO `god2_research`.`combatant_stat_observations`
        (`observation_id`,`session_id`,`entity_type`,`entity_id`,`observed_at_utc`,
         `level_observed`,`current_hp_observed`,`max_hp_observed`,`current_mp_observed`,`max_mp_observed`,
         `strength_base_observed`,`strength_bonus_observed`,`constitution_base_observed`,`constitution_bonus_observed`,
         `intelligence_base_observed`,`intelligence_bonus_observed`,`speed_base_observed`,`speed_bonus_observed`,
         `metal_observed`,`wood_observed`,`water_observed`,`fire_observed`,`earth_observed`,
         `physical_attack_observed`,`physical_defense_observed`,`magic_attack_observed`,`magic_defense_observed`,
         `observation_status`,`confidence`,`source_session_id`,`source_frame_ids`,`source_hash`)
     SELECT
         `observation_id`,`session_id`,`entity_type`,`entity_id`,`observed_at_utc`,
         `level_observed`,`current_hp_observed`,`max_hp_observed`,`current_mp_observed`,`max_mp_observed`,
         `strength_base_observed`,`strength_bonus_observed`,`constitution_base_observed`,`constitution_bonus_observed`,
         `intelligence_base_observed`,`intelligence_bonus_observed`,`speed_base_observed`,`speed_bonus_observed`,
         `metal_observed`,`wood_observed`,`water_observed`,`fire_observed`,`earth_observed`,
         `physical_attack_observed`,`physical_defense_observed`,`magic_attack_observed`,`magic_defense_observed`,
         `observation_status`,`confidence`,`source_session_id`,`source_frame_ids`,`source_hash`
     FROM `god2_game_meta`.`combatant_stat_observations`',
    'DO 0'
);
PREPARE copy_combatant_stat_observations_stmt FROM @copy_combatant_stat_observations_sql;
EXECUTE copy_combatant_stat_observations_stmt;
DEALLOCATE PREPARE copy_combatant_stat_observations_stmt;

DROP TABLE IF EXISTS `god2_game_meta`.`combatant_stat_observations`;
