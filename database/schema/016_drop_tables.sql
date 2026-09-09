CREATE TABLE IF NOT EXISTS `drop_tables` (
    `Id` int NOT NULL,
    `MonsterId` int NOT NULL,
    `Name` varchar(128) NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_DropTables_MonsterId` (`MonsterId`),
    CONSTRAINT `FK_DropTables_Monsters_MonsterId` FOREIGN KEY (`MonsterId`) REFERENCES `monsters` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `drop_table_items` (
    `DropTableId` int NOT NULL,
    `ItemId` int NOT NULL,
    `ChancePerMillion` int NOT NULL,
    `MinQuantity` int NOT NULL,
    `MaxQuantity` int NOT NULL,
    PRIMARY KEY (`DropTableId`, `ItemId`),
    KEY `IX_DropTableItems_ItemId` (`ItemId`),
    CONSTRAINT `FK_DropTableItems_DropTables_DropTableId` FOREIGN KEY (`DropTableId`) REFERENCES `drop_tables` (`Id`),
    CONSTRAINT `FK_DropTableItems_Items_ItemId` FOREIGN KEY (`ItemId`) REFERENCES `items` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
