INSERT INTO `client_map_identities` (
    `MapId`,
    `ClientBuildId`,
    `ClientMapId`,
    `ClientAreaId`,
    `ResourceIdentity`,
    `CoordinateScaleX`,
    `CoordinateScaleY`,
    `CoordinateOffsetX`,
    `CoordinateOffsetY`,
    `IdentityEvidenceStatus`,
    `CoordinateEvidenceStatus`,
    `ProductionEnabled`,
    `SourceType`,
    `SourceIdentity`,
    `SourceHash`,
    `EvidenceReference`,
    `ProvenanceJson`,
    `FieldEvidenceJson`,
    `CreatedAtUtc`,
    `UpdatedAtUtc`)
SELECT
    map.`Id`,
    'god2-opt-6b127086e0c0',
    3,
    4,
    'cityi2/cityi2.mdt',
    1,
    1,
    0,
    0,
    'Verified',
    'Verified',
    1,
    'OfficialClientFileIoAndStaticConsumer',
    'God2_opt:map3:Island03/cityi2:worldready-20260803',
    '292d0011561d97214fd77adcd0e0121a31b491b13bb26309c96fe4617630695e',
    'Reports/RoadMap.M1.MapIdentityEvidence.md',
    JSON_OBJECT(
        'clientBuildSha256', '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B',
        'databaseMapPayloadSha256', map.`PayloadSha256`,
        'packedMdtSha256', '292D0011561D97214FD77ADCD0E0121A31B491B13BB26309C96FE4617630695E',
        'decodedMdtSha256', 'B8332FAE2C3B0472A67A16FB1C7ED689002C859CF223E68003D0052638D22080',
        'packedMbdSha256', 'E6E12FD3FFE4D87D91205D9D00C02B8960238609C1248AD0E488DE4EB8630CE2',
        'decodedMbdSha256', 'EBA7AA7AE1DDC0381C7FE30D452FA251345AA9310386948B5EAF080C37D78E59',
        'fileIoEvidence', 'Artifacts/MapIdentityRecovery/map3-worldready-detector-fixed-20260803',
        'fileIoAnalysis', 'Artifacts/MapIdentityRecovery/map3-worldready-fileio-fixed-20260803/fileio-analysis-recovered.json',
        'officialClientWorldPosition', JSON_OBJECT('x', 196, 'y', 139)),
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
        'resourceIdentity', JSON_UNQUOTE(JSON_EXTRACT(map.`PayloadJson`, '$.resourceName'))),
    UTC_TIMESTAMP(6),
    UTC_TIMESTAMP(6)
FROM `maps` map
WHERE map.`Id` = 1675308248
  AND map.`Code` = 'r_acc5885731badfb4d1edda4362c2c99d'
  AND map.`PayloadSha256` = 'acc5885731badfb4d1edda4362c2c99d585b78b7da09f4f3294b06314d838dad'
  AND JSON_UNQUOTE(JSON_EXTRACT(map.`PayloadJson`, '$.id')) = 'client:map/island03/cityi2/cityi2'
  AND JSON_UNQUOTE(JSON_EXTRACT(map.`PayloadJson`, '$.resourceName')) = 'cityi2/cityi2.mdt'
  AND map.`Width` = 12
  AND map.`Height` = 12
  AND NOT EXISTS (
      SELECT 1
      FROM `client_map_identities` existing
      WHERE existing.`MapId` = map.`Id`
        AND existing.`ClientBuildId` = 'god2-opt-6b127086e0c0');
