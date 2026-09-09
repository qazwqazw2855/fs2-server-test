CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

DROP VIEW IF EXISTS `god2_game`.`vw_xjz_current_evidence_pack_summary`;
DROP VIEW IF EXISTS `god2_research`.`vw_xjz_current_evidence_pack_summary`;

CREATE TABLE IF NOT EXISTS `god2_research`.`xjz_evidence_pack_sources` LIKE `god2_game`.`xjz_evidence_pack_sources`;
INSERT IGNORE INTO `god2_research`.`xjz_evidence_pack_sources` SELECT * FROM `god2_game`.`xjz_evidence_pack_sources`;
DROP TABLE `god2_game`.`xjz_evidence_pack_sources`;

CREATE TABLE IF NOT EXISTS `god2_research`.`xjz_god_menu_evidence` LIKE `god2_game`.`xjz_god_menu_evidence`;
INSERT IGNORE INTO `god2_research`.`xjz_god_menu_evidence` SELECT * FROM `god2_game`.`xjz_god_menu_evidence`;
DROP TABLE `god2_game`.`xjz_god_menu_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`xjz_combat_pet_quality_star_evidence` LIKE `god2_game`.`xjz_combat_pet_quality_star_evidence`;
INSERT IGNORE INTO `god2_research`.`xjz_combat_pet_quality_star_evidence` SELECT * FROM `god2_game`.`xjz_combat_pet_quality_star_evidence`;
DROP TABLE `god2_game`.`xjz_combat_pet_quality_star_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`xjz_item_effect_visual_evidence` LIKE `god2_game`.`xjz_item_effect_visual_evidence`;
INSERT IGNORE INTO `god2_research`.`xjz_item_effect_visual_evidence` SELECT * FROM `god2_game`.`xjz_item_effect_visual_evidence`;
DROP TABLE `god2_game`.`xjz_item_effect_visual_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`xjz_monster_visual_evidence` LIKE `god2_game`.`xjz_monster_visual_evidence`;
INSERT IGNORE INTO `god2_research`.`xjz_monster_visual_evidence` SELECT * FROM `god2_game`.`xjz_monster_visual_evidence`;
DROP TABLE `god2_game`.`xjz_monster_visual_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`xjz_mission_text_evidence` LIKE `god2_game`.`xjz_mission_text_evidence`;
INSERT IGNORE INTO `god2_research`.`xjz_mission_text_evidence` SELECT * FROM `god2_game`.`xjz_mission_text_evidence`;
DROP TABLE `god2_game`.`xjz_mission_text_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`xjz_daily_mission_evidence` LIKE `god2_game`.`xjz_daily_mission_evidence`;
INSERT IGNORE INTO `god2_research`.`xjz_daily_mission_evidence` SELECT * FROM `god2_game`.`xjz_daily_mission_evidence`;
DROP TABLE `god2_game`.`xjz_daily_mission_evidence`;

CREATE OR REPLACE VIEW `god2_research`.`vw_xjz_current_evidence_pack_summary` AS
SELECT 'sources' AS evidence_area, COUNT(*) AS row_count, MIN(source_path) AS first_key FROM `god2_research`.`xjz_evidence_pack_sources`
UNION ALL SELECT 'god_menu', COUNT(*), MIN(name_zh_tw) FROM `god2_research`.`xjz_god_menu_evidence`
UNION ALL SELECT 'combat_pet_quality_star', COUNT(*), MIN(CAST(quality_level AS char)) FROM `god2_research`.`xjz_combat_pet_quality_star_evidence`
UNION ALL SELECT 'item_effect_visual', COUNT(*), MIN(CONCAT(source_table, ':', effect_key)) FROM `god2_research`.`xjz_item_effect_visual_evidence`
UNION ALL SELECT 'monster_visual', COUNT(*), MIN(CONCAT(source_table, ':', visual_id)) FROM `god2_research`.`xjz_monster_visual_evidence`
UNION ALL SELECT 'mission_text', COUNT(*), MIN(CONCAT(mission_id, ':', step_no)) FROM `god2_research`.`xjz_mission_text_evidence`
UNION ALL SELECT 'daily_mission', COUNT(*), MIN(CONCAT(daily_mission_id, ':', branch_id)) FROM `god2_research`.`xjz_daily_mission_evidence`;
