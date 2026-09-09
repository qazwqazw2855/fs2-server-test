CREATE TABLE IF NOT EXISTS `immortals` (
    `Id` int NOT NULL,
    `Code` varchar(64) NOT NULL,
    `Name` varchar(128) NOT NULL,
    `Rank` int NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_Immortals_Code` (`Code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
