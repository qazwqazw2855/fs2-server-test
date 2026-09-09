CREATE TABLE IF NOT EXISTS `rewards` (
    `Id` int NOT NULL,
    `Code` varchar(64) NOT NULL,
    `Name` varchar(128) NOT NULL,
    `CurrencyAmount` bigint NOT NULL,
    `ExperienceAmount` bigint NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_Rewards_Code` (`Code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `reward_items` (
    `RewardId` int NOT NULL,
    `ItemId` int NOT NULL,
    `Quantity` int NOT NULL,
    PRIMARY KEY (`RewardId`, `ItemId`),
    KEY `IX_RewardItems_ItemId` (`ItemId`),
    CONSTRAINT `FK_RewardItems_Rewards_RewardId` FOREIGN KEY (`RewardId`) REFERENCES `rewards` (`Id`),
    CONSTRAINT `FK_RewardItems_Items_ItemId` FOREIGN KEY (`ItemId`) REFERENCES `items` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
