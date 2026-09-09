INSERT INTO `god2_game`.`skills`
    (`skill_id`,`code`,`name_zh_tw`,`official_client_item_id`,`official_display_id`,`description_zh_tw`,
     `skill_family`,`skill_category`,`required_level`,`maximum_level`,`mp_cost`,`target_type`,`enabled`)
SELECT
    skill_row.`Id`,
    LEFT(NULLIF(skill_row.`Code`, ''), 100),
    LEFT(COALESCE(NULLIF(skill_row.`NameZhTw`, ''), NULLIF(profile_row.`NameZhTw`, ''), NULLIF(skill_row.`Name`, ''), CONCAT('Skill ', skill_row.`Id`)), 150),
    COALESCE(profile_row.`ClientSkillId`, semantic_row.`ClientSkillId`),
    semantic_row.`ClientSkillId`,
    skill_row.`DescriptionZhTw`,
    COALESCE(NULLIF(skill_row.`SkillFamily`, 'Unknown'), NULLIF(profile_row.`SkillFamily`, 'Unknown'), NULLIF(semantic_row.`SkillFamily`, 'Unknown')),
    semantic_row.`Profession`,
    skill_row.`RequiredLevel`,
    skill_row.`MaxLevel`,
    COALESCE(skill_row.`MpCost`, profile_row.`MpCost`, semantic_row.`MpCost`),
    COALESCE(NULLIF(skill_row.`TargetPolicy`, 'Unknown'), NULLIF(profile_row.`TargetPolicy`, 'Unknown'), NULLIF(semantic_row.`TargetPolicy`, 'Unknown')),
    CASE
        WHEN COALESCE(profile_row.`Enabled`, 0) = 1 OR COALESCE(semantic_row.`ProductionEnabled`, 0) = 1 THEN 1
        ELSE 0
    END
FROM `god2`.`skills` skill_row
LEFT JOIN
(
    SELECT
        `SkillId`,
        MAX(`ClientSkillId`) AS `ClientSkillId`,
        MAX(NULLIF(`NameZhTw`, '')) AS `NameZhTw`,
        MAX(NULLIF(`SkillFamily`, 'Unknown')) AS `SkillFamily`,
        MAX(NULLIF(`TargetPolicy`, 'Unknown')) AS `TargetPolicy`,
        MAX(`MpCost`) AS `MpCost`,
        MAX(`Enabled`) AS `Enabled`
    FROM `god2`.`skill_content_profiles`
    WHERE `SkillId` IS NOT NULL
    GROUP BY `SkillId`
) profile_row
  ON profile_row.`SkillId` = skill_row.`Id`
LEFT JOIN
(
    SELECT
        `SkillId`,
        MAX(`ClientSkillId`) AS `ClientSkillId`,
        MAX(NULLIF(`Profession`, '')) AS `Profession`,
        MAX(NULLIF(`SkillFamily`, 'Unknown')) AS `SkillFamily`,
        MAX(NULLIF(`TargetPolicy`, 'Unknown')) AS `TargetPolicy`,
        MAX(`MpCost`) AS `MpCost`,
        MAX(`ProductionEnabled`) AS `ProductionEnabled`
    FROM `god2`.`skill_semantic_profiles`
    GROUP BY `SkillId`
) semantic_row
  ON semantic_row.`SkillId` = skill_row.`Id`
ON DUPLICATE KEY UPDATE
    `code`=VALUES(`code`),
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `official_client_item_id`=COALESCE(VALUES(`official_client_item_id`), `official_client_item_id`),
    `official_display_id`=COALESCE(VALUES(`official_display_id`), `official_display_id`),
    `description_zh_tw`=COALESCE(VALUES(`description_zh_tw`), `description_zh_tw`),
    `skill_family`=COALESCE(VALUES(`skill_family`), `skill_family`),
    `skill_category`=COALESCE(VALUES(`skill_category`), `skill_category`),
    `required_level`=COALESCE(VALUES(`required_level`), `required_level`),
    `maximum_level`=COALESCE(VALUES(`maximum_level`), `maximum_level`),
    `mp_cost`=COALESCE(VALUES(`mp_cost`), `mp_cost`),
    `target_type`=COALESCE(VALUES(`target_type`), `target_type`),
    `enabled`=CASE WHEN `god2_game`.`skills`.`enabled` = 1 OR VALUES(`enabled`) = 1 THEN 1 ELSE 0 END,
    `updated_at_utc`=UTC_TIMESTAMP(6);
