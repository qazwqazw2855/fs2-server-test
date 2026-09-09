CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`character_map_identity_migrations` LIKE `god2`.`character_map_identity_migrations`;
INSERT IGNORE INTO `god2_research`.`character_map_identity_migrations` SELECT * FROM `god2`.`character_map_identity_migrations`;
DROP TABLE IF EXISTS `god2`.`character_map_identity_migrations`;

CREATE TABLE IF NOT EXISTS `god2_research`.`legacy_god2_inventory_item_identity_sequence` LIKE `god2`.`inventory_item_identity_sequence`;
INSERT IGNORE INTO `god2_research`.`legacy_god2_inventory_item_identity_sequence` SELECT * FROM `god2`.`inventory_item_identity_sequence`;
DROP TABLE IF EXISTS `god2`.`inventory_item_identity_sequence`;

DROP TABLE IF EXISTS `god2`.`battle_actor_recovery`;
DROP TABLE IF EXISTS `god2`.`character_lifecycle_idempotency`;
DROP TABLE IF EXISTS `god2`.`world_interaction_idempotency`;

DROP TABLE IF EXISTS `god2_game`.`combine_recipes`;
DROP TABLE IF EXISTS `god2_game`.`level_experience`;
DROP TABLE IF EXISTS `god2_game`.`map_rules`;

GRANT SELECT ON `god2_research`.`character_map_identity_migrations` TO 'god2_server'@'localhost';
GRANT SELECT ON `god2_research`.`character_map_identity_migrations` TO 'god2_server'@'127.0.0.1';
GRANT SELECT ON `god2_research`.`character_map_identity_migrations` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT ON `god2_research`.`character_map_identity_migrations` TO 'god2_catalog_builder'@'127.0.0.1';

GRANT SELECT ON `god2_research`.`legacy_god2_inventory_item_identity_sequence` TO 'god2_server'@'localhost';
GRANT SELECT ON `god2_research`.`legacy_god2_inventory_item_identity_sequence` TO 'god2_server'@'127.0.0.1';
GRANT SELECT ON `god2_research`.`legacy_god2_inventory_item_identity_sequence` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT ON `god2_research`.`legacy_god2_inventory_item_identity_sequence` TO 'god2_catalog_builder'@'127.0.0.1';

FLUSH PRIVILEGES;
