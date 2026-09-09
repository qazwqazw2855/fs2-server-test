CREATE TABLE IF NOT EXISTS `battle_pets` (
    `Id` int NOT NULL,
    `Code` varchar(64) NOT NULL,
    `Name` varchar(128) NOT NULL,
    `BaseLevel` int NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_BattlePets_Code` (`Code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
