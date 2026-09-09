ALTER TABLE `npc_spawns`
    ADD COLUMN `OfficialResourceType` tinyint unsigned NULL AFTER `ObservedClientEntityHandle`,
    ADD COLUMN `OfficialResourceOrdinal` tinyint unsigned NULL AFTER `OfficialResourceType`,
    ADD COLUMN `OfficialSelectorHighBits` tinyint unsigned NULL AFTER `OfficialResourceOrdinal`,
    ADD COLUMN `OfficialDirectionCode` tinyint unsigned NULL AFTER `OfficialSelectorHighBits`,
    ADD COLUMN `OfficialStateCode` tinyint unsigned NULL AFTER `OfficialDirectionCode`,
    ADD COLUMN `WireEvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked' AFTER `OfficialStateCode`,
    ADD COLUMN `ApplicationMessageSha256` char(64) NULL AFTER `WireEvidenceStatus`,
    ADD COLUMN `OpaqueTemplateSha256` char(64) NULL AFTER `ApplicationMessageSha256`,
    ADD CONSTRAINT `CK_NpcSpawns_OfficialResourceType`
        CHECK (`OfficialResourceType` IS NULL OR `OfficialResourceType` BETWEEN 0 AND 31),
    ADD CONSTRAINT `CK_NpcSpawns_OfficialResourceOrdinal`
        CHECK (`OfficialResourceOrdinal` IS NULL OR `OfficialResourceOrdinal` BETWEEN 0 AND 254),
    ADD CONSTRAINT `CK_NpcSpawns_OfficialSelectorHighBits`
        CHECK (`OfficialSelectorHighBits` IS NULL OR `OfficialSelectorHighBits` BETWEEN 0 AND 7),
    ADD CONSTRAINT `CK_NpcSpawns_OfficialDirectionCode`
        CHECK (`OfficialDirectionCode` IS NULL OR `OfficialDirectionCode` BETWEEN 0 AND 7),
    ADD CONSTRAINT `CK_NpcSpawns_OfficialStateCode`
        CHECK (`OfficialStateCode` IS NULL OR `OfficialStateCode` BETWEEN 0 AND 31),
    ADD CONSTRAINT `CK_NpcSpawns_WireEvidenceStatus`
        CHECK (`WireEvidenceStatus` IN ('Verified','Derived','Candidate','EvidenceBlocked','Deprecated'));

UPDATE `npc_spawns`
SET `OfficialResourceType`=2,
    `OfficialResourceOrdinal`=142,
    `OfficialSelectorHighBits`=3,
    `OfficialDirectionCode`=4,
    `OfficialStateCode`=1,
    `WireEvidenceStatus`='Verified',
    `ApplicationMessageSha256`='83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B',
    `OpaqueTemplateSha256`='C0CBF5CDFAC3669D695E66D9C789AAA027360488024C1C5E4323311364A419C0',
    `FieldEvidenceJson`=JSON_SET(
        COALESCE(`FieldEvidenceJson`, JSON_OBJECT()),
        '$.officialResourceSelector', 'S2C 0x72 payload offsets 4-5 and Client RVA 0x000904B1-0x000904E7',
        '$.officialDirectionCode', 'S2C 0x72 payload offset 8 high three bits and Client RVA 0x000F8188-0x000F8191',
        '$.officialStateCode', 'S2C 0x72 payload offset 8 low five bits and Client RVA 0x000F820B-0x000F8221',
        '$.opaqueTemplate', 'Build-pinned ignored/preserved region; SHA-256 only')
WHERE `Id`=316049902
  AND `NpcId`=1075128734
  AND `ClientBuildId`='god2-opt-6b127086e0c0'
  AND `ObservedClientEntityHandle`=1504
  AND `SourceHash`='a19ffb01241c8b49f8ce4014af18516f51874f235488dd8a0ff0c0c3a3bd57c8'
  AND `ProductionEnabled`=1;

UPDATE `npc_spawns`
SET `OfficialResourceType`=2,
    `OfficialResourceOrdinal`=142,
    `OfficialSelectorHighBits`=2,
    `OfficialDirectionCode`=5,
    `OfficialStateCode`=1,
    `WireEvidenceStatus`='Verified',
    `ApplicationMessageSha256`='7E3AC167DEF745DE3BE7D38C22B4C0ADC1419E6DC20851F8FA3CD22D61CCA644',
    `OpaqueTemplateSha256`='C0CBF5CDFAC3669D695E66D9C789AAA027360488024C1C5E4323311364A419C0',
    `FieldEvidenceJson`=JSON_SET(
        COALESCE(`FieldEvidenceJson`, JSON_OBJECT()),
        '$.officialResourceSelector', 'S2C 0x72 payload offsets 4-5 and Client RVA 0x000904B1-0x000904E7',
        '$.officialDirectionCode', 'S2C 0x72 payload offset 8 high three bits and Client RVA 0x000F8188-0x000F8191',
        '$.officialStateCode', 'S2C 0x72 payload offset 8 low five bits and Client RVA 0x000F820B-0x000F8221',
        '$.opaqueTemplate', 'Build-pinned ignored/preserved region; SHA-256 only')
WHERE `Id`=2124220824
  AND `NpcId`=1075128734
  AND `ClientBuildId`='god2-opt-6b127086e0c0'
  AND `ObservedClientEntityHandle`=4638
  AND `SourceHash`='a19ffb01241c8b49f8ce4014af18516f51874f235488dd8a0ff0c0c3a3bd57c8'
  AND `ProductionEnabled`=1;
