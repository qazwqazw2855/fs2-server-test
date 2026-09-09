-- Payload-free structural and correlation evidence from the validated v1.0.2
-- and v1.0.3 Evidence Packages. This migration is additive and EvidenceOnly;
-- it does not enable a gameplay handler, serializer or production mutation.

CREATE TABLE IF NOT EXISTS `packet_capture_supplemental_frame_catalog_evidence` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `BaseEvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `FamilyCount` int unsigned NOT NULL,
    `ObservationCount` int unsigned NOT NULL,
    `UniqueFrameCount` int unsigned NOT NULL,
    `FamilyCatalogJson` json NOT NULL,
    `EvidenceStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'EvidenceOnly',
    `FieldSemanticsStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Unknown',
    `GameplaySemanticsStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'NotVerified',
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`),
    CONSTRAINT `FK_PacketCaptureSupplementalFrameCatalog_Source`
        FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureSupplementalFrameCatalog_Counts`
        CHECK (`FamilyCount` = 62 AND `ObservationCount` = 87 AND `UniqueFrameCount` = 85),
    CONSTRAINT `CK_PacketCaptureSupplementalFrameCatalog_Json`
        CHECK (JSON_VALID(`FamilyCatalogJson`) AND JSON_LENGTH(`FamilyCatalogJson`) = `FamilyCount`),
    CONSTRAINT `CK_PacketCaptureSupplementalFrameCatalog_Authority`
        CHECK (`EvidenceStatus` = 'EvidenceOnly' AND `FieldSemanticsStatus` = 'Unknown'
            AND `GameplaySemanticsStatus` = 'NotVerified' AND `ProductionEnabled` = 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `packet_capture_stage_correlation_evidence` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `FromStage` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `ToStage` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `Direction` varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `CorrelationLevel` varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `SourceRecordCount` int unsigned NOT NULL,
    `TargetRecordCount` int unsigned NOT NULL,
    `MappingCount` int unsigned NOT NULL,
    `CorrelationBasis` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `EvidenceStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'EvidenceOnly',
    `FieldSemanticsStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Unknown',
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`, `FromStage`, `ToStage`, `Direction`),
    CONSTRAINT `FK_PacketCaptureStageCorrelation_Source`
        FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureStageCorrelation_Counts`
        CHECK (`SourceRecordCount` > 0 AND `TargetRecordCount` > 0 AND `MappingCount` > 0),
    CONSTRAINT `CK_PacketCaptureStageCorrelation_Authority`
        CHECK (`CorrelationLevel` = 'Strong' AND `EvidenceStatus` = 'EvidenceOnly'
            AND `FieldSemanticsStatus` = 'Unknown' AND `ProductionEnabled` = 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `packet_capture_frame_handler_correlation_evidence` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `OuterOpcode` tinyint unsigned NOT NULL,
    `OuterFrameLength` int unsigned NOT NULL,
    `CorrelatedFrameCount` int unsigned NOT NULL,
    `HandlerMappingCount` int unsigned NOT NULL,
    `HandlerFamiliesJson` json NOT NULL,
    `CorrelationLevel` varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `EvidenceStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'EvidenceOnly',
    `FieldSemanticsStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Unknown',
    `GameplaySemanticsStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'NotVerified',
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`, `OuterOpcode`, `OuterFrameLength`),
    CONSTRAINT `FK_PacketCaptureFrameHandlerCorrelation_Source`
        FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureFrameHandlerCorrelation_Counts`
        CHECK (`OuterFrameLength` >= 3 AND `CorrelatedFrameCount` > 0 AND `HandlerMappingCount` > 0),
    CONSTRAINT `CK_PacketCaptureFrameHandlerCorrelation_Json`
        CHECK (JSON_VALID(`HandlerFamiliesJson`) AND JSON_LENGTH(`HandlerFamiliesJson`) > 0),
    CONSTRAINT `CK_PacketCaptureFrameHandlerCorrelation_Authority`
        CHECK (`CorrelationLevel` = 'Strong' AND `EvidenceStatus` = 'EvidenceOnly'
            AND `FieldSemanticsStatus` = 'Unknown' AND `GameplaySemanticsStatus` = 'NotVerified'
            AND `ProductionEnabled` = 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `packet_capture_evidence_sources`
    (`EvidenceSourceId`, `EvidencePath`, `ImportPath`, `SchemaVersion`, `EvidencePolicy`, `GeneratedAtUtc`,
     `EvidenceSha256`, `PackageSha256`, `SessionCount`, `RawTransportRecords`, `FrameCount`, `UnknownFrameCount`,
     `CandidateFrameCount`, `SemanticCandidateClusterCount`, `HighConfidenceSemanticCandidateCount`,
     `RepresentativeCandidateCount`, `AppliedKnowledgeCount`, `VerifiedGameplayClassificationCount`)
VALUES
    ('god2-evidence-package-20260807-supplement',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json',
     'db/imports/evidence/packet_capture/god2_evidence_package_20260807_supplement.database.json',
     'god2-evidence-package-20260807-supplement-v1',
     'Candidate and Strong relationships remain EvidenceOnly; only direction, opcode, exact frame length and explicit stage provenance are retained.',
     '2026-08-07 10:19:28.609000',
     'f3fdbaf3ed4f703525e821865a9a49da26da48f41f3b12381a5b8c79e6f2246b',
     '48c85fa534db6c221a8ad06b24dfe4326e83df70bb27640c8c534c6f431c3b2e',
     2, 2833, 1344, 1344, 1004, 0, 0, 0, 62, 0)
ON DUPLICATE KEY UPDATE
    `EvidencePath`=VALUES(`EvidencePath`), `ImportPath`=VALUES(`ImportPath`),
    `SchemaVersion`=VALUES(`SchemaVersion`), `EvidencePolicy`=VALUES(`EvidencePolicy`),
    `GeneratedAtUtc`=VALUES(`GeneratedAtUtc`), `EvidenceSha256`=VALUES(`EvidenceSha256`),
    `PackageSha256`=VALUES(`PackageSha256`), `SessionCount`=VALUES(`SessionCount`),
    `RawTransportRecords`=VALUES(`RawTransportRecords`), `FrameCount`=VALUES(`FrameCount`),
    `UnknownFrameCount`=VALUES(`UnknownFrameCount`), `CandidateFrameCount`=VALUES(`CandidateFrameCount`),
    `SemanticCandidateClusterCount`=VALUES(`SemanticCandidateClusterCount`),
    `HighConfidenceSemanticCandidateCount`=VALUES(`HighConfidenceSemanticCandidateCount`),
    `RepresentativeCandidateCount`=VALUES(`RepresentativeCandidateCount`),
    `AppliedKnowledgeCount`=VALUES(`AppliedKnowledgeCount`),
    `VerifiedGameplayClassificationCount`=VALUES(`VerifiedGameplayClassificationCount`);

INSERT INTO `packet_capture_sessions`
    (`EvidenceSourceId`, `SessionId`, `RuntimeSessionId`, `AnalysisRunId`, `SourceLabel`, `Availability`,
     `TargetExecutable`, `TargetProcessId`, `TargetArchitecture`, `CaptureSource`, `Transport`,
     `InjectionSequence`, `ReadinessHandshake`, `CleanupStatus`, `RawTransportRecords`, `CaptureRecordCount`,
     `DecodedMessageCount`, `HandlerObservationCount`, `LengthPrefixValidatedCount`, `ClientToServerRecords`,
     `ServerToClientRecords`, `PayloadBytes`, `FrameCount`, `UnknownFrameCount`, `CandidateFrameCount`,
     `SemanticCandidateClusterCount`, `HighConfidenceSemanticCandidateCount`, `VerifiedGameplayClassificationCount`,
     `EvidenceStatus`, `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('god2-evidence-package-20260807-supplement', '870DB7D9-B96A-4F38-B225-8C7EA3D9A7E6',
     '24F5213C-845B-4F76-B67E-74597D9EACC4', '2C0C1826-56B6-4D5D-80C9-2883817FB0A4',
     'User-submitted validated God2PacketCapture v1.0.3 Evidence Package',
     'External package; payload-free structural and correlation evidence retained',
     'God2_opt.exe', 9852, 'x86', 'OptInX86Dll', 'InjectedWinsock+PreEncrypt+PostDecrypt+HandlerDecoded',
     'OptInRemoteThreadInjectionAfterLauncherStartedGame', 'God2TraceProbeWaitReady', 'Completed',
     1547, 2475, 727, 201, 727, 312, 1235, 20467, 727, 727, 426, 0, 0, 0,
     'EvidenceOnly', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', '2C820B07-0A81-459A-B04A-7C5936A015F5',
     'D4D971DF-4FC8-4753-9890-B4585BF181A0', '75EAC599-BBD7-4AD3-9A10-586774EA440A',
     'User-submitted validated God2PacketCapture v1.0.2 Evidence Package',
     'External package; payload-free supplemental frame evidence retained',
     'God2_opt.exe', 5700, 'x86', 'OptInX86Dll', 'InjectedWinsock+PreEncrypt+PostDecrypt+HandlerDecoded',
     'OptInRemoteThreadInjectionAfterLauncherStartedGame', 'God2TraceProbeWaitReady', 'Completed',
     1286, 2254, 617, 351, 617, 286, 1000, 24580, 617, 617, 578, 0, 0, 0,
     'EvidenceOnly', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json')
ON DUPLICATE KEY UPDATE
    `RuntimeSessionId`=VALUES(`RuntimeSessionId`), `AnalysisRunId`=VALUES(`AnalysisRunId`),
    `SourceLabel`=VALUES(`SourceLabel`), `Availability`=VALUES(`Availability`),
    `TargetProcessId`=VALUES(`TargetProcessId`), `CaptureSource`=VALUES(`CaptureSource`),
    `Transport`=VALUES(`Transport`), `InjectionSequence`=VALUES(`InjectionSequence`),
    `ReadinessHandshake`=VALUES(`ReadinessHandshake`), `CleanupStatus`=VALUES(`CleanupStatus`),
    `RawTransportRecords`=VALUES(`RawTransportRecords`), `CaptureRecordCount`=VALUES(`CaptureRecordCount`),
    `DecodedMessageCount`=VALUES(`DecodedMessageCount`), `HandlerObservationCount`=VALUES(`HandlerObservationCount`),
    `LengthPrefixValidatedCount`=VALUES(`LengthPrefixValidatedCount`),
    `ClientToServerRecords`=VALUES(`ClientToServerRecords`), `ServerToClientRecords`=VALUES(`ServerToClientRecords`),
    `PayloadBytes`=VALUES(`PayloadBytes`), `FrameCount`=VALUES(`FrameCount`),
    `UnknownFrameCount`=VALUES(`UnknownFrameCount`), `CandidateFrameCount`=VALUES(`CandidateFrameCount`),
    `SemanticCandidateClusterCount`=VALUES(`SemanticCandidateClusterCount`),
    `HighConfidenceSemanticCandidateCount`=VALUES(`HighConfidenceSemanticCandidateCount`),
    `VerifiedGameplayClassificationCount`=VALUES(`VerifiedGameplayClassificationCount`),
    `EvidenceStatus`=VALUES(`EvidenceStatus`), `EvidenceReference`=VALUES(`EvidenceReference`);

INSERT INTO `packet_capture_supplemental_frame_catalog_evidence`
    (`EvidenceSourceId`, `BaseEvidenceSourceId`, `FamilyCount`, `ObservationCount`, `UniqueFrameCount`,
     `FamilyCatalogJson`, `EvidenceStatus`, `FieldSemanticsStatus`, `GameplaySemanticsStatus`,
     `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('god2-evidence-package-20260807-supplement', 'god2-evidence-package-20260806-102928', 62, 87, 85,
     '[{"direction":"ClientToServer","opcode":"0x02","frameLength":5,"observedCount":2,"uniqueFrameCount":1},{"direction":"ClientToServer","opcode":"0x2F","frameLength":14,"observedCount":1,"uniqueFrameCount":1},{"direction":"ClientToServer","opcode":"0x67","frameLength":5,"observedCount":1,"uniqueFrameCount":1},{"direction":"ClientToServer","opcode":"0xB0","frameLength":6,"observedCount":1,"uniqueFrameCount":1},{"direction":"ClientToServer","opcode":"0xB4","frameLength":5,"observedCount":2,"uniqueFrameCount":2},{"direction":"ServerToClient","opcode":"0x03","frameLength":6,"observedCount":2,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x23","frameLength":115,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x23","frameLength":969,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x23","frameLength":1058,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x23","frameLength":1137,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x23","frameLength":1173,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x24","frameLength":20,"observedCount":7,"uniqueFrameCount":7},{"direction":"ServerToClient","opcode":"0x24","frameLength":41,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x36","frameLength":35,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x3A","frameLength":31,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x41","frameLength":50,"observedCount":2,"uniqueFrameCount":2},{"direction":"ServerToClient","opcode":"0x4C","frameLength":20,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x4C","frameLength":41,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5C","frameLength":26,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5C","frameLength":36,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5C","frameLength":37,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5C","frameLength":42,"observedCount":2,"uniqueFrameCount":2},{"direction":"ServerToClient","opcode":"0x5C","frameLength":44,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5C","frameLength":46,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5C","frameLength":48,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5C","frameLength":69,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5C","frameLength":78,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5D","frameLength":7,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5D","frameLength":104,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5D","frameLength":618,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5F","frameLength":35,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x5F","frameLength":42,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x73","frameLength":8,"observedCount":2,"uniqueFrameCount":2},{"direction":"ServerToClient","opcode":"0x85","frameLength":241,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x85","frameLength":243,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x85","frameLength":252,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x85","frameLength":260,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x85","frameLength":263,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x85","frameLength":299,"observedCount":2,"uniqueFrameCount":2},{"direction":"ServerToClient","opcode":"0x85","frameLength":314,"observedCount":2,"uniqueFrameCount":2},{"direction":"ServerToClient","opcode":"0x85","frameLength":322,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x86","frameLength":39,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x86","frameLength":71,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x86","frameLength":95,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x86","frameLength":260,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x86","frameLength":264,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x86","frameLength":277,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x86","frameLength":279,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x86","frameLength":311,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x86","frameLength":316,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x86","frameLength":348,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x86","frameLength":373,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0x86","frameLength":440,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0xAE","frameLength":28,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0xAE","frameLength":61,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0xB2","frameLength":8,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0xC8","frameLength":161,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0xDA","frameLength":215,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0xE4","frameLength":154,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0xE5","frameLength":21,"observedCount":11,"uniqueFrameCount":11},{"direction":"ServerToClient","opcode":"0xE6","frameLength":62,"observedCount":1,"uniqueFrameCount":1},{"direction":"ServerToClient","opcode":"0xE8","frameLength":5,"observedCount":2,"uniqueFrameCount":2}]',
     'EvidenceOnly', 'Unknown', 'NotVerified', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json')
ON DUPLICATE KEY UPDATE
    `BaseEvidenceSourceId`=VALUES(`BaseEvidenceSourceId`), `FamilyCount`=VALUES(`FamilyCount`),
    `ObservationCount`=VALUES(`ObservationCount`), `UniqueFrameCount`=VALUES(`UniqueFrameCount`),
    `FamilyCatalogJson`=VALUES(`FamilyCatalogJson`), `EvidenceReference`=VALUES(`EvidenceReference`);

INSERT INTO `packet_capture_stage_correlation_evidence`
    (`EvidenceSourceId`, `FromStage`, `ToStage`, `Direction`, `CorrelationLevel`, `SourceRecordCount`,
     `TargetRecordCount`, `MappingCount`, `CorrelationBasis`, `EvidenceStatus`, `FieldSemanticsStatus`,
     `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('god2-evidence-package-20260807-supplement', 'Transport', 'PostDecrypt', 'ServerToClient', 'Strong',
     403, 403, 403, 'SameThreadLatestInboundTransport', 'EvidenceOnly', 'Unknown', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 'PostDecrypt', 'HandlerDecoded', 'ServerToClient', 'Strong',
     48, 201, 201, 'SameThreadLatestPostDecrypt', 'EvidenceOnly', 'Unknown', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json')
ON DUPLICATE KEY UPDATE
    `CorrelationLevel`=VALUES(`CorrelationLevel`), `SourceRecordCount`=VALUES(`SourceRecordCount`),
    `TargetRecordCount`=VALUES(`TargetRecordCount`), `MappingCount`=VALUES(`MappingCount`),
    `CorrelationBasis`=VALUES(`CorrelationBasis`), `EvidenceReference`=VALUES(`EvidenceReference`);

INSERT INTO `packet_capture_frame_handler_correlation_evidence`
    (`EvidenceSourceId`, `OuterOpcode`, `OuterFrameLength`, `CorrelatedFrameCount`, `HandlerMappingCount`,
     `HandlerFamiliesJson`, `CorrelationLevel`, `EvidenceStatus`, `FieldSemanticsStatus`,
     `GameplaySemanticsStatus`, `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('god2-evidence-package-20260807-supplement', 0x86, 20, 28, 28, '[{"opcode":"0x86","length":17,"count":28}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x5D, 539, 1, 20, '[{"opcode":"0x86","length":17,"count":12},{"opcode":"0x1C","length":45,"count":4},{"opcode":"0x83","length":15,"count":1},{"opcode":"0x38","length":2,"count":1},{"opcode":"0x87","length":3,"count":1},{"opcode":"0x88","length":121,"count":1}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x82, 537, 1, 20, '[{"opcode":"0x86","length":17,"count":12},{"opcode":"0x1C","length":45,"count":4},{"opcode":"0x83","length":15,"count":1},{"opcode":"0x38","length":2,"count":1},{"opcode":"0x87","length":3,"count":1},{"opcode":"0x88","length":121,"count":1}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x86, 279, 1, 15, '[{"opcode":"0x86","length":17,"count":5},{"opcode":"0x85","length":2,"count":5},{"opcode":"0x83","length":15,"count":4},{"opcode":"0x88","length":121,"count":1}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x88, 192, 3, 15, '[{"opcode":"0x88","length":121,"count":3},{"opcode":"0x86","length":17,"count":12}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x86, 264, 1, 14, '[{"opcode":"0x86","length":17,"count":5},{"opcode":"0x85","length":2,"count":5},{"opcode":"0x83","length":15,"count":3},{"opcode":"0x88","length":121,"count":1}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x86, 277, 1, 13, '[{"opcode":"0x86","length":17,"count":6},{"opcode":"0x85","length":2,"count":3},{"opcode":"0x83","length":15,"count":3},{"opcode":"0x88","length":121,"count":1}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x85, 260, 1, 12, '[{"opcode":"0x85","length":2,"count":3},{"opcode":"0x83","length":15,"count":3},{"opcode":"0x86","length":17,"count":5},{"opcode":"0x88","length":121,"count":1}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x86, 107, 1, 11, '[{"opcode":"0x86","length":17,"count":2},{"opcode":"0x85","length":2,"count":5},{"opcode":"0x83","length":15,"count":4}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x85, 243, 1, 11, '[{"opcode":"0x85","length":2,"count":3},{"opcode":"0x83","length":15,"count":3},{"opcode":"0x88","length":121,"count":1},{"opcode":"0x86","length":17,"count":4}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x85, 252, 1, 11, '[{"opcode":"0x85","length":2,"count":3},{"opcode":"0x83","length":15,"count":3},{"opcode":"0x88","length":121,"count":1},{"opcode":"0x86","length":17,"count":4}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x86, 112, 1, 9, '[{"opcode":"0x86","length":17,"count":2},{"opcode":"0x85","length":2,"count":3},{"opcode":"0x83","length":15,"count":4}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x85, 322, 1, 8, '[{"opcode":"0x85","length":2,"count":2},{"opcode":"0x83","length":15,"count":1},{"opcode":"0x88","length":121,"count":1},{"opcode":"0x86","length":17,"count":4}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x86, 37, 3, 6, '[{"opcode":"0x86","length":17,"count":6}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x86, 39, 1, 4, '[{"opcode":"0x86","length":17,"count":1},{"opcode":"0x85","length":2,"count":2},{"opcode":"0x83","length":15,"count":1}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x86, 295, 1, 2, '[{"opcode":"0x86","length":17,"count":1},{"opcode":"0x89","length":57,"count":1}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 0x86, 201, 1, 2, '[{"opcode":"0x86","length":17,"count":1},{"opcode":"0x89","length":57,"count":1}]', 'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json')
ON DUPLICATE KEY UPDATE
    `CorrelatedFrameCount`=VALUES(`CorrelatedFrameCount`), `HandlerMappingCount`=VALUES(`HandlerMappingCount`),
    `HandlerFamiliesJson`=VALUES(`HandlerFamiliesJson`), `CorrelationLevel`=VALUES(`CorrelationLevel`),
    `EvidenceReference`=VALUES(`EvidenceReference`);

INSERT INTO `packet_capture_content_application_gates`
    (`EvidenceSourceId`, `ContentDomain`, `ApplicationStatus`, `ObservedEvidenceCount`, `Reason`,
     `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('god2-evidence-package-20260807-supplement', 'SupplementalDecodedFrameStructure', 'EvidenceOnly', 87,
     'Sixty-two direction/opcode/exact-length variants absent from the 2026-08-06 structural catalog were retained; field and gameplay semantics remain unknown.', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 'FrameHandlerCorrelation', 'EvidenceOnly', 201,
     'Seventeen outer-frame length groups have Strong same-thread PostDecrypt-to-HandlerDecoded relationships; Strong is not Exact and grants no semantic authority.', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'),
    ('god2-evidence-package-20260807-supplement', 'GameplayRuntimeMutation', 'EvidenceBlocked', 0,
     'No attacker, target, skill, hit, critical, element, inventory identity, mount identity, character identity or authoritative formula field was verified.', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json')
ON DUPLICATE KEY UPDATE
    `ApplicationStatus`=VALUES(`ApplicationStatus`), `ObservedEvidenceCount`=VALUES(`ObservedEvidenceCount`),
    `Reason`=VALUES(`Reason`), `ProductionEnabled`=VALUES(`ProductionEnabled`),
    `EvidenceReference`=VALUES(`EvidenceReference`);
