CREATE TABLE IF NOT EXISTS `inventory_item_identity_sequence` (
    `PersistentInventoryItemId` bigint NOT NULL AUTO_INCREMENT,
    `ReservedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`PersistentInventoryItemId`)
) ENGINE=InnoDB AUTO_INCREMENT=8000000000000000000 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `inventory_slots`
    MODIFY COLUMN `PersistentInventoryItemId` bigint NOT NULL,
    ADD UNIQUE INDEX IF NOT EXISTS `UX_InventorySlots_PersistentInventoryItemId` (`PersistentInventoryItemId`);
