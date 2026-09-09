-- Validated God2PacketCapture v1.0.1 Evidence Package structural evidence.
-- Additive and evidence-only: raw sensitive payloads are not copied here and no
-- gameplay/content row, handler, serializer, or production mutation gate is enabled.

ALTER TABLE `packet_capture_evidence_sources`
    ADD COLUMN IF NOT EXISTS `PackageSha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `EvidenceSha256`;

ALTER TABLE `packet_capture_sessions`
    ADD COLUMN IF NOT EXISTS `CaptureRecordCount` int unsigned NULL AFTER `RawTransportRecords`,
    ADD COLUMN IF NOT EXISTS `DecodedMessageCount` int unsigned NULL AFTER `CaptureRecordCount`,
    ADD COLUMN IF NOT EXISTS `HandlerObservationCount` int unsigned NULL AFTER `DecodedMessageCount`,
    ADD COLUMN IF NOT EXISTS `LengthPrefixValidatedCount` int unsigned NULL AFTER `HandlerObservationCount`,
    ADD COLUMN IF NOT EXISTS `CleanupStatus` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `ReadinessHandshake`;

CREATE TABLE IF NOT EXISTS `packet_capture_decoded_stage_evidence` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `CaptureStage` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `Direction` varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `RecordCount` int unsigned NOT NULL,
    `UniquePayloadCount` int unsigned NOT NULL,
    `OpcodeFamilyCount` int unsigned NOT NULL,
    `LengthPrefixValidatedCount` int unsigned NOT NULL,
    `MinimumFrameLength` int unsigned NOT NULL,
    `MaximumFrameLength` int unsigned NOT NULL,
    `OpcodeCatalogJson` json NOT NULL,
    `EvidenceStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'EvidenceOnly',
    `FieldSemanticsStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Unknown',
    `GameplaySemanticsStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'NotVerified',
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`, `CaptureStage`, `Direction`),
    CONSTRAINT `FK_PacketCaptureDecodedStageEvidence_Source`
        FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureDecodedStageEvidence_Direction`
        CHECK (`Direction` IN ('ClientToServer', 'ServerToClient')),
    CONSTRAINT `CK_PacketCaptureDecodedStageEvidence_Counts`
        CHECK (`RecordCount` > 0 AND `UniquePayloadCount` > 0 AND `UniquePayloadCount` <= `RecordCount`
            AND `OpcodeFamilyCount` > 0 AND `LengthPrefixValidatedCount` = `RecordCount`
            AND `MinimumFrameLength` >= 3 AND `MaximumFrameLength` >= `MinimumFrameLength`),
    CONSTRAINT `CK_PacketCaptureDecodedStageEvidence_Authority`
        CHECK (`EvidenceStatus` = 'EvidenceOnly' AND `FieldSemanticsStatus` = 'Unknown'
            AND `GameplaySemanticsStatus` = 'NotVerified' AND `ProductionEnabled` = 0),
    CONSTRAINT `CK_PacketCaptureDecodedStageEvidence_CatalogJson`
        CHECK (JSON_VALID(`OpcodeCatalogJson`))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `packet_capture_handler_family_evidence` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `HandlerOpcode` tinyint unsigned NOT NULL,
    `ObservedCount` int unsigned NOT NULL,
    `UniquePayloadCount` int unsigned NOT NULL,
    `PayloadLength` int unsigned NOT NULL,
    `HandlerAddress` int unsigned NOT NULL,
    `EvidenceStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'EvidenceOnly',
    `FieldSemanticsStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Unknown',
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`, `HandlerOpcode`),
    CONSTRAINT `FK_PacketCaptureHandlerFamilyEvidence_Source`
        FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureHandlerFamilyEvidence_Counts`
        CHECK (`ObservedCount` > 0 AND `UniquePayloadCount` > 0 AND `UniquePayloadCount` <= `ObservedCount`
            AND `PayloadLength` > 0 AND `HandlerAddress` > 0),
    CONSTRAINT `CK_PacketCaptureHandlerFamilyEvidence_Authority`
        CHECK (`EvidenceStatus` = 'EvidenceOnly' AND `FieldSemanticsStatus` = 'Unknown' AND `ProductionEnabled` = 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `packet_capture_evidence_sources`
    (`EvidenceSourceId`, `EvidencePath`, `ImportPath`, `SchemaVersion`, `EvidencePolicy`, `GeneratedAtUtc`,
     `EvidenceSha256`, `PackageSha256`, `SessionCount`, `RawTransportRecords`, `FrameCount`, `UnknownFrameCount`,
     `CandidateFrameCount`, `SemanticCandidateClusterCount`, `HighConfidenceSemanticCandidateCount`,
     `RepresentativeCandidateCount`, `AppliedKnowledgeCount`, `VerifiedGameplayClassificationCount`)
VALUES
    ('god2-evidence-package-20260806-102928',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json',
     'db/imports/evidence/packet_capture/god2_evidence_package_20260806_102928.database.json',
     'god2-evidence-package-20260806-102928-v1',
     'Decoded and Handler structural evidence is EvidenceOnly. Candidate or Unknown gameplay semantics are never promoted without verified fields.',
     '2026-08-06 10:29:36.655000',
     '87195621b5d56a53ea01f1c5ac86bfdcc0b564e9772394c3f24ec7fd8475b73e',
     '6c8d52617f652f522610abdb3d4db1b381e0a21a08b8e60e4a574ac66f7e9951',
     1, 3482, 1685, 1685, 0, 64, 0, 0, 7, 0)
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
    ('god2-evidence-package-20260806-102928',
     '2026-08-06_18-14-15_05A7F156-259E-4110-A553-2C125ED962CC',
     '65BBF007-6117-419F-BF8E-F3C7C95B8809',
     '7D843178-F12E-4993-8EF2-991AD9FD324D',
     'User-submitted validated God2PacketCapture v1.0.1 Evidence Package',
     'External source package; payload-free aggregate evidence retained',
     'God2_opt.exe', 368, 'x86', 'OptInX86Dll', 'InjectedWinsock+PreEncrypt+PostDecrypt+HandlerDecoded',
     'OptInRemoteThreadInjectionAfterLauncherStartedGame', 'God2TraceProbeWaitReady', 'Completed',
     3482, 5797, 1685, 630, 1685, 767, 2715, 44327, 1685, 1685, 0, 64, 0, 0,
     'EvidenceOnly', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json')
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

INSERT INTO `packet_capture_semantic_families`
    (`EvidenceSourceId`, `SemanticName`, `ClusterCount`, `ObservedCount`, `MaxConfidenceScore`,
     `EvidenceStatus`, `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('god2-evidence-package-20260806-102928', 'DecodedFrameEnvelopeStructuralEvidence', 1, 1685, 100,
     'EvidenceOnly', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'PreEncryptClientOpcodeFamilies', 29, 786, 100,
     'EvidenceOnly', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'PostDecryptServerOpcodeFamilies', 35, 899, 100,
     'EvidenceOnly', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'HandlerDecodedSubmessageFamilies', 8, 630, 100,
     'EvidenceOnly', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json')
ON DUPLICATE KEY UPDATE
    `ClusterCount`=VALUES(`ClusterCount`), `ObservedCount`=VALUES(`ObservedCount`),
    `MaxConfidenceScore`=VALUES(`MaxConfidenceScore`), `EvidenceStatus`=VALUES(`EvidenceStatus`),
    `ProductionEnabled`=VALUES(`ProductionEnabled`), `EvidenceReference`=VALUES(`EvidenceReference`);

INSERT INTO `packet_capture_decoded_stage_evidence`
    (`EvidenceSourceId`, `CaptureStage`, `Direction`, `RecordCount`, `UniquePayloadCount`, `OpcodeFamilyCount`,
     `LengthPrefixValidatedCount`, `MinimumFrameLength`, `MaximumFrameLength`, `OpcodeCatalogJson`,
     `EvidenceStatus`, `FieldSemanticsStatus`, `GameplaySemanticsStatus`, `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('god2-evidence-package-20260806-102928', 'PreEncrypt', 'ClientToServer', 786, 483, 29, 786, 5, 208,
     '[{"opcode":"2E","count":367},{"opcode":"30","count":136},{"opcode":"35","count":112},{"opcode":"6D","count":33},{"opcode":"36","count":33},{"opcode":"66","count":17},{"opcode":"28","count":13},{"opcode":"1C","count":12},{"opcode":"85","count":9},{"opcode":"1F","count":8},{"opcode":"1D","count":8},{"opcode":"21","count":6},{"opcode":"37","count":5},{"opcode":"C2","count":4},{"opcode":"20","count":4},{"opcode":"86","count":3},{"opcode":"25","count":2},{"opcode":"39","count":2},{"opcode":"38","count":2},{"opcode":"B5","count":1},{"opcode":"68","count":1},{"opcode":"AB","count":1},{"opcode":"00","count":1},{"opcode":"1A","count":1},{"opcode":"04","count":1},{"opcode":"A6","count":1},{"opcode":"9A","count":1},{"opcode":"23","count":1},{"opcode":"0A","count":1}]',
     'EvidenceOnly', 'Unknown', 'NotVerified', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'PostDecrypt', 'ServerToClient', 899, 490, 35, 899, 5, 1254,
     '[{"opcode":"5D","count":388},{"opcode":"5F","count":167},{"opcode":"86","count":131},{"opcode":"36","count":26},{"opcode":"72","count":25},{"opcode":"77","count":17},{"opcode":"71","count":16},{"opcode":"85","count":13},{"opcode":"41","count":13},{"opcode":"7A","count":11},{"opcode":"88","count":10},{"opcode":"BB","count":10},{"opcode":"74","count":10},{"opcode":"E5","count":10},{"opcode":"22","count":9},{"opcode":"B2","count":7},{"opcode":"23","count":6},{"opcode":"82","count":4},{"opcode":"E6","count":3},{"opcode":"61","count":3},{"opcode":"F5","count":3},{"opcode":"02","count":2},{"opcode":"68","count":2},{"opcode":"01","count":2},{"opcode":"3A","count":1},{"opcode":"5C","count":1},{"opcode":"42","count":1},{"opcode":"3F","count":1},{"opcode":"EE","count":1},{"opcode":"1F","count":1},{"opcode":"07","count":1},{"opcode":"3B","count":1},{"opcode":"4B","count":1},{"opcode":"2E","count":1},{"opcode":"89","count":1}]',
     'EvidenceOnly', 'Unknown', 'NotVerified', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json')
ON DUPLICATE KEY UPDATE
    `RecordCount`=VALUES(`RecordCount`), `UniquePayloadCount`=VALUES(`UniquePayloadCount`),
    `OpcodeFamilyCount`=VALUES(`OpcodeFamilyCount`), `LengthPrefixValidatedCount`=VALUES(`LengthPrefixValidatedCount`),
    `MinimumFrameLength`=VALUES(`MinimumFrameLength`), `MaximumFrameLength`=VALUES(`MaximumFrameLength`),
    `OpcodeCatalogJson`=VALUES(`OpcodeCatalogJson`), `EvidenceReference`=VALUES(`EvidenceReference`);

INSERT INTO `packet_capture_handler_family_evidence`
    (`EvidenceSourceId`, `HandlerOpcode`, `ObservedCount`, `UniquePayloadCount`, `PayloadLength`, `HandlerAddress`,
     `EvidenceStatus`, `FieldSemanticsStatus`, `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('god2-evidence-package-20260806-102928', 0x86, 337, 103, 17, 0x0054709B, 'EvidenceOnly', 'Unknown', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 0x83, 125, 85, 15, 0x0054709B, 'EvidenceOnly', 'Unknown', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 0x85, 92, 1, 2, 0x0054709B, 'EvidenceOnly', 'Unknown', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 0x88, 33, 33, 121, 0x0054709B, 'EvidenceOnly', 'Unknown', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 0x1C, 23, 14, 45, 0x0054709B, 'EvidenceOnly', 'Unknown', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 0x89, 8, 7, 57, 0x005470B3, 'EvidenceOnly', 'Unknown', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 0x38, 6, 1, 2, 0x0054709B, 'EvidenceOnly', 'Unknown', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 0x87, 6, 2, 3, 0x0054709B, 'EvidenceOnly', 'Unknown', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json')
ON DUPLICATE KEY UPDATE
    `ObservedCount`=VALUES(`ObservedCount`), `UniquePayloadCount`=VALUES(`UniquePayloadCount`),
    `PayloadLength`=VALUES(`PayloadLength`), `HandlerAddress`=VALUES(`HandlerAddress`),
    `EvidenceReference`=VALUES(`EvidenceReference`);

INSERT INTO `packet_capture_protocol_applications`
    (`EvidenceSourceId`, `KnowledgeId`, `PacketFamily`, `Direction`, `FrameLength`, `RecoveryStatus`,
     `ProtocolConfidence`, `VerifiedPromotion`, `RepresentativeCandidateCount`, `TotalObservedCount`,
     `FieldSemanticsStatus`, `GameplaySemanticsStatus`, `EvidenceReference`)
VALUES
    ('god2-evidence-package-20260806-102928', 'evidence-package-20260806-c2s-opcode-2e-10', 'DecodedOpcode', 'ClientToServer', 10, 'EvidenceOnly', 'Inferred', 0, 367, 367, 'Unknown', 'NotVerified', 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'evidence-package-20260806-c2s-opcode-30-5', 'DecodedOpcode', 'ClientToServer', 5, 'EvidenceOnly', 'Inferred', 0, 1, 136, 'Unknown', 'NotVerified', 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'evidence-package-20260806-c2s-opcode-35-20', 'DecodedOpcode', 'ClientToServer', 20, 'EvidenceOnly', 'Inferred', 0, 46, 112, 'Unknown', 'NotVerified', 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'evidence-package-20260806-c2s-opcode-6d-5', 'DecodedOpcode', 'ClientToServer', 5, 'EvidenceOnly', 'Inferred', 0, 1, 33, 'Unknown', 'NotVerified', 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'evidence-package-20260806-c2s-opcode-36-12', 'DecodedOpcode', 'ClientToServer', 12, 'EvidenceOnly', 'Inferred', 0, 3, 33, 'Unknown', 'NotVerified', 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'evidence-package-20260806-c2s-opcode-66-7', 'DecodedOpcode', 'ClientToServer', 7, 'EvidenceOnly', 'Inferred', 0, 14, 17, 'Unknown', 'NotVerified', 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'evidence-package-20260806-s2c-opcode-e6-battle-result', 'BattleResultCandidate', 'ServerToClient', 0, 'EvidenceOnly', 'Inferred', 0, 3, 3, 'Unknown', 'NotVerified', 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json')
ON DUPLICATE KEY UPDATE
    `RepresentativeCandidateCount`=VALUES(`RepresentativeCandidateCount`),
    `TotalObservedCount`=VALUES(`TotalObservedCount`), `EvidenceReference`=VALUES(`EvidenceReference`);

INSERT INTO `packet_capture_content_application_gates`
    (`EvidenceSourceId`, `ContentDomain`, `ApplicationStatus`, `ObservedEvidenceCount`, `Reason`,
     `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('god2-evidence-package-20260806-102928', 'ProtocolKnowledge', 'EvidenceOnly', 7,
     'Six fixed-length decoded C2S opcode families and the previously mapped S2C 0xE6 battle-result family are registered as structural EvidenceOnly knowledge; no handler or serializer is enabled.', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'DecodedFrameEnvelope', 'EvidenceOnly', 1685,
     'All 1685 PreEncrypt/PostDecrypt records have a valid uint16le length prefix and a recovered opcode byte at offset 2; gameplay field semantics remain unknown.', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'HandlerObservation', 'EvidenceOnly', 630,
     'Eight payload-free HandlerDecoded structural families are retained with counts, lengths and handler addresses; semantic field ownership is unknown.', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'BattleProtocol', 'EvidenceOnly', 3,
     'Three additional PostDecrypt 0xE6 frames reinforce the existing battle-result family mapping; result fields and serializer remain blocked.', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'FormulaRecovery', 'EvidenceBlocked', 3,
     'Battle-result family observations exist, but attacker, target, skill, damage, hit, critical, elemental and turn-order fields are not decoded.', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'NPC', 'EvidenceBlocked', 0, 'Handler records exist but NPC identity and field ownership are not verified.', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'Monster', 'EvidenceBlocked', 0, 'No verified monster field semantics were decoded.', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'Character', 'EvidenceBlocked', 0, 'No verified character lifecycle field semantics were decoded.', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'PetBattlePetImmortal', 'EvidenceBlocked', 0, 'No verified pet, battle-pet or immortal field semantics were decoded.', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'MountMovement', 'EvidenceBlocked', 0, 'Decoded opcode families exist but movement direction, coordinate and mount-state fields are not verified.', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'Quest', 'EvidenceBlocked', 0, 'No verified quest lifecycle field semantics were decoded.', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'Skill', 'EvidenceBlocked', 0, 'No verified skill, cost, facet or item-versus-skill field semantics were decoded.', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'ItemInventoryShop', 'EvidenceBlocked', 0, 'No verified inventory, equipment, mount equipment, shop or currency field semantics were decoded.', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json'),
    ('god2-evidence-package-20260806-102928', 'Party', 'EvidenceBlocked', 0, 'No verified party lifecycle field semantics were decoded.', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json')
ON DUPLICATE KEY UPDATE
    `ApplicationStatus`=VALUES(`ApplicationStatus`), `ObservedEvidenceCount`=VALUES(`ObservedEvidenceCount`),
    `Reason`=VALUES(`Reason`), `ProductionEnabled`=VALUES(`ProductionEnabled`),
    `EvidenceReference`=VALUES(`EvidenceReference`);
