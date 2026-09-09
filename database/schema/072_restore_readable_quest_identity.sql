CREATE TEMPORARY TABLE `quest_identity_remap` (
    `old_id` bigint NOT NULL,
    `client_quest_id` bigint NOT NULL,
    PRIMARY KEY (`old_id`),
    UNIQUE KEY `ux_quest_identity_remap_client` (`client_quest_id`)
) ENGINE=InnoDB;

GRANT TRIGGER ON `god2_game`.`portals` TO 'god2_catalog_builder'@'localhost';
GRANT TRIGGER ON `god2_game`.`quests` TO 'god2_catalog_builder'@'localhost';
GRANT TRIGGER ON `god2_game`.`quest_objectives` TO 'god2_catalog_builder'@'localhost';
GRANT TRIGGER ON `god2_game`.`quest_prerequisites` TO 'god2_catalog_builder'@'localhost';
GRANT TRIGGER ON `god2_game`.`quest_rewards` TO 'god2_catalog_builder'@'localhost';
GRANT TRIGGER ON `god2_player`.`character_quests` TO 'god2_catalog_builder'@'localhost';

SET @god2_sync_mode=1;

INSERT INTO `quest_identity_remap` (`old_id`,`client_quest_id`)
SELECT `Id`,CAST(JSON_VALUE(`PayloadJson`,'$.clientQuestId') AS UNSIGNED)
FROM `god2`.`quests`
WHERE JSON_VALUE(`PayloadJson`,'$.clientQuestId') IS NOT NULL
  AND CAST(JSON_VALUE(`PayloadJson`,'$.clientQuestId') AS UNSIGNED)>0
  AND `Id`<>CAST(JSON_VALUE(`PayloadJson`,'$.clientQuestId') AS UNSIGNED);

INSERT INTO `god2`.`quests`
    (`Id`,`Code`,`Name`,`RequiredLevel`,`StartNpcId`,`EndNpcId`,`RecoveryStatus`,`SourceReference`,
     `PayloadJson`,`PayloadSha256`,`ImportedAtUtc`,`OriginalName`,`NameZhTw`,`OriginalDescription`,
     `DescriptionZhTw`,`EvidenceStatus`,`LocalizationStatus`,`ContentRecoveryRunId`)
SELECT remap.`client_quest_id`,CONCAT('quest_',remap.`client_quest_id`),quest_row.`Name`,quest_row.`RequiredLevel`,
       quest_row.`StartNpcId`,quest_row.`EndNpcId`,quest_row.`RecoveryStatus`,quest_row.`SourceReference`,
       quest_row.`PayloadJson`,quest_row.`PayloadSha256`,quest_row.`ImportedAtUtc`,quest_row.`OriginalName`,
       quest_row.`NameZhTw`,quest_row.`OriginalDescription`,quest_row.`DescriptionZhTw`,quest_row.`EvidenceStatus`,
       quest_row.`LocalizationStatus`,quest_row.`ContentRecoveryRunId`
FROM `god2`.`quests` quest_row
JOIN `quest_identity_remap` remap ON remap.`old_id`=quest_row.`Id`
ON DUPLICATE KEY UPDATE
    `Code`=VALUES(`Code`),
    `Name`=VALUES(`Name`),
    `NameZhTw`=VALUES(`NameZhTw`),
    `DescriptionZhTw`=VALUES(`DescriptionZhTw`),
    `PayloadJson`=VALUES(`PayloadJson`),
    `PayloadSha256`=VALUES(`PayloadSha256`);

UPDATE `god2`.`quest_content_profiles` profile_row
JOIN `quest_identity_remap` remap ON remap.`old_id`=profile_row.`QuestId`
SET profile_row.`QuestId`=remap.`client_quest_id`;

INSERT INTO `god2_game`.`quests`
    (`quest_id`,`code`,`name_zh_tw`,`name_original`,`quest_type`,`start_npc_id`,`end_npc_id`,`required_level`,
     `maximum_level`,`repeatable`,`repeat_interval_seconds`,`description_zh_tw`,`completion_text_zh_tw`,
     `evidence_status`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`)
SELECT remap.`client_quest_id`,CONCAT('quest_',remap.`client_quest_id`),quest_row.`name_zh_tw`,
       quest_row.`name_original`,quest_row.`quest_type`,quest_row.`start_npc_id`,quest_row.`end_npc_id`,
       quest_row.`required_level`,quest_row.`maximum_level`,quest_row.`repeatable`,
       quest_row.`repeat_interval_seconds`,quest_row.`description_zh_tw`,quest_row.`completion_text_zh_tw`,
       quest_row.`evidence_status`,quest_row.`enabled`,quest_row.`admin_note`,quest_row.`created_at_utc`,
       quest_row.`updated_at_utc`
FROM `god2_game`.`quests` quest_row
JOIN `quest_identity_remap` remap ON remap.`old_id`=quest_row.`quest_id`
ON DUPLICATE KEY UPDATE
    `code`=VALUES(`code`),
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `name_original`=VALUES(`name_original`),
    `description_zh_tw`=VALUES(`description_zh_tw`),
    `evidence_status`=VALUES(`evidence_status`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

UPDATE `god2_game`.`portals` portal_row
JOIN `quest_identity_remap` remap ON remap.`old_id`=portal_row.`required_quest_id`
SET portal_row.`required_quest_id`=remap.`client_quest_id`;

UPDATE `god2_game`.`quest_objectives` objective_row
JOIN `quest_identity_remap` remap ON remap.`old_id`=objective_row.`quest_id`
SET objective_row.`quest_id`=remap.`client_quest_id`;

UPDATE `god2_game`.`quest_prerequisites` prerequisite_row
JOIN `quest_identity_remap` remap ON remap.`old_id`=prerequisite_row.`required_quest_id`
SET prerequisite_row.`required_quest_id`=remap.`client_quest_id`;

UPDATE `god2_game`.`quest_prerequisites` prerequisite_row
JOIN `quest_identity_remap` remap ON remap.`old_id`=prerequisite_row.`quest_id`
SET prerequisite_row.`quest_id`=remap.`client_quest_id`;

UPDATE `god2_game`.`quest_rewards` reward_row
JOIN `quest_identity_remap` remap ON remap.`old_id`=reward_row.`quest_id`
SET reward_row.`quest_id`=remap.`client_quest_id`;

UPDATE `god2_player`.`character_quests` character_quest_row
JOIN `quest_identity_remap` remap ON remap.`old_id`=character_quest_row.`quest_id`
SET character_quest_row.`quest_id`=remap.`client_quest_id`;

DELETE quest_row
FROM `god2_game`.`quests` quest_row
JOIN `quest_identity_remap` remap ON remap.`old_id`=quest_row.`quest_id`
WHERE remap.`old_id`<>remap.`client_quest_id`;

DELETE quest_row
FROM `god2`.`quests` quest_row
JOIN `quest_identity_remap` remap ON remap.`old_id`=quest_row.`Id`
WHERE remap.`old_id`<>remap.`client_quest_id`;

DROP TEMPORARY TABLE `quest_identity_remap`;

CREATE OR REPLACE VIEW `god2_game`.`vw_quests_readable` AS
SELECT
    quest_row.`quest_id` AS `任務編號`,
    quest_row.`name_zh_tw` AS `任務名稱`,
    quest_row.`quest_type` AS `任務類型`,
    start_npc.`name_zh_tw` AS `起始NPC`,
    end_npc.`name_zh_tw` AS `結束NPC`,
    quest_row.`required_level` AS `需求等級`,
    quest_row.`maximum_level` AS `最高等級`,
    quest_row.`repeatable` AS `可重複`,
    quest_row.`description_zh_tw` AS `任務說明`,
    quest_row.`evidence_status` AS `資料狀態`,
    quest_row.`enabled` AS `啟用`
FROM `god2_game`.`quests` quest_row
LEFT JOIN `god2_game`.`npcs` start_npc ON start_npc.`npc_id`=quest_row.`start_npc_id`
LEFT JOIN `god2_game`.`npcs` end_npc ON end_npc.`npc_id`=quest_row.`end_npc_id`;

SET @god2_sync_mode=0;
