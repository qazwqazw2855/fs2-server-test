INSERT INTO `god2_game`.`item_sets`
    (`set_id`,`name_zh_tw`,`description_zh_tw`,`enabled`,`admin_note`)
SELECT
    staging_row.`SetId`,
    staging_row.`NameZhTw`,
    NULLIF(staging_row.`EffectsZhTw`, ''),
    staging_row.`ProductionRelationshipEnabled`,
    CONCAT('由 legacy equipment_set_definitions 同步；bonus_enabled=', staging_row.`ProductionBonusEnabled`, '; status=', staging_row.`SetRelationshipStatus`)
FROM `god2`.`equipment_set_definitions` staging_row
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `description_zh_tw`=VALUES(`description_zh_tw`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`),
    `updated_at_utc`=UTC_TIMESTAMP(6);

INSERT INTO `god2_game`.`item_set_members`
    (`set_id`,`item_id`,`slot_name`,`enabled`,`admin_note`)
SELECT
    member_row.`SetId`,
    member_row.`ItemId`,
    NULLIF(member_row.`SlotName`, ''),
    member_row.`ProductionRelationshipEnabled`,
    CONCAT('由 legacy equipment_set_members 同步；promotion_run=', COALESCE(member_row.`PromotionRunId`, 'none'))
FROM `god2`.`equipment_set_members` member_row
JOIN `god2_game`.`item_sets` set_row
  ON set_row.`set_id` = member_row.`SetId`
JOIN `god2_game`.`item_registry` item_row
  ON item_row.`item_id` = member_row.`ItemId`
ON DUPLICATE KEY UPDATE
    `slot_name`=VALUES(`slot_name`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);
