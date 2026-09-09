-- 037 correctly failed closed because the Phase 1 normalized record calls this field
-- resourceKey rather than resourceName. Keep 037 immutable and apply the corrected exact gate.
INSERT INTO `maps` (
    `Id`, `Code`, `Name`, `Width`, `Height`, `CreatedAtUtc`, `UpdatedAtUtc`,
    `RecoveryStatus`, `SourceReference`, `PayloadJson`, `PayloadSha256`, `ImportedAtUtc`,
    `OriginalName`, `NameZhTw`, `EvidenceStatus`, `LocalizationStatus`, `ContentRecoveryRunId`)
SELECT
    557790525,
    'r_967cb770094c654b074d24dda2132f8a',
    'Island01/indoor/groceryL',
    10,
    30,
    UTC_TIMESTAMP(6),
    UTC_TIMESTAMP(6),
    'Verified',
    'OfficialClientFileIo:Map19:Island01/indoor/groceryL.hmdZ',
    '{"id":"client:map/island01/indoor/groceryl","numericMapId":19,"area":"island01","resourceName":"indoor/groceryl.hmd","resourceType":"HMD","gridWidth":10,"gridHeight":30,"coordinateScale":1,"sourceCanSha256":"f2b9ca9dc4dcebcd1c1c40de14ea63f1b2061993f644c49b9c7baca9ce2d2f97","packedResourceSha256":"42d769c0ff328db3e1e295e17208d0db7a0ff12608976f952dae800ff79c3c00","decodedResourceSha256":"95c178a9babc34ff116cd9022b7ff33716eaff783df14a46419adb2a7d9a4230","evidence":"Reports/RoadMap.M1.MapIdentityEvidence.md"}',
    '967cb770094c654b074d24dda2132f8a531932e4135a6c369649c67a3ae5bb82',
    UTC_TIMESTAMP(6),
    'Island01/indoor/groceryL',
    'Island01/indoor/groceryL',
    'Verified',
    'NotApplicable',
    evidence.`RunId`
FROM `content_validated_records` evidence
WHERE evidence.`Domain` = 'Map'
  AND evidence.`RunId` = '552b1a69-7a75-422a-b071-fa9e5e6c3bb4'
  AND evidence.`AuthorityKey` = 'client:map/island01/indoor/groceryl'
  AND evidence.`SourceHash` = 'f2b9ca9dc4dcebcd1c1c40de14ea63f1b2061993f644c49b9c7baca9ce2d2f97'
  AND evidence.`NormalizedHash` = '5c2722a664300f1561c81b02196f76f8b59206e8bef6a2f52293d50eae167ec4'
  AND JSON_UNQUOTE(JSON_EXTRACT(evidence.`NormalizedData`, '$.resourceKey')) = 'indoor/groceryl.hmd'
  AND NOT EXISTS (SELECT 1 FROM `maps` existing WHERE existing.`Id` = 557790525)
  AND NOT EXISTS (
      SELECT 1 FROM `maps` existing
      WHERE existing.`Code` = 'r_967cb770094c654b074d24dda2132f8a')
LIMIT 1;

INSERT INTO `client_map_identities` (
    `MapId`, `ClientBuildId`, `ClientMapId`, `ClientAreaId`, `ResourceIdentity`,
    `CoordinateScaleX`, `CoordinateScaleY`, `CoordinateOffsetX`, `CoordinateOffsetY`,
    `IdentityEvidenceStatus`, `CoordinateEvidenceStatus`, `ProductionEnabled`,
    `SourceType`, `SourceIdentity`, `SourceHash`, `EvidenceReference`,
    `ProvenanceJson`, `FieldEvidenceJson`, `CreatedAtUtc`, `UpdatedAtUtc`)
SELECT
    map.`Id`,
    'god2-opt-6b127086e0c0',
    19,
    4,
    'indoor/groceryl.hmd',
    1,
    1,
    0,
    0,
    'Verified',
    'Verified',
    1,
    'OfficialClientFileIoAndStaticConsumer',
    'God2_opt:map19:Island01/indoor/groceryL:worldready-20260803',
    '42d769c0ff328db3e1e295e17208d0db7a0ff12608976f952dae800ff79c3c00',
    'Reports/RoadMap.M1.MapIdentityEvidence.md',
    JSON_OBJECT(
        'clientBuildSha256', '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B',
        'databaseMapId', map.`Id`,
        'databaseMapPayloadSha256', map.`PayloadSha256`,
        'islandCanSha256', 'F2B9CA9DC4DCEBCD1C1C40DE14EA63F1B2061993F644C49B9C7BACA9CE2D2F97',
        'packedHmdSha256', '42D769C0FF328DB3E1E295E17208D0DB7A0FF12608976F952DAE800FF79C3C00',
        'decodedHmdSha256', '95C178A9BABC34FF116CD9022B7FF33716EAFF783DF14A46419ADB2A7D9A4230',
        'fileIoEvidence', 'Artifacts/MapIdentityRecovery/map19-worldready-fileio-20260803',
        'officialClientWorldPosition', JSON_OBJECT('x', 28, 'y', 34)),
    JSON_OBJECT(
        'mapPacketIdentity', '(ClientMapId<<6)|ClientAreaId',
        'positionPacking', 'mode=(packed&3);x=(packed>>2)&0x7FFF;y=packed>>17',
        'positionConsumerRva', '0x0008E14C',
        'coordinateValidationEntryRva', '0x0006AF70',
        'coordinateGridValidatorRva', '0x00064B90',
        'resourceCellDivisor', 21,
        'coordinateScaleX', 1,
        'coordinateScaleY', 1,
        'gridWidth', map.`Width`,
        'gridHeight', map.`Height`,
        'hmdHeader', 'HMD v1.6',
        'hmdPassGrid', 'Pass 10 30'),
    UTC_TIMESTAMP(6),
    UTC_TIMESTAMP(6)
FROM `maps` map
WHERE map.`Id` = 557790525
  AND map.`Code` = 'r_967cb770094c654b074d24dda2132f8a'
  AND map.`PayloadSha256` = '967cb770094c654b074d24dda2132f8a531932e4135a6c369649c67a3ae5bb82'
  AND JSON_UNQUOTE(JSON_EXTRACT(map.`PayloadJson`, '$.id')) = 'client:map/island01/indoor/groceryl'
  AND JSON_UNQUOTE(JSON_EXTRACT(map.`PayloadJson`, '$.resourceName')) = 'indoor/groceryl.hmd'
  AND map.`Width` = 10
  AND map.`Height` = 30
  AND NOT EXISTS (
      SELECT 1 FROM `client_map_identities` existing
      WHERE existing.`MapId` = map.`Id`
        AND existing.`ClientBuildId` = 'god2-opt-6b127086e0c0');

INSERT INTO `character_map_identity_migrations` (
    `CharacterId`, `ClientBuildId`, `LegacyClientMapId`, `DatabaseMapId`,
    `PositionX`, `PositionY`, `EvidenceReference`, `MigratedAtUtc`)
SELECT
    characterRow.`Id`, identity.`ClientBuildId`, characterRow.`MapId`, identity.`MapId`,
    characterRow.`PositionX`, characterRow.`PositionY`, identity.`EvidenceReference`, UTC_TIMESTAMP(6)
FROM `characters` characterRow
JOIN `client_map_identities` identity
  ON identity.`ClientBuildId` = 'god2-opt-6b127086e0c0'
 AND identity.`ClientMapId` = characterRow.`MapId`
 AND identity.`ClientAreaId` = 4
 AND identity.`ProductionEnabled` = 1
JOIN `maps` map ON map.`Id` = identity.`MapId`
WHERE characterRow.`Status` <> 'Deleted'
  AND characterRow.`MapId` IN (3, 19)
  AND characterRow.`PositionX` BETWEEN 0 AND ((map.`Width` * 21) - 1)
  AND characterRow.`PositionY` BETWEEN 0 AND ((map.`Height` * 21) - 1)
  AND NOT EXISTS (
      SELECT 1 FROM `character_map_identity_migrations` existing
      WHERE existing.`CharacterId` = characterRow.`Id`
        AND existing.`ClientBuildId` = identity.`ClientBuildId`);

UPDATE `characters` characterRow
JOIN `character_map_identity_migrations` migration
  ON migration.`CharacterId` = characterRow.`Id`
 AND migration.`ClientBuildId` = 'god2-opt-6b127086e0c0'
SET characterRow.`MapId` = migration.`DatabaseMapId`,
    characterRow.`UpdatedAtUtc` = UTC_TIMESTAMP(6),
    characterRow.`ConcurrencyToken` = REPLACE(UUID(), '-', '')
WHERE characterRow.`Status` <> 'Deleted'
  AND characterRow.`MapId` = migration.`LegacyClientMapId`;
