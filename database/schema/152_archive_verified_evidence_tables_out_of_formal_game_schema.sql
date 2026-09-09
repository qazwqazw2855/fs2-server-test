CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

DROP VIEW IF EXISTS `god2_game`.`vw_equipment_set_bonus_static_evidence_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_equipment_set_item_evidence_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_equipment_slot_evidence_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_equipment_slot_verified_evidence_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_equipment_static_stat_evidence_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_item_class_restrictions_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_item_gender_restrictions_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_item_requirements_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_item_static_permission_evidence_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_weapon_attack_ranges_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_weapon_static_stat_evidence_readable`;

CREATE TABLE IF NOT EXISTS `god2_research`.`equipment_set_bonus_static_evidence` LIKE `god2_game`.`equipment_set_bonus_static_evidence`;
INSERT IGNORE INTO `god2_research`.`equipment_set_bonus_static_evidence` SELECT * FROM `god2_game`.`equipment_set_bonus_static_evidence`;
DROP TABLE `god2_game`.`equipment_set_bonus_static_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`equipment_set_item_evidence` LIKE `god2_game`.`equipment_set_item_evidence`;
INSERT IGNORE INTO `god2_research`.`equipment_set_item_evidence` SELECT * FROM `god2_game`.`equipment_set_item_evidence`;
DROP TABLE `god2_game`.`equipment_set_item_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`equipment_slot_evidence` LIKE `god2_game`.`equipment_slot_evidence`;
INSERT IGNORE INTO `god2_research`.`equipment_slot_evidence` SELECT * FROM `god2_game`.`equipment_slot_evidence`;
DROP TABLE `god2_game`.`equipment_slot_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`equipment_slot_verified_evidence` LIKE `god2_game`.`equipment_slot_verified_evidence`;
INSERT IGNORE INTO `god2_research`.`equipment_slot_verified_evidence` SELECT * FROM `god2_game`.`equipment_slot_verified_evidence`;
DROP TABLE `god2_game`.`equipment_slot_verified_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`equipment_static_stat_evidence` LIKE `god2_game`.`equipment_static_stat_evidence`;
INSERT IGNORE INTO `god2_research`.`equipment_static_stat_evidence` SELECT * FROM `god2_game`.`equipment_static_stat_evidence`;
DROP TABLE `god2_game`.`equipment_static_stat_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`item_class_restriction_evidence` LIKE `god2_game`.`item_class_restriction_evidence`;
INSERT IGNORE INTO `god2_research`.`item_class_restriction_evidence` SELECT * FROM `god2_game`.`item_class_restriction_evidence`;
DROP TABLE `god2_game`.`item_class_restriction_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`item_gender_restriction_evidence` LIKE `god2_game`.`item_gender_restriction_evidence`;
INSERT IGNORE INTO `god2_research`.`item_gender_restriction_evidence` SELECT * FROM `god2_game`.`item_gender_restriction_evidence`;
DROP TABLE `god2_game`.`item_gender_restriction_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`item_requirement_evidence` LIKE `god2_game`.`item_requirement_evidence`;
INSERT IGNORE INTO `god2_research`.`item_requirement_evidence` SELECT * FROM `god2_game`.`item_requirement_evidence`;
DROP TABLE `god2_game`.`item_requirement_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`item_static_permission_evidence` LIKE `god2_game`.`item_static_permission_evidence`;
INSERT IGNORE INTO `god2_research`.`item_static_permission_evidence` SELECT * FROM `god2_game`.`item_static_permission_evidence`;
DROP TABLE `god2_game`.`item_static_permission_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`npc_appearance_source_rows` LIKE `god2_game`.`npc_appearance_source_rows`;
INSERT IGNORE INTO `god2_research`.`npc_appearance_source_rows` SELECT * FROM `god2_game`.`npc_appearance_source_rows`;
DROP TABLE `god2_game`.`npc_appearance_source_rows`;

CREATE TABLE IF NOT EXISTS `god2_research`.`npc_coordinate_evidence` LIKE `god2_game`.`npc_coordinate_evidence`;
INSERT IGNORE INTO `god2_research`.`npc_coordinate_evidence` SELECT * FROM `god2_game`.`npc_coordinate_evidence`;
DROP TABLE `god2_game`.`npc_coordinate_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`weapon_attack_range_evidence` LIKE `god2_game`.`weapon_attack_range_evidence`;
INSERT IGNORE INTO `god2_research`.`weapon_attack_range_evidence` SELECT * FROM `god2_game`.`weapon_attack_range_evidence`;
DROP TABLE `god2_game`.`weapon_attack_range_evidence`;

CREATE TABLE IF NOT EXISTS `god2_research`.`weapon_static_stat_evidence` LIKE `god2_game`.`weapon_static_stat_evidence`;
INSERT IGNORE INTO `god2_research`.`weapon_static_stat_evidence` SELECT * FROM `god2_game`.`weapon_static_stat_evidence`;
DROP TABLE `god2_game`.`weapon_static_stat_evidence`;
