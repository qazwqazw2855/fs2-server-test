CREATE TABLE IF NOT EXISTS `localization_entries` (
    `Language` varchar(16) NOT NULL,
    `TextKey` varchar(128) NOT NULL,
    `TextValue` text NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Language`, `TextKey`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
