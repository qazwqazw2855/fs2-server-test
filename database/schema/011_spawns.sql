CREATE TABLE IF NOT EXISTS `spawns` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `MapId` int NOT NULL,
    `MonsterId` int NOT NULL,
    `PositionX` int NOT NULL,
    `PositionY` int NOT NULL,
    `RespawnSeconds` int NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_Spawns_MapId` (`MapId`),
    KEY `IX_Spawns_MonsterId` (`MonsterId`),
    CONSTRAINT `FK_Spawns_Maps_MapId` FOREIGN KEY (`MapId`) REFERENCES `maps` (`Id`),
    CONSTRAINT `FK_Spawns_Monsters_MonsterId` FOREIGN KEY (`MonsterId`) REFERENCES `monsters` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
