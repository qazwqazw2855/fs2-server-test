TRUNCATE TABLE `god2_research`.`container_reward_candidate_rows`;
TRUNCATE TABLE `god2_research`.`equipment_set_member_candidate_rows`;
TRUNCATE TABLE `god2_research`.`monster_drop_candidate_rows`;

INSERT INTO `god2_research`.`container_reward_candidate_rows`
    (`container_item_id`,`container_name_zh_tw`,`reward_item_id`,`reward_item_name_zh_tw`,`quantity`,`declared_probability`,
     `effective_probability`,`relationship_status_zh_tw`,`production_enabled`,`sync_status_zh_tw`,`sync_policy_zh_tw`)
SELECT
    relationship_row.`ContainerItemId`,
    container_item.`name_zh_tw`,
    relationship_row.`ContainedItemId`,
    reward_item.`name_zh_tw`,
    relationship_row.`Quantity`,
    relationship_row.`DeclaredProbability`,
    relationship_row.`EffectiveProbability`,
    relationship_row.`RelationshipStatus`,
    relationship_row.`ProductionRelationshipEnabled`,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM `god2_game`.`containers` formal_container
            JOIN `god2_game`.`container_rewards` formal_reward
              ON formal_reward.`container_id` = formal_container.`container_id`
            WHERE formal_container.`item_id` = relationship_row.`ContainerItemId`
              AND formal_reward.`item_id` = relationship_row.`ContainedItemId`
        ) THEN '已同步正式獎勵'
        ELSE '缺正式獎勵'
    END,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM `god2_game`.`containers` formal_container
            JOIN `god2_game`.`container_rewards` formal_reward
              ON formal_reward.`container_id` = formal_container.`container_id`
            WHERE formal_container.`item_id` = relationship_row.`ContainerItemId`
              AND formal_reward.`item_id` = relationship_row.`ContainedItemId`
        ) THEN '已同步 god2_game.container_rewards；此表只供可讀核對。'
        ELSE '正式獎勵表缺對應列，需檢查同步流程。'
    END
FROM `god2`.`container_item_relationships` relationship_row
LEFT JOIN `god2_game`.`item_registry` container_item
  ON container_item.`item_id` = relationship_row.`ContainerItemId`
LEFT JOIN `god2_game`.`item_registry` reward_item
  ON reward_item.`item_id` = relationship_row.`ContainedItemId`;

INSERT INTO `god2_research`.`equipment_set_member_candidate_rows`
    (`set_id`,`set_name_zh_tw`,`required_pieces`,`set_effects_zh_tw`,`item_id`,`item_name_zh_tw`,`slot_name_zh_tw`,
     `production_relationship_enabled`,`production_bonus_enabled`,`sync_status_zh_tw`,`sync_policy_zh_tw`)
SELECT
    set_row.`SetId`,
    set_row.`NameZhTw`,
    set_row.`RequiredPieces`,
    set_row.`EffectsZhTw`,
    member_row.`ItemId`,
    item_row.`name_zh_tw`,
    member_row.`SlotName`,
    member_row.`ProductionRelationshipEnabled`,
    set_row.`ProductionBonusEnabled`,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM `god2_game`.`item_set_members` formal_member
            WHERE formal_member.`set_id` = member_row.`SetId`
              AND formal_member.`item_id` = member_row.`ItemId`
        ) THEN '已同步正式套裝'
        ELSE '缺正式套裝部件'
    END,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM `god2_game`.`item_set_members` formal_member
            WHERE formal_member.`set_id` = member_row.`SetId`
              AND formal_member.`item_id` = member_row.`ItemId`
        ) THEN '已同步 god2_game.item_sets 與 item_set_members；此表只供可讀核對。'
        ELSE '正式套裝部件表缺對應列，需檢查同步流程。'
    END
FROM `god2`.`equipment_set_members` member_row
JOIN `god2`.`equipment_set_definitions` set_row
  ON set_row.`SetId` = member_row.`SetId`
LEFT JOIN `god2_game`.`item_registry` item_row
  ON item_row.`item_id` = member_row.`ItemId`;

INSERT INTO `god2_research`.`monster_drop_candidate_rows`
    (`legacy_monster_id`,`formal_monster_id`,`monster_name_zh_tw`,`item_id`,`item_name_zh_tw`,`minimum_quantity`,`maximum_quantity`,
     `declared_drop_chance`,`effective_drop_chance`,`drop_group_zh_tw`,`guaranteed`,`drop_relationship_status_zh_tw`,
     `production_enabled`,`sync_status_zh_tw`,`sync_policy_zh_tw`)
SELECT
    relationship_row.`MonsterId`,
    formal_monster.`monster_id`,
    COALESCE(formal_monster.`name_zh_tw`, legacy_monster.`NameZhTw`),
    relationship_row.`ItemId`,
    item_row.`name_zh_tw`,
    relationship_row.`MinimumQuantity`,
    relationship_row.`MaximumQuantity`,
    relationship_row.`DeclaredDropChance`,
    relationship_row.`EffectiveDropChance`,
    COALESCE(relationship_row.`DropGroupId`, relationship_row.`DropEntryId`),
    relationship_row.`Guaranteed`,
    relationship_row.`DropRelationshipStatus`,
    relationship_row.`ProductionDropEnabled`,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM `god2_game`.`monster_drops` formal_drop
            WHERE formal_drop.`monster_id` = formal_monster.`monster_id`
              AND formal_drop.`item_id` = relationship_row.`ItemId`
              AND formal_drop.`drop_source_zh_tw` = 'legacy monster_drop_relationships'
        ) THEN '已同步正式掉落'
        ELSE '缺正式掉落'
    END,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM `god2_game`.`monster_drops` formal_drop
            WHERE formal_drop.`monster_id` = formal_monster.`monster_id`
              AND formal_drop.`item_id` = relationship_row.`ItemId`
              AND formal_drop.`drop_source_zh_tw` = 'legacy monster_drop_relationships'
        ) THEN '已同步 god2_game.monster_drops；此表只供可讀核對。'
        ELSE '正式怪物掉落表缺對應列，需檢查同步流程。'
    END
FROM `god2`.`monster_drop_relationships` relationship_row
LEFT JOIN `god2`.`drop_tables` drop_table
  ON drop_table.`Id` = relationship_row.`DropTableId`
LEFT JOIN `god2`.`monsters` legacy_monster
  ON legacy_monster.`Id` = drop_table.`MonsterId`
LEFT JOIN `god2_game`.`monsters` formal_monster
  ON formal_monster.`code` = legacy_monster.`Code`
LEFT JOIN `god2_game`.`item_registry` item_row
  ON item_row.`item_id` = relationship_row.`ItemId`;
