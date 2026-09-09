CREATE TABLE IF NOT EXISTS `characters` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `AccountId` bigint NOT NULL,
    `Name` varchar(32) NOT NULL,
    `MapId` int NOT NULL,
    `PositionX` int NOT NULL,
    `PositionY` int NOT NULL,
    `Level` int NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `ConcurrencyToken` varchar(64) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_Characters_Name` (`Name`),
    KEY `IX_Characters_AccountId` (`AccountId`),
    CONSTRAINT `FK_Characters_Accounts_AccountId` FOREIGN KEY (`AccountId`) REFERENCES `accounts` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
