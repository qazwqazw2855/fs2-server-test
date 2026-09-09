CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

DROP TABLE IF EXISTS `god2`.`advanced_headless_verification_transactions`;

CREATE TABLE IF NOT EXISTS `god2_research`.`official_skill_book_catalog` LIKE `god2`.`official_skill_book_catalog`;
INSERT IGNORE INTO `god2_research`.`official_skill_book_catalog` SELECT * FROM `god2`.`official_skill_book_catalog`;
DROP TABLE IF EXISTS `god2`.`official_skill_book_catalog`;

CREATE TABLE IF NOT EXISTS `god2_research`.`latest_controlled_gameplay_observation` LIKE `god2`.`latest_controlled_gameplay_observation`;
INSERT IGNORE INTO `god2_research`.`latest_controlled_gameplay_observation` SELECT * FROM `god2`.`latest_controlled_gameplay_observation`;
DROP TABLE IF EXISTS `god2`.`latest_controlled_gameplay_observation`;

CREATE TABLE IF NOT EXISTS `god2_research`.`verified_battle_action_semantics` LIKE `god2`.`verified_battle_action_semantics`;
INSERT IGNORE INTO `god2_research`.`verified_battle_action_semantics` SELECT * FROM `god2`.`verified_battle_action_semantics`;
DROP TABLE IF EXISTS `god2`.`verified_battle_action_semantics`;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`official_skill_book_catalog` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`official_skill_book_catalog` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`official_skill_book_catalog` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`official_skill_book_catalog` TO 'god2_catalog_builder'@'127.0.0.1';

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`latest_controlled_gameplay_observation` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`latest_controlled_gameplay_observation` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`latest_controlled_gameplay_observation` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`latest_controlled_gameplay_observation` TO 'god2_catalog_builder'@'127.0.0.1';

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`verified_battle_action_semantics` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`verified_battle_action_semantics` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`verified_battle_action_semantics` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`verified_battle_action_semantics` TO 'god2_catalog_builder'@'127.0.0.1';

FLUSH PRIVILEGES;
