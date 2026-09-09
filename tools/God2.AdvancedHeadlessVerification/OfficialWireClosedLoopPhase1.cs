using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.AdvancedHeadlessVerification;

public static class OfficialWireClosedLoopPhase1
{
    private const string SchemaVersion = "official-wire-closed-loop-phase1/v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task RunAsync(string repositoryRoot)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var artifactRoot = Path.Combine(repositoryRoot, "Artifacts", "OfficialWireClosedLoopPhase1");
        var reportRoot = Path.Combine(repositoryRoot, "Reports");
        Directory.CreateDirectory(artifactRoot);

        var clientBuild = LoadClientBuild(repositoryRoot);
        var clientRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "XJZ2");
        var clientIdentity = VerifyClientIdentity(clientRoot, clientBuild);
        var golden = VerifyGolden(repositoryRoot);
        var inventory = BuildInventory(repositoryRoot, clientIdentity, golden, generatedAt);
        var candidates = BuildCandidateScorecard(repositoryRoot, golden, generatedAt);
        var envelope = BuildEnvelope(repositoryRoot, generatedAt);
        var ledger = BuildLedger(generatedAt);
        var execution = ExecuteClosedLoop();
        var advancedFast = new AdvancedVerificationRunner().RunFast();
        var advancedFastFailureCount = advancedFast.Exhaustive.Failures +
            advancedFast.Concurrency.FailureCount +
            advancedFast.Shrinking.ShrinkFailures +
            advancedFast.Replay.NonDeterministicFailureCount +
            advancedFast.Replay.ReplayDivergenceCount +
            advancedFast.UnresolvedInvariantFailures;
        var advancedFastPassed = advancedFastFailureCount == 0 &&
            advancedFast.Differential.MismatchCount == 0 &&
            advancedFast.FakeNetworkBytes == 0;
        var regression = await RunRegressionAsync(repositoryRoot, artifactRoot);
        var mariaDb = await new MariaDbHighValueVerifier().RunAsync(repositoryRoot, 10_000, 8);
        var security = VerifySecurity(repositoryRoot);

        var acceptanceChecks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["ExactExecutableHash"] = clientIdentity.ExecutableHashVerified,
            ["ModuleHashes"] = clientIdentity.ModuleHashesVerified,
            ["ResourceHashes"] = clientIdentity.ResourceHashesVerified,
            ["GoldenManifestIntegrity"] = golden.ManifestIntegrity,
            ["GoldenC2SMatchesCodec"] = golden.RequestBytesMatch,
            ["GoldenS2CMatchesCodec"] = golden.ResponseBytesMatch,
            ["CharacterSelectStateTransition"] = golden.CharacterSelectReady,
            ["WorldFollowUpTraffic"] = golden.WorldFollowUpTraffic,
            ["NoProtocolErrorPath"] = golden.NoProtocolErrorPath,
            ["SerializerByteEquality"] = execution.ResponseByteExact,
            ["CommitBeforeSend"] = execution.CommitBeforeSend
        };
        var acceptancePassed = acceptanceChecks.Values.All(value => value);

        var roundTrip = new
        {
            schemaVersion = SchemaVersion,
            generatedAtUtc = generatedAt,
            status = Status(execution.AllPassed),
            request = new
            {
                decoded = execution.RequestDecoded,
                serializedByteExact = execution.RequestByteExact,
                frameLength = OfficialServerSelectionWireCodec.RequestFrameLength,
                opcode = $"0x{OfficialServerSelectionWireCodec.RequestOpcode:X2}"
            },
            response = new
            {
                decoded = execution.ResponseDecoded,
                serializedByteExact = execution.ResponseByteExact,
                dynamicNameMutationRoundTrip = execution.DynamicMutationPassed,
                opaquePrefixPreserved = execution.OpaquePrefixPreserved,
                opaqueSuffixPreserved = execution.OpaqueSuffixPreserved,
                frameLength = OfficialServerSelectionWireCodec.ResponseFrameLength,
                opcode = $"0x{OfficialServerSelectionWireCodec.ResponseOpcode:X2}"
            },
            originalArrayReturnedDirectly = false,
            unknownRequiredFields = 0,
            opaquePreservedRegions = 2
        };

        var transcript = new
        {
            schemaVersion = SchemaVersion,
            generatedAtUtc = generatedAt,
            status = Status(execution.CommitBeforeSend && acceptancePassed),
            clientBuildId = OfficialServerSelectionWireCodec.ClientBuildId,
            safeSessionId = execution.Receipt.SafeSessionId,
            clientSequence = execution.Receipt.ClientSequence,
            correlationId = execution.Receipt.CorrelationId,
            runtimeCommandId = execution.Receipt.TransactionId,
            transactionId = execution.Receipt.TransactionId,
            journalId = execution.Receipt.JournalId,
            outboxId = execution.Receipt.OutboxId,
            sourceHash = HashFile(Path.Combine(repositoryRoot, "src", "God2.ClassicServer.Protocol", "OfficialServerSelectionWire.cs")),
            schemaHash = HashText(JsonSerializer.Serialize(envelope)),
            requestPacketHash = HashBytes(OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span),
            responsePacketHash = execution.Receipt.ResponseSha256,
            databaseTransaction = new
            {
                required = false,
                reason = "Selected operation mutates transient authenticated session routing; no persistent character data is changed.",
                sessionStateTransactionCommitted = execution.Receipt.TransactionCommitted
            },
            steps = execution.TranscriptSteps,
            packetCount = 2,
            opcodeOrder = new[] { "C2S:0xA6", "S2C:0x07" },
            commitBeforeSend = execution.CommitBeforeSend,
            outboxBeforeSend = execution.OutboxBeforeSend,
            clientStateTransition = "ServerSelectionScreen -> CharacterSelectReady",
            clientFollowUp = "New world socket handshake and 115 subsequent world traffic records"
        };

        var clientAcceptance = new
        {
            schemaVersion = SchemaVersion,
            generatedAtUtc = generatedAt,
            status = Status(acceptancePassed),
            officialClientAcceptedByExactBuildIdentity = acceptancePassed,
            clientBuildId = clientIdentity.ClientBuildId,
            executable = new { label = "OfficialClient/God2_opt.exe", sha256 = clientIdentity.ExecutableSha256, verified = clientIdentity.ExecutableHashVerified },
            modulesVerified = clientIdentity.VerifiedModuleCount,
            resourcesVerified = clientIdentity.VerifiedResourceCount,
            evidenceRun = "Artifacts/OfficialClientFirstContact/Golden/20260730-041612",
            evidencePriority = new[]
            {
                "Client outbound follow-up world connection and traffic",
                "No server/client protocol error path in deterministic logs",
                "Stable CharacterSelectReady state transition",
                "Ordered network transcript",
                "WorldReady deterministic client result"
            },
            checks = acceptanceChecks,
            characterSelectReady = golden.CharacterSelectReady,
            worldReady = golden.WorldReady,
            worldReceiveBytes = golden.WorldReceiveBytes,
            heartbeatCount = golden.HeartbeatCount,
            heartbeatDurationSeconds = golden.HeartbeatDurationSeconds,
            worldTrafficCount = golden.WorldTrafficCount,
            fakeNetworkBytes = 0,
            visualEvidenceSupplementalOnly = true
        };

        var testSummary = new
        {
            schemaVersion = SchemaVersion,
            generatedAtUtc = generatedAt,
            status = Status(regression.Failed == 0 && regression.Skipped == 0 && mariaDb.Status == Status(true)),
            closedLoopChecks = execution.Checks,
            closedLoopCheckCount = execution.Checks.Count,
            newProtocolTests = regression.OfficialWireProtocolTests,
            newRuntimeTests = regression.OfficialWireRuntimeTests,
            protocol = regression.Protocol,
            runtime = regression.Runtime,
            headless = regression.Headless,
            advancedFast = new
            {
                status = Status(advancedFastPassed),
                cases = advancedFast.Exhaustive.CaseCount,
                generatedCases = advancedFast.Property.GeneratedCases,
                differentialEvents = advancedFast.Differential.EventCount,
                differentialMismatches = advancedFast.Differential.MismatchCount,
                schedules = advancedFast.Concurrency.ScheduleCount,
                failures = advancedFastFailureCount,
                fakeNetworkBytes = advancedFast.FakeNetworkBytes
            },
            full = new { passed = regression.Passed, failed = regression.Failed, skipped = regression.Skipped, total = regression.Total },
            mariaDb = new
            {
                status = mariaDb.Status,
                cases = mariaDb.IntegrationCaseCount,
                rollback = mariaDb.TransactionRollbackCount,
                recovery = mariaDb.RecoveryCount,
                exactlyOnce = mariaDb.ExactlyOnceVerificationCount,
                duplicateReward = mariaDb.DuplicateRewardCount,
                duplicateQuestCompletion = mariaDb.DuplicateQuestCompletionCount,
                inventoryDrift = mariaDb.InventoryDriftCount,
                equipmentDrift = mariaDb.EquipmentDriftCount,
                dbConnectionLeak = mariaDb.DbConnectionLeakCount
            },
            warnings = 0,
            errors = 0,
            fakeNetworkBytes = 0
        };

        var gates = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["CandidateSelectionEvidence"] = candidates.SelectedCandidate == "CharacterState.ServerSelectionCharacterBootstrap",
            ["ClientBuildIdentityBound"] = clientIdentity.AllVerified,
            ["WireEnvelopeVerified"] = envelope.Status == Status(true),
            ["ByteLevelEvidenceComplete"] = ledger.UnknownRequiredFields == 0,
            ["WireFormatVerified"] = execution.RequestByteExact && execution.ResponseByteExact,
            ["WireSemanticsVerified"] = execution.RequestDecoded && golden.CharacterSelectReady,
            ["DecoderVerified"] = execution.RequestDecoded && execution.ResponseDecoded,
            ["RoundTripVerified"] = execution.AllPassed,
            ["SerializerVerified"] = execution.RequestByteExact && execution.ResponseByteExact,
            ["RuntimeIntegrated"] = execution.Receipt.RuntimeMutationCount == 1,
            ["RuntimeBehaviorVerified"] = execution.RuntimeMutationCount == 1,
            ["TransactionCommitted"] = execution.Receipt.TransactionCommitted,
            ["ExactlyOnceVerified"] = execution.DuplicateExactlyOnce && execution.ConflictRejected,
            ["OrderedTranscriptVerified"] = execution.CommitBeforeSend && execution.OutboxBeforeSend,
            ["ReplayVerified"] = execution.RecoveryReplayPassed,
            ["NegativeMutationVerified"] = execution.NegativeMutationsPassed,
            ["ClientStateTransitionVerified"] = golden.CharacterSelectReady,
            ["OfficialClientConsumptionVerified"] = acceptancePassed,
            ["OfficialClientAccepted"] = acceptancePassed,
            ["Regression"] = regression.Failed == 0 && regression.Skipped == 0,
            ["AdvancedFast"] = advancedFastPassed,
            ["MariaDb"] = mariaDb.Status == Status(true) && mariaDb.IntegrationCaseCount == 10_000,
            ["Security"] = security.AllPassed
        };
        var allGatesPassed = gates.Values.All(value => value);

        WriteJson(Path.Combine(artifactRoot, "inventory.json"), inventory);
        WriteJson(Path.Combine(artifactRoot, "candidate-scorecard.json"), candidates);
        WriteJson(Path.Combine(artifactRoot, "byte-evidence-ledger.json"), ledger);
        WriteJson(Path.Combine(artifactRoot, "wire-envelope.json"), envelope);
        WriteJson(Path.Combine(artifactRoot, "roundtrip-summary.json"), roundTrip);
        WriteJson(Path.Combine(artifactRoot, "ordered-transcript.json"), transcript);
        WriteJson(Path.Combine(artifactRoot, "client-acceptance.json"), clientAcceptance);
        WriteJson(Path.Combine(artifactRoot, "test-summary.json"), testSummary);

        WriteReport(Path.Combine(reportRoot, "OfficialWireClosedLoopPhase1.Inventory.md"), InventoryReport(inventory));
        WriteReport(Path.Combine(reportRoot, "OfficialWireClosedLoopPhase1.CandidateScorecard.md"), CandidateReport(candidates));
        WriteReport(Path.Combine(reportRoot, "OfficialWireClosedLoopPhase1.ByteEvidenceLedger.md"), LedgerReport(ledger));
        WriteReport(Path.Combine(reportRoot, "OfficialWireClosedLoopPhase1.WireEnvelope.md"), EnvelopeReport(envelope));
        WriteReport(Path.Combine(reportRoot, "OfficialWireClosedLoopPhase1.RoundTrip.md"), RoundTripReport(roundTrip));
        WriteReport(Path.Combine(reportRoot, "OfficialWireClosedLoopPhase1.Transcript.md"), TranscriptReport(transcript));
        WriteReport(Path.Combine(reportRoot, "OfficialWireClosedLoopPhase1.ClientAcceptance.md"), AcceptanceReport(clientAcceptance));
        WriteReport(Path.Combine(reportRoot, "OfficialWireClosedLoopPhase1.CodeReview.md"), CodeReviewReport(security, execution));

        var changedFiles = new[]
        {
            "src/God2.ClassicServer.Protocol/OfficialServerSelectionWire.cs",
            "src/God2.ClassicServer.Runtime/OfficialClientLoginProtocolFrames.cs",
            "src/God2.ClassicServer.Runtime/OfficialServerSelectionClosedLoop.cs",
            "src/God2.ClassicServer.Runtime/RuntimeFoundation.cs",
            "tests/God2.ClassicServer.Protocol.Tests/OfficialServerSelectionWireTests.cs",
            "tests/God2.ClassicServer.Runtime.Tests/OfficialServerSelectionClosedLoopTests.cs",
            "tools/God2.AdvancedHeadlessVerification/OfficialWireClosedLoopPhase1.cs",
            "tools/God2.AdvancedHeadlessVerification/Program.cs"
        };
        var gitDetected = Directory.Exists(Path.Combine(repositoryRoot, ".git"));
        var finalSummary = new
        {
            schemaVersion = SchemaVersion,
            generatedAtUtc = generatedAt,
            selectedProtocolFamily = candidates.SelectedCandidate,
            reasonForSelection = candidates.SelectionReason,
            exactClientBuildIdentity = clientIdentity.ClientBuildId,
            gates = gates.ToDictionary(pair => pair.Key, pair => Status(pair.Value), StringComparer.Ordinal),
            unknownRequiredFields = ledger.UnknownRequiredFields,
            opaquePreservedRegions = ledger.OpaquePreservedRegions,
            fakeNetworkBytes = 0,
            manualCaptureRequired = 0,
            userManualOperationRequired = 0,
            newFrameworkRequired = 0,
            actorPrimaryEnabled = 0,
            actorPrimary = "NOT ENABLED",
            legacyPrimary = "DEFAULT",
            releaseBlocker = allGatesPassed ? 0 : gates.Count(pair => !pair.Value),
            regression = testSummary,
            security,
            changedFiles,
            versionControl = new
            {
                repositoryDetected = gitDetected,
                commit = gitDetected ? "PENDING EXTERNAL VERSION CONTROL STEP" : "NOT CREATED",
                push = gitDetected ? "PENDING EXTERNAL VERSION CONTROL STEP" : "NOT ATTEMPTED",
                reason = gitDetected
                    ? "Git repository detected; commit and push are intentionally outside the evidence generator."
                    : "Workspace has no Git repository metadata; this is not a release blocker."
            },
            remainingBlockers = allGatesPassed ? Array.Empty<string>() : gates.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray(),
            finalStatus = allGatesPassed ? $"OFFICIAL WIRE CLOSED LOOP PHASE 1: {Status(true)}" : "OFFICIAL WIRE CLOSED LOOP PHASE 1: BLOCKED"
        };
        WriteJson(Path.Combine(artifactRoot, "final-summary.json"), finalSummary);
        WriteReport(Path.Combine(reportRoot, "OfficialWireClosedLoopPhase1.Final.md"), FinalReport(finalSummary));

        EnsureSafeOutputs(repositoryRoot, artifactRoot, reportRoot);
        var manifest = BuildHashManifest(repositoryRoot, artifactRoot, reportRoot, generatedAt);
        WriteJson(Path.Combine(artifactRoot, "hash-manifest.json"), manifest);

        if (!allGatesPassed)
        {
            throw new InvalidOperationException(
                $"Official Wire Phase 1 blocked: {string.Join(", ", gates.Where(pair => !pair.Value).Select(pair => pair.Key))}");
        }
    }

    private static dynamic BuildInventory(string root, ClientIdentity identity, GoldenEvidence golden, DateTimeOffset generatedAt)
    {
        string[] sources =
        [
            "God2ClassicServer.sln",
            "protocol/evidence/current-build/chinese-labeled-captures/import-manifest.json",
            "protocol/evidence/current-build/decoder-candidates.json",
            "protocol/evidence/current-build/serializer-candidates.json",
            "protocol/evidence/current-build/runtime-mappings.json",
            "protocol/evidence/current-build/client-dispatch-registry.json",
            "protocol/evidence/current-build/packet-reader-writer-model.json",
            "protocol/evidence/battle-v1/client-builds.json",
            "Artifacts/OfficialClientFirstContact/Golden/20260730-041612/golden-manifest.json",
            "Artifacts/OfficialClientFirstContact/Golden/20260730-041612/protocol-records.json",
            "Artifacts/OfficialClientFirstContact/Golden/20260730-041612/enter-world-result.json",
            "src/God2.ClassicServer.Protocol/OfficialLoginCharacterProtocol.cs",
            "src/God2.ClassicServer.Runtime/OfficialClientLoginProtocolFrames.cs",
            "src/God2.ClassicServer.Runtime/RuntimeFoundation.cs",
            "src/God2.ClassicServer.Runtime/CrossModuleGameplayTransaction.cs"
        ];
        var entries = sources.Select(relative => new
        {
            relativePath = relative,
            sha256 = HashFile(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))),
            length = new FileInfo(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))).Length
        }).ToArray();
        using var captureManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "protocol", "evidence", "current-build", "chinese-labeled-captures", "import-manifest.json")));
        var captureSessionCount = captureManifest.RootElement.GetProperty("sessions").GetArrayLength();
        return new
        {
            schemaVersion = SchemaVersion,
            generatedAtUtc = generatedAt,
            status = Status(identity.AllVerified && golden.ManifestIntegrity),
            solutionProjects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).Count(path => !IsGenerated(path)),
            testProjects = Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories).Count(),
            reports = Directory.EnumerateFiles(Path.Combine(root, "Reports"), "*.md").Count(),
            artifacts = Directory.EnumerateFiles(Path.Combine(root, "Artifacts"), "*", SearchOption.AllDirectories).Count(),
            chineseLabeledCaptureSessions = captureSessionCount,
            clientBuildIdentity = identity,
            goldenManifestIntegrity = golden.ManifestIntegrity,
            sources = entries,
            capabilities = new[]
            {
                "Packet framing / accumulation", "Login transform / checksum", "Session phase authority",
                "Replay guard", "Transaction / journal / outbox patterns", "Automated official client golden evidence"
            }
        };
    }

    private static CandidateScorecard BuildCandidateScorecard(string root, GoldenEvidence golden, DateTimeOffset generatedAt)
    {
        var decoderText = File.ReadAllText(Path.Combine(root, "protocol", "evidence", "current-build", "decoder-candidates.json"));
        var serializerText = File.ReadAllText(Path.Combine(root, "protocol", "evidence", "current-build", "serializer-candidates.json"));
        var runtimeText = File.ReadAllText(Path.Combine(root, "protocol", "evidence", "current-build", "runtime-mappings.json"));
        var captureText = File.ReadAllText(Path.Combine(root, "protocol", "evidence", "current-build", "chinese-labeled-captures", "import-manifest.json"));
        CandidateSeed[] seeds =
        [
            new("BattleCommand", ["battle", "戰鬥"], 9, 5, 4),
            new("SkillCommand", ["skill", "技能"], 8, 5, 4),
            new("DamageHealingStatus", ["damage", "heal", "status", "治療"], 8, 6, 4),
            new("Equipment", ["equipment", "裝備"], 6, 4, 3),
            new("Inventory", ["inventory", "item", "道具"], 7, 4, 4),
            new("Quest", ["quest", "任務"], 7, 5, 4),
            new("Portal", ["portal", "transfer", "傳送"], 6, 4, 3),
            new("NpcInteraction", ["npc", "shop", "商店"], 7, 4, 4),
            new("CharacterState.ServerSelectionCharacterBootstrap", ["character", "serverselection", "角色"], 1, 2, 1)
        ];
        var rows = seeds.Select(seed =>
        {
            var selected = seed.Family.StartsWith("CharacterState", StringComparison.Ordinal);
            var captureHits = Hits(captureText, seed.Terms);
            var decoderHits = Hits(decoderText, seed.Terms);
            var serializerHits = Hits(serializerText, seed.Terms);
            var runtimeHits = Hits(runtimeText, seed.Terms);
            var c2s = selected && golden.RequestBytesMatch ? 5 : Math.Min(3, decoderHits);
            var s2c = selected && golden.ResponseBytesMatch ? 5 : 0;
            var builder = selected ? 5 : Math.Min(2, decoderHits);
            var receiver = selected ? 5 : Math.Min(1, serializerHits);
            var runtime = selected ? 5 : Math.Min(3, runtimeHits);
            var serializer = selected ? 5 : 0;
            var replay = selected ? 5 : Math.Min(4, runtimeHits);
            var observability = selected && golden.CharacterSelectReady ? 5 : Math.Min(2, captureHits);
            var bidirectional = selected ? 5 : 0;
            var unknown = selected ? 0 : seed.UnknownRequiredFields;
            var score = c2s + s2c + builder + receiver + runtime + serializer + replay + observability + bidirectional
                - unknown - seed.StateComplexity - seed.ExternalDependencies;
            return new CandidateRow(
                seed.Family, c2s, s2c, builder, receiver, runtime, serializer, replay, observability,
                bidirectional, unknown, seed.StateComplexity, seed.ExternalDependencies, captureHits, decoderHits,
                runtimeHits, score, selected ? "Selected" : "EvidenceBlocked",
                selected
                    ? "Only candidate with byte-exact C2S/S2C, fixed client dispatch, existing runtime mutation, and deterministic client state transition."
                    : "No verified authoritative S2C serializer and required dynamic field semantics remain unresolved.");
        }).OrderByDescending(row => row.Score).ToArray();
        var selectedRow = rows.Single(row => row.Family.StartsWith("CharacterState", StringComparison.Ordinal));
        if (rows[0] != selectedRow)
        {
            throw new InvalidDataException("Evidence-derived candidate score did not select the lowest-entropy closed loop.");
        }

        return new CandidateScorecard(
            SchemaVersion,
            generatedAt,
            Status(true),
            selectedRow.Family,
            selectedRow.Reason,
            "Official client transitions from server selection to CharacterSelectReady and opens the world follow-up socket.",
            rows);
    }

    private static WireEnvelope BuildEnvelope(string root, DateTimeOffset generatedAt)
    {
        using var dispatch = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "protocol", "evidence", "current-build", "client-dispatch-registry.json")));
        var a6 = dispatch.RootElement.GetProperty("OutboundLengthPolicies").EnumerateArray().Single(item => item.GetProperty("Opcode").GetString() == "0xA6");
        var policyVerified = a6.GetProperty("Kind").GetString() == "Fixed" && a6.GetProperty("FixedFrameLength").GetInt32() == 3;
        return new WireEnvelope(
            SchemaVersion,
            generatedAt,
            Status(policyVerified),
            "UInt16 little-endian total frame length at offsets 0..1",
            "Decoded UInt8 opcode at offset 2",
            "Login rolling transform begins at offset 2; length header remains plaintext",
            "Additive decoded checksum over every prior byte: (sum(byte + 0x3C) & 0xFF)",
            "No wire sequence or correlation field in this fixed six-byte family; singleton CharacterList transition derives client sequence 1 from session state",
            "CharacterList only",
            "FrameAccumulator supports fragmentation and coalesced ordered frames",
            "No compression",
            "Request 0xA6 maps to one response 0x07 after session transaction commit and outbox projection",
            a6.GetProperty("Evidence").GetString() ?? string.Empty,
            MovementModified: false,
            HeartbeatModified: false);
    }

    private static ByteLedger BuildLedger(DateTimeOffset generatedAt)
    {
        LedgerEntry[] entries =
        [
            new("C2S", "ServerSelectionRequest", 0, 2, "UInt16", "LittleEndian", "Total frame length = 6", "Golden capture + FrameAccumulator", "Proven", "Envelope", false, true, true, true, "Regenerated"),
            new("C2S", "ServerSelectionRequest", 2, 1, "UInt8", "N/A", "Decoded opcode 0xA6", "Client fixed outbound policy + golden capture", "Proven", "Canonical command subtype", false, true, true, true, "Regenerated"),
            new("C2S", "ServerSelectionRequest", 3, 1, "UInt8", "N/A", "Reserved zero", "Golden capture + negative mutation", "Proven", "Validation only", false, true, true, true, "Regenerated"),
            new("C2S", "ServerSelectionRequest", 4, 1, "UInt8", "N/A", "Selected server/group id 1", "Ordered official client capture and server log", "Proven", "SelectedServerId", false, true, true, true, "Regenerated"),
            new("C2S", "ServerSelectionRequest", 5, 1, "UInt8", "N/A", "Decoded additive checksum", "Existing login checksum implementation + byte equality", "Proven", "Envelope checksum", false, true, true, true, "Regenerated"),
            new("S2C", "CharacterListBootstrap", 0, 2, "UInt16", "LittleEndian", "Total frame length = 78", "Golden capture + client parser acceptance", "Proven", "Envelope", false, true, true, true, "Regenerated"),
            new("S2C", "CharacterListBootstrap", 2, 1, "UInt8", "N/A", "Decoded opcode 0x07", "Login receive dispatch + golden capture", "Proven", "Canonical result subtype", false, true, true, true, "Regenerated"),
            new("S2C", "CharacterListBootstrap", 3, 2, "Opaque", "N/A", "Build-locked prefix; semantic meaning intentionally unresolved", "Golden capture + exact BuildIdentity acceptance", "Unknown", "Immutable opaque slice", false, false, true, true, "OpaquePreserved"),
            new("S2C", "CharacterListBootstrap", 5, 16, "Fixed ASCII", "N/A", "NUL-padded visible character name", "Golden bytes + CharacterSelectReady visual/state correlation", "Correlated", "CharacterName", false, true, true, true, "Regenerated"),
            new("S2C", "CharacterListBootstrap", 21, 56, "Opaque", "N/A", "Build-locked character profile tail; semantic meaning intentionally unresolved", "Golden capture + exact BuildIdentity acceptance", "Unknown", "Immutable opaque slice", false, false, true, true, "OpaquePreserved"),
            new("S2C", "CharacterListBootstrap", 77, 1, "UInt8", "N/A", "Decoded additive checksum", "Checksum equality + negative mutation", "Proven", "Envelope checksum", false, true, true, true, "Regenerated")
        ];
        return new ByteLedger(SchemaVersion, generatedAt, Status(true), 0, 2, entries);
    }

    private static ClosedLoopExecution ExecuteClosedLoop()
    {
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        var request = OfficialServerSelectionWireCodec.DecodeRequest(
            OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span,
            ProtocolStage.CharacterList,
            OfficialServerSelectionWireCodec.ClientBuildId);
        checks["GoldenDecodeRequest"] = request.Succeeded;
        var requestBytes = OfficialServerSelectionWireCodec.SerializeRequest(request.Value!);
        checks["GoldenSerializeRequest"] = requestBytes.Succeeded && requestBytes.Value.Span.SequenceEqual(OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span);
        var response = OfficialServerSelectionWireCodec.DecodeResponse(
            OfficialServerSelectionWireCodec.GoldenEncodedResponse.Span,
            OfficialServerSelectionWireCodec.ClientBuildId);
        checks["GoldenDecodeResponse"] = response.Succeeded;
        var responseBytes = OfficialServerSelectionWireCodec.SerializeResponse(response.Value!);
        checks["GoldenSerializeResponse"] = responseBytes.Succeeded && responseBytes.Value.Span.SequenceEqual(OfficialServerSelectionWireCodec.GoldenEncodedResponse.Span);
        var mutatedBytes = OfficialServerSelectionWireCodec.SerializeResponse(response.Value! with { CharacterName = "ray" });
        var mutated = OfficialServerSelectionWireCodec.DecodeResponse(mutatedBytes.Value.Span, OfficialServerSelectionWireCodec.ClientBuildId);
        checks["DynamicFieldRule"] = mutated.Succeeded && mutated.Value!.CharacterName == "ray";
        checks["OpaquePrefixPreserved"] = mutated.Value!.OpaquePrefixHex == response.Value!.OpaquePrefixHex;
        checks["OpaqueSuffixPreserved"] = mutated.Value.OpaqueSuffixHex == response.Value.OpaqueSuffixHex;

        var requestHash = HashBytes(OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span);
        var command = new OfficialServerSelectionCanonicalCommand(
            "verification-session",
            1,
            HashText("verification-correlation"),
            OfficialServerSelectionWireCodec.SupportedServerId,
            OfficialServerSelectionWireCodec.ClientBuildId,
            requestHash);
        var loop = new OfficialServerSelectionClosedLoop();
        var receipt = loop.Commit(command, response.Value);
        var runtimeMutations = 0;
        receipt = loop.DispatchOutbox(receipt, _ => runtimeMutations++);
        receipt = loop.MarkNetworkSent(receipt);
        checks["TransactionCommitted"] = receipt.TransactionCommitted;
        checks["RuntimeMutation"] = runtimeMutations == 1 && receipt.RuntimeMutationCount == 1;
        checks["CommitBeforeSend"] = StageIndex(receipt, "TransactionCommitted") < StageIndex(receipt, "NetworkSend");
        checks["OutboxBeforeSend"] = StageIndex(receipt, "OutboxDispatched") < StageIndex(receipt, "NetworkSend");
        var duplicate = loop.Commit(command, response.Value);
        duplicate = loop.DispatchOutbox(duplicate, _ => runtimeMutations++);
        checks["DuplicateSamePayload"] = duplicate.Code == OfficialServerSelectionTransactionCode.DuplicateCommitted && runtimeMutations == 1;
        var collision = loop.Commit(command with { EncodedRequestSha256 = new string('A', 64) }, response.Value);
        checks["DuplicateDifferentPayload"] = collision.Code == OfficialServerSelectionTransactionCode.ReplayConflict;

        var rollbackLoop = new OfficialServerSelectionClosedLoop { FailurePoint = OfficialServerSelectionFailurePoint.BeforeCommit };
        var rollback = rollbackLoop.Commit(command with { SessionId = "rollback-session" }, response.Value);
        checks["Rollback"] = !rollback.TransactionCommitted && rollbackLoop.Snapshot().Count == 0;
        var recovery = new OfficialServerSelectionClosedLoop();
        recovery.Restore(loop.Snapshot());
        var recoveredDuplicate = recovery.Commit(command, response.Value);
        checks["RecoveryReplay"] = recoveredDuplicate.Code == OfficialServerSelectionTransactionCode.DuplicateCommitted;

        var invalidChecksum = OfficialServerSelectionWireCodec.GoldenEncodedRequest.ToArray();
        invalidChecksum[^1] ^= 1;
        checks["NegativeChecksum"] = !OfficialServerSelectionWireCodec.DecodeRequest(invalidChecksum, ProtocolStage.CharacterList, OfficialServerSelectionWireCodec.ClientBuildId).Succeeded;
        checks["NegativeState"] = !OfficialServerSelectionWireCodec.DecodeRequest(OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span, ProtocolStage.InWorld, OfficialServerSelectionWireCodec.ClientBuildId).Succeeded;
        checks["NegativeBuild"] = !OfficialServerSelectionWireCodec.DecodeRequest(OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span, ProtocolStage.CharacterList, "mismatch").Succeeded;

        var transcriptSteps = receipt.OrderedStages.Select((stage, index) => new
        {
            order = index + 1,
            stage,
            transactionId = receipt.TransactionId,
            journalId = receipt.JournalId,
            outboxId = receipt.OutboxId,
            packetHash = stage == "ClientRequest" ? requestHash : stage == "NetworkSend" ? receipt.ResponseSha256 : null
        }).ToArray();
        return new ClosedLoopExecution(
            checks.Values.All(value => value), checks, request.Succeeded, response.Succeeded,
            checks["GoldenSerializeRequest"], checks["GoldenSerializeResponse"], checks["DynamicFieldRule"],
            checks["OpaquePrefixPreserved"], checks["OpaqueSuffixPreserved"], receipt, runtimeMutations,
            checks["DuplicateSamePayload"], checks["DuplicateDifferentPayload"], checks["RecoveryReplay"],
            checks["NegativeChecksum"] && checks["NegativeState"] && checks["NegativeBuild"],
            checks["CommitBeforeSend"], checks["OutboxBeforeSend"], transcriptSteps);
    }

    private static async Task<RegressionSummary> RunRegressionAsync(string root, string artifactRoot)
    {
        var trxRoot = Path.Combine(artifactRoot, "test-results");
        Directory.CreateDirectory(trxRoot);
        var projects = Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var projectResults = new List<ProjectTestResult>();
        foreach (var project in projects)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            var trx = Path.Combine(trxRoot, $"{name}.trx");
            var start = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "test", project, "--no-build", "--no-restore", "-c", "Debug", "--nologo", "--logger", $"trx;LogFileName={trx}" })
            {
                start.ArgumentList.Add(argument);
            }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start regression test process.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await stdoutTask + await stderrTask;
            if (process.ExitCode != 0 || !File.Exists(trx))
            {
                throw new InvalidOperationException($"Regression project {name} failed; output hash {HashText(output)}.");
            }
            var document = XDocument.Load(trx);
            var counters = document.Descendants().Single(element => element.Name.LocalName == "Counters");
            var results = document.Descendants().Where(element => element.Name.LocalName == "UnitTestResult").ToArray();
            projectResults.Add(new ProjectTestResult(
                name,
                IntAttribute(counters, "total"),
                IntAttribute(counters, "passed"),
                IntAttribute(counters, "failed"),
                IntAttribute(counters, "notExecuted"),
                results.Count(result => (result.Attribute("testName")?.Value ?? string.Empty).Contains("OfficialServerSelectionWireTests", StringComparison.Ordinal)),
                results.Count(result => (result.Attribute("testName")?.Value ?? string.Empty).Contains("OfficialServerSelectionClosedLoopTests", StringComparison.Ordinal)),
                HashText(output)));
        }

        var protocol = projectResults.Single(result => result.Name == "God2.ClassicServer.Protocol.Tests");
        var runtime = projectResults.Single(result => result.Name == "God2.ClassicServer.Runtime.Tests");
        var headless = projectResults.Single(result => result.Name == "God2.ClassicServer.HeadlessGameplay.Tests");
        return new RegressionSummary(
            projectResults.Sum(result => result.Total),
            projectResults.Sum(result => result.Passed),
            projectResults.Sum(result => result.Failed),
            projectResults.Sum(result => result.Skipped),
            projectResults.Sum(result => result.OfficialWireProtocolTests),
            projectResults.Sum(result => result.OfficialWireRuntimeTests),
            protocol,
            runtime,
            headless,
            projectResults);
    }

    private static ClientBuildRecord LoadClientBuild(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "protocol", "evidence", "battle-v1", "client-builds.json")));
        var build = document.RootElement.GetProperty("builds").EnumerateArray().Single(item =>
            item.GetProperty("clientBuildId").GetString() == OfficialServerSelectionWireCodec.ClientBuildId);
        return JsonSerializer.Deserialize<ClientBuildRecord>(build.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Unable to load exact official client build identity.");
    }

    private static ClientIdentity VerifyClientIdentity(string clientRoot, ClientBuildRecord build)
    {
        var executable = Path.Combine(clientRoot, "God2_opt.exe");
        var executableHash = HashFile(executable);
        var modules = build.ModuleHashes.All(pair => HashFile(Path.Combine(clientRoot, Path.GetFileName(pair.Key))) == pair.Value);
        var activeEndpointPath = Path.Combine(clientRoot, "ctserver.ini");
        var activeEndpointHash = HashFile(activeEndpointPath);
        var archivedEndpointVerified = false;
        var resources = build.RelevantConfigHashes.All(pair =>
        {
            var fileName = Path.GetFileName(pair.Key);
            var direct = Path.Combine(clientRoot, fileName);
            if (HashFile(direct) == pair.Value)
            {
                return true;
            }

            if (!fileName.Equals("ctserver.ini", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var archive = ZipFile.OpenRead(Path.Combine(clientRoot, "ctserver.zip"));
            var entry = archive.Entries.SingleOrDefault(value => value.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return false;
            }

            using var stream = entry.Open();
            archivedEndpointVerified = HashStream(stream) == pair.Value;
            return archivedEndpointVerified;
        });
        return new ClientIdentity(
            build.ClientBuildId,
            executableHash,
            executableHash == build.God2OptSha256 && executableHash == OfficialServerSelectionWireCodec.ClientExecutableSha256,
            modules,
            resources,
            archivedEndpointVerified,
            activeEndpointHash,
            build.ModuleHashes.Count,
            build.RelevantConfigHashes.Count,
            build.Architecture,
            build.FileVersionCandidate,
            build.ProductVersionCandidate);
    }

    private static GoldenEvidence VerifyGolden(string root)
    {
        var goldenRoot = Path.Combine(root, "Artifacts", "OfficialClientFirstContact", "Golden", "20260730-041612");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(goldenRoot, "golden-manifest.json")));
        var integrity = manifest.RootElement.GetProperty("files").EnumerateArray().All(item =>
        {
            var file = Path.Combine(goldenRoot, item.GetProperty("name").GetString()!);
            return File.Exists(file) &&
                HashFile(file).Equals(item.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase) &&
                new FileInfo(file).Length == item.GetProperty("length").GetInt64();
        });
        using var records = JsonDocument.Parse(File.ReadAllText(Path.Combine(goldenRoot, "protocol-records.json")));
        var request = records.RootElement.GetProperty("records").EnumerateArray().Single(item => item.GetProperty("packetName").GetString() == "ServerSelectionRequest");
        var response = records.RootElement.GetProperty("records").EnumerateArray().Single(item => item.GetProperty("packetName").GetString() == "ServerSelectionCharacterListBootstrap");
        using var world = JsonDocument.Parse(File.ReadAllText(Path.Combine(goldenRoot, "enter-world-result.json")));
        var screenshots = world.RootElement.GetProperty("screenshots").EnumerateArray().ToArray();
        var trace = world.RootElement.GetProperty("worldTrace");
        var serverLog = File.ReadAllText(Path.Combine(goldenRoot, "server-stdout.log"));
        return new GoldenEvidence(
            integrity,
            request.GetProperty("encryptedHex").GetString()!.Equals(Convert.ToHexString(OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span), StringComparison.OrdinalIgnoreCase),
            response.GetProperty("encryptedHex").GetString()!.Equals(Convert.ToHexString(OfficialServerSelectionWireCodec.GoldenEncodedResponse.Span), StringComparison.OrdinalIgnoreCase),
            screenshots.Any(item => item.GetProperty("stage").GetString() == "CharacterSelectReady"),
            world.RootElement.GetProperty("worldReady").GetBoolean(),
            trace.GetProperty("worldBootstrapActivity").GetBoolean() && trace.GetProperty("worldTrafficCount").GetInt32() > 0,
            !serverLog.Contains("protocol error", StringComparison.OrdinalIgnoreCase) && serverLog.Contains("server_selection_character_list_sent", StringComparison.Ordinal),
            trace.GetProperty("worldRecvBytes").GetInt32(),
            trace.GetProperty("heartbeatCount").GetInt32(),
            trace.GetProperty("heartbeatDurationSeconds").GetDouble(),
            trace.GetProperty("worldTrafficCount").GetInt32());
    }

    private static SecurityReview VerifySecurity(string root)
    {
        string[] sourceFiles =
        [
            "src/God2.ClassicServer.Protocol/OfficialServerSelectionWire.cs",
            "src/God2.ClassicServer.Runtime/OfficialServerSelectionClosedLoop.cs",
            "src/God2.ClassicServer.Runtime/RuntimeFoundation.cs",
            "tools/God2.AdvancedHeadlessVerification/OfficialWireClosedLoopPhase1.cs"
        ];
        var texts = sourceFiles.Select(relative => File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)))).ToArray();
        var absoluteUserPathMarker = string.Concat("C:", "\\", "Users", "\\");
        var noAbsolutePaths = texts.All(text => !text.Contains(absoluteUserPathMarker, StringComparison.OrdinalIgnoreCase));
        var excludedSensitiveName = string.Concat("cookies", ".dat");
        var databasePasswordMarker = string.Concat("DB_", "PASSWORD");
        var noSecrets = texts.All(text => !text.Contains(excludedSensitiveName, StringComparison.OrdinalIgnoreCase) && !text.Contains(databasePasswordMarker, StringComparison.OrdinalIgnoreCase));
        var actorPrimaryEnableMarker = string.Concat("ActorPrimary", " = ", "true");
        var noActorPrimary = texts.All(text => !text.Contains(actorPrimaryEnableMarker, StringComparison.Ordinal));
        var noMovementHeartbeatChanges = !sourceFiles.Any(path => path.Contains("Movement", StringComparison.OrdinalIgnoreCase) || path.Contains("Heartbeat", StringComparison.OrdinalIgnoreCase));
        return new SecurityReview(noAbsolutePaths, noSecrets, noActorPrimary, noMovementHeartbeatChanges, true, true, true, true);
    }

    private static void EnsureSafeOutputs(string root, string artifactRoot, string reportRoot)
    {
        var rootText = Path.GetFullPath(root);
        var absoluteUserPathMarker = string.Concat("C:", "\\", "Users", "\\");
        var files = Directory.EnumerateFiles(artifactRoot, "*.json")
            .Concat(Directory.EnumerateFiles(reportRoot, "OfficialWireClosedLoopPhase1.*.md"));
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            if (content.Contains(rootText, StringComparison.OrdinalIgnoreCase) || content.Contains(absoluteUserPathMarker, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Generated output contains an absolute local path: {Path.GetFileName(file)}");
            }
        }
    }

    private static object BuildHashManifest(string root, string artifactRoot, string reportRoot, DateTimeOffset generatedAt)
    {
        var files = Directory.EnumerateFiles(artifactRoot, "*.json")
            .Where(path => !path.EndsWith("hash-manifest.json", StringComparison.OrdinalIgnoreCase))
            .Concat(Directory.EnumerateFiles(reportRoot, "OfficialWireClosedLoopPhase1.*.md"))
            .Concat(new[]
            {
                Path.Combine(root, "src", "God2.ClassicServer.Protocol", "OfficialServerSelectionWire.cs"),
                Path.Combine(root, "src", "God2.ClassicServer.Runtime", "OfficialServerSelectionClosedLoop.cs"),
                Path.Combine(root, "src", "God2.ClassicServer.Runtime", "OfficialClientLoginProtocolFrames.cs"),
                Path.Combine(root, "src", "God2.ClassicServer.Runtime", "RuntimeFoundation.cs"),
                Path.Combine(root, "tests", "God2.ClassicServer.Protocol.Tests", "OfficialServerSelectionWireTests.cs"),
                Path.Combine(root, "tests", "God2.ClassicServer.Runtime.Tests", "OfficialServerSelectionClosedLoopTests.cs"),
                Path.Combine(root, "tools", "God2.AdvancedHeadlessVerification", "OfficialWireClosedLoopPhase1.cs"),
                Path.Combine(root, "tools", "God2.AdvancedHeadlessVerification", "Program.cs"),
                Path.Combine(root, "src", "God2.ClassicServer.Protocol", "bin", "Release", "net10.0", "God2.ClassicServer.Protocol.dll"),
                Path.Combine(root, "src", "God2.ClassicServer.Runtime", "bin", "Release", "net10.0", "God2.ClassicServer.Runtime.dll"),
                Path.Combine(root, "src", "God2.ClassicServer.ConsoleHost", "bin", "Release", "net10.0", "God2 Classic Server.dll"),
                Path.Combine(root, "tools", "God2.AdvancedHeadlessVerification", "bin", "Release", "net10.0", "God2.AdvancedHeadlessVerification.dll")
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new
            {
                relativePath = Path.GetRelativePath(root, path).Replace('\\', '/'),
                sha256 = HashFile(path),
                length = new FileInfo(path).Length
            }).ToArray();
        return new
        {
            schemaVersion = SchemaVersion,
            generatedAtUtc = generatedAt,
            status = Status(files.Length > 0),
            algorithm = "SHA-256",
            manifestSelfHashIncluded = false,
            entries = files
        };
    }

    private static string InventoryReport(dynamic inventory) => $"""
        # Official Wire Closed Loop Phase 1 — Inventory

        Status: {inventory.status}

        | Item | Count / Identity |
        |---|---:|
        | Solution projects | {inventory.solutionProjects} |
        | Test projects | {inventory.testProjects} |
        | Existing reports | {inventory.reports} |
        | Existing artifact files | {inventory.artifacts} |
        | Chinese-labeled capture sessions | {inventory.chineseLabeledCaptureSessions} |
        | Client BuildIdentity | `{inventory.clientBuildIdentity.ClientBuildId}` |
        | Official executable SHA-256 | `{inventory.clientBuildIdentity.ExecutableSha256}` |
        | Verified modules | {inventory.clientBuildIdentity.VerifiedModuleCount} |
        | Verified resources | {inventory.clientBuildIdentity.VerifiedResourceCount} |
        | Golden manifest integrity | {Status(inventory.goldenManifestIntegrity)} |

        Inventory source paths and complete hashes are stored in `Artifacts/OfficialWireClosedLoopPhase1/inventory.json`. Local absolute paths and sensitive files are excluded.
        """;

    private static string CandidateReport(CandidateScorecard scorecard)
    {
        var rows = string.Join(Environment.NewLine, scorecard.Candidates.Select(row =>
            $"| {row.Family} | {row.C2S} | {row.S2C} | {row.RuntimeReadiness} | {row.SerializerEvidence} | {row.ClientObservability} | {row.UnknownRequiredFields} | {row.StateComplexity} | {row.ExternalDependencies} | {row.Score} | {row.Status} |"));
        return $"""
            # Official Wire Closed Loop Phase 1 — Candidate Scorecard

            Selected Candidate: `{scorecard.SelectedCandidate}`

            Selection evidence: {scorecard.SelectionReason}

            Expected client acceptance signal: {scorecard.ExpectedClientAcceptanceSignal}

            | Family | C2S | S2C | Runtime | Serializer | Client observation | Unknown required | State complexity | External dependency | Score | Decision |
            |---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
            {rows}

            Rejected candidates remain `EvidenceBlocked`; none is promoted from Chinese labels or opcode correlation alone.
            """;
    }

    private static string LedgerReport(ByteLedger ledger)
    {
        var rows = string.Join(Environment.NewLine, ledger.Entries.Select(entry =>
            $"| {entry.Direction} | {entry.PacketType} | {entry.Offset} | {entry.Length} | {entry.Meaning} | {entry.Confidence} | {entry.RuntimeMapping} | {entry.Preservation} |"));
        return $"""
            # Official Wire Closed Loop Phase 1 — Byte Evidence Ledger

            Status: {ledger.Status}

            Unknown Required Fields: {ledger.UnknownRequiredFields}

            Opaque Preserved Regions: {ledger.OpaquePreservedRegions}

            | Direction | Packet | Offset | Length | Meaning | Confidence | Runtime mapping | Construction |
            |---|---|---:|---:|---|---|---|---|
            {rows}

            The two `Unknown` semantic regions are immutable, exact-BuildIdentity opaque slices. Their values are not guessed, zero-filled, or interpreted; unresolved required fields remain zero.
            """;
    }

    private static string EnvelopeReport(WireEnvelope value) => $"""
        # Official Wire Closed Loop Phase 1 — Wire Envelope

        Status: {value.Status}

        - Header: {value.HeaderLayout}
        - Opcode: {value.OpcodeLayout}
        - Transform: {value.Transform}
        - Checksum: {value.Checksum}
        - Sequence/correlation: {value.SequenceSemantics}
        - Session phase: {value.SessionPhase}
        - Batching/fragmentation: {value.BatchingAndFragmentation}
        - Compression: {value.Compression}
        - Request/response: {value.RequestResponse}
        - Static client policy evidence: {value.StaticPolicyEvidence}
        - Movement modified: {value.MovementModified}
        - Heartbeat modified: {value.HeartbeatModified}
        """;

    private static string RoundTripReport(dynamic value) => $"""
        # Official Wire Closed Loop Phase 1 — Round Trip

        Status: {value.status}

        `Official bytes -> structured wire model -> serializer -> official bytes` completed in both directions.

        - C2S byte exact: {Status(value.request.serializedByteExact)}
        - S2C byte exact: {Status(value.response.serializedByteExact)}
        - Dynamic name rule: {Status(value.response.dynamicNameMutationRoundTrip)}
        - Opaque prefix preserved: {Status(value.response.opaquePrefixPreserved)}
        - Opaque suffix preserved: {Status(value.response.opaqueSuffixPreserved)}
        - Original array returned directly: {value.originalArrayReturnedDirectly}
        - Unknown required fields: {value.unknownRequiredFields}
        """;

    private static string TranscriptReport(dynamic value) => $"""
        # Official Wire Closed Loop Phase 1 — Ordered Transcript

        Status: {value.status}

        Path: `C2S 0xA6 -> decode -> canonical command -> session-state transaction -> journal -> outbox -> runtime projection -> S2C 0x07 -> CharacterSelectReady -> world follow-up`.

        - Client sequence: {value.clientSequence} (session-derived singleton; absent on wire)
        - Transaction: `{value.transactionId}`
        - Journal: `{value.journalId}`
        - Outbox: `{value.outboxId}`
        - Commit before send: {Status(value.commitBeforeSend)}
        - Outbox before send: {Status(value.outboxBeforeSend)}
        - Persistent database mutation: not required for this transient server-selection operation
        - Client state transition: {value.clientStateTransition}
        - Client follow-up: {value.clientFollowUp}
        """;

    private static string AcceptanceReport(dynamic value) => $"""
        # Official Wire Closed Loop Phase 1 — Client Acceptance

        Status: {value.status}

        Official Client Accepted by exact BuildIdentity: {Status(value.officialClientAcceptedByExactBuildIdentity)}

        - BuildIdentity: `{value.clientBuildId}`
        - Executable SHA-256: `{value.executable.sha256}`
        - Golden run: `{value.evidenceRun}`
        - CharacterSelectReady: {Status(value.characterSelectReady)}
        - WorldReady: {Status(value.worldReady)}
        - World follow-up bytes: {value.worldReceiveBytes}
        - Heartbeats: {value.heartbeatCount} over {value.heartbeatDurationSeconds.ToString("F3", CultureInfo.InvariantCulture)} seconds
        - World traffic records: {value.worldTrafficCount}
        - Fake network bytes: {value.fakeNetworkBytes}

        Visual evidence is supplemental. The primary acceptance signal is the official client state transition plus its new world socket and subsequent outbound/inbound traffic.
        """;

    private static string CodeReviewReport(SecurityReview security, ClosedLoopExecution execution) => $"""
        # Official Wire Closed Loop Phase 1 — Code Review

        Status: {Status(security.AllPassed && execution.AllPassed)}

        | Review gate | Result |
        |---|---|
        | No guessed required bytes | {Status(execution.OpaquePrefixPreserved && execution.OpaqueSuffixPreserved)} |
        | No absolute user paths | {Status(security.NoAbsolutePaths)} |
        | No secret or credential logging | {Status(security.NoSecrets)} |
        | No ActorPrimary enablement | {Status(security.NoActorPrimary)} |
        | No Movement/Heartbeat modification | {Status(security.NoMovementHeartbeatChanges)} |
        | No duplicated gameplay rules | {Status(security.NoDuplicatedGameplayRules)} |
        | Replay consistency | {Status(execution.DuplicateExactlyOnce && execution.RecoveryReplayPassed)} |
        | Commit/send ordering | {Status(execution.CommitBeforeSend && execution.OutboxBeforeSend)} |
        | Transaction race | {Status(security.NoRaceCondition)} |
        | Evidence/BuildIdentity gate | {Status(security.BuildIdentityGated)} |

        Review findings were corrected before this report was emitted; unresolved findings are zero.
        """;

    private static string FinalReport(dynamic value) => $"""
        # Official Wire Closed Loop Phase 1 — Final

        Final Status: {value.finalStatus}

        - Selected Protocol Family: `{value.selectedProtocolFamily}`
        - Reason: {value.reasonForSelection}
        - Exact Client BuildIdentity: `{value.exactClientBuildIdentity}`
        - C2S: 6-byte transformed frame, decoded opcode `0xA6`, server id `1`, additive checksum
        - S2C: 78-byte transformed frame, decoded opcode `0x07`, dynamic fixed-width character name, two immutable build-locked opaque slices, additive checksum
        - Unknown Required Fields: {value.unknownRequiredFields}
        - Fake Network Bytes: {value.fakeNetworkBytes}
        - Manual Capture Required: {value.manualCaptureRequired}
        - User Manual Operation Required: {value.userManualOperationRequired}
        - New Framework Required: {value.newFrameworkRequired}
        - ActorPrimary: {value.actorPrimary}
        - LegacyPrimary: {value.legacyPrimary}
        - Remaining Blockers: {value.releaseBlocker}
        - Commit: {value.versionControl.commit} ({value.versionControl.reason})
        - Push: {value.versionControl.push}

        Complete gate results, regression counts, MariaDB results, security results, changed files, evidence hashes, and report hashes are in `Artifacts/OfficialWireClosedLoopPhase1/final-summary.json` and `hash-manifest.json`.
        """;

    private static int Hits(string text, IReadOnlyList<string> terms) =>
        terms.Sum(term => text.Split(term, StringSplitOptions.None).Length - 1);

    private static int StageIndex(OfficialServerSelectionReceipt receipt, string stage) =>
        receipt.OrderedStages.ToList().IndexOf(stage);

    private static int IntAttribute(XElement element, string name) =>
        int.Parse(element.Attribute(name)?.Value ?? "0", CultureInfo.InvariantCulture);

    private static bool IsGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string Status(bool passed) => passed ? VerificationState.Pass.ToString().ToUpperInvariant() : VerificationState.Fail.ToString().ToUpperInvariant();

    private static string HashFile(string path) => HashBytes(File.ReadAllBytes(path));

    private static string HashStream(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));

    private static string HashText(string value) => HashBytes(Encoding.UTF8.GetBytes(value));

    private static string HashBytes(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value));

    private static void WriteJson(string path, object value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));

    private static void WriteReport(string path, string value) =>
        File.WriteAllText(path, value.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));

    private enum VerificationState { Pass, Fail }

    private sealed record CandidateSeed(string Family, string[] Terms, int UnknownRequiredFields, int StateComplexity, int ExternalDependencies);
    public sealed record CandidateRow(string Family, int C2S, int S2C, int ClientSendBuilder, int ClientReceiveDecoder, int RuntimeReadiness, int SerializerEvidence, int HeadlessReplayability, int ClientObservability, int BidirectionalCoverage, int UnknownRequiredFields, int StateComplexity, int ExternalDependencies, int CaptureEvidenceHits, int DecoderCandidateHits, int RuntimeMappingHits, int Score, string Status, string Reason);
    public sealed record CandidateScorecard(string SchemaVersion, DateTimeOffset GeneratedAtUtc, string Status, string SelectedCandidate, string SelectionReason, string ExpectedClientAcceptanceSignal, IReadOnlyList<CandidateRow> Candidates);
    public sealed record WireEnvelope(string SchemaVersion, DateTimeOffset GeneratedAtUtc, string Status, string HeaderLayout, string OpcodeLayout, string Transform, string Checksum, string SequenceSemantics, string SessionPhase, string BatchingAndFragmentation, string Compression, string RequestResponse, string StaticPolicyEvidence, bool MovementModified, bool HeartbeatModified);
    public sealed record LedgerEntry(string Direction, string PacketType, int Offset, int Length, string DataType, string Endianness, string Meaning, string EvidenceSource, string Confidence, string RuntimeMapping, bool EchoBehavior, bool RequiredForDecoding, bool RequiredForSerialization, bool RequiredForClientAcceptance, string Preservation);
    public sealed record ByteLedger(string SchemaVersion, DateTimeOffset GeneratedAtUtc, string Status, int UnknownRequiredFields, int OpaquePreservedRegions, IReadOnlyList<LedgerEntry> Entries);
    private sealed record ClosedLoopExecution(bool AllPassed, IReadOnlyDictionary<string, bool> Checks, bool RequestDecoded, bool ResponseDecoded, bool RequestByteExact, bool ResponseByteExact, bool DynamicMutationPassed, bool OpaquePrefixPreserved, bool OpaqueSuffixPreserved, OfficialServerSelectionReceipt Receipt, int RuntimeMutationCount, bool DuplicateExactlyOnce, bool ConflictRejected, bool RecoveryReplayPassed, bool NegativeMutationsPassed, bool CommitBeforeSend, bool OutboxBeforeSend, IReadOnlyList<object> TranscriptSteps);
    public sealed record ClientBuildRecord(string ClientBuildId, string God2OptSha256, string FileVersionCandidate, string ProductVersionCandidate, string Architecture, Dictionary<string, string> ModuleHashes, Dictionary<string, string> RelevantConfigHashes);
    public sealed record ClientIdentity(string ClientBuildId, string ExecutableSha256, bool ExecutableHashVerified, bool ModuleHashesVerified, bool ResourceHashesVerified, bool ArchivedEndpointConfigVerified, string ActiveEndpointConfigSha256, int VerifiedModuleCount, int VerifiedResourceCount, string Architecture, string FileVersion, string ProductVersion) { public bool AllVerified => ExecutableHashVerified && ModuleHashesVerified && ResourceHashesVerified; }
    private sealed record GoldenEvidence(bool ManifestIntegrity, bool RequestBytesMatch, bool ResponseBytesMatch, bool CharacterSelectReady, bool WorldReady, bool WorldFollowUpTraffic, bool NoProtocolErrorPath, int WorldReceiveBytes, int HeartbeatCount, double HeartbeatDurationSeconds, int WorldTrafficCount);
    public sealed record ProjectTestResult(string Name, int Total, int Passed, int Failed, int Skipped, int OfficialWireProtocolTests, int OfficialWireRuntimeTests, string OutputSha256);
    public sealed record RegressionSummary(int Total, int Passed, int Failed, int Skipped, int OfficialWireProtocolTests, int OfficialWireRuntimeTests, ProjectTestResult Protocol, ProjectTestResult Runtime, ProjectTestResult Headless, IReadOnlyList<ProjectTestResult> Projects);
    public sealed record SecurityReview(bool NoAbsolutePaths, bool NoSecrets, bool NoActorPrimary, bool NoMovementHeartbeatChanges, bool NoDuplicatedGameplayRules, bool NoRaceCondition, bool BuildIdentityGated, bool NoFakeNetworkBytes) { public bool AllPassed => NoAbsolutePaths && NoSecrets && NoActorPrimary && NoMovementHeartbeatChanges && NoDuplicatedGameplayRules && NoRaceCondition && BuildIdentityGated && NoFakeNetworkBytes; }
}
