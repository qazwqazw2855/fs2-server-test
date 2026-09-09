CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

RENAME TABLE
    `god2`.`content_client_table_layouts` TO `god2_research`.`content_client_table_layouts`,
    `god2`.`content_localization_audit` TO `god2_research`.`content_localization_audit`,
    `god2`.`content_source_inventory` TO `god2_research`.`content_source_inventory`,
    `god2`.`content_validated_records` TO `god2_research`.`content_validated_records`,
    `god2`.`content_validation_results` TO `god2_research`.`content_validation_results`;
