CREATE TABLE IF NOT EXISTS `client_map_identities` (
    `MapId` int NOT NULL,
    `ClientBuildId` varchar(64) NOT NULL,
    `ClientMapId` smallint unsigned NOT NULL,
    `ClientAreaId` tinyint unsigned NOT NULL,
    `ResourceIdentity` varchar(191) NOT NULL,
    `CoordinateScaleX` decimal(18,9) NOT NULL,
    `CoordinateScaleY` decimal(18,9) NOT NULL,
    `CoordinateOffsetX` decimal(18,9) NOT NULL DEFAULT 0,
    `CoordinateOffsetY` decimal(18,9) NOT NULL DEFAULT 0,
    `IdentityEvidenceStatus` varchar(32) NOT NULL,
    `CoordinateEvidenceStatus` varchar(32) NOT NULL,
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `SourceType` varchar(64) NOT NULL,
    `SourceIdentity` varchar(256) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `EvidenceReference` varchar(512) NOT NULL,
    `ProvenanceJson` json NOT NULL,
    `FieldEvidenceJson` json NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`MapId`, `ClientBuildId`),
    UNIQUE KEY `UX_ClientMapIdentities_ClientIdentity` (`ClientBuildId`, `ClientMapId`, `ClientAreaId`),
    KEY `IX_ClientMapIdentities_Production` (`ClientBuildId`, `ProductionEnabled`),
    CONSTRAINT `FK_ClientMapIdentities_Map` FOREIGN KEY (`MapId`) REFERENCES `maps` (`Id`),
    CONSTRAINT `CK_ClientMapIdentities_ClientMapRange` CHECK (`ClientMapId` <= 1023),
    CONSTRAINT `CK_ClientMapIdentities_ClientAreaRange` CHECK (`ClientAreaId` <= 63),
    CONSTRAINT `CK_ClientMapIdentities_IdentityEvidence` CHECK (`IdentityEvidenceStatus` IN
        ('Verified','Derived','Candidate','EvidenceBlocked')),
    CONSTRAINT `CK_ClientMapIdentities_CoordinateEvidence` CHECK (`CoordinateEvidenceStatus` IN
        ('Verified','Derived','Candidate','EvidenceBlocked')),
    CONSTRAINT `CK_ClientMapIdentities_ProductionGate` CHECK (
        `ProductionEnabled` = 0 OR (
            `IdentityEvidenceStatus` IN ('Verified','Derived') AND
            `CoordinateEvidenceStatus` IN ('Verified','Derived') AND
            `CoordinateScaleX` > 0 AND
            `CoordinateScaleY` > 0 AND
            CHAR_LENGTH(`ResourceIdentity`) > 0 AND
            CHAR_LENGTH(`SourceHash`) = 64 AND
            CHAR_LENGTH(`EvidenceReference`) > 0
        )
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
