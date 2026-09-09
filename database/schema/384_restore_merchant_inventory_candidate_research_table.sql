CREATE DATABASE IF NOT EXISTS `god2_research`
    DEFAULT CHARACTER SET utf8mb4
    DEFAULT COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`merchant_inventory_candidates`
(
    `CandidateId` char(64) NOT NULL,
    `RunId` char(36) NOT NULL,
    `MerchantId` int NULL,
    `ClientInventoryGroupId` int NOT NULL,
    `ItemId` int NOT NULL,
    `BuyPrice` bigint NULL,
    `SellPrice` bigint NULL,
    `QuantityLimit` int NULL,
    `RefreshPolicy` varchar(32) NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `RelationshipEvidenceStatus` varchar(32) NOT NULL DEFAULT 'Candidate',
    `PriceEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    `Enabled` tinyint(1) NOT NULL DEFAULT 0,
    `ProductionSaleEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `PromotionRunId` char(36) NULL,
    `SourceHash` char(64) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`CandidateId`),
    KEY `IX_MerchantInventoryCandidates_Run` (`RunId`),
    KEY `IX_MerchantInventoryCandidates_Group` (`ClientInventoryGroupId`),
    KEY `IX_MerchantInventoryCandidates_Item` (`ItemId`),
    KEY `IX_MerchantInventoryCandidates_Merchant` (`MerchantId`),
    KEY `IX_MerchantInventoryCandidates_Promotion` (`PromotionRunId`,`ProductionSaleEnabled`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `god2_research`.`merchant_inventory_candidates`
    ADD COLUMN IF NOT EXISTS `RelationshipEvidenceStatus` varchar(32) NOT NULL DEFAULT 'Candidate',
    ADD COLUMN IF NOT EXISTS `PriceEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked',
    ADD COLUMN IF NOT EXISTS `ProductionSaleEnabled` tinyint(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `PromotionRunId` char(36) NULL;

CREATE INDEX IF NOT EXISTS `IX_MerchantInventoryCandidates_Promotion`
    ON `god2_research`.`merchant_inventory_candidates` (`PromotionRunId`,`ProductionSaleEnabled`);
