CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

DROP VIEW IF EXISTS `god2_game`.`vw_immortal_templates_full`;
DROP VIEW IF EXISTS `god2_game`.`vw_immortal_level_one_baseline`;

CREATE TABLE IF NOT EXISTS `god2_research`.`immortal_template_evidence` (
    `immortal_template_id` bigint NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`immortal_template_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Immortal template source/evidence metadata split from the runtime catalog';

CREATE TABLE IF NOT EXISTS `god2_research`.`immortal_base_stat_evidence` (
    `immortal_template_id` bigint NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `evidence_reference` varchar(500) NOT NULL,
    `admin_note` varchar(500) NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`immortal_template_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Immortal level-one baseline stat evidence split from the runtime catalog';

INSERT INTO `god2_research`.`immortal_template_evidence`
    (`immortal_template_id`,`evidence_status`,`admin_note`)
SELECT `immortal_template_id`,`evidence_status`,`admin_note`
FROM `god2_game`.`immortal_templates`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`immortal_base_stat_evidence`
    (`immortal_template_id`,`evidence_status`,`evidence_reference`,`admin_note`)
SELECT `immortal_template_id`,`evidence_status`,`evidence_reference`,`admin_note`
FROM `god2_game`.`immortal_base_stats`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `evidence_reference`=VALUES(`evidence_reference`),
    `admin_note`=VALUES(`admin_note`);

ALTER TABLE `god2_game`.`immortal_templates`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`immortal_base_stats`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `evidence_reference`,
    DROP COLUMN IF EXISTS `admin_note`;

CREATE OR REPLACE VIEW `god2_game`.`vw_immortal_templates_full` AS
SELECT `immortal_template_id` AS `神仙模板ID`,
       `code` AS `代碼`,
       `name_zh_tw` AS `名稱`,
       `name_original` AS `原文名稱`,
       `resource_id` AS `客戶端資源ID`,
       `immortal_family` AS `神仙家族`,
       `profession_code` AS `對應職業`,
       `is_special` AS `特殊神仙`,
       `initial_rank_id` AS `初始階位ID`,
       `initial_level` AS `初始等級`,
       `base_max_hp` AS `基礎最大HP`,
       `base_max_mp` AS `基礎最大MP`,
       `base_strength` AS `基礎力量`,
       `base_constitution` AS `基礎體力`,
       `base_intelligence` AS `基礎智力`,
       `base_speed` AS `基礎速度`,
       `base_metal` AS `基礎金`,
       `base_wood` AS `基礎木`,
       `base_water` AS `基礎水`,
       `base_fire` AS `基礎火`,
       `base_earth` AS `基礎土`,
       `base_physical_attack` AS `基礎物攻`,
       `base_physical_defense` AS `基礎物防`,
       `base_magic_attack` AS `基礎魔攻`,
       `base_magic_defense` AS `基礎魔防`,
       `maximum_innate_skill_slots` AS `最大固有技能槽`,
       `maximum_active_skill_slots` AS `最大主動技能槽`,
       `enabled` AS `啟用狀態`
FROM `god2_game`.`immortal_templates`;

CREATE OR REPLACE VIEW `god2_game`.`vw_immortal_level_one_baseline` AS
SELECT template_row.`immortal_template_id` AS `神仙模板ID`,
       template_row.`name_zh_tw` AS `神仙名稱`,
       template_row.`profession_code` AS `對應職業`,
       template_row.`is_special` AS `特殊神仙`,
       stats_row.`level` AS `等級`,
       stats_row.`maximum_hp` AS `最大HP`,
       stats_row.`maximum_mp` AS `最大MP`,
       stats_row.`strength` AS `腕力`,
       stats_row.`constitution` AS `體力`,
       stats_row.`intelligence` AS `智力`,
       stats_row.`speed` AS `速度`,
       stats_row.`metal` AS `金`,
       stats_row.`wood` AS `木`,
       stats_row.`water` AS `水`,
       stats_row.`fire` AS `火`,
       stats_row.`earth` AS `土`,
       stats_row.`enabled` AS `正式啟用`
FROM `god2_game`.`immortal_templates` template_row
JOIN `god2_game`.`immortal_base_stats` stats_row
  ON stats_row.`immortal_template_id`=template_row.`immortal_template_id`
WHERE template_row.`enabled`=1 AND stats_row.`enabled`=1;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`immortal_template_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`immortal_template_evidence` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`immortal_base_stat_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`immortal_base_stat_evidence` TO 'god2_server'@'127.0.0.1';
