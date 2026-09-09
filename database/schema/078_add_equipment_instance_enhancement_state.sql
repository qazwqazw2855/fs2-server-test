-- Store per-owned-item enhancement state separately from base catalog definitions.

CREATE TABLE IF NOT EXISTS `god2_player`.`equipment_instances` (
    `inventory_id` bigint NOT NULL COMMENT 'Character inventory row and owned item instance',
    `character_id` bigint NOT NULL COMMENT 'Owning character ID',
    `item_id` bigint NOT NULL COMMENT 'Shared item registry ID',
    `catalog_type` varchar(30) NOT NULL COMMENT 'Weapon, Equipment or MagicTreasure',
    `enhancement_level` int NOT NULL DEFAULT 0 COMMENT 'Current enhancement level for this owned instance',
    `refinement_level` int NOT NULL DEFAULT 0 COMMENT 'Current refinement level',
    `current_durability` int NULL COMMENT 'Current durability',
    `maximum_durability` int NULL COMMENT 'Maximum durability after instance modifiers',
    `socket_count` int NOT NULL DEFAULT 0 COMMENT 'Unlocked socket count',
    `metal_bonus` int NULL COMMENT 'Instance metal bonus',
    `wood_bonus` int NULL COMMENT 'Instance wood bonus',
    `water_bonus` int NULL COMMENT 'Instance water bonus',
    `fire_bonus` int NULL COMMENT 'Instance fire bonus',
    `earth_bonus` int NULL COMMENT 'Instance earth bonus',
    `magic_treasure_experience` bigint NULL COMMENT 'Per-instance magic treasure experience',
    `bound` tinyint(1) NULL COMMENT 'Per-instance binding state',
    `enabled` tinyint(1) NOT NULL DEFAULT 1,
    `admin_note` varchar(500) NULL,
    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`inventory_id`),
    KEY `ix_equipment_instances_character` (`character_id`),
    KEY `ix_equipment_instances_item` (`item_id`),
    CONSTRAINT `fk_equipment_instances_inventory` FOREIGN KEY (`inventory_id`) REFERENCES `god2_player`.`character_inventory` (`inventory_id`) ON DELETE CASCADE,
    CONSTRAINT `fk_equipment_instances_character` FOREIGN KEY (`character_id`) REFERENCES `god2_player`.`characters` (`character_id`) ON DELETE CASCADE,
    CONSTRAINT `fk_equipment_instances_registry` FOREIGN KEY (`item_id`) REFERENCES `god2_game`.`item_registry` (`item_id`),
    CONSTRAINT `ck_equipment_instances_catalog_type` CHECK (`catalog_type` IN ('Weapon','Equipment','MagicTreasure')),
    CONSTRAINT `ck_equipment_instances_enhancement` CHECK (`enhancement_level` >= 0 AND `refinement_level` >= 0 AND `socket_count` >= 0),
    CONSTRAINT `ck_equipment_instances_durability` CHECK (`current_durability` IS NULL OR (`current_durability` >= 0 AND `maximum_durability` >= `current_durability`)),
    CONSTRAINT `ck_equipment_instances_enabled` CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Owned weapon, equipment and magic treasure enhancement state';

CREATE OR REPLACE VIEW `god2_player`.`vw_character_equipment_enhancement` AS
SELECT instance_row.`character_id` AS `角色ID`,instance_row.`inventory_id` AS `物品實例ID`,
       definition_row.`client_item_id` AS `客戶端道具ID`,definition_row.`name_zh_tw` AS `裝備名稱`,
       instance_row.`catalog_type` AS `裝備分類`,instance_row.`enhancement_level` AS `目前強化`,
       instance_row.`refinement_level` AS `目前精煉`,instance_row.`current_durability` AS `目前耐久`,
       instance_row.`maximum_durability` AS `最大耐久`,instance_row.`socket_count` AS `鑲嵌槽數`,
       instance_row.`metal_bonus` AS `金加成`,instance_row.`wood_bonus` AS `木加成`,
       instance_row.`water_bonus` AS `水加成`,instance_row.`fire_bonus` AS `火加成`,instance_row.`earth_bonus` AS `土加成`
FROM `god2_player`.`equipment_instances` instance_row
JOIN `god2_game`.`item_registry` definition_row ON definition_row.`item_id` = instance_row.`item_id`;
