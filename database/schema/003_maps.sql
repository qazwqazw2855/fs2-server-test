CREATE TABLE IF NOT EXISTS `maps` (
    `Id` int NOT NULL,
    `Code` varchar(64) NOT NULL,
    `Name` varchar(128) NOT NULL,
    `Width` int NOT NULL,
    `Height` int NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_Maps_Code` (`Code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
