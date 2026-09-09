CREATE DATABASE IF NOT EXISTS `god2_research` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

DROP VIEW IF EXISTS `god2_game`.`vw_monster_capture_eligibility_readable`;

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
    monster_row.`capture_rule_source_article_sn` AS `capture_rule_source_article_sn`,
    monster_row.`evidence_status` AS `evidence_status`,
    monster_row.`enabled` AS `enabled`
FROM `god2_game`.`monsters` monster_row
ORDER BY monster_row.`monster_id`;
