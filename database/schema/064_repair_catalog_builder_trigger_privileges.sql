GRANT TRIGGER ON `god2_game`.`monsters`
TO `god2_catalog_builder`@`localhost`;

GRANT INSERT ON `god2_game_meta`.`admin_change_audit`
TO `god2_catalog_builder`@`localhost`;

GRANT SELECT, INSERT, UPDATE ON `god2_game_meta`.`admin_field_locks`
TO `god2_catalog_builder`@`localhost`;
