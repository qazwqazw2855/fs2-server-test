INSERT INTO `god2_game`.`monsters`
    (`monster_id`,`code`,`name_zh_tw`,`level`,`max_hp`,`max_mp`,`physical_attack`,`physical_defense`,
     `experience_reward`,`currency_reward`,`combat_stat_source_zh_tw`,`combat_stat_policy_zh_tw`,`enabled`)
SELECT DISTINCT
    legacy_monster.`Id`,
    legacy_monster.`Code`,
    LEFT(COALESCE(NULLIF(TRIM(legacy_monster.`NameZhTw`),''), NULLIF(TRIM(legacy_monster.`Name`),''), CONCAT('Monster ', legacy_monster.`Id`)), 150),
    legacy_monster.`Level`,
    legacy_monster.`MaxHp`,
    legacy_monster.`MaxMp`,
    legacy_monster.`Attack`,
    legacy_monster.`Defense`,
    legacy_monster.`ExperienceReward`,
    COALESCE(legacy_monster.`CurrencyRewardMaximum`, legacy_monster.`CurrencyRewardMinimum`),
    CASE
        WHEN legacy_monster.`Level` IS NOT NULL OR legacy_monster.`MaxHp` IS NOT NULL OR legacy_monster.`MaxMp` IS NOT NULL
            OR legacy_monster.`Attack` IS NOT NULL OR legacy_monster.`Defense` IS NOT NULL THEN '既有怪物資料同步'
        ELSE '待服務端設計'
    END,
    CASE
        WHEN legacy_monster.`Level` IS NOT NULL OR legacy_monster.`MaxHp` IS NOT NULL OR legacy_monster.`MaxMp` IS NOT NULL
            OR legacy_monster.`Attack` IS NOT NULL OR legacy_monster.`Defense` IS NOT NULL THEN '只同步既有欄位，未證實戰鬥公式不覆蓋'
        ELSE '尚未建立平衡規則'
    END,
    0
FROM `god2`.`monster_drop_relationships` relationship_row
JOIN `god2`.`drop_tables` drop_table
  ON drop_table.`Id` = relationship_row.`DropTableId`
JOIN `god2`.`monsters` legacy_monster
  ON legacy_monster.`Id` = drop_table.`MonsterId`
ON DUPLICATE KEY UPDATE
    `code`=COALESCE(VALUES(`code`), `god2_game`.`monsters`.`code`),
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `level`=COALESCE(VALUES(`level`), `god2_game`.`monsters`.`level`),
    `max_hp`=COALESCE(VALUES(`max_hp`), `god2_game`.`monsters`.`max_hp`),
    `max_mp`=COALESCE(VALUES(`max_mp`), `god2_game`.`monsters`.`max_mp`),
    `physical_attack`=COALESCE(VALUES(`physical_attack`), `god2_game`.`monsters`.`physical_attack`),
    `physical_defense`=COALESCE(VALUES(`physical_defense`), `god2_game`.`monsters`.`physical_defense`),
    `experience_reward`=COALESCE(VALUES(`experience_reward`), `god2_game`.`monsters`.`experience_reward`),
    `currency_reward`=COALESCE(VALUES(`currency_reward`), `god2_game`.`monsters`.`currency_reward`),
    `combat_stat_source_zh_tw`=CASE
        WHEN VALUES(`combat_stat_source_zh_tw`) <> '待服務端設計' THEN VALUES(`combat_stat_source_zh_tw`)
        ELSE `god2_game`.`monsters`.`combat_stat_source_zh_tw`
    END,
    `combat_stat_policy_zh_tw`=CASE
        WHEN VALUES(`combat_stat_policy_zh_tw`) <> '尚未建立平衡規則' THEN VALUES(`combat_stat_policy_zh_tw`)
        ELSE `god2_game`.`monsters`.`combat_stat_policy_zh_tw`
    END,
    `enabled`=CASE WHEN `god2_game`.`monsters`.`enabled`=1 THEN 1 ELSE VALUES(`enabled`) END,
    `updated_at_utc`=UTC_TIMESTAMP(6);

INSERT INTO `god2_game`.`monster_drops`
    (`drop_id`,`monster_id`,`monster_name_cache`,`item_id`,`item_name_cache`,`minimum_quantity`,`maximum_quantity`,
     `drop_rate`,`drop_rate_unit`,`drop_group`,`drop_source_zh_tw`,`drop_policy_zh_tw`,`is_guaranteed`,`enabled`)
SELECT
    CAST(CONV(SUBSTRING(relationship_row.`RelationshipId`, 1, 15), 16, 10) AS unsigned),
    drop_table.`MonsterId`,
    LEFT(COALESCE(formal_monster.`name_zh_tw`, legacy_monster.`NameZhTw`, legacy_monster.`Name`, CONCAT('Monster ', drop_table.`MonsterId`)), 150),
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
  ON formal_monster.`monster_id` = drop_table.`MonsterId`
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
