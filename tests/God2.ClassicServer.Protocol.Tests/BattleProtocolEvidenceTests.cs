using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class BattleProtocolEvidenceTests
{
    private const string BuildId = "god2-opt-6b127086e0c0";
    private static readonly DateTimeOffset FixedTime = new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Initial_registry_contains_every_family_and_emits_no_production_gate()
    {
        var registry = BattlePacketEvidenceRegistry.CreateInitial(BuildId);

        Assert.Equal(Enum.GetValues<BattleProtocolPacketFamily>().Length, registry.Snapshot().Count);
        Assert.All(registry.Snapshot(), evidence =>
        {
            Assert.Null(evidence.CandidateOpcode);
            Assert.Equal(0, evidence.SampleCount);
            Assert.NotEqual(BattleProtocolGateStatus.ProductionReady, evidence.Gate);
            Assert.True(evidence.HasUnknownRequiredDynamicField);
            Assert.NotEmpty(evidence.UnknownFields);
        });
    }

    [Fact]
    public void Initial_gate_blocks_basic_attack_decode_and_all_serializer_bytes()
    {
        var registry = BattlePacketEvidenceRegistry.CreateInitial(BuildId);
        var gate = new EvidenceBackedBattleProtocolGate(registry);

        var inbound = gate.CanDecode(BattleProtocolPacketFamily.BasicAttackClientCommand, BuildId);
        var outbound = gate.CanSerialize(BattleProtocolPacketFamily.BattleEnter, BuildId);

        Assert.False(inbound.Allowed);
        Assert.Equal(BattleProtocolAdapterResultCode.EvidenceBlocked, inbound.ResultCode);
        Assert.False(outbound.Allowed);
        Assert.Equal(BattleProtocolAdapterResultCode.SerializerBlockedByEvidence, outbound.ResultCode);
    }

    [Fact]
    public void Client_build_change_requires_revalidation()
    {
        var gate = new EvidenceBackedBattleProtocolGate(
            BattlePacketEvidenceRegistry.CreateInitial(BuildId));

        var decision = gate.CanSerialize(BattleProtocolPacketFamily.BattleEnter, "different-build");

        Assert.False(decision.Allowed);
        Assert.Equal(BattleProtocolGateStatus.RevalidationRequired, decision.Gate);
        Assert.Equal("battle.protocol.client_build_mismatch", decision.FailureCode);
    }

    [Fact]
    public void Production_ready_policy_rejects_one_sample_and_unknown_dynamic_fields()
    {
        var evidence = EvidenceRow(
            BattleProtocolPacketFamily.Damage,
            PacketDirection.ServerToClient,
            BattleEvidenceConfidence.ProductionReady,
            BattleProtocolGateStatus.ProductionReady,
            sampleCount: 1,
            serializerTests: 1,
            acceptanceCount: 1,
            hasUnknown: true);

        var errors = BattleEvidencePromotionPolicy.Validate(evidence);

        Assert.Contains("battle.evidence.production_ready_requires_three_samples", errors);
        Assert.Contains("battle.evidence.production_ready_serializer_acceptance_insufficient", errors);
        Assert.Contains("battle.evidence.production_ready_unknown_required_field", errors);
    }

    [Fact]
    public void Production_ready_decoder_policy_requires_three_golden_and_ten_negative_tests()
    {
        var evidence = EvidenceRow(
            BattleProtocolPacketFamily.BasicAttackClientCommand,
            PacketDirection.ClientToServer,
            BattleEvidenceConfidence.ProductionReady,
            BattleProtocolGateStatus.ProductionReady,
            sampleCount: 3,
            decoderTests: 2,
            negativeTests: 9,
            hasUnknown: false);

        Assert.Contains(
            "battle.evidence.production_ready_decoder_tests_insufficient",
            BattleEvidencePromotionPolicy.Validate(evidence));
    }

    [Fact]
    public void Client_build_identity_rejects_absolute_module_paths()
    {
        var identity = new ClientBuildIdentity(
            BuildId,
            Hash("client"),
            Hash("launcher"),
            new Dictionary<string, string> { [@"C:\client\module.dll"] = Hash("module") },
            "1.0.0.1",
            "2026-07-14T16:33:10+08:00",
            "x86",
            "zh-TW",
            new Dictionary<string, string>(),
            "official-classic",
            FixedTime);

        Assert.Contains("battle.client_build.relative_hash_entry_invalid", identity.Validate());
    }

    [Fact]
    public void Evidence_source_requires_safe_relative_path_hash_and_build()
    {
        var evidence = new BattleEvidenceSource(
            "battle-visual-note",
            BattleEvidenceType.ExistingGolden,
            "Artifacts/OfficialServerCapture/action-notes/battle-started.json",
            Hash("note"),
            BuildId,
            null,
            "ExistingVisualEvidence",
            null,
            ["WorldActive"],
            "Historical official-client observation",
            BattleCaptureRedactionStatus.NotRequired,
            FixedTime,
            "Visual behavior only; no raw opcode.");

        Assert.Empty(evidence.Validate());
        Assert.Contains(
            "battle.evidence.source_path_not_safe_relative",
            (evidence with { SourcePath = @"C:\absolute\evidence.json" }).Validate());
    }

    [Fact]
    public void Capture_session_sequence_is_monotonic_under_concurrency()
    {
        var session = new BattleCaptureSession("capture-1", BuildId, "automation-1", FixedTime);
        var sequences = new long[10_000];

        Parallel.For(0, sequences.Length, index => sequences[index] = session.NextSequence());

        Assert.Equal(Enumerable.Range(1, sequences.Length).Select(value => (long)value), sequences.Order());
        Assert.Equal(sequences.Length, session.LastSequence);
    }

    [Fact]
    public void Capture_sink_is_bounded_and_records_drops_without_blocking()
    {
        var sink = new BoundedBattleProtocolCaptureSink(2);
        var one = sink.TryCapture(CaptureRecord(1, "one"));
        var two = sink.TryCapture(CaptureRecord(2, "two"));
        var three = sink.TryCapture(CaptureRecord(3, "three"));

        Assert.True(one.Accepted);
        Assert.True(two.Accepted);
        Assert.False(three.Accepted);
        Assert.Equal("battle.capture.queue_full", three.FailureCode);
        Assert.Equal(1, sink.DroppedRecordCount);
        Assert.Equal(2, sink.Snapshot().Count);
        Assert.Equal(BattleCaptureDropPolicy.DropNewestAndCount, sink.DropPolicy);
    }

    [Fact]
    public void Invalid_capture_is_rejected_before_queue_ownership()
    {
        var sink = new BoundedBattleProtocolCaptureSink(2);
        var invalid = CaptureRecord(0, "invalid");

        var result = sink.TryCapture(invalid);

        Assert.False(result.Accepted);
        Assert.Equal("battle.capture.record_invalid", result.FailureCode);
        Assert.Empty(sink.Snapshot());
        Assert.Equal(0, sink.DroppedRecordCount);
    }

    [Fact]
    public void Redactor_removes_every_protected_value_and_preserves_source()
    {
        var source = Encoding.UTF8.GetBytes("header-token-value-footer-token-value");
        var original = source.ToArray();
        var secret = Encoding.UTF8.GetBytes("token-value");

        var result = new BattleCaptureRedactor().Redact(source, [secret]);

        Assert.True(result.Succeeded);
        Assert.Equal(BattleCaptureRedactionStatus.Redacted, result.Status);
        Assert.Equal(2, result.RedactedMatchCount);
        Assert.Equal(original, source);
        Assert.DoesNotContain("token-value", Encoding.UTF8.GetString(result.RedactedBytes.Span), StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_capture_hash_is_culture_and_dictionary_order_independent()
    {
        var record = CaptureRecord(1, "stable");
        var canonicalizer = new BattleCaptureCanonicalizer();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var first = canonicalizer.Hash(record);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-TW");
            var second = canonicalizer.Hash(record);

            Assert.Equal(first, second);
            Assert.True(ClientBuildIdentity.IsSha256(first));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Offline_replay_is_deterministic_and_has_zero_external_side_effects()
    {
        var raw = Encoding.UTF8.GetBytes("verified-fixture");
        var record = CaptureRecord(1, "replay") with
        {
            RawFrameLength = raw.Length,
            PayloadLength = raw.Length,
            RawFrameHash = Hash(raw)
        };
        var semanticHash = Hash("semantic");
        var result = new BattleCaptureReplayRunner().Replay(
            BuildId,
            [new BattleCaptureReplayItem(record, raw, semanticHash)],
            new FixedReplayDecoder(semanticHash));

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.ReplayedCount);
        Assert.False(result.NetworkSideEffect);
        Assert.False(result.DatabaseSideEffect);
        Assert.False(result.RewardSideEffect);
        Assert.False(result.QuestSideEffect);
    }

    [Fact]
    public void Offline_replay_rejects_wrong_build_corrupt_bytes_and_sequence_regression()
    {
        var raw = Encoding.UTF8.GetBytes("verified-fixture");
        var record = CaptureRecord(1, "replay") with
        {
            RawFrameLength = raw.Length,
            PayloadLength = raw.Length,
            RawFrameHash = Hash(raw)
        };
        var item = new BattleCaptureReplayItem(record, raw, Hash("semantic"));
        var runner = new BattleCaptureReplayRunner();

        Assert.Equal(
            "battle.capture.replay_client_build_mismatch",
            runner.Replay("wrong", [item], new FixedReplayDecoder(item.ExpectedSemanticHash)).FailureCode);
        Assert.Equal(
            "battle.capture.replay_raw_hash_mismatch",
            runner.Replay(BuildId, [item with { RawData = Encoding.UTF8.GetBytes("corrupt") }], new FixedReplayDecoder(item.ExpectedSemanticHash)).FailureCode);
        Assert.Equal(
            "battle.capture.replay_sequence_invalid",
            runner.Replay(BuildId, [item, item], new FixedReplayDecoder(item.ExpectedSemanticHash)).FailureCode);
    }

    [Fact]
    public void State_machine_reaches_command_window_only_through_required_states()
    {
        var tracker = NewTracker();
        long version = 0;
        foreach (var state in new[]
                 {
                     OfficialBattleProtocolState.EncounterPending,
                     OfficialBattleProtocolState.BattleEntering,
                     OfficialBattleProtocolState.BattleInitializing,
                     OfficialBattleProtocolState.FormationLoading,
                     OfficialBattleProtocolState.ParticipantsLoading,
                     OfficialBattleProtocolState.WaitingForRoundStart,
                     OfficialBattleProtocolState.CommandWindowOpen
                 })
        {
            var result = tracker.Transition("connection-1", 3, version, state, Guid.Parse("11111111-1111-1111-1111-111111111111"), FixedTime);
            Assert.True(result.Succeeded);
            version = result.Snapshot!.ProtocolStateVersion;
        }

        var snapshot = tracker.Get("connection-1").Snapshot!;
        Assert.Equal(OfficialBattleProtocolState.CommandWindowOpen, snapshot.ProtocolState);
        Assert.Contains(BattleProtocolPacketFamily.BasicAttackClientCommand, snapshot.ExpectedInboundFamilies);
    }

    [Fact]
    public void State_machine_rejects_skipped_state_stale_version_and_old_session()
    {
        var tracker = NewTracker();

        Assert.Equal(
            BattleProtocolStateResultCode.InvalidTransition,
            tracker.Transition("connection-1", 3, 0, OfficialBattleProtocolState.CommandWindowOpen, null, FixedTime).Code);
        Assert.Equal(
            BattleProtocolStateResultCode.VersionConflict,
            tracker.Transition("connection-1", 3, 9, OfficialBattleProtocolState.EncounterPending, null, FixedTime).Code);
        Assert.Equal(
            BattleProtocolStateResultCode.InvalidSession,
            tracker.Transition("connection-1", 2, 0, OfficialBattleProtocolState.EncounterPending, null, FixedTime).Code);
    }

    [Fact]
    public void State_machine_audits_unexpected_and_stale_packets()
    {
        var audit = new InMemoryBattleProtocolStateAudit();
        var tracker = NewTracker(audit);

        var unexpected = tracker.ObserveInbound(
            "connection-1",
            3,
            1,
            BattleProtocolPacketFamily.BasicAttackClientCommand,
            FixedTime);
        Assert.Equal(BattleProtocolStateResultCode.UnexpectedPacket, unexpected.Code);

        MoveToCommandWindow(tracker);
        Assert.True(tracker.ObserveInbound(
            "connection-1",
            3,
            2,
            BattleProtocolPacketFamily.BasicAttackClientCommand,
            FixedTime).Succeeded);
        Assert.Equal(
            BattleProtocolStateResultCode.StaleSequence,
            tracker.ObserveInbound(
                "connection-1",
                3,
                2,
                BattleProtocolPacketFamily.BasicAttackClientCommand,
                FixedTime).Code);
        Assert.Contains(audit.Snapshot(), entry => entry.Result == BattleProtocolStateResultCode.UnexpectedPacket);
        Assert.Contains(audit.Snapshot(), entry => entry.Result == BattleProtocolStateResultCode.StaleSequence);
    }

    [Fact]
    public void Adapter_never_emits_bytes_while_serializer_is_evidence_blocked()
    {
        var adapter = BlockedAdapter();
        var packet = new BattleSemanticPacketDto(
            BattleProtocolPacketFamily.BattleEnter,
            BuildId,
            1,
            new Dictionary<string, string>(),
            new Dictionary<string, BattleUnknownFieldPolicy>(),
            Hash("semantic"));

        var result = adapter.SerializeOutbound(
            new BattlePacketSerializeContext(
                BuildId,
                "connection-1",
                3,
                OfficialBattleProtocolState.EncounterPending,
                1),
            packet);

        Assert.Equal(BattleProtocolAdapterResultCode.SerializerBlockedByEvidence, result.Code);
        Assert.True(result.Bytes.IsEmpty);
        Assert.Empty(result.BytesHash);
    }

    [Fact]
    public void Adapter_never_decodes_basic_attack_before_command_window_or_evidence_gate()
    {
        var adapter = BlockedAdapter();
        var frame = new byte[] { 2, 0 };

        var wrongState = adapter.DecodeInbound(
            BattleProtocolPacketFamily.BasicAttackClientCommand,
            new BattlePacketDecodeContext(
                BuildId,
                "connection-1",
                "session-1",
                3,
                OfficialBattleProtocolState.WorldActive,
                1),
            frame);
        var blocked = adapter.DecodeInbound(
            BattleProtocolPacketFamily.BasicAttackClientCommand,
            new BattlePacketDecodeContext(
                BuildId,
                "connection-1",
                "session-1",
                3,
                OfficialBattleProtocolState.CommandWindowOpen,
                1),
            frame);

        Assert.Equal(BattleProtocolAdapterResultCode.InvalidState, wrongState.Code);
        Assert.Equal(BattleProtocolAdapterResultCode.EvidenceBlocked, blocked.Code);
        Assert.Null(wrongState.Candidate);
        Assert.Null(blocked.Candidate);
    }

    [Fact]
    public void Verified_test_serializer_must_be_registered_for_exact_build()
    {
        var evidence = EvidenceRow(
            BattleProtocolPacketFamily.BattleEnter,
            PacketDirection.ServerToClient,
            BattleEvidenceConfidence.SerializerVerified,
            BattleProtocolGateStatus.SerializerVerified,
            sampleCount: 3,
            serializerTests: 3,
            acceptanceCount: 3,
            hasUnknown: false);
        var registry = new BattlePacketEvidenceRegistry([evidence]);
        var adapter = new EvidenceGatedGod2BattleProtocolAdapter(
            new EvidenceBackedBattleProtocolGate(registry),
            serializers: [new FixedSerializer(BuildId)]);
        var packet = new BattleSemanticPacketDto(
            BattleProtocolPacketFamily.BattleEnter,
            BuildId,
            1,
            new Dictionary<string, string>(),
            new Dictionary<string, BattleUnknownFieldPolicy>(),
            Hash("semantic"));

        var result = adapter.SerializeOutbound(
            new BattlePacketSerializeContext(
                BuildId,
                "connection-1",
                3,
                OfficialBattleProtocolState.EncounterPending,
                1),
            packet);

        Assert.True(result.Succeeded);
        Assert.Equal(new byte[] { 3, 0, 1 }, result.Bytes.ToArray());
    }

    [Fact]
    public void Unknown_field_policy_blocks_even_a_registered_test_serializer()
    {
        var evidence = EvidenceRow(
            BattleProtocolPacketFamily.BattleEnter,
            PacketDirection.ServerToClient,
            BattleEvidenceConfidence.SerializerVerified,
            BattleProtocolGateStatus.SerializerVerified,
            sampleCount: 3,
            serializerTests: 3,
            acceptanceCount: 3,
            hasUnknown: false);
        var adapter = new EvidenceGatedGod2BattleProtocolAdapter(
            new EvidenceBackedBattleProtocolGate(new BattlePacketEvidenceRegistry([evidence])),
            serializers: [new FixedSerializer(BuildId)]);
        var packet = new BattleSemanticPacketDto(
            BattleProtocolPacketFamily.BattleEnter,
            BuildId,
            1,
            new Dictionary<string, string>(),
            new Dictionary<string, BattleUnknownFieldPolicy>
            {
                ["unknown"] = BattleUnknownFieldPolicy.EvidenceBlocked
            },
            Hash("semantic"));

        var result = adapter.SerializeOutbound(
            new BattlePacketSerializeContext(
                BuildId,
                "connection-1",
                3,
                OfficialBattleProtocolState.EncounterPending,
                1),
            packet);

        Assert.Equal(BattleProtocolAdapterResultCode.SerializerBlockedByEvidence, result.Code);
        Assert.True(result.Bytes.IsEmpty);
    }

    [Fact]
    public void Serializer_failure_is_not_allowed_to_return_network_bytes()
    {
        var evidence = EvidenceRow(
            BattleProtocolPacketFamily.BattleEnter,
            PacketDirection.ServerToClient,
            BattleEvidenceConfidence.SerializerVerified,
            BattleProtocolGateStatus.SerializerVerified,
            sampleCount: 3,
            serializerTests: 3,
            acceptanceCount: 3,
            hasUnknown: false);
        var adapter = new EvidenceGatedGod2BattleProtocolAdapter(
            new EvidenceBackedBattleProtocolGate(new BattlePacketEvidenceRegistry([evidence])),
            serializers: [new FailingSerializer(BuildId)]);
        var packet = new BattleSemanticPacketDto(
            BattleProtocolPacketFamily.BattleEnter,
            BuildId,
            1,
            new Dictionary<string, string>(),
            new Dictionary<string, BattleUnknownFieldPolicy>(),
            Hash("semantic"));

        var result = adapter.SerializeOutbound(
            new BattlePacketSerializeContext(
                BuildId,
                "connection-1",
                3,
                OfficialBattleProtocolState.EncounterPending,
                1),
            packet);

        Assert.Equal(BattleProtocolAdapterResultCode.SerializerFailure, result.Code);
        Assert.True(result.Bytes.IsEmpty);
    }

    private static EvidenceGatedGod2BattleProtocolAdapter BlockedAdapter()
    {
        var registry = BattlePacketEvidenceRegistry.CreateInitial(BuildId);
        return new EvidenceGatedGod2BattleProtocolAdapter(
            new EvidenceBackedBattleProtocolGate(registry));
    }

    private static BattleProtocolStateTracker NewTracker(IBattleProtocolStateAudit? audit = null)
    {
        var tracker = new BattleProtocolStateTracker(audit);
        var registered = tracker.Register(new BattleProtocolStateSnapshot(
            "connection-1",
            SessionEpoch: 3,
            CharacterId: 42,
            BattleId: null,
            ProtocolState: OfficialBattleProtocolState.WorldActive,
            ProtocolStateVersion: 0,
            LastInboundSequence: 0,
            LastOutboundSequence: 0,
            ExpectedInboundFamilies: [],
            AllowedOutboundFamilies: [],
            ClientBuildId: BuildId,
            UpdatedAtUtc: FixedTime));
        Assert.True(registered.Succeeded);
        return tracker;
    }

    private static void MoveToCommandWindow(BattleProtocolStateTracker tracker)
    {
        long version = 0;
        foreach (var state in new[]
                 {
                     OfficialBattleProtocolState.EncounterPending,
                     OfficialBattleProtocolState.BattleEntering,
                     OfficialBattleProtocolState.BattleInitializing,
                     OfficialBattleProtocolState.FormationLoading,
                     OfficialBattleProtocolState.ParticipantsLoading,
                     OfficialBattleProtocolState.WaitingForRoundStart,
                     OfficialBattleProtocolState.CommandWindowOpen
                 })
        {
            version = tracker.Transition("connection-1", 3, version, state, null, FixedTime)
                .Snapshot!.ProtocolStateVersion;
        }
    }

    private static BattleCaptureRecord CaptureRecord(long sequence, string id) =>
        new(
            id,
            "capture-1",
            BattleCaptureDirection.ClientToServer,
            "connection-1",
            sequence,
            TimeSpan.FromMilliseconds(Math.Max(0, sequence)),
            BattleCaptureStage.SocketRaw,
            RawFrameLength: 4,
            RawFrameHash: Hash(id),
            OpcodeCandidate: null,
            PayloadLength: 2,
            DecryptedPayloadHash: null,
            DecompressedPayloadHash: null,
            OfficialBattleProtocolState.WorldActive,
            BuildId,
            "correlation-1",
            BattleCaptureRedactionStatus.Restricted,
            "Artifacts/BattleProtocolEvidenceV1/Captures/restricted.bin",
            "test");

    private static BattlePacketEvidence EvidenceRow(
        BattleProtocolPacketFamily family,
        PacketDirection direction,
        BattleEvidenceConfidence confidence,
        BattleProtocolGateStatus gate,
        int sampleCount,
        int decoderTests = 0,
        int negativeTests = 0,
        int serializerTests = 0,
        int acceptanceCount = 0,
        bool hasUnknown = false) =>
        new(
            family,
            direction,
            "TEST",
            "verified test framing",
            direction == PacketDirection.ClientToServer ? "DecoderVerified" : "N/A",
            "Verified test semantic mapping",
            direction == PacketDirection.ServerToClient ? "SerializerVerified" : "N/A",
            confidence,
            gate,
            BuildId,
            sampleCount,
            FixedTime,
            FixedTime,
            ["test"],
            "test",
            "test",
            ["test"],
            ["test"],
            hasUnknown ? ["test"] : [],
            decoderTests,
            negativeTests,
            serializerTests,
            acceptanceCount,
            ["test-evidence"],
            hasUnknown,
            string.Empty);

    private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));

    private static string Hash(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed class FixedReplayDecoder : IBattleCaptureReplayDecoder
    {
        private readonly string _semanticHash;

        public FixedReplayDecoder(string semanticHash)
        {
            _semanticHash = semanticHash;
        }

        public BattleCaptureReplayDecoderResult Decode(
            BattleCaptureRecord record,
            ReadOnlyMemory<byte> rawData) =>
            new(true, _semanticHash, string.Empty);
    }

    private sealed class FixedSerializer : IBattlePacketSerializer
    {
        public FixedSerializer(string clientBuildId)
        {
            ClientBuildId = clientBuildId;
        }

        public BattleProtocolPacketFamily Family => BattleProtocolPacketFamily.BattleEnter;

        public string ClientBuildId { get; }

        public BattlePacketSerializeResult Serialize(
            BattlePacketSerializeContext context,
            BattleSemanticPacketDto packet) =>
            new(
                BattleProtocolAdapterResultCode.Success,
                new byte[] { 3, 0, 1 },
                Hash(new byte[] { 3, 0, 1 }),
                string.Empty);
    }

    private sealed class FailingSerializer : IBattlePacketSerializer
    {
        public FailingSerializer(string clientBuildId)
        {
            ClientBuildId = clientBuildId;
        }

        public BattleProtocolPacketFamily Family => BattleProtocolPacketFamily.BattleEnter;

        public string ClientBuildId { get; }

        public BattlePacketSerializeResult Serialize(
            BattlePacketSerializeContext context,
            BattleSemanticPacketDto packet) =>
            new(
                BattleProtocolAdapterResultCode.SerializerFailure,
                new byte[] { 3, 0, 0 },
                Hash(new byte[] { 3, 0, 0 }),
                "test.failure");
    }
}
