CREATE DATABASE IF NOT EXISTS `god2_research` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

SET @god2_previous_foreign_key_checks = @@FOREIGN_KEY_CHECKS;
SET FOREIGN_KEY_CHECKS=0;

RENAME TABLE
    `god2`.`content_phase3_field_closure` TO `god2_research`.`content_phase3_field_closure`,
    `god2`.`content_phase3_promotions` TO `god2_research`.`content_phase3_promotions`,
    `god2`.`content_production_manifest` TO `god2_research`.`content_production_manifest`;

SET FOREIGN_KEY_CHECKS=@god2_previous_foreign_key_checks;

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_phase3_field_closure` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_phase3_field_closure` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_phase3_field_closure` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_phase3_field_closure` TO 'god2_catalog_builder'@'127.0.0.1';

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_phase3_promotions` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_phase3_promotions` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_phase3_promotions` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_phase3_promotions` TO 'god2_catalog_builder'@'127.0.0.1';

GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_production_manifest` TO 'god2_server'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_production_manifest` TO 'god2_server'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_production_manifest` TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON `god2_research`.`content_production_manifest` TO 'god2_catalog_builder'@'127.0.0.1';

FLUSH PRIVILEGES;
