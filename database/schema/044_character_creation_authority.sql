CREATE TABLE IF NOT EXISTS `character_creation_profiles` (
    `ClientBuildId` varchar(64) NOT NULL,
    `Class` varchar(32) NOT NULL,
    `Gender` varchar(32) NOT NULL,
    `LifeSkill` varchar(32) NOT NULL,
    `MapId` int NOT NULL,
    `PositionX` int NOT NULL,
    `PositionY` int NOT NULL,
    `EvidenceStatus` varchar(32) NOT NULL,
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(512) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    `UpdatedAtUtc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`ClientBuildId`, `Class`, `Gender`, `LifeSkill`),
    KEY `IX_CharacterCreationProfiles_Map` (`MapId`, `ProductionEnabled`),
    CONSTRAINT `FK_CharacterCreationProfiles_Map`
        FOREIGN KEY (`MapId`) REFERENCES `maps` (`Id`),
    CONSTRAINT `CK_CharacterCreationProfiles_EvidenceStatus`
        CHECK (`EvidenceStatus` IN ('Verified', 'Derived', 'Candidate', 'EvidenceBlocked', 'NotApplicable')),
    CONSTRAINT `CK_CharacterCreationProfiles_ProductionGate`
        CHECK (`ProductionEnabled` = 0 OR `EvidenceStatus` IN ('Verified', 'Derived')),
    CONSTRAINT `CK_CharacterCreationProfiles_Coordinates`
        CHECK (`PositionX` >= 0 AND `PositionY` >= 0),
    CONSTRAINT `CK_CharacterCreationProfiles_EvidenceReference`
        CHECK (CHAR_LENGTH(TRIM(`EvidenceReference`)) > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- No profile is seeded here. A creation location is production authority only
-- after current-build class/gender/life-skill identity and official spawn
-- coordinates have been independently proven and explicitly promoted.
