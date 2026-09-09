INSERT INTO `god2_game`.`quests`
    (`quest_id`,`code`,`name_zh_tw`,`start_npc_id`,`end_npc_id`,`required_level`,`description_zh_tw`,`completion_text_zh_tw`,`enabled`)
SELECT
    quest_row.`Id`,
    LEFT(NULLIF(quest_row.`Code`, ''), 100),
    LEFT(COALESCE(NULLIF(quest_row.`NameZhTw`, ''), NULLIF(profile_row.`NameZhTw`, ''), NULLIF(quest_row.`Name`, ''), CONCAT('Quest ', quest_row.`Id`)), 200),
    quest_row.`StartNpcId`,
    quest_row.`EndNpcId`,
    quest_row.`RequiredLevel`,
    COALESCE(NULLIF(quest_row.`DescriptionZhTw`, ''), NULLIF(profile_row.`DescriptionZhTw`, '')),
    profile_row.`RewardTextZhTw`,
    CASE WHEN COALESCE(profile_row.`ProductionProfileEnabled`, 0) = 1 THEN 1 ELSE 0 END
FROM `god2`.`quests` quest_row
LEFT JOIN
(
    SELECT
        `QuestId`,
        MAX(NULLIF(`NameZhTw`, '')) AS `NameZhTw`,
        MAX(NULLIF(`DescriptionZhTw`, '')) AS `DescriptionZhTw`,
        MAX(NULLIF(`RewardTextZhTw`, '')) AS `RewardTextZhTw`,
        MAX(`ProductionProfileEnabled`) AS `ProductionProfileEnabled`
    FROM `god2`.`quest_content_profiles`
    WHERE `QuestId` IS NOT NULL
    GROUP BY `QuestId`
) profile_row
  ON profile_row.`QuestId` = quest_row.`Id`
ON DUPLICATE KEY UPDATE
    `code`=VALUES(`code`),
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `start_npc_id`=COALESCE(VALUES(`start_npc_id`), `start_npc_id`),
    `end_npc_id`=COALESCE(VALUES(`end_npc_id`), `end_npc_id`),
    `required_level`=COALESCE(VALUES(`required_level`), `required_level`),
    `description_zh_tw`=COALESCE(VALUES(`description_zh_tw`), `description_zh_tw`),
    `completion_text_zh_tw`=COALESCE(VALUES(`completion_text_zh_tw`), `completion_text_zh_tw`),
    `enabled`=CASE WHEN `god2_game`.`quests`.`enabled` = 1 OR VALUES(`enabled`) = 1 THEN 1 ELSE 0 END,
    `updated_at_utc`=UTC_TIMESTAMP(6);
