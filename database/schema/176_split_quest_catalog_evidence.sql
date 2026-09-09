CREATE TABLE IF NOT EXISTS `god2_research`.`quest_catalog_evidence` (
    `quest_id` bigint NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`quest_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`quest_objective_evidence` (
    `objective_id` bigint NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`objective_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`quest_reward_evidence` (
    `reward_id` bigint NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`reward_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`quest_catalog_evidence`
    (`quest_id`,`evidence_status`,`admin_note`)
SELECT `quest_id`,`evidence_status`,`admin_note`
FROM `god2_game`.`quests`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`quest_objective_evidence`
    (`objective_id`,`evidence_status`,`admin_note`)
SELECT `objective_id`,`evidence_status`,`admin_note`
FROM `god2_game`.`quest_objectives`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`quest_reward_evidence`
    (`reward_id`,`evidence_status`,`admin_note`)
SELECT `reward_id`,`evidence_status`,`admin_note`
FROM `god2_game`.`quest_rewards`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

DROP VIEW IF EXISTS `god2_game`.`vw_quests_full`;
DROP VIEW IF EXISTS `god2_game`.`vw_quests_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_quest_objectives_full`;
DROP VIEW IF EXISTS `god2_game`.`vw_quest_rewards_full`;

ALTER TABLE `god2_game`.`quests`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`quest_objectives`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`quest_rewards`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

CREATE OR REPLACE VIEW `god2_game`.`vw_quests_full` AS
SELECT
    quest_row.`quest_id` AS `任務編號`,
    quest_row.`code` AS `服務端代碼`,
    quest_row.`name_zh_tw` AS `任務名稱`,
    quest_row.`quest_type` AS `任務類型`,
    quest_row.`start_npc_id` AS `起始NPC`,
    start_npc.`name_zh_tw` AS `起始NPC名稱`,
    quest_row.`end_npc_id` AS `結束NPC`,
    end_npc.`name_zh_tw` AS `結束NPC名稱`,
    quest_row.`required_level` AS `最低等級`,
    quest_row.`maximum_level` AS `最高等級`,
    quest_row.`repeatable` AS `可重複`,
    quest_row.`repeat_interval_seconds` AS `重複間隔秒數`,
    quest_row.`description_zh_tw` AS `任務描述`,
    quest_row.`completion_text_zh_tw` AS `完成文字`,
    CASE quest_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`quests` quest_row
LEFT JOIN `god2_game`.`npcs` start_npc ON start_npc.`npc_id`=quest_row.`start_npc_id`
LEFT JOIN `god2_game`.`npcs` end_npc ON end_npc.`npc_id`=quest_row.`end_npc_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_quests_readable` AS
SELECT * FROM `god2_game`.`vw_quests_full`;

CREATE OR REPLACE VIEW `god2_game`.`vw_quest_objectives_full` AS
SELECT
    objective_row.`objective_id` AS `目標編號`,
    objective_row.`quest_id` AS `任務編號`,
    quest_row.`name_zh_tw` AS `任務名稱`,
    objective_row.`objective_order` AS `目標順序`,
    objective_row.`objective_type` AS `目標類型`,
    objective_row.`target_id` AS `目標ID`,
    objective_row.`target_name_cache` AS `目標名稱`,
    objective_row.`required_quantity` AS `需要數量`,
    objective_row.`map_id` AS `地圖編號`,
    map_row.`name_zh_tw` AS `地圖名稱`,
    objective_row.`description_zh_tw` AS `目標描述`,
    CASE objective_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`quest_objectives` objective_row
JOIN `god2_game`.`quests` quest_row ON quest_row.`quest_id`=objective_row.`quest_id`
LEFT JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=objective_row.`map_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_quest_rewards_full` AS
SELECT
    reward_row.`reward_id` AS `獎勵編號`,
    reward_row.`quest_id` AS `任務編號`,
    quest_row.`name_zh_tw` AS `任務名稱`,
    reward_row.`reward_order` AS `獎勵順序`,
    reward_row.`reward_type` AS `獎勵類型`,
    reward_row.`item_id` AS `物品編號`,
    item_row.`name_zh_tw` AS `物品名稱`,
    reward_row.`quantity` AS `數量`,
    reward_row.`experience` AS `經驗`,
    reward_row.`currency` AS `金錢`,
    reward_row.`selection_group` AS `選擇群組`,
    CASE reward_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`quest_rewards` reward_row
JOIN `god2_game`.`quests` quest_row ON quest_row.`quest_id`=reward_row.`quest_id`
LEFT JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=reward_row.`item_id`;
