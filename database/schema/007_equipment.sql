CREATE TABLE IF NOT EXISTS `equipment_slots` (
    `CharacterId` bigint NOT NULL,
    `SlotName` varchar(32) NOT NULL,
    `ItemId` int NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`CharacterId`, `SlotName`),
    KEY `IX_EquipmentSlots_ItemId` (`ItemId`),
    CONSTRAINT `FK_EquipmentSlots_Characters_CharacterId` FOREIGN KEY (`CharacterId`) REFERENCES `characters` (`Id`),
    CONSTRAINT `FK_EquipmentSlots_Items_ItemId` FOREIGN KEY (`ItemId`) REFERENCES `items` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
