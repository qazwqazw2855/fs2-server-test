ALTER TABLE `god2_research`.`pet_category_evidence`
    ADD COLUMN IF NOT EXISTS `source_url` varchar(500) NULL AFTER `source_article_sn`;

ALTER TABLE `god2_research`.`pet_template_evidence`
    ADD COLUMN IF NOT EXISTS `source_url` varchar(500) NULL AFTER `source_article_sn`;

CREATE TABLE IF NOT EXISTS `god2_research`.`monster_capture_rule_source_archive` (
    `monster_id` bigint NOT NULL,
    `capture_rule_source_article_sn` int NULL,
    `archive_reason_zh_tw` varchar(512) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`monster_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='怪物捕捉資格規則來源文章移出正式怪物 runtime 表';

CREATE TABLE IF NOT EXISTS `god2_research`.`character_pet_skill_rule_source_archive` (
    `pet_instance_id` bigint NOT NULL,
    `slot_index` tinyint NOT NULL,
    `learning_rule_source_article_sn` int NULL,
    `archive_reason_zh_tw` varchar(512) NOT NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`pet_instance_id`,`slot_index`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='玩家戰寵技能學習規則來源文章移出正式玩家技能欄位表';

SET @has_pet_category_source_url := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='pet_categories'
      AND `COLUMN_NAME`='source_url'
);
SET @archive_pet_category_source_url_sql := IF(
    @has_pet_category_source_url > 0,
    'INSERT INTO `god2_research`.`pet_category_evidence`
        (`category_id`,`source_article_sn`,`source_url`,`evidence_status`,`admin_note`)
     SELECT category_row.`category_id`, evidence_row.`source_article_sn`, category_row.`source_url`,
            COALESCE(evidence_row.`evidence_status`,''EvidenceMovedToResearch''),
            evidence_row.`admin_note`
     FROM `god2_game`.`pet_categories` category_row
     LEFT JOIN `god2_research`.`pet_category_evidence` evidence_row ON evidence_row.`category_id`=category_row.`category_id`
     WHERE COALESCE(category_row.`source_url`,'''')<>''''
     ON DUPLICATE KEY UPDATE `source_url`=VALUES(`source_url`),`evidence_status`=VALUES(`evidence_status`),`admin_note`=VALUES(`admin_note`)',
    'SELECT 1'
);
PREPARE archive_pet_category_source_url_stmt FROM @archive_pet_category_source_url_sql;
EXECUTE archive_pet_category_source_url_stmt;
DEALLOCATE PREPARE archive_pet_category_source_url_stmt;

SET @has_pet_template_source_url := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='pet_templates'
      AND `COLUMN_NAME`='source_url'
);
SET @archive_pet_template_source_url_sql := IF(
    @has_pet_template_source_url > 0,
    'INSERT INTO `god2_research`.`pet_template_evidence`
        (`pet_template_id`,`source_section_zh_tw`,`source_article_sn`,`source_url`,`evidence_status`,`admin_note`)
     SELECT template_row.`pet_template_id`, evidence_row.`source_section_zh_tw`, evidence_row.`source_article_sn`, template_row.`source_url`,
            COALESCE(evidence_row.`evidence_status`,''EvidenceMovedToResearch''),
            evidence_row.`admin_note`
     FROM `god2_game`.`pet_templates` template_row
     LEFT JOIN `god2_research`.`pet_template_evidence` evidence_row ON evidence_row.`pet_template_id`=template_row.`pet_template_id`
     WHERE COALESCE(template_row.`source_url`,'''')<>''''
     ON DUPLICATE KEY UPDATE `source_url`=VALUES(`source_url`),`evidence_status`=VALUES(`evidence_status`),`admin_note`=VALUES(`admin_note`)',
    'SELECT 1'
);
PREPARE archive_pet_template_source_url_stmt FROM @archive_pet_template_source_url_sql;
EXECUTE archive_pet_template_source_url_stmt;
DEALLOCATE PREPARE archive_pet_template_source_url_stmt;

SET @has_monster_capture_rule_source := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_game'
      AND `TABLE_NAME`='monsters'
      AND `COLUMN_NAME`='capture_rule_source_article_sn'
);
SET @archive_monster_capture_rule_source_sql := IF(
    @has_monster_capture_rule_source > 0,
    'INSERT INTO `god2_research`.`monster_capture_rule_source_archive`
        (`monster_id`,`capture_rule_source_article_sn`,`archive_reason_zh_tw`)
     SELECT `monster_id`,`capture_rule_source_article_sn`,
            ''怪物可捕捉規則來源文章已移入 research；正式怪物表只保留捕捉資格、顏色、任務怪與陣法魔王等 runtime 判斷欄位。''
     FROM `god2_game`.`monsters`
     ON DUPLICATE KEY UPDATE `capture_rule_source_article_sn`=VALUES(`capture_rule_source_article_sn`),`archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),`archived_at_utc`=UTC_TIMESTAMP(6)',
    'SELECT 1'
);
PREPARE archive_monster_capture_rule_source_stmt FROM @archive_monster_capture_rule_source_sql;
EXECUTE archive_monster_capture_rule_source_stmt;
DEALLOCATE PREPARE archive_monster_capture_rule_source_stmt;

SET @has_pet_skill_learning_rule_source := (
    SELECT COUNT(*)
    FROM `information_schema`.`COLUMNS`
    WHERE `TABLE_SCHEMA`='god2_player'
      AND `TABLE_NAME`='character_pet_skills'
      AND `COLUMN_NAME`='learning_rule_source_article_sn'
);
SET @archive_pet_skill_learning_rule_source_sql := IF(
    @has_pet_skill_learning_rule_source > 0,
    'INSERT INTO `god2_research`.`character_pet_skill_rule_source_archive`
        (`pet_instance_id`,`slot_index`,`learning_rule_source_article_sn`,`archive_reason_zh_tw`)
     SELECT `pet_instance_id`,`slot_index`,`learning_rule_source_article_sn`,
            ''戰寵技能學習規則來源文章已移入 research；正式玩家技能表只保留技能、欄位、解鎖與啟用狀態。''
     FROM `god2_player`.`character_pet_skills`
     WHERE `learning_rule_source_article_sn` IS NOT NULL
     ON DUPLICATE KEY UPDATE `learning_rule_source_article_sn`=VALUES(`learning_rule_source_article_sn`),`archive_reason_zh_tw`=VALUES(`archive_reason_zh_tw`),`archived_at_utc`=UTC_TIMESTAMP(6)',
    'SELECT 1'
);
PREPARE archive_pet_skill_learning_rule_source_stmt FROM @archive_pet_skill_learning_rule_source_sql;
EXECUTE archive_pet_skill_learning_rule_source_stmt;
DEALLOCATE PREPARE archive_pet_skill_learning_rule_source_stmt;

DROP VIEW IF EXISTS `god2_research`.`vw_monster_capture_eligibility_readable`;

ALTER TABLE `god2_game`.`pet_categories`
    DROP CONSTRAINT IF EXISTS `ck_pet_categories_source`,
    DROP COLUMN IF EXISTS `source_url`;

ALTER TABLE `god2_game`.`pet_templates`
    DROP CONSTRAINT IF EXISTS `ck_pet_templates_source_article`,
    DROP COLUMN IF EXISTS `source_url`;

ALTER TABLE `god2_game`.`monsters`
    DROP CONSTRAINT IF EXISTS `ck_monsters_capture_source`,
    DROP COLUMN IF EXISTS `capture_rule_source_article_sn`;

ALTER TABLE `god2_player`.`character_pet_skills`
    DROP CONSTRAINT IF EXISTS `ck_character_pet_skills_learning_source`,
    DROP COLUMN IF EXISTS `learning_rule_source_article_sn`;

CREATE OR REPLACE VIEW `god2_research`.`vw_monster_capture_eligibility_readable` AS
SELECT
    monster_row.`monster_id` AS `monster_id`,
    monster_row.`name_zh_tw` AS `monster_name_zh_tw`,
    monster_row.`level` AS `wild_level`,
    monster_row.`name_color_zh_tw` AS `name_color_zh_tw`,
    monster_row.`is_quest_monster` AS `is_quest_monster`,
    monster_row.`is_formation_boss` AS `is_formation_boss`,
    monster_row.`capture_eligibility` AS `capture_eligibility`,
    monster_row.`capture_block_reason_zh_tw` AS `capture_block_reason_zh_tw`,
    source_row.`capture_rule_source_article_sn` AS `capture_rule_source_article_sn`,
    monster_row.`enabled` AS `enabled`
FROM `god2_game`.`monsters` monster_row
LEFT JOIN `god2_research`.`monster_capture_rule_source_archive` source_row
    ON source_row.`monster_id`=monster_row.`monster_id`
ORDER BY monster_row.`monster_id`;
