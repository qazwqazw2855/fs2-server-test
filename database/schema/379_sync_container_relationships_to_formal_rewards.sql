CREATE TABLE IF NOT EXISTS `god2_game`.`containers`
(
    `container_id` bigint NOT NULL,
    `item_id` bigint NOT NULL,
    `name_zh_tw` varchar(200) NOT NULL,
    `roll_count` int NULL,
    `enabled` tinyint(1) NOT NULL DEFAULT 0,
    `admin_note` varchar(500) NULL,
    PRIMARY KEY (`container_id`),
    UNIQUE KEY `ux_containers_item_id` (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_game`.`container_rewards`
(
    `container_id` bigint NOT NULL,
    `reward_order` int NOT NULL,
    `item_id` bigint NOT NULL,
    `item_name_cache` varchar(200) NULL,
    `minimum_quantity` int NULL,
    `maximum_quantity` int NULL,
    `reward_probability` decimal(12,8) NULL,
    `reward_group` varchar(50) NULL,
    `enabled` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`container_id`,`reward_order`),
    KEY `ix_container_rewards_item_id` (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_game`.`containers`
    (`container_id`,`item_id`,`name_zh_tw`,`roll_count`,`enabled`,`admin_note`)
SELECT
    relationship_row.`ContainerItemId`,
    relationship_row.`ContainerItemId`,
    LEFT(COALESCE(container_item.`name_zh_tw`, CONCAT('Container ', relationship_row.`ContainerItemId`)), 200),
    NULL,
    MAX(relationship_row.`ProductionRelationshipEnabled`),
    CONCAT('由 legacy container_item_relationships 同步；關係數=', COUNT(*))
FROM `god2`.`container_item_relationships` relationship_row
LEFT JOIN `god2_game`.`item_registry` container_item
  ON container_item.`item_id` = relationship_row.`ContainerItemId`
JOIN `god2_game`.`item_registry` contained_item
  ON contained_item.`item_id` = relationship_row.`ContainedItemId`
GROUP BY relationship_row.`ContainerItemId`, container_item.`name_zh_tw`
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_game`.`container_rewards`
    (`container_id`,`reward_order`,`item_id`,`item_name_cache`,`minimum_quantity`,`maximum_quantity`,`reward_probability`,`reward_group`,`enabled`)
SELECT
    ranked_row.`ContainerItemId`,
    ranked_row.`RewardOrder`,
    ranked_row.`ContainedItemId`,
    LEFT(COALESCE(reward_item.`name_zh_tw`, CONCAT('Item ', ranked_row.`ContainedItemId`)), 200),
    ranked_row.`Quantity`,
    ranked_row.`Quantity`,
    CASE
        WHEN ranked_row.`DeclaredProbability` IS NULL THEN NULL
        WHEN ranked_row.`DeclaredProbability` > 9999.99999999 THEN 9999.99999999
        ELSE ranked_row.`DeclaredProbability`
    END,
    ranked_row.`RelationshipStatus`,
    ranked_row.`ProductionRelationshipEnabled`
FROM
(
    SELECT
        relationship_row.`ContainerItemId`,
        relationship_row.`ContainedItemId`,
        relationship_row.`Quantity`,
        relationship_row.`DeclaredProbability`,
        relationship_row.`RelationshipStatus`,
        relationship_row.`ProductionRelationshipEnabled`,
        ROW_NUMBER() OVER (
            PARTITION BY relationship_row.`ContainerItemId`
            ORDER BY relationship_row.`RelationshipId`
        ) AS `RewardOrder`
    FROM `god2`.`container_item_relationships` relationship_row
) ranked_row
JOIN `god2_game`.`containers` container_row
  ON container_row.`container_id` = ranked_row.`ContainerItemId`
JOIN `god2_game`.`item_registry` reward_item
  ON reward_item.`item_id` = ranked_row.`ContainedItemId`
ON DUPLICATE KEY UPDATE
    `item_id`=VALUES(`item_id`),
    `item_name_cache`=VALUES(`item_name_cache`),
    `minimum_quantity`=VALUES(`minimum_quantity`),
    `maximum_quantity`=VALUES(`maximum_quantity`),
    `reward_probability`=VALUES(`reward_probability`),
    `reward_group`=VALUES(`reward_group`),
    `enabled`=VALUES(`enabled`);
