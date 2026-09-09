CREATE TABLE IF NOT EXISTS `npc_client_identities` (
    `NpcId` int NOT NULL,
    `ClientBuildId` varchar(64) NOT NULL,
    `ResourceType` int NOT NULL,
    `ResourceOrdinal` int NOT NULL,
    `NpcCsvSourceRow` int NOT NULL,
    `ResourceKey` varchar(256) NOT NULL,
    `IdentityEvidenceStatus` varchar(32) NOT NULL,
    `SourceType` varchar(64) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `EvidenceReference` varchar(512) NOT NULL,
    `ProvenanceJson` json NOT NULL,
    `FieldEvidenceJson` json NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`NpcId`, `ClientBuildId`),
    UNIQUE KEY `UX_NpcClientIdentities_Resource` (`ClientBuildId`, `ResourceType`, `ResourceOrdinal`),
    CONSTRAINT `FK_NpcClientIdentities_Npc` FOREIGN KEY (`NpcId`) REFERENCES `npcs` (`Id`),
    CONSTRAINT `CK_NpcClientIdentities_State` CHECK (`IdentityEvidenceStatus` IN
        ('Verified','Derived','Candidate','EvidenceBlocked','Deprecated'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `npc_spawns` (
    `Id` int NOT NULL,
    `NpcId` int NOT NULL,
    `MapId` int NOT NULL,
    `PositionX` int NOT NULL,
    `PositionY` int NOT NULL,
    `Direction` int NULL,
    `SpawnCondition` varchar(128) NOT NULL,
    `ClientBuildId` varchar(64) NOT NULL,
    `ObservedClientEntityHandle` int unsigned NULL,
    `IdentityEvidenceStatus` varchar(32) NOT NULL,
    `CoordinateEvidenceStatus` varchar(32) NOT NULL,
    `ServiceEvidenceStatus` varchar(32) NOT NULL,
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `SourceType` varchar(64) NOT NULL,
    `SourceIdentity` varchar(512) NOT NULL,
    `SourceHash` char(64) NOT NULL,
    `EvidenceReference` varchar(512) NOT NULL,
    `ProvenanceJson` json NOT NULL,
    `FieldEvidenceJson` json NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_NpcSpawns_ObservedEntity` (`ClientBuildId`, `SourceHash`, `ObservedClientEntityHandle`),
    KEY `IX_NpcSpawns_Map` (`MapId`, `ProductionEnabled`),
    KEY `IX_NpcSpawns_Npc` (`NpcId`, `ProductionEnabled`),
    CONSTRAINT `FK_NpcSpawns_Npc` FOREIGN KEY (`NpcId`) REFERENCES `npcs` (`Id`),
    CONSTRAINT `FK_NpcSpawns_Map` FOREIGN KEY (`MapId`) REFERENCES `maps` (`Id`),
    CONSTRAINT `FK_NpcSpawns_ClientIdentity` FOREIGN KEY (`NpcId`, `ClientBuildId`)
        REFERENCES `npc_client_identities` (`NpcId`, `ClientBuildId`),
    CONSTRAINT `CK_NpcSpawns_IdentityState` CHECK (`IdentityEvidenceStatus` IN
        ('Verified','Derived','Candidate','EvidenceBlocked','Deprecated')),
    CONSTRAINT `CK_NpcSpawns_CoordinateState` CHECK (`CoordinateEvidenceStatus` IN
        ('Verified','Derived','Candidate','EvidenceBlocked','NotApplicable','Deprecated')),
    CONSTRAINT `CK_NpcSpawns_ServiceState` CHECK (`ServiceEvidenceStatus` IN
        ('Verified','Derived','Candidate','EvidenceBlocked','NotApplicable','Deprecated'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `npc_client_identities` (
    `NpcId`, `ClientBuildId`, `ResourceType`, `ResourceOrdinal`, `NpcCsvSourceRow`,
    `ResourceKey`, `IdentityEvidenceStatus`, `SourceType`, `SourceIdentity`, `SourceHash`,
    `EvidenceReference`, `ProvenanceJson`, `FieldEvidenceJson`, `CreatedAtUtc`, `UpdatedAtUtc`)
SELECT
    npc.`Id`,
    'god2-opt-6b127086e0c0',
    2,
    142,
    249,
    'data2/rom/npc/npc2643.ROM',
    'Verified',
    'OfficialClientStaticConsumerAndResource',
    'God2_opt:rva-00082270->00082A90->00031AF0:NPC.csv:type-2:ordinal-142',
    '8c190363dee7ce69ebc6dde92aca71101832040f40ec86336ae0a90fe4082127',
    'Reports/RoadMap.M2.RealContentSlice.md',
    JSON_OBJECT(
        'officialClientBuildSha256', '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B',
        'runtimeCodeSha256', 'E997DB114BD71E0BD5046D64A53D7D0C1EE78DD96A8C2D5BF224B638A2D2077A',
        'npcCsvSourceHash', validated.`SourceHash`,
        'npcCsvNormalizedHash', validated.`NormalizedHash`,
        'npcCsvSourceRow', 249,
        'resourceType', 2,
        'resourceOrdinal', 142,
        'resourceLookupRvas', JSON_ARRAY('0x00082270','0x00082A90','0x00031AF0')),
    JSON_OBJECT(
        'formalNpcId', 'Exact content_production_manifest TargetRowIdentity',
        'resourceType', 'S2C 0x72 payload byte 5 low five bits',
        'resourceOrdinal', 'S2C 0x72 payload byte 4 minus one',
        'resourceKey', 'Exact NPC.csvZ grouped row lookup'),
    UTC_TIMESTAMP(6),
    UTC_TIMESTAMP(6)
FROM `npcs` npc
JOIN `content_production_manifest` manifest
  ON manifest.`Domain`='NpcTemplate'
 AND manifest.`AuthorityKey`='client:npc-template/row-249'
 AND manifest.`TargetRowIdentity`=CAST(npc.`Id` AS char)
JOIN `content_validated_records` validated
  ON validated.`RunId`=manifest.`RunId`
 AND validated.`Domain`=manifest.`Domain`
 AND validated.`AuthorityKey`=manifest.`AuthorityKey`
WHERE npc.`Id`=1075128734
  AND npc.`NameZhTw`='雜貨老闆'
  AND npc.`LocalizationStatus` IN ('TraditionalVerified','ConvertedToTraditional','MixedLanguageNormalized')
  AND npc.`OfficialIdentityStatus`='Verified'
  AND npc.`InteractionFamily`='Merchant'
  AND validated.`SourceHash`='8c190363dee7ce69ebc6dde92aca71101832040f40ec86336ae0a90fe4082127'
  AND validated.`NormalizedHash`='0583d7fef8b8b7a47930179adfcb0f94c2f748c72d9b182da4c667e199f24b33'
  AND JSON_UNQUOTE(JSON_EXTRACT(validated.`NormalizedData`, '$.resourceKey'))='data2/rom/npc/npc2643.ROM'
  AND NOT EXISTS (
      SELECT 1 FROM `npc_client_identities` existing
      WHERE existing.`NpcId`=npc.`Id`
        AND existing.`ClientBuildId`='god2-opt-6b127086e0c0');

INSERT INTO `npc_spawns` (
    `Id`, `NpcId`, `MapId`, `PositionX`, `PositionY`, `Direction`, `SpawnCondition`,
    `ClientBuildId`, `ObservedClientEntityHandle`, `IdentityEvidenceStatus`,
    `CoordinateEvidenceStatus`, `ServiceEvidenceStatus`, `ProductionEnabled`,
    `SourceType`, `SourceIdentity`, `SourceHash`, `EvidenceReference`,
    `ProvenanceJson`, `FieldEvidenceJson`, `CreatedAtUtc`, `UpdatedAtUtc`)
SELECT
    candidate.`SpawnId`, identity.`NpcId`, map.`Id`, candidate.`PositionX`, candidate.`PositionY`, NULL, 'Always',
    identity.`ClientBuildId`, candidate.`ObservedEntityHandle`, 'Verified', 'Verified', 'Derived', 1,
    'OfficialClientBootstrapAndStaticConsumer',
    CONCAT('God2_opt:Map19:WorldStaticStateBootstrap:entity-', candidate.`ObservedEntityHandle`),
    'a19ffb01241c8b49f8ce4014af18516f51874f235488dd8a0ff0c0c3a3bd57c8',
    'Reports/RoadMap.M2.RealContentSlice.md',
    JSON_OBJECT(
        'officialClientBuildSha256', '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B',
        'runtimeCodeSha256', 'E997DB114BD71E0BD5046D64A53D7D0C1EE78DD96A8C2D5BF224B638A2D2077A',
        'worldStaticStateBootstrapSha256', 'A19FFB01241C8B49F8CE4014AF18516F51874F235488DD8A0FF0C0C3A3BD57C8',
        'applicationMessageSha256', candidate.`MessageSha256`,
        'observedClientEntityHandle', candidate.`ObservedEntityHandle`,
        'entityHandleScope', 'SessionEvidenceOnly',
        'packedPosition', candidate.`PackedPosition`,
        'positionConsumerRvas', JSON_ARRAY('0x000F7FD0','0x000FB000')),
    JSON_OBJECT(
        'npcIdentity', 'npc_client_identities exact type/ordinal resource lookup',
        'mapIdentity', 'client_map_identities ClientMapId=19 ClientAreaId=4',
        'coordinateFormula', 'x=(packed>>2)&0x7FFF;y=packed>>17',
        'bounds', '0<=x<Width*21;0<=y<Height*21',
        'service', 'NPC.csv type label 老闆NPC plus formal InteractionFamily=Merchant'),
    UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
FROM (
    SELECT 316049902 AS `SpawnId`, 1504 AS `ObservedEntityHandle`, 17 AS `PositionX`, 8 AS `PositionY`,
           1048646 AS `PackedPosition`, '83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B' AS `MessageSha256`
    UNION ALL
    SELECT 2124220824, 4638, 14, 14, 1835066,
           '7E3AC167DEF745DE3BE7D38C22B4C0ADC1419E6DC20851F8FA3CD22D61CCA644'
) candidate
JOIN `npc_client_identities` identity
  ON identity.`NpcId`=1075128734
 AND identity.`ClientBuildId`='god2-opt-6b127086e0c0'
 AND identity.`ResourceType`=2
 AND identity.`ResourceOrdinal`=142
 AND identity.`NpcCsvSourceRow`=249
 AND identity.`ResourceKey`='data2/rom/npc/npc2643.ROM'
 AND identity.`IdentityEvidenceStatus`='Verified'
JOIN `client_map_identities` mapIdentity
  ON mapIdentity.`ClientBuildId`=identity.`ClientBuildId`
 AND mapIdentity.`ClientMapId`=19
 AND mapIdentity.`ClientAreaId`=4
 AND mapIdentity.`IdentityEvidenceStatus`='Verified'
 AND mapIdentity.`CoordinateEvidenceStatus`='Verified'
 AND mapIdentity.`ProductionEnabled`=1
JOIN `maps` map
  ON map.`Id`=mapIdentity.`MapId`
 AND map.`Id`=557790525
 AND map.`PayloadSha256`='967cb770094c654b074d24dda2132f8a531932e4135a6c369649c67a3ae5bb82'
 AND JSON_UNQUOTE(JSON_EXTRACT(map.`PayloadJson`, '$.resourceName'))='indoor/groceryl.hmd'
WHERE candidate.`PositionX` BETWEEN 0 AND ((map.`Width` * 21) - 1)
  AND candidate.`PositionY` BETWEEN 0 AND ((map.`Height` * 21) - 1)
  AND NOT EXISTS (SELECT 1 FROM `npc_spawns` existing WHERE existing.`Id`=candidate.`SpawnId`);
