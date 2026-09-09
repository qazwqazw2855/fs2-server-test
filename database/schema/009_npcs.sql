CREATE TABLE IF NOT EXISTS `npcs` (
    `Id` int NOT NULL,
    `Code` varchar(64) NOT NULL,
    `Name` varchar(128) NOT NULL,
    `MapId` int NOT NULL,
    `PositionX` int NOT NULL,
    `PositionY` int NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_Npcs_Code` (`Code`),
    KEY `IX_Npcs_MapId` (`MapId`),
    CONSTRAINT `FK_Npcs_Maps_MapId` FOREIGN KEY (`MapId`) REFERENCES `maps` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
