CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

DROP VIEW IF EXISTS `god2_game`.`vw_pet_growth_grades_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_pet_category_growth_values_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_pet_growth_manual_fill_queue`;

CREATE TABLE IF NOT EXISTS `god2_research`.`pet_growth_grade_rule_evidence` (
    `growth_grade` varchar(30) NOT NULL,
    `minimum_level` int NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `source_reference_zh_tw` varchar(500) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`growth_grade`,`minimum_level`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='戰寵成長品級分段規則的證據狀態與來源；正式表只保留成長規則';

CREATE TABLE IF NOT EXISTS `god2_research`.`pet_automatic_growth_allocation_evidence` (
    `growth_archetype_id` smallint NOT NULL,
    `growth_grade` varchar(30) NOT NULL,
    `minimum_level` int NOT NULL,
    `source_value_zh_tw` varchar(100) NULL,
    `source_article_sn` int NULL,
    `source_url` varchar(500) NULL,
    `source_reference_zh_tw` varchar(500) NULL,
    `admin_note` varchar(500) NULL,
    `evidence_status` varchar(30) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`growth_archetype_id`,`growth_grade`,`minimum_level`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='戰寵自動配點規則的來源、文章與證據狀態；正式表只保留配點數值';

INSERT INTO `god2_research`.`pet_growth_grade_rule_evidence`
    (`growth_grade`,`minimum_level`,`evidence_status`,`source_reference_zh_tw`)
SELECT `growth_grade`,`minimum_level`,`evidence_status`,`source_reference_zh_tw`
FROM `god2_game`.`pet_growth_grade_rules`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `source_reference_zh_tw`=VALUES(`source_reference_zh_tw`);

INSERT INTO `god2_research`.`pet_automatic_growth_allocation_evidence`
    (`growth_archetype_id`,`growth_grade`,`minimum_level`,`source_value_zh_tw`,
     `source_article_sn`,`source_url`,`source_reference_zh_tw`,`admin_note`,`evidence_status`)
SELECT `growth_archetype_id`,`growth_grade`,`minimum_level`,`source_value_zh_tw`,
       `source_article_sn`,`source_url`,`source_reference_zh_tw`,`admin_note`,`evidence_status`
FROM `god2_game`.`pet_automatic_growth_allocations`
ON DUPLICATE KEY UPDATE
    `source_value_zh_tw`=VALUES(`source_value_zh_tw`),
    `source_article_sn`=VALUES(`source_article_sn`),
    `source_url`=VALUES(`source_url`),
    `source_reference_zh_tw`=VALUES(`source_reference_zh_tw`),
    `admin_note`=VALUES(`admin_note`),
    `evidence_status`=VALUES(`evidence_status`);

ALTER TABLE `god2_game`.`pet_automatic_growth_allocations`
    DROP CONSTRAINT IF EXISTS `ck_pet_auto_growth_values`;

ALTER TABLE `god2_game`.`pet_growth_grade_rules`
    DROP CONSTRAINT IF EXISTS `ck_pet_growth_grade_evidence`,
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `source_reference_zh_tw`;

ALTER TABLE `god2_game`.`pet_automatic_growth_allocations`
    DROP COLUMN IF EXISTS `source_value_zh_tw`,
    DROP COLUMN IF EXISTS `source_article_sn`,
    DROP COLUMN IF EXISTS `source_url`,
    DROP COLUMN IF EXISTS `source_reference_zh_tw`,
    DROP COLUMN IF EXISTS `admin_note`,
    DROP COLUMN IF EXISTS `evidence_status`,
    ADD CONSTRAINT `ck_pet_auto_growth_values`
        CHECK (`enabled` = 0 OR `published_total` = `expected_total`);

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_growth_grades_readable` AS
SELECT CASE rule_row.`growth_grade`
           WHEN 'Normal' THEN '普通'
           WHEN 'Top' THEN '頂級'
           WHEN 'LateBreakthrough' THEN '晚破'
           WHEN 'Breakthrough' THEN '破頂'
       END AS `戰寵品級`,
       rule_row.`minimum_level` AS `起始等級`,
       rule_row.`maximum_level` AS `結束等級`,
       rule_row.`automatic_points_per_level` AS `每級自動點數`,
       rule_row.`manual_points_per_level` AS `每級手動點數`,
       CASE rule_row.`initial_quality`
           WHEN 'Normal' THEN '普通'
           WHEN 'Top' THEN '頂級'
       END AS `初始品質`,
       rule_row.`breakthrough_level` AS `突破等級`,
       rule_row.`enabled` AS `正式啟用`
FROM `god2_game`.`pet_growth_grade_rules` rule_row
ORDER BY FIELD(rule_row.`growth_grade`,'Normal','Top','LateBreakthrough','Breakthrough'),
         rule_row.`minimum_level`;

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_category_growth_values_readable` AS
SELECT category_row.`category_id` AS `寵物類別ID`,
       category_row.`name_zh_tw` AS `寵物類別`,
       CASE allocation.`growth_grade`
           WHEN 'Normal' THEN '普通'
           WHEN 'Top' THEN '頂級'
           WHEN 'LateBreakthrough' THEN '晚破'
           WHEN 'Breakthrough' THEN '破頂'
       END AS `成長品級`,
       allocation.`minimum_level` AS `起始等級`,
       allocation.`maximum_level` AS `結束等級`,
       allocation.`constitution_delta` AS `每級體力`,
       allocation.`strength_delta` AS `每級力量`,
       allocation.`intelligence_delta` AS `每級智力`,
       allocation.`speed_delta` AS `每級速度`,
       allocation.`published_total` AS `合計點數`,
       allocation.`expected_total` AS `應有點數`,
       allocation.`enabled` AS `正式啟用`
FROM `god2_game`.`pet_categories` category_row
JOIN `god2_game`.`pet_automatic_growth_allocations` allocation
  ON allocation.`growth_archetype_id` = category_row.`growth_archetype_id`
ORDER BY category_row.`category_id`,
         FIELD(allocation.`growth_grade`,'Normal','Top','Breakthrough','LateBreakthrough'),
         allocation.`minimum_level`;

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_growth_manual_fill_queue` AS
SELECT evidence_row.`growth_archetype_id` AS `成長原型ID`,
       evidence_row.`growth_grade` AS `成長品級`,
       evidence_row.`minimum_level` AS `起始等級`,
       evidence_row.`evidence_status` AS `待處理原因`,
       evidence_row.`source_reference_zh_tw` AS `資料來源`,
       evidence_row.`admin_note` AS `補值備註`
FROM `god2_research`.`pet_automatic_growth_allocation_evidence` evidence_row
WHERE evidence_row.`evidence_status` IN ('SourceMissing','SourceConflict');

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_growth_grade_rule_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_growth_grade_rule_evidence` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_automatic_growth_allocation_evidence` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`pet_automatic_growth_allocation_evidence` TO 'god2_server'@'127.0.0.1';
