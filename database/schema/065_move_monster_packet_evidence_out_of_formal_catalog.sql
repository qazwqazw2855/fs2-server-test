CREATE TABLE IF NOT EXISTS `god2`.`verified_monster_observations` (
    `monster_id` int NOT NULL,
    `name_zh_tw` varchar(256) NOT NULL,
    `client_monster_id` int NOT NULL,
    `encounter_local_id` int NOT NULL,
    `level` int NOT NULL,
    `maximum_hp` bigint NULL,
    `experience_reward` bigint NULL,
    `source_session_id` varchar(128) NOT NULL,
    `official_authority_key` varchar(191) NOT NULL,
    `official_source_row` int NOT NULL,
    `battle_entry_frame` varchar(128) NOT NULL,
    `battle_actor_frame` varchar(128) NOT NULL,
    `battle_settlement_frame` varchar(128) NULL,
    `evidence_status` varchar(64) NOT NULL,
    `observed_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`monster_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='封包驗證怪物數值證據；不屬於正式遊戲目錄';

-- Earlier captures created this table from the commit script. Creating an empty
-- compatibility copy makes the migration safe on clean installations as well.
CREATE TABLE IF NOT EXISTS `god2_game`.`verified_monster_observations` LIKE
    `god2`.`verified_monster_observations`;

INSERT INTO `god2`.`verified_monster_observations`
    (`monster_id`,`name_zh_tw`,`client_monster_id`,`encounter_local_id`,`level`,
     `maximum_hp`,`experience_reward`,`source_session_id`,`official_authority_key`,
     `official_source_row`,`battle_entry_frame`,`battle_actor_frame`,
     `battle_settlement_frame`,`evidence_status`,`observed_at_utc`)
SELECT
    `monster_id`,`name_zh_tw`,`client_monster_id`,`encounter_local_id`,`level`,
    `maximum_hp`,`experience_reward`,`source_session_id`,`official_authority_key`,
    `official_source_row`,`battle_entry_frame`,`battle_actor_frame`,
    `battle_settlement_frame`,`evidence_status`,`observed_at_utc`
FROM `god2_game`.`verified_monster_observations`
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `client_monster_id`=VALUES(`client_monster_id`),
    `encounter_local_id`=VALUES(`encounter_local_id`),
    `level`=VALUES(`level`),
    `maximum_hp`=VALUES(`maximum_hp`),
    `experience_reward`=VALUES(`experience_reward`),
    `source_session_id`=VALUES(`source_session_id`),
    `official_authority_key`=VALUES(`official_authority_key`),
    `official_source_row`=VALUES(`official_source_row`),
    `battle_entry_frame`=VALUES(`battle_entry_frame`),
    `battle_actor_frame`=VALUES(`battle_actor_frame`),
    `battle_settlement_frame`=VALUES(`battle_settlement_frame`),
    `evidence_status`=VALUES(`evidence_status`),
    `observed_at_utc`=VALUES(`observed_at_utc`);

DROP TABLE `god2_game`.`verified_monster_observations`;
