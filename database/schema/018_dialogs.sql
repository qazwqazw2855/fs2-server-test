CREATE TABLE IF NOT EXISTS `dialogs` (
    `Id` int NOT NULL,
    `Code` varchar(64) NOT NULL,
    `NpcId` int NULL,
    `TextKey` varchar(128) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_Dialogs_Code` (`Code`),
    KEY `IX_Dialogs_NpcId` (`NpcId`),
    CONSTRAINT `FK_Dialogs_Npcs_NpcId` FOREIGN KEY (`NpcId`) REFERENCES `npcs` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
