CREATE TABLE IF NOT EXISTS `inventory_slots` (
    `CharacterId` bigint NOT NULL,
    `SlotIndex` int NOT NULL,
    `ItemId` int NOT NULL,
    `Quantity` int NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`CharacterId`, `SlotIndex`),
    KEY `IX_InventorySlots_ItemId` (`ItemId`),
    CONSTRAINT `FK_InventorySlots_Characters_CharacterId` FOREIGN KEY (`CharacterId`) REFERENCES `characters` (`Id`),
    CONSTRAINT `FK_InventorySlots_Items_ItemId` FOREIGN KEY (`ItemId`) REFERENCES `items` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
