CREATE DATABASE IF NOT EXISTS `god2_research`
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`container_reward_candidate_rows` (
    `candidate_row_id` bigint NOT NULL AUTO_INCREMENT COMMENT '可讀候選流水號',
    `container_item_id` bigint NOT NULL COMMENT '容器道具 ID',
    `container_name_zh_tw` varchar(200) NULL COMMENT '容器道具繁體中文名稱',
    `reward_item_id` bigint NOT NULL COMMENT '獎勵道具 ID',
    `reward_item_name_zh_tw` varchar(200) NULL COMMENT '獎勵道具繁體中文名稱',
    `quantity` int NULL COMMENT '數量',
    `declared_probability` decimal(18,9) NULL COMMENT '宣告機率',
    `effective_probability` decimal(18,9) NULL COMMENT '實際機率',
    `relationship_status_zh_tw` varchar(64) NOT NULL COMMENT '關係狀態',
    `production_enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否正式啟用',
    `sync_status_zh_tw` varchar(64) NOT NULL COMMENT '同步判定',
    `sync_policy_zh_tw` varchar(256) NOT NULL COMMENT '同步政策與原因',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`candidate_row_id`),
    KEY `ix_container_reward_candidate_rows_container` (`container_item_id`),
    KEY `ix_container_reward_candidate_rows_reward` (`reward_item_id`),
    KEY `ix_container_reward_candidate_rows_status` (`sync_status_zh_tw`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='容器與禮包獎勵可讀候選列；不包含 relationship hash';

CREATE TABLE IF NOT EXISTS `god2_research`.`equipment_set_member_candidate_rows` (
    `candidate_row_id` bigint NOT NULL AUTO_INCREMENT COMMENT '可讀候選流水號',
    `set_id` bigint NOT NULL COMMENT '套裝 ID',
    `set_name_zh_tw` varchar(200) NULL COMMENT '套裝繁體中文名稱',
    `required_pieces` int NULL COMMENT '需求件數',
    `set_effects_zh_tw` text NULL COMMENT '套裝效果繁體中文描述',
    `item_id` bigint NOT NULL COMMENT '套裝部件道具 ID',
    `item_name_zh_tw` varchar(200) NULL COMMENT '套裝部件道具繁體中文名稱',
    `slot_name_zh_tw` varchar(80) NULL COMMENT '部位名稱',
    `production_relationship_enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '部件關係是否正式啟用',
    `production_bonus_enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '套裝加成是否正式啟用',
    `sync_status_zh_tw` varchar(64) NOT NULL COMMENT '同步判定',
    `sync_policy_zh_tw` varchar(256) NOT NULL COMMENT '同步政策與原因',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`candidate_row_id`),
    KEY `ix_equipment_set_member_candidate_rows_set` (`set_id`),
    KEY `ix_equipment_set_member_candidate_rows_item` (`item_id`),
    KEY `ix_equipment_set_member_candidate_rows_status` (`sync_status_zh_tw`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='裝備套裝部件可讀候選列；不包含 run hash';

CREATE TABLE IF NOT EXISTS `god2_research`.`monster_drop_candidate_rows` (
    `candidate_row_id` bigint NOT NULL AUTO_INCREMENT COMMENT '可讀候選流水號',
    `legacy_monster_id` bigint NOT NULL COMMENT 'legacy 怪物 ID',
    `formal_monster_id` bigint NULL COMMENT '正式怪物 ID',
    `monster_name_zh_tw` varchar(150) NULL COMMENT '怪物繁體中文名稱',
    `item_id` bigint NOT NULL COMMENT '掉落道具 ID',
    `item_name_zh_tw` varchar(200) NULL COMMENT '掉落道具繁體中文名稱',
    `minimum_quantity` int NULL COMMENT '最小數量',
    `maximum_quantity` int NULL COMMENT '最大數量',
    `declared_drop_chance` decimal(18,9) NULL COMMENT '宣告掉落機率',
    `effective_drop_chance` decimal(18,9) NULL COMMENT '實際掉落機率',
    `drop_group_zh_tw` varchar(128) NULL COMMENT '掉落群組',
    `guaranteed` tinyint(1) NULL COMMENT '是否保證掉落',
    `drop_relationship_status_zh_tw` varchar(64) NOT NULL COMMENT '掉落關係狀態',
    `production_enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否正式啟用',
    `sync_status_zh_tw` varchar(64) NOT NULL COMMENT '同步判定',
    `sync_policy_zh_tw` varchar(256) NOT NULL COMMENT '同步政策與原因',
    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '更新 UTC 時間',
    PRIMARY KEY (`candidate_row_id`),
    KEY `ix_monster_drop_candidate_rows_legacy_monster` (`legacy_monster_id`),
    KEY `ix_monster_drop_candidate_rows_formal_monster` (`formal_monster_id`),
    KEY `ix_monster_drop_candidate_rows_item` (`item_id`),
    KEY `ix_monster_drop_candidate_rows_status` (`sync_status_zh_tw`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='怪物掉落可讀候選列；不包含 relationship hash';

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
        WHEN formal_reward.`container_id` IS NOT NULL THEN '已同步正式獎勵'
        ELSE '缺正式獎勵'
    END,
    CASE
        WHEN formal_reward.`container_id` IS NOT NULL THEN '已同步 god2_game.container_rewards；此表只供可讀核對。'
        ELSE '正式獎勵表缺對應列，需檢查同步流程。'
    END
FROM `god2`.`container_item_relationships` relationship_row
LEFT JOIN `god2_game`.`item_registry` container_item
  ON container_item.`item_id` = relationship_row.`ContainerItemId`
LEFT JOIN `god2_game`.`item_registry` reward_item
  ON reward_item.`item_id` = relationship_row.`ContainedItemId`
LEFT JOIN `god2_game`.`containers` formal_container
  ON formal_container.`item_id` = relationship_row.`ContainerItemId`
LEFT JOIN `god2_game`.`container_rewards` formal_reward
  ON formal_reward.`container_id` = formal_container.`container_id`
 AND formal_reward.`item_id` = relationship_row.`ContainedItemId`;

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
        WHEN formal_member.`set_id` IS NOT NULL THEN '已同步正式套裝'
        ELSE '缺正式套裝部件'
    END,
    CASE
        WHEN formal_member.`set_id` IS NOT NULL THEN '已同步 god2_game.item_sets 與 item_set_members；此表只供可讀核對。'
        ELSE '正式套裝部件表缺對應列，需檢查同步流程。'
    END
FROM `god2`.`equipment_set_members` member_row
JOIN `god2`.`equipment_set_definitions` set_row
  ON set_row.`SetId` = member_row.`SetId`
LEFT JOIN `god2_game`.`item_registry` item_row
  ON item_row.`item_id` = member_row.`ItemId`
LEFT JOIN `god2_game`.`item_set_members` formal_member
  ON formal_member.`set_id` = member_row.`SetId`
 AND formal_member.`item_id` = member_row.`ItemId`;

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
        WHEN formal_drop.`drop_id` IS NOT NULL THEN '已同步正式掉落'
        ELSE '缺正式掉落'
    END,
    CASE
        WHEN formal_drop.`drop_id` IS NOT NULL THEN '已同步 god2_game.monster_drops；此表只供可讀核對。'
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
  ON item_row.`item_id` = relationship_row.`ItemId`
LEFT JOIN `god2_game`.`monster_drops` formal_drop
  ON formal_drop.`monster_id` = formal_monster.`monster_id`
 AND formal_drop.`item_id` = relationship_row.`ItemId`;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`container_reward_candidate_rows` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`container_reward_candidate_rows` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`equipment_set_member_candidate_rows` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`equipment_set_member_candidate_rows` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`monster_drop_candidate_rows` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`monster_drop_candidate_rows` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`container_reward_candidate_rows` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`container_reward_candidate_rows` TO 'god2_catalog_builder'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`equipment_set_member_candidate_rows` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`equipment_set_member_candidate_rows` TO 'god2_catalog_builder'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`monster_drop_candidate_rows` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`monster_drop_candidate_rows` TO 'god2_catalog_builder'@'127.0.0.1';
