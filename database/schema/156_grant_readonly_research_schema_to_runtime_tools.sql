GRANT SELECT ON `god2_research`.* TO 'god2_server'@'localhost';
GRANT SELECT ON `god2_research`.* TO 'god2_server'@'127.0.0.1';
GRANT SELECT ON `god2_research`.* TO 'god2_catalog_builder'@'localhost';
GRANT SELECT ON `god2_research`.* TO 'god2_catalog_builder'@'127.0.0.1';

FLUSH PRIVILEGES;
