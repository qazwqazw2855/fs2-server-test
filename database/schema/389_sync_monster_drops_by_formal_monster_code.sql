INSERT INTO `god2_game`.`monster_drops`
    (`drop_id`,`monster_id`,`monster_name_cache`,`item_id`,`item_name_cache`,`minimum_quantity`,`maximum_quantity`,
     `drop_rate`,`drop_rate_unit`,`drop_group`,`drop_source_zh_tw`,`drop_policy_zh_tw`,`is_guaranteed`,`enabled`)
SELECT
    CAST(CONV(SUBSTRING(relationship_row.`RelationshipId`, 1, 15), 16, 10) AS unsigned),
    formal_monster.`monster_id`,
    LEFT(COALESCE(formal_monster.`name_zh_tw`, legacy_monster.`NameZhTw`, legacy_monster.`Name`, CONCAT('Monster ', formal_monster.`monster_id`)), 150),
    relationship_row.`ItemId`,
    LEFT(COALESCE(item_row.`name_zh_tw`, CONCAT('Item ', relationship_row.`ItemId`)), 200),
    relationship_row.`MinimumQuantity`,
    relationship_row.`MaximumQuantity`,
    CASE
        WHEN relationship_row.`DeclaredDropChance` IS NULL THEN NULL
        WHEN relationship_row.`DeclaredDropChance` > 9999.99999999 THEN 9999.99999999
        ELSE relationship_row.`DeclaredDropChance`
    END,
    CASE WHEN relationship_row.`DeclaredDropChance` IS NULL THEN 'Unknown' ELSE 'OfficialEvidence' END,
    LEFT(COALESCE(relationship_row.`DropGroupId`, relationship_row.`DropEntryId`), 50),
    'legacy monster_drop_relationships',
    CASE
        WHEN relationship_row.`DropRelationshipStatus` IN ('Verified','Derived') THEN '既有證據同步'
        ELSE '候選掉落，尚未正式啟用'
    END,
    relationship_row.`Guaranteed`,
    relationship_row.`ProductionDropEnabled`
FROM `god2`.`monster_drop_relationships` relationship_row
JOIN `god2`.`drop_tables` drop_table
  ON drop_table.`Id` = relationship_row.`DropTableId`
JOIN `god2`.`monsters` legacy_monster
  ON legacy_monster.`Id` = drop_table.`MonsterId`
JOIN `god2_game`.`monsters` formal_monster
  ON formal_monster.`code` = legacy_monster.`Code`
JOIN `god2_game`.`item_registry` item_row
  ON item_row.`item_id` = relationship_row.`ItemId`
ON DUPLICATE KEY UPDATE
    `monster_id`=VALUES(`monster_id`),
    `monster_name_cache`=VALUES(`monster_name_cache`),
    `item_id`=VALUES(`item_id`),
    `item_name_cache`=VALUES(`item_name_cache`),
    `minimum_quantity`=VALUES(`minimum_quantity`),
    `maximum_quantity`=VALUES(`maximum_quantity`),
    `drop_rate`=VALUES(`drop_rate`),
    `drop_rate_unit`=VALUES(`drop_rate_unit`),
    `drop_group`=VALUES(`drop_group`),
    `drop_source_zh_tw`=VALUES(`drop_source_zh_tw`),
    `drop_policy_zh_tw`=VALUES(`drop_policy_zh_tw`),
    `is_guaranteed`=VALUES(`is_guaranteed`),
    `enabled`=CASE WHEN `god2_game`.`monster_drops`.`enabled`=1 THEN 1 ELSE VALUES(`enabled`) END,
    `updated_at_utc`=UTC_TIMESTAMP(6);
