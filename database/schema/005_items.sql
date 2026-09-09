CREATE TABLE IF NOT EXISTS `items` (
    `Id` int NOT NULL,
    `Code` varchar(64) NOT NULL,
    `Name` varchar(128) NOT NULL,
    `ItemType` varchar(32) NOT NULL,
    `MaxStack` int NOT NULL,
    `SellPrice` bigint NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_Items_Code` (`Code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
