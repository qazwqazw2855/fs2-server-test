SET @god2_sync_mode = 1;

CREATE TABLE IF NOT EXISTS `god2_research`.`unused_unmapped_skill_shell_archive` (
    `skill_id` bigint NOT NULL,
    `code` varchar(100) NULL,
    `name_zh_tw` varchar(256) NULL,
    `description_zh_tw` varchar(500) NULL,
    `skill_family` varchar(50) NULL,
    `skill_category` varchar(50) NULL,
    `damage_type` varchar(30) NULL,
    `element` varchar(20) NULL,
    `required_level` int NULL,
    `maximum_level` int NULL,
    `mp_cost` int NULL,
    `target_type` varchar(50) NULL,
    `target_scope_zh_tw` varchar(256) NULL,
    `enabled` tinyint(1) NOT NULL,
    `archive_reason_zh_tw` varchar(500) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`skill_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='未啟用、未對照、未被正式內容引用的技能空殼封存';

CREATE TEMPORARY TABLE `god2_unused_unmapped_skill_shells` (
    `skill_id` bigint NOT NULL PRIMARY KEY
) ENGINE=Memory;

INSERT INTO `god2_unused_unmapped_skill_shells` (`skill_id`)
SELECT skill_row.`skill_id`
FROM `god2_game`.`skills` skill_row
WHERE skill_row.`enabled`=0
  AND NOT EXISTS (
      SELECT 1 FROM `god2_game`.`skill_client_metadata_mappings` mapping
      WHERE mapping.`skill_id`=skill_row.`skill_id`
  )
  AND NOT EXISTS (SELECT 1 FROM `god2_game`.`monster_skills` x WHERE x.`skill_id`=skill_row.`skill_id`)
  AND NOT EXISTS (SELECT 1 FROM `god2_game`.`monster_ai_rules` x WHERE x.`skill_id`=skill_row.`skill_id`)
  AND NOT EXISTS (SELECT 1 FROM `god2_game`.`pet_template_skills` x WHERE x.`skill_id`=skill_row.`skill_id`)
  AND NOT EXISTS (SELECT 1 FROM `god2_game`.`pet_skill_learning_items` x WHERE x.`skill_id`=skill_row.`skill_id`)
  AND NOT EXISTS (SELECT 1 FROM `god2_player`.`character_pet_skills` x WHERE x.`skill_id`=skill_row.`skill_id`);

INSERT INTO `god2_research`.`unused_unmapped_skill_shell_archive`
    (`skill_id`,`code`,`name_zh_tw`,`description_zh_tw`,`skill_family`,`skill_category`,
     `damage_type`,`element`,`required_level`,`maximum_level`,`mp_cost`,`target_type`,
     `target_scope_zh_tw`,`enabled`,`archive_reason_zh_tw`,`archived_at_utc`)
SELECT skill_row.`skill_id`,skill_row.`code`,skill_row.`name_zh_tw`,skill_row.`description_zh_tw`,
       skill_row.`skill_family`,skill_row.`skill_category`,skill_row.`damage_type`,skill_row.`element`,
       skill_row.`required_level`,skill_row.`maximum_level`,skill_row.`mp_cost`,skill_row.`target_type`,
       skill_row.`target_scope_zh_tw`,skill_row.`enabled`,
       '正式技能表清理：此列未啟用、沒有官方技能書對照，且未被怪物技能、怪物 AI、寵物模板、寵物學習道具或玩家寵物技能引用；正式服務端不需要保留不可讀空殼資料。',
       UTC_TIMESTAMP(6)
FROM `god2_game`.`skills` skill_row
JOIN `god2_unused_unmapped_skill_shells` removable
  ON removable.`skill_id`=skill_row.`skill_id`
ON DUPLICATE KEY UPDATE
    `code`=VALUES(`code`),
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `description_zh_tw`=VALUES(`description_zh_tw`),
    `skill_family`=VALUES(`skill_family`),
    `skill_category`=VALUES(`skill_category`),
    `damage_type`=VALUES(`damage_type`),
    `element`=VALUES(`element`),
    `required_level`=VALUES(`required_level`),
    `maximum_level`=VALUES(`maximum_level`),
    `mp_cost`=VALUES(`mp_cost`),
    `target_type`=VALUES(`target_type`),
    `target_scope_zh_tw`=VALUES(`target_scope_zh_tw`),
    `enabled`=VALUES(`enabled`),
    `archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),
    `archived_at_utc`=VALUES(`archived_at_utc`);

DELETE evidence_row
FROM `god2_research`.`skill_catalog_evidence` evidence_row
JOIN `god2_unused_unmapped_skill_shells` removable
  ON removable.`skill_id`=evidence_row.`skill_id`;

DELETE skill_row
FROM `god2_game`.`skills` skill_row
JOIN `god2_unused_unmapped_skill_shells` removable
  ON removable.`skill_id`=skill_row.`skill_id`;

DROP TEMPORARY TABLE IF EXISTS `god2_unused_unmapped_skill_shells`;

SET @god2_sync_mode = NULL;
