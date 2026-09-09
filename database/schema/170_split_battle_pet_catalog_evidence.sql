CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

DROP VIEW IF EXISTS `god2_game`.`vw_pet_categories_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_pet_templates_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_pet_templates_full`;

CREATE TABLE IF NOT EXISTS `god2_research`.`pet_category_evidence` (
    `category_id` smallint NOT NULL,
    `source_article_sn` int NULL,
    `evidence_status` varchar(40) NOT NULL,
    `admin_note` varchar(300) NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`category_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Battle pet category source/evidence metadata split from the runtime catalog';

CREATE TABLE IF NOT EXISTS `god2_research`.`pet_template_evidence` (
    `pet_template_id` bigint NOT NULL,
    `source_section_zh_tw` varchar(50) NULL,
    `source_article_sn` int NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`pet_template_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Battle pet template source/evidence metadata split from the runtime catalog';

CREATE TABLE IF NOT EXISTS `god2_research`.`pet_skill_learning_item_evidence` (
    `item_id` bigint NOT NULL,
    `skill_id` bigint NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Battle pet fourth-skill learning-item evidence split from the runtime mapping';

ALTER TABLE `god2_research`.`pet_category_evidence`
    MODIFY COLUMN `source_article_sn` int NULL;

INSERT INTO `god2_research`.`pet_category_evidence`
    (`category_id`,`source_article_sn`,`evidence_status`,`admin_note`)
SELECT `category_id`,`source_article_sn`,`evidence_status`,`admin_note`
FROM `god2_game`.`pet_categories`
ON DUPLICATE KEY UPDATE
    `source_article_sn`=VALUES(`source_article_sn`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`pet_template_evidence`
    (`pet_template_id`,`source_section_zh_tw`,`source_article_sn`,`evidence_status`,`admin_note`)
SELECT `pet_template_id`,`source_section_zh_tw`,`source_article_sn`,`evidence_status`,`admin_note`
FROM `god2_game`.`pet_templates`
ON DUPLICATE KEY UPDATE
    `source_section_zh_tw`=VALUES(`source_section_zh_tw`),
    `source_article_sn`=VALUES(`source_article_sn`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`pet_skill_learning_item_evidence`
    (`item_id`,`skill_id`,`evidence_status`,`admin_note`)
SELECT `item_id`,`skill_id`,`evidence_status`,`admin_note`
FROM `god2_game`.`pet_skill_learning_items`
ON DUPLICATE KEY UPDATE
    `skill_id`=VALUES(`skill_id`),
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

ALTER TABLE `god2_game`.`pet_categories`
    DROP CONSTRAINT IF EXISTS `ck_pet_categories_source`,
    DROP CONSTRAINT IF EXISTS `ck_pet_categories_evidence`,
    DROP COLUMN IF EXISTS `source_article_sn`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`pet_templates`
    DROP CONSTRAINT IF EXISTS `ck_pet_templates_source_article`,
    DROP COLUMN IF EXISTS `source_section_zh_tw`,
    DROP COLUMN IF EXISTS `source_article_sn`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

ALTER TABLE `god2_game`.`pet_skill_learning_items`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_categories_readable` AS
SELECT category_row.`category_id` AS `類別ID`,
       category_row.`name_zh_tw` AS `戰寵族群`,
       category_row.`article_section_zh_tw` AS `文章分類`,
       CASE category_row.`growth_pattern`
           WHEN 'Pure' THEN '純種型'
           WHEN 'Mixed' THEN '雜種型'
           ELSE '平均型'
       END AS `成長型態`,
       category_row.`growth_archetype_id` AS `成長原型ID`,
       category_row.`enabled` AS `正式啟用`
FROM `god2_game`.`pet_categories` category_row
WHERE category_row.`enabled`=1
ORDER BY category_row.`category_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_templates_readable` AS
SELECT template_row.`pet_template_id` AS `戰寵模板ID`,
       template_row.`name_zh_tw` AS `戰寵名稱`,
       category_row.`name_zh_tw` AS `戰寵族群`,
       template_row.`element_zh_tw` AS `五行`,
       template_row.`base_level` AS `玩家初始等級`,
       monster_row.`name_zh_tw` AS `野生怪物來源`,
       monster_row.`level` AS `野生遭遇等級`,
       template_row.`level_20_skill_name_zh_tw` AS `20級技能`,
       template_row.`level_40_skill_name_zh_tw` AS `40級技能`,
       template_row.`level_60_skill_name_zh_tw` AS `60級技能`,
       template_row.`capture_rule_zh_tw` AS `捕捉轉換規則`,
       template_row.`base_max_hp` AS `基礎最大HP`,
       template_row.`base_max_mp` AS `基礎最大MP`,
       template_row.`base_strength` AS `基礎力量`,
       template_row.`base_constitution` AS `基礎體力`,
       template_row.`base_intelligence` AS `基礎智力`,
       template_row.`base_speed` AS `基礎速度`,
       template_row.`enabled` AS `正式啟用`
FROM `god2_game`.`pet_templates` template_row
LEFT JOIN `god2_game`.`pet_categories` category_row ON category_row.`category_id`=template_row.`pet_category_id`
LEFT JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=template_row.`wild_source_monster_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_templates_full` AS
SELECT `pet_template_id` AS `戰寵模板ID`,
       `code` AS `代碼`,
       `name_zh_tw` AS `名稱`,
       `name_original` AS `原文名稱`,
       `pet_family` AS `戰寵族系`,
       `pet_category_id` AS `戰寵族群ID`,
       `wild_source_monster_id` AS `野生怪物來源ID`,
       `element_zh_tw` AS `五行`,
       `base_level` AS `基礎等級`,
       `base_max_hp` AS `基礎最大HP`,
       `base_max_mp` AS `基礎最大MP`,
       `base_max_lifespan` AS `基礎最大壽命`,
       `base_strength` AS `力量`,
       `base_constitution` AS `體力`,
       `base_intelligence` AS `智力`,
       `base_speed` AS `速度`,
       `base_metal` AS `金`,
       `base_wood` AS `木`,
       `base_water` AS `水`,
       `base_fire` AS `火`,
       `base_earth` AS `土`,
       `maximum_skill_slots` AS `最大技能槽`,
       `level_20_skill_name_zh_tw` AS `20級技能`,
       `level_40_skill_name_zh_tw` AS `40級技能`,
       `level_60_skill_name_zh_tw` AS `60級技能`,
       `capture_rule_zh_tw` AS `捕捉轉換規則`,
       `enabled` AS `啟用狀態`
FROM `god2_game`.`pet_templates`;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_category_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_category_evidence` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_template_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_template_evidence` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_skill_learning_item_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_skill_learning_item_evidence` TO 'god2_server'@'127.0.0.1';
