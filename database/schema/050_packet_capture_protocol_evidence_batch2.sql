-- PacketCapture.zip batch2 evidence from a real x86 God2_opt.exe session.
-- Additive evidence-only migration. Do not rewrite 049 after it has been applied.

ALTER TABLE `packet_capture_sessions`
    ADD COLUMN IF NOT EXISTS `RuntimeSessionId` char(36) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `SessionId`,
    ADD COLUMN IF NOT EXISTS `AnalysisRunId` char(36) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `RuntimeSessionId`,
    ADD COLUMN IF NOT EXISTS `CaptureSource` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `TargetArchitecture`,
    ADD COLUMN IF NOT EXISTS `Transport` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `CaptureSource`,
    ADD COLUMN IF NOT EXISTS `InjectionSequence` varchar(256) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `Transport`,
    ADD COLUMN IF NOT EXISTS `ReadinessHandshake` varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `InjectionSequence`;

INSERT INTO `packet_capture_evidence_sources`
    (`EvidenceSourceId`, `EvidencePath`, `ImportPath`, `SchemaVersion`, `EvidencePolicy`, `GeneratedAtUtc`, `EvidenceSha256`,
     `SessionCount`, `RawTransportRecords`, `FrameCount`, `UnknownFrameCount`, `CandidateFrameCount`,
     `SemanticCandidateClusterCount`, `HighConfidenceSemanticCandidateCount`, `RepresentativeCandidateCount`,
     `AppliedKnowledgeCount`, `VerifiedGameplayClassificationCount`)
VALUES
    ('captured-packet-evidence-20260806-batch2',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json',
     'db/imports/evidence/packet_capture/captured_packet_evidence_20260806_batch2.database.json',
     'captured-packet-evidence-20260806-batch2-v1',
     'Captured process-scoped x86 God2_opt.exe Winsock evidence is retained as Candidate/EvidenceOnly. No gameplay packet is promoted to Verified without byte-exact repeated role evidence and field semantics.',
     '2026-08-06 02:55:14.996000',
     '733a172df16ceec3eb518587e749e1db74f2dc556673d495abd73ea9915db1e2',
     1, 4779, 4779, 4779, 2451, 1334, 12, 50, 5, 0)
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
    (`EvidenceSourceId`, `SessionId`, `RuntimeSessionId`, `AnalysisRunId`, `SourceLabel`, `Availability`,
     `TargetExecutable`, `TargetProcessId`, `TargetArchitecture`, `CaptureSource`, `Transport`,
     `InjectionSequence`, `ReadinessHandshake`, `RawTransportRecords`, `ClientToServerRecords`,
     `ServerToClientRecords`, `PayloadBytes`, `FrameCount`, `UnknownFrameCount`, `CandidateFrameCount`,
     `SemanticCandidateClusterCount`, `HighConfidenceSemanticCandidateCount`, `VerifiedGameplayClassificationCount`,
     `EvidenceStatus`, `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('captured-packet-evidence-20260806-batch2',
     '2026-08-06_10-31-22_36125A66-1515-4051-9B83-3F31B75FF3C9',
     'DA1367E4-128F-409D-A7F7-7D88132DAD43',
     '7588444D-8D06-43E8-9D84-0B91441C426D',
     'Submitted PacketCapture.zip 2026-08-06 10:49',
     'ArchiveAvailable; extracted evidence retained',
     'God2_opt.exe', 6948, 'x86', 'OptInX86Dll', 'InjectedWinsock',
     'PauseVerifiedPrimaryThread,RemoteThreadLoadLibraryW,ResumePrimaryThread',
     'God2TraceProbeWaitReady',
     4779, 1287, 3492, 60071, 4779, 4779, 2451, 1334, 12, 0,
     'Candidate', 0,
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json')
ON DUPLICATE KEY UPDATE
    `RuntimeSessionId`=VALUES(`RuntimeSessionId`),
    `AnalysisRunId`=VALUES(`AnalysisRunId`),
    `SourceLabel`=VALUES(`SourceLabel`),
    `Availability`=VALUES(`Availability`),
    `TargetProcessId`=VALUES(`TargetProcessId`),
    `CaptureSource`=VALUES(`CaptureSource`),
    `Transport`=VALUES(`Transport`),
    `InjectionSequence`=VALUES(`InjectionSequence`),
    `ReadinessHandshake`=VALUES(`ReadinessHandshake`),
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
    ('captured-packet-evidence-20260806-batch2', 'UnknownOpcodeSemanticCandidate', 1181, 1871, 64, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'ClientHeartbeatOrKeepAliveCandidate', 3, 266, 80, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'ServerLargeResponseOrEntityStateCandidate', 127, 130, 40, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'ServerWorldStateDeltaCandidate', 6, 126, 72, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'ClientMovementOrActionCandidate', 4, 45, 64, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'EncryptedLoginOrHandshakeBlobCandidate', 10, 10, 80, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'ClientLargeRequestOrEncryptedStateCandidate', 3, 3, 40, 'Candidate', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json')
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
    ('captured-packet-evidence-20260806-batch2', 'capture-20260806-login-handshake-c2s-208-candidate',
     'Login', 'ClientToServer', 208, 'EvidenceOnly', 'Inferred', 0, 10, 10, 'Unknown', 'NotVerified',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'capture-20260806-client-keepalive-c2s-5-candidate',
     'Heartbeat', 'ClientToServer', 5, 'EvidenceOnly', 'Inferred', 0, 3, 266, 'Unknown', 'NotVerified',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'capture-20260806-client-action-c2s-20-candidate',
     'Movement', 'ClientToServer', 20, 'EvidenceOnly', 'Inferred', 0, 1, 10, 'Unknown', 'NotVerified',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'capture-20260806-server-world-delta-s2c-20-candidate',
     'WorldState', 'ServerToClient', 20, 'EvidenceOnly', 'Inferred', 0, 3, 53, 'Unknown', 'NotVerified',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'capture-20260806-server-world-delta-s2c-14-candidate',
     'WorldState', 'ServerToClient', 14, 'EvidenceOnly', 'Inferred', 0, 2, 61, 'Unknown', 'NotVerified',
     'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json')
ON DUPLICATE KEY UPDATE
    `RepresentativeCandidateCount`=VALUES(`RepresentativeCandidateCount`),
    `TotalObservedCount`=VALUES(`TotalObservedCount`),
    `EvidenceReference`=VALUES(`EvidenceReference`);

INSERT INTO `packet_capture_content_application_gates`
    (`EvidenceSourceId`, `ContentDomain`, `ApplicationStatus`, `ObservedEvidenceCount`, `Reason`, `ProductionEnabled`, `EvidenceReference`)
VALUES
    ('captured-packet-evidence-20260806-batch2', 'ProtocolKnowledge', 'EvidenceOnly', 5,
     'Batch2 adds repeated EvidenceOnly packet candidates for existing protocol knowledge; no handler or serializer was promoted to Verified.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'NPC', 'EvidenceBlocked', 0,
     'No verified NPC packet field semantics were decoded from PacketCapture.zip batch2.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'Monster', 'EvidenceBlocked', 0,
     'No verified monster packet field semantics were decoded from PacketCapture.zip batch2.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'Character', 'EvidenceBlocked', 0,
     'No verified character lifecycle packet field semantics were decoded from PacketCapture.zip batch2.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'PetBattlePetImmortal', 'EvidenceBlocked', 0,
     'No verified pet, battle-pet or immortal packet field semantics were decoded from PacketCapture.zip batch2.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'MountMovement', 'EvidenceBlocked', 0,
     'Movement/action candidates exist, but mount state, mounted speed and eight-direction field semantics remain unverified.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'Quest', 'EvidenceBlocked', 0,
     'No verified quest packet field semantics were decoded from PacketCapture.zip batch2.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'Skill', 'EvidenceBlocked', 0,
     'No verified skill packet field semantics were decoded from PacketCapture.zip batch2.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'ItemInventoryShop', 'EvidenceBlocked', 0,
     'No verified item, inventory, shop or currency packet field semantics were decoded from PacketCapture.zip batch2.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'Party', 'EvidenceBlocked', 0,
     'No verified party lifecycle packet field semantics were decoded from PacketCapture.zip batch2.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json'),
    ('captured-packet-evidence-20260806-batch2', 'FormulaRecovery', 'EvidenceBlocked', 0,
     'No verified formula sample fields were decoded from PacketCapture.zip batch2.',
     0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json')
ON DUPLICATE KEY UPDATE
    `ApplicationStatus`=VALUES(`ApplicationStatus`),
    `ObservedEvidenceCount`=VALUES(`ObservedEvidenceCount`),
    `Reason`=VALUES(`Reason`),
    `EvidenceReference`=VALUES(`EvidenceReference`);
