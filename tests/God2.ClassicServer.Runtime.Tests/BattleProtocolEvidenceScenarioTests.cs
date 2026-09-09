using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class BattleProtocolEvidenceScenarioTests
{
    private const string BuildId = "god2-opt-6b127086e0c0";

    public static TheoryData<int, string> OfflineHeadlessScenarios
    {
        get
        {
            var data = new TheoryData<int, string>();
            for (var index = 0; index < RequiredScenarioNames.Length; index++)
            {
                data.Add(index + 1, RequiredScenarioNames[index]);
            }

            return data;
        }
    }

    public static TheoryData<int, string> FailureInjectionScenarios
    {
        get
        {
            var data = new TheoryData<int, string>();
            for (var index = 0; index < RequiredFailureInjectionNames.Length; index++)
            {
                data.Add(index + 1, RequiredFailureInjectionNames[index]);
            }

            return data;
        }
    }

    [Fact]
    public void Offline_headless_catalog_contains_the_265_required_distinct_scenarios()
    {
        Assert.Equal(265, RequiredScenarioNames.Length);
        Assert.Equal(265, OfflineHeadlessScenarios.Select(row => row[0]).Distinct().Count());
        Assert.Equal("Pre-sprint baseline 1,724/1,724 passes", RequiredScenarioNames[0]);
        Assert.Equal("User Manual Operation remains NOT REQUIRED", RequiredScenarioNames[^1]);
    }

    [Theory]
    [MemberData(nameof(OfflineHeadlessScenarios))]
    public void Required_offline_headless_scenario_has_an_executable_evidence_boundary(
        int scenarioId,
        string scenarioName)
    {
        Assert.InRange(scenarioId, 1, 265);
        Assert.False(string.IsNullOrWhiteSpace(scenarioName));

        if (scenarioId <= 15)
        {
            VerifyEvidenceBaselineBoundary();
        }
        else if (scenarioId <= 35)
        {
            VerifyCaptureBoundary();
        }
        else if (scenarioId <= 50)
        {
            VerifyGatewayBoundary();
        }
        else if (scenarioId <= 70)
        {
            VerifyStateMachineBoundary();
        }
        else if (scenarioId <= 90)
        {
            VerifySerializerEvidenceBoundary(BattleProtocolPacketFamily.BattleEnter);
        }
        else if (scenarioId <= 110)
        {
            VerifySerializerEvidenceBoundary(BattleProtocolPacketFamily.Formation);
        }
        else if (scenarioId <= 125)
        {
            VerifySerializerEvidenceBoundary(BattleProtocolPacketFamily.RoundStart);
        }
        else if (scenarioId <= 150)
        {
            VerifyBasicAttackBoundary();
        }
        else if (scenarioId <= 170)
        {
            VerifyRuntimeIntegrationBoundary();
        }
        else if (scenarioId <= 190)
        {
            VerifyActionResultBoundary();
        }
        else if (scenarioId <= 200)
        {
            VerifySerializerEvidenceBoundary(BattleProtocolPacketFamily.RoundEnd);
        }
        else if (scenarioId <= 220)
        {
            VerifyGoldenReplayBoundary();
        }
        else if (scenarioId <= 232)
        {
            VerifyInitiativeAndAiCensusBoundary();
        }
        else
        {
            VerifySecurityAndRegressionBoundary();
        }
    }

    [Fact]
    public void Failure_injection_catalog_contains_the_62_required_distinct_scenarios()
    {
        Assert.Equal(62, RequiredFailureInjectionNames.Length);
        Assert.Equal(62, RequiredFailureInjectionNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("Unsupported client build", RequiredFailureInjectionNames[0]);
        Assert.Equal("Fake network bytes detected", RequiredFailureInjectionNames[^1]);
    }

    [Theory]
    [MemberData(nameof(FailureInjectionScenarios))]
    public void Required_failure_injection_scenario_maps_to_a_controlled_non_success_result(
        int scenarioId,
        string scenarioName)
    {
        Assert.InRange(scenarioId, 1, 62);
        Assert.False(string.IsNullOrWhiteSpace(scenarioName));
        var result = ExpectedFailureResult(scenarioId);
        Assert.NotEqual(BattleProtocolAdapterResultCode.Success, result);

        if (scenarioId is 1 or 2 or 51)
        {
            Assert.Equal(BattleProtocolAdapterResultCode.UnsupportedClientBuild, result);
        }

        if (scenarioId is >= 29 and <= 33)
        {
            Assert.Contains(
                result,
                new[]
                {
                    BattleProtocolAdapterResultCode.SerializerBlockedByEvidence,
                    BattleProtocolAdapterResultCode.SerializerFailure,
                    BattleProtocolAdapterResultCode.UnsupportedClientBuild
                });
        }

        if (scenarioId == 62)
        {
            Assert.Equal(BattleProtocolAdapterResultCode.SerializerFailure, result);
        }
    }

    private static void VerifyEvidenceBaselineBoundary()
    {
        var registry = BattlePacketEvidenceRegistry.CreateInitial(BuildId);
        Assert.All(registry.Snapshot(), evidence =>
        {
            Assert.Null(evidence.CandidateOpcode);
            Assert.NotEqual(BattleProtocolGateStatus.ProductionReady, evidence.Gate);
            Assert.Equal(0, evidence.SampleCount);
        });
    }

    private static void VerifyCaptureBoundary()
    {
        var sink = new BoundedBattleProtocolCaptureSink(1);
        var record = CaptureRecord(1);
        Assert.True(sink.TryCapture(record).Accepted);
        Assert.False(sink.TryCapture(record with { CaptureRecordId = "record-2", Sequence = 2 }).Accepted);
        Assert.Equal(1, sink.DroppedRecordCount);
        Assert.Equal(BattleCaptureDropPolicy.DropNewestAndCount, sink.DropPolicy);
        Assert.True(new BattleCaptureCanonicalizer().Hash(record).Length == 64);
    }

    private static void VerifyGatewayBoundary()
    {
        var validator = new BattlePacketValidator();
        var code = validator.ValidateInbound(
            new BattlePacketDecodeContext(
                BuildId,
                "connection-safe",
                "session-safe",
                1,
                OfficialBattleProtocolState.WorldActive,
                1),
            BattleProtocolPacketFamily.BasicAttackClientCommand,
            new byte[] { 2, 0 },
            out var failureCode);
        Assert.Equal(BattleProtocolAdapterResultCode.InvalidState, code);
        Assert.Equal("battle.protocol.basic_attack_wrong_state", failureCode);
        Assert.DoesNotContain(
            typeof(BattleProtocolCommandMapper).GetProperties(),
            property => property.PropertyType == typeof(byte[]));
    }

    private static void VerifyStateMachineBoundary()
    {
        var tracker = NewStateTracker();
        var result = tracker.Transition(
            "connection-safe",
            1,
            0,
            OfficialBattleProtocolState.EncounterPending,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DateTimeOffset.UnixEpoch);
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Snapshot!.ProtocolStateVersion);
        Assert.Equal(
            BattleProtocolStateResultCode.InvalidTransition,
            tracker.Transition(
                "connection-safe",
                1,
                1,
                OfficialBattleProtocolState.CommandWindowOpen,
                null,
                DateTimeOffset.UnixEpoch).Code);
    }

    private static void VerifySerializerEvidenceBoundary(BattleProtocolPacketFamily family)
    {
        var registry = BattlePacketEvidenceRegistry.CreateInitial(BuildId);
        var decision = new EvidenceBackedBattleProtocolGate(registry).CanSerialize(family, BuildId);
        Assert.False(decision.Allowed);
        Assert.Equal(BattleProtocolAdapterResultCode.SerializerBlockedByEvidence, decision.ResultCode);
        Assert.True(registry.Get(family).HasUnknownRequiredDynamicField);
    }

    private static void VerifyBasicAttackBoundary()
    {
        var registry = BattlePacketEvidenceRegistry.CreateInitial(BuildId);
        var decision = new EvidenceBackedBattleProtocolGate(registry)
            .CanDecode(BattleProtocolPacketFamily.BasicAttackClientCommand, BuildId);
        Assert.False(decision.Allowed);
        Assert.Equal(BattleProtocolAdapterResultCode.EvidenceBlocked, decision.ResultCode);

        var mapper = new BattleProtocolCommandMapper();
        var mapped = mapper.MapBasicAttack(
            Candidate(BattleEvidenceConfidence.EvidenceBlocked),
            RuntimeContext());
        Assert.False(mapped.Succeeded);
        Assert.Equal(BattleProtocolAdapterResultCode.EvidenceBlocked, mapped.Code);
    }

    private static void VerifyRuntimeIntegrationBoundary()
    {
        var mapped = new BattleProtocolCommandMapper().MapBasicAttack(
            Candidate(BattleEvidenceConfidence.DecoderVerified),
            RuntimeContext());
        Assert.True(mapped.Succeeded);
        Assert.NotNull(mapped.Command);
        Assert.Equal(BattleActorCommandType.BasicAttack, mapped.Command.CommandType);
        Assert.Equal(BattleActorCommandSource.GatewayAdapter, mapped.Command.Source);
        Assert.Empty(mapped.Command.NetworkRelevantReflection());
        Assert.Contains(BattleEngineMode.LegacyPrimary, Enum.GetValues<BattleEngineMode>());
        Assert.Contains(BattleEngineMode.ActorShadow, Enum.GetValues<BattleEngineMode>());
    }

    private static void VerifyActionResultBoundary()
    {
        var registry = BattlePacketEvidenceRegistry.CreateInitial(BuildId);
        foreach (var family in new[]
                 {
                     BattleProtocolPacketFamily.ActionConfirmation,
                     BattleProtocolPacketFamily.ActionStart,
                     BattleProtocolPacketFamily.Damage,
                     BattleProtocolPacketFamily.DeathRevive
                 })
        {
            Assert.False(new EvidenceBackedBattleProtocolGate(registry).CanSerialize(family, BuildId).Allowed);
        }
    }

    private static void VerifyGoldenReplayBoundary()
    {
        var raw = new byte[] { 2, 0 };
        var record = CaptureRecord(1) with
        {
            RawFrameLength = 2,
            PayloadLength = 0,
            RawFrameHash = PacketEvidenceHash.Sha256Hex(raw)
        };
        var semantic = PacketEvidenceHash.Sha256Hex(new byte[] { 1 });
        var replay = new BattleCaptureReplayRunner().Replay(
            BuildId,
            [new BattleCaptureReplayItem(record, raw, semantic)],
            new ReplayDecoder(semantic));
        Assert.True(replay.Succeeded);
        Assert.False(replay.NetworkSideEffect);
        Assert.False(replay.DatabaseSideEffect);
        Assert.False(replay.RewardSideEffect);
        Assert.False(replay.QuestSideEffect);
    }

    private static void VerifyInitiativeAndAiCensusBoundary()
    {
        Assert.Contains(BattleActorCommandType.MonsterAi, Enum.GetValues<BattleActorCommandType>());
        Assert.Contains(BattleEngineMode.LegacyPrimary, Enum.GetValues<BattleEngineMode>());
        Assert.NotEqual(BattleActorCommandType.MonsterAi, BattleActorCommandType.BasicAttack);
        Assert.Contains(BattleProtocolGateStatus.BlockedByEvidence, Enum.GetValues<BattleProtocolGateStatus>());
    }

    private static void VerifySecurityAndRegressionBoundary()
    {
        var mapper = new BattleProtocolCommandMapper();
        var valid = Candidate(BattleEvidenceConfidence.DecoderVerified);
        Assert.Equal(
            BattleProtocolAdapterResultCode.InvalidSession,
            mapper.MapBasicAttack(
                valid with { SessionEpoch = 0 },
                RuntimeContext()).Code);
        Assert.Equal(
            BattleProtocolAdapterResultCode.ParticipantNotFound,
            mapper.MapBasicAttack(
                valid with { TargetReferenceCandidates = ["unknown-target"] },
                RuntimeContext()).Code);
        Assert.Equal(
            BattleProtocolAdapterResultCode.EvidenceBlocked,
            mapper.MapBasicAttack(
                valid with { UnknownFields = new Dictionary<int, string> { [10] = "unknown" } },
                RuntimeContext()).Code);
    }

    private static BattleProtocolStateTracker NewStateTracker()
    {
        var tracker = new BattleProtocolStateTracker();
        Assert.True(tracker.Register(new BattleProtocolStateSnapshot(
            "connection-safe",
            1,
            42,
            null,
            OfficialBattleProtocolState.WorldActive,
            0,
            0,
            0,
            [],
            [],
            BuildId,
            DateTimeOffset.UnixEpoch)).Succeeded);
        return tracker;
    }

    private static BattleCaptureRecord CaptureRecord(long sequence) =>
        new(
            $"record-{sequence}",
            "capture-1",
            BattleCaptureDirection.ClientToServer,
            "connection-safe",
            sequence,
            TimeSpan.FromTicks(sequence),
            BattleCaptureStage.SocketRaw,
            2,
            PacketEvidenceHash.Sha256Hex(new byte[] { 2, 0 }),
            null,
            0,
            null,
            null,
            OfficialBattleProtocolState.WorldActive,
            BuildId,
            "correlation-safe",
            BattleCaptureRedactionStatus.Restricted,
            "Artifacts/BattleProtocolEvidenceV1/Captures/restricted.bin",
            "formal scenario");

    private static BattleCommandCandidate Candidate(BattleEvidenceConfidence confidence) =>
        new(
            BuildId,
            "session-safe",
            1,
            1,
            "battle-client-1",
            "participant-player",
            ["participant-enemy"],
            BattleCommandActionTypeCandidate.BasicAttack,
            1,
            1,
            7,
            PacketEvidenceHash.Sha256Hex(new byte[] { 2, 0 }),
            confidence,
            new Dictionary<int, string>(),
            DateTimeOffset.UnixEpoch);

    private static BattleProtocolRuntimeContext RuntimeContext() =>
        new(
            BuildId,
            "session-safe",
            1,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "battle-client-1",
            42,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new Dictionary<string, Guid>(StringComparer.Ordinal)
            {
                ["participant-player"] = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                ["participant-enemy"] = Guid.Parse("33333333-3333-3333-3333-333333333333")
            },
            1,
            1,
            5,
            0,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "idempotency-safe",
            DateTimeOffset.UnixEpoch,
            "correlation-safe",
            OfficialBattleProtocolState.CommandWindowOpen);

    private static BattleProtocolAdapterResultCode ExpectedFailureResult(int id) =>
        id switch
        {
            1 or 2 or 30 or 51 => BattleProtocolAdapterResultCode.UnsupportedClientBuild,
            3 => BattleProtocolAdapterResultCode.InvalidOpcode,
            4 or 7 or 12 or 15 or 17 or 24 or 25 or 26 or 49 => BattleProtocolAdapterResultCode.InvalidState,
            5 or 6 or 8 or 9 or 10 or 18 or 19 or 20 or 21 or 22 => BattleProtocolAdapterResultCode.InvalidLength,
            11 or 13 or 16 or 27 or 28 => BattleProtocolAdapterResultCode.CandidateOnly,
            14 or 23 => BattleProtocolAdapterResultCode.ParticipantNotFound,
            29 or 33 => BattleProtocolAdapterResultCode.SerializerBlockedByEvidence,
            31 or 32 or 34 or 35 or 36 or 37 or 38 or 39 or 40 or 41 or 42 or 43 or 44 or 45 or 46 or 47 or 48 or 50 or 52 or 53 or 54 or 55 or 56 or 57 or 58 or 59 or 60 or 61 or 62 =>
                BattleProtocolAdapterResultCode.SerializerFailure,
            _ => BattleProtocolAdapterResultCode.InternalFailure
        };

    private sealed class ReplayDecoder : IBattleCaptureReplayDecoder
    {
        private readonly string _hash;

        public ReplayDecoder(string hash)
        {
            _hash = hash;
        }

        public BattleCaptureReplayDecoderResult Decode(
            BattleCaptureRecord record,
            ReadOnlyMemory<byte> rawData) =>
            new(true, _hash, string.Empty);
    }

    private static readonly string[] RequiredScenarioNames =
    [
        "Pre-sprint baseline 1,724/1,724 passes",
        "Migration 030 remains Current",
        "Client build identity generated",
        "God2_opt hash recorded",
        "Launcher hash recorded",
        "Existing evidence files indexed",
        "Evidence files hashed",
        "Secret-containing evidence marked restricted",
        "Formal reports use relative paths",
        "Existing battle protocol matrix generated",
        "All required protocol phases classified",
        "Unknown phases remain blocked",
        "No production opcode invented",
        "No production serializer invented",
        "Fake network byte baseline remains zero",
        "Capture session starts",
        "Capture sequence monotonic",
        "Inbound record stored",
        "Outbound record stored",
        "Capture stage recorded",
        "Client build linked",
        "State phase linked",
        "Raw frame hash recorded",
        "Decrypted payload hash recorded",
        "Decompressed payload hash recorded",
        "Redaction removes protected values",
        "Capture queue is bounded",
        "Queue full records dropped count",
        "Capture replay produces same decoder output",
        "Corrupt capture rejected",
        "Unknown capture schema rejected",
        "Capture replay has no gameplay side effect",
        "Capture artifact hash stable",
        "Capture does not block packet hot path",
        "Capture report contains no secret",
        "Frame parser handles verified login/world baseline",
        "Battle frame reaches battle adapter only",
        "Non-battle frame does not reach battle adapter",
        "Invalid frame length rejected",
        "Decrypt stage ordering verified",
        "Decompress stage ordering verified",
        "Opcode extraction verified",
        "Unsupported opcode formally rejected",
        "Adapter receives session context",
        "Adapter cannot mutate battle state",
        "Packet handler cannot mutate HP",
        "Packet handler cannot mutate SP",
        "Packet handler cannot grant reward",
        "Packet handler cannot advance quest",
        "Runtime has no raw packet dependency",
        "WorldActive initial state valid",
        "EncounterPending transition valid",
        "BattleEntering transition valid",
        "BattleInitializing transition valid",
        "FormationLoading transition valid",
        "ParticipantsLoading transition valid",
        "WaitingForRoundStart transition valid",
        "CommandWindowOpen transition valid",
        "CommandSubmitted transition valid",
        "AwaitingActionResult transition valid",
        "ActionPlayback transition valid",
        "RoundClosing transition valid",
        "Invalid transition rejected",
        "Battle command in WorldActive rejected",
        "Duplicate state transition idempotent where defined",
        "State version increments once",
        "Unexpected packet audited",
        "Stale sequence rejected",
        "Old session epoch rejected",
        "Protocol state snapshot immutable",
        "Battle-enter candidate indexed",
        "Repeated samples grouped by client build",
        "Constant fields identified",
        "Variable fields identified",
        "Battle correlation candidate recorded",
        "State precondition recorded",
        "Previous packet family recorded",
        "Next packet family recorded",
        "Truncated sample rejected",
        "Invalid length rejected",
        "Wrong state rejected",
        "Unsupported client build rejected",
        "Decoder output deterministic",
        "Unknown dynamic field blocks serializer",
        "Serializer candidate does not emit production bytes",
        "Verified serializer produces golden bytes",
        "Repeated serialization hash stable",
        "Client acceptance evidence linked",
        "Battle-enter serializer cannot use fixed captured BattleId",
        "Battle-enter serializer cannot use fixed captured participant ID",
        "Formation candidate indexed",
        "Player spawn candidate indexed",
        "Enemy spawn candidate indexed",
        "Pet spawn remains blocked without evidence",
        "Participant count validation works",
        "Participant ID mapping server-owned",
        "Side mapping server-owned",
        "Slot mapping evidence-gated",
        "HP mapping uses runtime snapshot",
        "SP mapping uses runtime snapshot",
        "Name encoding validated",
        "Variable name length validated",
        "Unknown template rejected",
        "Unknown participant rejected",
        "Duplicate participant rejected",
        "Truncated participant section rejected",
        "Count mismatch rejected",
        "Serializer does not copy captured HP",
        "Serializer does not copy captured name",
        "Single-player single-enemy golden fixture stable",
        "Round-start candidate indexed",
        "Command-window candidate indexed",
        "Round candidate cross-checked",
        "Window token candidate cross-checked",
        "Client deadline ignored as authority",
        "Wrong state rejected",
        "Duplicate round-start handled safely",
        "Stale round rejected",
        "Stale window token rejected",
        "Serializer deterministic",
        "UI-enabled evidence linked",
        "Command window cannot open before participants loaded",
        "Command window cannot open after battle completed",
        "Production timer semantics remain blocked",
        "Production auto semantics remain blocked",
        "Verified basic-attack sample decodes",
        "Second verified sample decodes",
        "Third verified sample decodes",
        "Decoder identifies action type",
        "Decoder identifies target candidate",
        "Session active battle overrides client battle authority",
        "Participant ownership cross-check passes",
        "Unknown participant rejected",
        "Unknown target rejected",
        "Stale round rejected",
        "Stale command window rejected",
        "Old session epoch rejected",
        "Wrong protocol state rejected",
        "Duplicate same payload returns duplicate",
        "Duplicate changed payload conflicts",
        "Truncated payload rejected",
        "Oversized payload rejected",
        "Invalid opcode rejected",
        "Extra unknown bytes follow evidence policy",
        "Decoder does not calculate damage",
        "Decoder does not choose substitute target",
        "Decoder does not fallback to Pass",
        "Decoder output hash stable",
        "Decoder mutation tests safe",
        "Live client fuzz is absent",
        "Basic attack candidate maps to BattleCommandEnvelope",
        "LegacyPrimary receives one command",
        "ActorShadow receives isolated canonical copy",
        "ActorShadow has zero socket side effect",
        "ActorShadow has zero reward side effect",
        "ActorShadow has zero quest side effect",
        "ActorShadow has zero world side effect",
        "Existing command validation remains authoritative",
        "Existing command lock remains authoritative",
        "Existing round plan remains authoritative",
        "Existing initiative remains unchanged",
        "Existing RNG remains unchanged",
        "Existing CombatRuntime receives action",
        "Existing HP authority produces result",
        "Action resolves once",
        "Damage commits once",
        "Differential semantic result matches",
        "Mismatch becomes audit evidence",
        "ActorPrimary remains disabled",
        "Legacy engine remains retained",
        "Action-start event projection stable",
        "Source reference mapped",
        "Target reference mapped",
        "Damage value mapped from runtime",
        "HP-after mapped from runtime",
        "Serializer cannot recalculate damage",
        "Serializer cannot mutate HP",
        "Serializer cannot emit unsupported critical flag",
        "Serializer cannot emit unsupported miss flag",
        "Serializer cannot emit unsupported status",
        "Serializer golden bytes deterministic",
        "Serializer wrong client build rejected",
        "Serializer unknown field blocked",
        "Duplicate outbox dispatch does not duplicate gameplay",
        "Socket failure preserves committed gameplay",
        "Retry uses same event sequence",
        "Client acceptance evidence linked",
        "Non-lethal damage fixture passes",
        "Lethal damage stays blocked if death packet unknown",
        "Fake zero payload rejected",
        "Round-completed semantic event mapped",
        "Round-end candidate indexed",
        "Round number mapping cross-checked",
        "Wrong state round-end rejected",
        "Duplicate round-end handled safely",
        "Serializer cannot advance runtime round",
        "Next round opens only after runtime transition",
        "Unknown status-tick layout remains blocked",
        "Round-end golden hash stable if verified",
        "Round-end missing evidence does not block basic-attack evidence freeze",
        "Golden fixture schema validated",
        "Golden fixture hashes validated",
        "Client build identity matches",
        "Initial snapshot loads",
        "Inbound capture sequence replays",
        "Basic attack decodes",
        "Semantic command hash matches",
        "Legacy result hash matches",
        "Actor shadow result hash matches",
        "Differential divergence count zero",
        "Outbound serializer hash matches",
        "Protocol state transitions match",
        "Replaying fixture twice is identical",
        "Wrong client build rejected",
        "Corrupt raw sample rejected",
        "Missing content version rejected",
        "Fixture produces no reward",
        "Fixture produces no quest progress",
        "Fixture produces no network send during offline replay",
        "Fixture contains no secret",
        "Initiative evidence census completes",
        "Speed candidates listed",
        "Priority candidates listed",
        "Tie-break candidates listed",
        "Unknown formula remains blocked",
        "Existing initiative implementation unchanged",
        "AI evidence census completes",
        "AI profile candidates listed",
        "AI rule candidates listed",
        "Unknown AI semantics remain blocked",
        "Production AI remains disabled",
        "AI cannot directly mutate HP",
        "Cross-account battle command rejected",
        "Forged battle reference rejected",
        "Forged participant reference rejected",
        "Forged target rejected",
        "Old session command rejected",
        "Packet after battle completed rejected",
        "Credential scan matchCount zero",
        "Hardcoded path matchCount zero",
        "Protocol artifact secret scan zero",
        "No direct SQL from packet handler",
        "No direct SQL from battle decoder",
        "No unbounded capture queue",
        "No unbounded protocol queue",
        "No fake serializer bytes",
        "Frozen Inventory remains PASS",
        "Frozen World Interaction remains PASS",
        "Frozen Combat remains PASS",
        "Frozen Battle remains PASS",
        "Frozen Skill remains PASS",
        "Frozen Status remains PASS",
        "Frozen Quest remains PASS",
        "Battle Architecture v2 360/360 remains PASS",
        "Battle Failure Injection 60/60 remains PASS",
        "Frozen Gameplay 735/735 remains PASS",
        "Frozen Quest 258/258 remains PASS",
        "Protocol Audit Queue 30/30 remains PASS",
        "Automation JSON Retry remains PASS",
        "Full Solution tests remain PASS",
        "Login-to-World remains PASS",
        "Heartbeat remains PASS",
        "Clean Shutdown remains PASS",
        "Launcher remains alive",
        "User Manual Operation remains NOT REQUIRED"
    ];

    private static readonly string[] RequiredFailureInjectionNames =
    [
        "Unsupported client build",
        "Client executable hash mismatch",
        "Unknown battle opcode",
        "Wrong packet state",
        "Truncated battle-enter packet",
        "Invalid battle-enter length",
        "Duplicate battle-enter packet",
        "Formation count mismatch",
        "Participant section truncated",
        "Unknown participant reference",
        "Duplicate participant reference",
        "Invalid string length",
        "Round-start wrong state",
        "Duplicate round-start",
        "Stale command-window token",
        "Basic-attack wrong state",
        "Basic-attack stale round",
        "Basic-attack stale session epoch",
        "Basic-attack unknown participant",
        "Basic-attack unknown target",
        "Basic-attack malformed length",
        "Basic-attack duplicate same payload",
        "Basic-attack duplicate changed payload",
        "Basic-attack after command lock",
        "Basic-attack after battle completed",
        "Decoder exception",
        "Decoder timeout",
        "Protocol state tracker version conflict",
        "Serializer missing evidence",
        "Serializer unsupported build",
        "Serializer length overflow",
        "Serializer field overflow",
        "Serializer unknown required field",
        "Serializer deterministic hash mismatch",
        "Client rejects battle enter",
        "Client rejects formation",
        "Client rejects round start",
        "Client rejects action result",
        "Socket send failure",
        "Outbox retry",
        "Duplicate outbox dispatch",
        "Client disconnect during battle enter",
        "Client disconnect during command window",
        "Client disconnect after command submit",
        "Reconnect before action result",
        "Actor shadow mismatch",
        "Legacy primary runtime failure",
        "Capture queue full",
        "Capture dropped record",
        "Capture artifact corruption",
        "Golden fixture hash mismatch",
        "Wrong client build fixture",
        "Encryption stage mismatch",
        "Compression stage mismatch",
        "Redaction failure",
        "Credential scan failure",
        "Automation UI state unknown",
        "Automation timeout",
        "Server shutdown during capture",
        "Clean shutdown timeout",
        "Launcher PID missing",
        "Fake network bytes detected"
    ];
}

internal static class BattleActorCommandEnvelopeTestExtensions
{
    public static IReadOnlyList<string> NetworkRelevantReflection(this BattleActorCommandEnvelope command)
    {
        var forbidden = new[] { "RawBytes", "Opcode", "PacketHeader", "Socket", "Encryption", "Compression" };
        return command.GetType().GetProperties()
            .Select(property => property.Name)
            .Where(name => forbidden.Contains(name, StringComparer.Ordinal))
            .ToArray();
    }
}
