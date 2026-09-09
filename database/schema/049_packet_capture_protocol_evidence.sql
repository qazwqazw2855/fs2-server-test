-- Packet capture evidence from real x86 God2_opt.exe sessions.
-- Evidence-only migration: this does not promote gameplay/content rows,
-- implement packet handlers, enable serializers, or mutate production content.

CREATE TABLE IF NOT EXISTS `packet_capture_evidence_sources` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `EvidencePath` varchar(768) NOT NULL,
    `ImportPath` varchar(768) NOT NULL,
    `SchemaVersion` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `EvidencePolicy` varchar(1024) NOT NULL,
    `GeneratedAtUtc` datetime(6) NOT NULL,
    `EvidenceSha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `SessionCount` int unsigned NOT NULL,
    `RawTransportRecords` int unsigned NOT NULL,
    `FrameCount` int unsigned NOT NULL,
    `UnknownFrameCount` int unsigned NOT NULL,
    `CandidateFrameCount` int unsigned NOT NULL,
    `SemanticCandidateClusterCount` int unsigned NOT NULL,
    `HighConfidenceSemanticCandidateCount` int unsigned NOT NULL,
    `RepresentativeCandidateCount` int unsigned NOT NULL,
    `AppliedKnowledgeCount` int unsigned NOT NULL,
    `VerifiedGameplayClassificationCount` int unsigned NOT NULL,
    `ImportedAtUtc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (`EvidenceSourceId`),
    CONSTRAINT `CK_PacketCaptureEvidenceSources_Hash`
        CHECK (CHAR_LENGTH(`EvidenceSha256`) = 64),
    CONSTRAINT `CK_PacketCaptureEvidenceSources_Policy`
        CHECK (`EvidencePolicy` LIKE '%Candidate%' AND `EvidencePolicy` LIKE '%EvidenceOnly%'),
    CONSTRAINT `CK_PacketCaptureEvidenceSources_NoVerifiedGameplay`
        CHECK (`VerifiedGameplayClassificationCount` = 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `packet_capture_sessions` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `SessionId` varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `SourceLabel` varchar(256) NOT NULL,
    `Availability` varchar(128) NOT NULL,
    `TargetExecutable` varchar(128) NOT NULL,
    `TargetProcessId` int unsigned NULL,
    `TargetArchitecture` varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `RawTransportRecords` int unsigned NOT NULL,
    `ClientToServerRecords` int unsigned NOT NULL,
    `ServerToClientRecords` int unsigned NOT NULL,
    `PayloadBytes` bigint unsigned NOT NULL,
    `FrameCount` int unsigned NOT NULL,
    `UnknownFrameCount` int unsigned NOT NULL,
    `CandidateFrameCount` int unsigned NOT NULL,
    `SemanticCandidateClusterCount` int unsigned NOT NULL,
    `HighConfidenceSemanticCandidateCount` int unsigned NOT NULL,
    `VerifiedGameplayClassificationCount` int unsigned NOT NULL,
    `EvidenceStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Candidate',
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`, `SessionId`),
    KEY `IX_PacketCaptureSessions_Target` (`TargetExecutable`, `TargetArchitecture`),
    CONSTRAINT `FK_PacketCaptureSessions_Source`
        FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureSessions_Status`
        CHECK (`EvidenceStatus` IN ('Candidate', 'EvidenceOnly', 'EvidenceBlocked', 'NotObserved', 'NotTransmitted')),
    CONSTRAINT `CK_PacketCaptureSessions_ProductionGate`
        CHECK (`ProductionEnabled` = 0),
    CONSTRAINT `CK_PacketCaptureSessions_TargetArchitecture`
        CHECK (`TargetArchitecture` = 'x86'),
    CONSTRAINT `CK_PacketCaptureSessions_Counters`
        CHECK (`RawTransportRecords` = `ClientToServerRecords` + `ServerToClientRecords`
            AND `UnknownFrameCount` <= `FrameCount`
            AND `VerifiedGameplayClassificationCount` = 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `packet_capture_semantic_families` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `SemanticName` varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `ClusterCount` int unsigned NOT NULL,
    `ObservedCount` int unsigned NOT NULL,
    `MaxConfidenceScore` tinyint unsigned NOT NULL,
    `EvidenceStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Candidate',
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`, `SemanticName`),
    KEY `IX_PacketCaptureSemanticFamilies_Observed` (`ObservedCount`),
    CONSTRAINT `FK_PacketCaptureSemanticFamilies_Source`
        FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureSemanticFamilies_Status`
        CHECK (`EvidenceStatus` IN ('Candidate', 'EvidenceOnly')),
    CONSTRAINT `CK_PacketCaptureSemanticFamilies_ProductionGate`
        CHECK (`ProductionEnabled` = 0),
    CONSTRAINT `CK_PacketCaptureSemanticFamilies_Score`
        CHECK (`MaxConfidenceScore` <= 100)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `packet_capture_protocol_applications` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `KnowledgeId` varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `PacketFamily` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `Direction` varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `FrameLength` int unsigned NOT NULL,
    `RecoveryStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'EvidenceOnly',
    `ProtocolConfidence` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Inferred',
    `VerifiedPromotion` tinyint(1) NOT NULL DEFAULT 0,
    `RepresentativeCandidateCount` int unsigned NOT NULL,
    `TotalObservedCount` int unsigned NOT NULL,
    `FieldSemanticsStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Unknown',
    `GameplaySemanticsStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'NotVerified',
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`, `KnowledgeId`),
    CONSTRAINT `FK_PacketCaptureProtocolApplications_Source`
        FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureProtocolApplications_Direction`
        CHECK (`Direction` IN ('ClientToServer', 'ServerToClient')),
    CONSTRAINT `CK_PacketCaptureProtocolApplications_EvidenceOnly`
        CHECK (`RecoveryStatus` = 'EvidenceOnly'
            AND `ProtocolConfidence` = 'Inferred'
            AND `VerifiedPromotion` = 0
            AND `FieldSemanticsStatus` = 'Unknown'
            AND `GameplaySemanticsStatus` = 'NotVerified')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `packet_capture_content_application_gates` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `ContentDomain` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `ApplicationStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `ObservedEvidenceCount` int unsigned NOT NULL,
    `Reason` varchar(1024) NOT NULL,
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`, `ContentDomain`),
    CONSTRAINT `FK_PacketCaptureContentApplicationGates_Source`
        FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureContentApplicationGates_Status`
        CHECK (`ApplicationStatus` IN ('EvidenceOnly', 'EvidenceBlocked', 'NotObserved', 'NotTransmitted', 'Candidate')),
    CONSTRAINT `CK_PacketCaptureContentApplicationGates_ProductionGate`
        CHECK (`ProductionEnabled` = 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `packet_capture_evidence_sources`
    (`EvidenceSourceId`, `EvidencePath`, `ImportPath`, `SchemaVersion`, `EvidencePolicy`, `GeneratedAtUtc`, `EvidenceSha256`,
     `SessionCount`, `RawTransportRecords`, `FrameCount`, `UnknownFrameCount`, `CandidateFrameCount`,
     `SemanticCandidateClusterCount`, `HighConfidenceSemanticCandidateCount`, `RepresentativeCandidateCount`,
     `AppliedKnowledgeCount`, `VerifiedGameplayClassificationCount`)
VALUES
    ('captured-packet-evidence-20260806',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json',
     'db/imports/evidence/packet_capture/captured_packet_evidence_20260806.database.json',
     'captured-packet-evidence-20260806-v1',
     'Captured process-scoped x86 God2_opt.exe Winsock evidence is retained as Candidate/EvidenceOnly. No gameplay packet is promoted to Verified without byte-exact repeated role evidence and field semantics.',
     '2026-08-06 02:07:46.954000',
     '1c955b87fea07b08ba562acb5425ddd65a52e3ab9adaf738c90e6f845e62c27a',
     2, 5522, 5522, 5522, 2618, 1290, 13, 50, 5, 0)
ON DUPLICATE KEY UPDATE
    `EvidencePath`=VALUES(`EvidencePath`),
    `ImportPath`=VALUES(`ImportPath`),
    `SchemaVersion`=VALUES(`SchemaVersion`),
    `EvidencePolicy`=VALUES(`EvidencePolicy`),
    `GeneratedAtUtc`=VALUES(`GeneratedAtUtc`),
    `EvidenceSha256`=VALUES(`EvidenceSha256`),
    `SessionCount`=VALUES(`SessionCount`),
    `RawTransportRecords`=VALUES(`RawTransportRecords`),
    `FrameCount`=VALUES(`FrameCount`),
    `UnknownFrameCount`=VALUES(`UnknownFrameCount`),
    `CandidateFrameCount`=VALUES(`CandidateFrameCount`),
    `SemanticCandidateClusterCount`=VALUES(`SemanticCandidateClusterCount`),
    `HighConfidenceSemanticCandidateCount`=VALUES(`HighConfidenceSemanticCandidateCount`),
    `RepresentativeCandidateCount`=VALUES(`RepresentativeCandidateCount`),
    `AppliedKnowledgeCount`=VALUES(`AppliedKnowledgeCount`),
    `VerifiedGameplayClassificationCount`=VALUES(`VerifiedGameplayClassificationCount`);

INSERT INTO `packet_capture_sessions`
    (`EvidenceSourceId`, `SessionId`, `SourceLabel`, `Availability`, `TargetExecutable`, `TargetProcessId`, `TargetArchitecture`,
     `RawTransportRecords`, `ClientToServerRecords`, `ServerToClientRecords`, `PayloadBytes`, `FrameCount`, `UnknownFrameCount`,
     `CandidateFrameCount`, `SemanticCandidateClusterCount`, `HighConfidenceSemanticCandidateCount`,
     `VerifiedGameplayClassificationCount`, `EvidenceStatus`, `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('captured-packet-evidence-20260806', '2026-08-06_09-36-00_FD3FE6E1-887D-433A-83A5-FE84EF3962BC',
     'Submitted PacketCapture.zip 2026-08-06 09:49', 'ArchiveNoLongerPresent; extracted evidence retained',
     'God2_opt.exe', 728, 'x86', 1896, 375, 1521, 36892, 1896, 1896, 882, 454, 6, 0,
     'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', '2026-08-06_08-32-03_86FA350C-1757-4C70-B295-83EEF58EDBE7',
     'Previous PacketCapture.zip extracted in Temp', 'ArchiveNoLongerPresent; extracted session retained',
     'God2_opt.exe', 6136, 'x86', 3626, 838, 2788, 54611, 3626, 3626, 1736, 836, 7, 0,
     'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json')
ON DUPLICATE KEY UPDATE
    `SourceLabel`=VALUES(`SourceLabel`),
    `Availability`=VALUES(`Availability`),
    `TargetProcessId`=VALUES(`TargetProcessId`),
    `RawTransportRecords`=VALUES(`RawTransportRecords`),
    `ClientToServerRecords`=VALUES(`ClientToServerRecords`),
    `ServerToClientRecords`=VALUES(`ServerToClientRecords`),
    `PayloadBytes`=VALUES(`PayloadBytes`),
    `FrameCount`=VALUES(`FrameCount`),
    `UnknownFrameCount`=VALUES(`UnknownFrameCount`),
    `CandidateFrameCount`=VALUES(`CandidateFrameCount`),
    `SemanticCandidateClusterCount`=VALUES(`SemanticCandidateClusterCount`),
    `HighConfidenceSemanticCandidateCount`=VALUES(`HighConfidenceSemanticCandidateCount`),
    `VerifiedGameplayClassificationCount`=VALUES(`VerifiedGameplayClassificationCount`),
    `EvidenceReference`=VALUES(`EvidenceReference`);

INSERT INTO `packet_capture_semantic_families`
    (`EvidenceSourceId`, `SemanticName`, `ClusterCount`, `ObservedCount`, `MaxConfidenceScore`, `EvidenceStatus`, `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('captured-packet-evidence-20260806', 'UnknownOpcodeSemanticCandidate', 1072, 1671, 64, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'ServerLargeResponseOrEntityStateCandidate', 176, 210, 46, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'ServerWorldStateDeltaCandidate', 14, 253, 80, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'EncryptedLoginOrHandshakeBlobCandidate', 10, 10, 80, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'ClientMovementOrActionCandidate', 10, 188, 72, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'ClientHeartbeatOrKeepAliveCandidate', 6, 284, 80, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'ClientLargeRequestOrEncryptedStateCandidate', 2, 2, 40, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json')
ON DUPLICATE KEY UPDATE
    `ClusterCount`=VALUES(`ClusterCount`),
    `ObservedCount`=VALUES(`ObservedCount`),
    `MaxConfidenceScore`=VALUES(`MaxConfidenceScore`),
    `EvidenceReference`=VALUES(`EvidenceReference`);

INSERT INTO `packet_capture_protocol_applications`
    (`EvidenceSourceId`, `KnowledgeId`, `PacketFamily`, `Direction`, `FrameLength`, `RecoveryStatus`, `ProtocolConfidence`,
     `VerifiedPromotion`, `RepresentativeCandidateCount`, `TotalObservedCount`, `FieldSemanticsStatus`,
     `GameplaySemanticsStatus`, `EvidenceReference`)
VALUES
    ('captured-packet-evidence-20260806', 'capture-20260806-login-handshake-c2s-208-candidate',
     'Login', 'ClientToServer', 208, 'EvidenceOnly', 'Inferred', 0, 10, 10, 'Unknown', 'NotVerified',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'capture-20260806-client-keepalive-c2s-5-candidate',
     'Heartbeat', 'ClientToServer', 5, 'EvidenceOnly', 'Inferred', 0, 6, 284, 'Unknown', 'NotVerified',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'capture-20260806-client-action-c2s-20-candidate',
     'Movement', 'ClientToServer', 20, 'EvidenceOnly', 'Inferred', 0, 10, 188, 'Unknown', 'NotVerified',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'capture-20260806-server-world-delta-s2c-20-candidate',
     'WorldState', 'ServerToClient', 20, 'EvidenceOnly', 'Inferred', 0, 5, 110, 'Unknown', 'NotVerified',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'capture-20260806-server-world-delta-s2c-14-candidate',
     'WorldState', 'ServerToClient', 14, 'EvidenceOnly', 'Inferred', 0, 3, 74, 'Unknown', 'NotVerified',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json')
ON DUPLICATE KEY UPDATE
    `RepresentativeCandidateCount`=VALUES(`RepresentativeCandidateCount`),
    `TotalObservedCount`=VALUES(`TotalObservedCount`),
    `EvidenceReference`=VALUES(`EvidenceReference`);

INSERT INTO `packet_capture_content_application_gates`
    (`EvidenceSourceId`, `ContentDomain`, `ApplicationStatus`, `ObservedEvidenceCount`, `Reason`, `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('captured-packet-evidence-20260806', 'ProtocolKnowledge', 'EvidenceOnly', 5,
     'Five EvidenceOnly PacketKnowledge rows were registered from captured packet candidates; no handler or serializer was promoted to Verified.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'NPC', 'EvidenceBlocked', 0,
     'No verified NPC identity, spawn, coordinate, dialog, HP/MP/stat or packet field semantics were decoded from this capture batch.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'Monster', 'EvidenceBlocked', 0,
     'No verified monster identity, spawn, level, HP/MP, stat, elemental or combat packet field semantics were decoded from this capture batch.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'Character', 'EvidenceBlocked', 0,
     'No verified character create/select/delete/logout gameplay fields were decoded from this capture batch.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'PetBattlePetImmortal', 'EvidenceBlocked', 0,
     'No verified pet, battle-pet or immortal identity/stat/skill packet fields were decoded from this capture batch.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'MountMovement', 'EvidenceBlocked', 0,
     'Movement/action candidates exist, but mount state, mounted speed and eight-direction field semantics remain unverified.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'Quest', 'EvidenceBlocked', 0,
     'No verified quest accept/progress/complete/fail/abandon/share/reward packet fields were decoded from this capture batch.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'Skill', 'EvidenceBlocked', 0,
     'No verified skill cast/facet/effect/cost or item-vs-skill separation fields were decoded from this capture batch.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'ItemInventoryShop', 'EvidenceBlocked', 0,
     'No verified item, inventory, shop buy/sell/repair or currency packet fields were decoded from this capture batch.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'Party', 'EvidenceBlocked', 0,
     'No verified party create/invite/join/leave/kick/disband/lifecycle packet fields were decoded from this capture batch.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json'),
    ('captured-packet-evidence-20260806', 'FormulaRecovery', 'EvidenceBlocked', 0,
     'No verified level-up, battle, damage, elemental, hit, dodge, crit or turn-speed formula samples were decoded from this capture batch.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json')
ON DUPLICATE KEY UPDATE
    `ApplicationStatus`=VALUES(`ApplicationStatus`),
    `ObservedEvidenceCount`=VALUES(`ObservedEvidenceCount`),
    `Reason`=VALUES(`Reason`),
    `EvidenceReference`=VALUES(`EvidenceReference`);
