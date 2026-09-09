CREATE TABLE IF NOT EXISTS `monsters` (
    `Id` int NOT NULL,
    `Code` varchar(64) NOT NULL,
    `Name` varchar(128) NOT NULL,
    `Level` int NOT NULL,
    `MaxHp` bigint NOT NULL,
    `Attack` int NOT NULL,
    `Defense` int NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_Monsters_Code` (`Code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
