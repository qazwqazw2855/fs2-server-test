CREATE TABLE IF NOT EXISTS `merchants` (
    `Id` int NOT NULL,
    `NpcId` int NOT NULL,
    `Name` varchar(128) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_Merchants_NpcId` (`NpcId`),
    CONSTRAINT `FK_Merchants_Npcs_NpcId` FOREIGN KEY (`NpcId`) REFERENCES `npcs` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `merchant_items` (
    `MerchantId` int NOT NULL,
    `ItemId` int NOT NULL,
    `Price` bigint NOT NULL,
    PRIMARY KEY (`MerchantId`, `ItemId`),
    KEY `IX_MerchantItems_ItemId` (`ItemId`),
    CONSTRAINT `FK_MerchantItems_Merchants_MerchantId` FOREIGN KEY (`MerchantId`) REFERENCES `merchants` (`Id`),
    CONSTRAINT `FK_MerchantItems_Items_ItemId` FOREIGN KEY (`ItemId`) REFERENCES `items` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
