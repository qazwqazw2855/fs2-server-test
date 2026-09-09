CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

UPDATE `god2_game_meta`.`field_mappings`
SET `source_schema`='god2_research'
WHERE `source_schema`='god2'
  AND `source_table`='content_field_evidence';

DROP TABLE IF EXISTS `god2_research`.`content_field_evidence`;

RENAME TABLE `god2`.`content_field_evidence` TO `god2_research`.`content_field_evidence`;
