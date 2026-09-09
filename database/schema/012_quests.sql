CREATE TABLE IF NOT EXISTS `quests` (
    `Id` int NOT NULL,
    `Code` varchar(64) NOT NULL,
    `Name` varchar(128) NOT NULL,
    `RequiredLevel` int NOT NULL,
    `StartNpcId` int NULL,
    `EndNpcId` int NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_Quests_Code` (`Code`),
    KEY `IX_Quests_StartNpcId` (`StartNpcId`),
    KEY `IX_Quests_EndNpcId` (`EndNpcId`),
    CONSTRAINT `FK_Quests_Npcs_StartNpcId` FOREIGN KEY (`StartNpcId`) REFERENCES `npcs` (`Id`),
    CONSTRAINT `FK_Quests_Npcs_EndNpcId` FOREIGN KEY (`EndNpcId`) REFERENCES `npcs` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
