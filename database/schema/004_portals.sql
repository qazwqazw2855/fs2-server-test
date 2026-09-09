CREATE TABLE IF NOT EXISTS `portals` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `SourceMapId` int NOT NULL,
    `SourceX` int NOT NULL,
    `SourceY` int NOT NULL,
    `TargetMapId` int NOT NULL,
    `TargetX` int NOT NULL,
    `TargetY` int NOT NULL,
    `Name` varchar(128) NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_Portals_SourceMapId` (`SourceMapId`),
    KEY `IX_Portals_TargetMapId` (`TargetMapId`),
    CONSTRAINT `FK_Portals_Maps_SourceMapId` FOREIGN KEY (`SourceMapId`) REFERENCES `maps` (`Id`),
    CONSTRAINT `FK_Portals_Maps_TargetMapId` FOREIGN KEY (`TargetMapId`) REFERENCES `maps` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
