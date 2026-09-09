using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using God2.ClassicServer.Protocol;

namespace God2.OfflineClientReverseEngineering;

public sealed record M5LiveBattleRecordLengthEvidence(
    string Opcode,
    int RecordLength,
    string ResolverTargetRva,
    int RuntimeInvocationCount,
    int ParsedRecordCount,
    bool RuntimeCountMatchesParsedCount);

public sealed record M5LiveBattleCommandObservation(
    ulong Sequence,
    long ObservedAtUnixMs,
    int PositionIndexCandidate,
    int ActionCodeCandidate,
    bool Continuation,
    IReadOnlyList<int> TargetPositionCandidates,
    ushort BattleContextCandidate,
    uint ActionParameterCandidate,
    string SemanticStatus);

public sealed record M5LiveBattleEffect83Candidate(
    ulong Sequence,
    int FrameOffset,
    byte EffectKind,
    byte SourceBattlePosition,
    byte SourceSide,
    byte SourceSlot,
    byte PlaybackGate,
    ushort FriendlyTargetMask,
    ushort EnemyTargetMask,
    byte ReservedByte8,
    IReadOnlyList<int> TargetBattlePositions,
    short SignedResultCandidate,
    ushort AuxiliaryValue0,
    ushort AuxiliaryValue1,
    string RecordSha256,
    bool LayoutRoundTripVerified,
    string SemanticStatus);

public sealed record M5LiveBattleEffectKindSummary(
    byte EffectKind,
    int RecordCount,
    int SameFrameSnapshotRecordCount,
    IReadOnlyList<byte> SourceBattlePositions,
    IReadOnlyList<int> TargetCardinalities,
    IReadOnlyList<short> SignedResults,
    IReadOnlyList<ushort> AuxiliaryValues0,
    IReadOnlyList<ushort> AuxiliaryValues1,
    string SemanticStatus);

public sealed record M5LiveBattleEffectKindStaticProfile(
    byte EffectKind,
    string ProjectorTargetRva,
    string ActionQueueTargetRva,
    bool DirectSignedResultSlotWriteInProjector,
    string TypedMutatorRoutingStatus);

public sealed record M5LiveBattleActorStateSelectorRoute(
    string SelectorIndex,
    string ActorStateValue,
    string TypedVitalRoute,
    bool ObservedInCapture);

public sealed record M5LiveBattleActorStateRoutingEvidence(
    int RecordSelectorOffset,
    string RecordSelectorField,
    string ProjectorSourceRva,
    string MapperRva,
    string SelectorTableRva,
    string SetterRva,
    string ActorStateOffset,
    IReadOnlyList<string> MapperDirectCallers,
    IReadOnlyList<string> SetterDirectCallers,
    IReadOnlyList<string> ObservedSelectorIndices,
    IReadOnlyList<M5LiveBattleActorStateSelectorRoute> TypedRoutes,
    string EvidenceStatus);

public sealed record M5LiveBattleEffectSnapshotOrderingEvidence(
    int SnapshotCount,
    int SnapshotsSharingFrameWithEffects,
    int EffectsSharingFrameWithSnapshot,
    int EffectsBeforeSameFrameSnapshot,
    bool AllObservedSharedFrameEffectsPrecedeSnapshot,
    bool CompleteSerializerOrderingProven,
    string SemanticStatus);

public sealed record M5LiveBattleSnapshot88Candidate(
    ulong Sequence,
    int FrameOffset,
    int RoundIndexCandidate,
    IReadOnlyList<OfficialBattleCurrentVitals> CurrentVitals,
    uint OpaqueTrailer,
    byte ConsumedTrailerByte118,
    string RecordSha256,
    bool LayoutRoundTripVerified,
    IReadOnlyList<int> ChangedByteOffsetsFromPrevious,
    string SemanticStatus);

public sealed record M5LiveBattleFrameObservation(
    ulong Sequence,
    long ObservedAtUnixMs,
    int FrameLength,
    bool ChecksumValid,
    IReadOnlyList<string> RecordOpcodes,
    int ParsedRecordBytes,
    int TrailingExtensionLength,
    string? TrailingExtensionSha256);

public sealed record M5LiveBattleCaptureSnapshot(
    string SchemaVersion,
    string Status,
    string OfficialClientSha256,
    string RuntimeCodeSha256,
    bool CaptureTimeBuildAttested,
    int ProcessId,
    int DecodedFrameCount,
    int ChecksumFailureCount,
    IReadOnlyList<M5LiveBattleRecordLengthEvidence> RecordLengthEvidence,
    IReadOnlyList<M5LiveBattleCommandObservation> Commands,
    IReadOnlyList<M5LiveBattleEffect83Candidate> EffectCandidates,
    IReadOnlyList<M5LiveBattleSnapshot88Candidate> SnapshotCandidates,
    IReadOnlyList<M5LiveBattleFrameObservation> BattleFrames,
    M5LiveBattleActorStateRoutingEvidence ActorStateRouting,
    IReadOnlyList<M5LiveBattleEffectKindStaticProfile> EffectKindStaticProfiles,
    IReadOnlyList<M5LiveBattleEffectKindSummary> EffectKindSummaries,
    M5LiveBattleEffectSnapshotOrderingEvidence EffectSnapshotOrdering,
    string FormulaStatus,
    IReadOnlyList<string> ProvenFacts,
    IReadOnlyList<string> RemainingBlockers,
    bool RawPacketBodiesRetained,
    bool ClientWriteAccessUsed,
    bool NetworkBytesEmitted,
    bool FakeNetworkBytes);

/// <summary>
/// Replays an attested live plaintext capture against the official Client's read-only
/// record-length resolver. Unknown payloads are represented by bounded hashes only.
/// </summary>
public static class M5LiveBattleCaptureAnalyzer
{
    public const uint RecordLengthJumpTableRva = 0x001664B4;
    public const uint ImageBase = 0x00400000;
    public const uint EffectProjectorJumpTableRva = 0x00155C04;
    public const uint EffectProjectorIndexTableRva = 0x00155C3C;
    public const uint EffectActionQueueJumpTableRva = 0x001611E4;
    public const uint EffectActionQueueIndexTableRva = 0x00161214;
    public const uint ActorStateSelectorTableRva = 0x003F4F10;
    public const uint ActorStateMapperRva = 0x00160D20;
    public const uint ActorStateSetterRva = 0x00163240;

    private const int MaximumInputBytes = 16 * 1024 * 1024;
    private static readonly HashSet<byte> BattleOpcodes = [0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89];

    public static M5LiveBattleCaptureSnapshot Analyze(
        string captureDirectory,
        string runtimeCodePath,
        string expectedRuntimeCodeSha256,
        string expectedOfficialClientSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeCodePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRuntimeCodeSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedOfficialClientSha256);

        captureDirectory = Path.GetFullPath(captureDirectory);
        runtimeCodePath = Path.GetFullPath(runtimeCodePath);
        var metadataPath = Path.Combine(captureDirectory, "metadata.jsonl");
        var semanticPath = Path.Combine(captureDirectory, "sensitive", "semantic-events.jsonl");
        var runtimeCode = BoundedFile.ReadAllBytes(runtimeCodePath, MaximumInputBytes, "M5 live runtime snapshot");
        try
        {
            var runtimeCodeSha256 = Convert.ToHexString(SHA256.HashData(runtimeCode));
            if (!string.Equals(runtimeCodeSha256, expectedRuntimeCodeSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("M5 live runtime snapshot SHA-256 does not match the requested evidence input.");
            }

            var semantic = ReadSemanticAttestation(semanticPath, expectedOfficialClientSha256);
            var commands = new List<M5LiveBattleCommandObservation>();
            var effects = new List<M5LiveBattleEffect83Candidate>();
            var snapshots = new List<M5LiveBattleSnapshot88Candidate>();
            var battleFrames = new List<M5LiveBattleFrameObservation>();
            var parsedCounts = new Dictionary<byte, int>();
            var observedLengthTargets = new Dictionary<byte, (int Length, uint TargetRva)>();
            byte[]? previousSnapshot = null;
            var decodedFrameCount = 0;
            var checksumFailureCount = 0;
            var processIds = new HashSet<int>();

            foreach (var line in ReadBoundedLines(metadataPath, "M5 live metadata"))
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!StringPropertyEquals(root, "MessageType", "PlaintextProtocolFrame") ||
                    !root.TryGetProperty("Plaintext", out var plaintext) || !plaintext.GetBoolean() ||
                    !root.TryGetProperty("PlaintextHex", out var plaintextHexElement))
                {
                    continue;
                }

                var frame = Convert.FromHexString(plaintextHexElement.GetString()
                    ?? throw new InvalidDataException("M5 live frame has no plaintext hex."));
                try
                {
                    decodedFrameCount++;
                    var sequence = ReadUInt64(root, "sequence");
                    var observedAt = ReadInt64(root, "ObservedAtUnixMs");
                    if (root.TryGetProperty("ProcessId", out var processIdElement))
                    {
                        processIds.Add(processIdElement.GetInt32());
                    }

                    ValidateFrameEnvelope(frame);
                    var checksumValid = frame[^1] == OfficialLoginWireTransform.ComputeChecksum(frame);
                    if (!checksumValid)
                    {
                        checksumFailureCount++;
                        continue;
                    }

                    if (StringPropertyEquals(root, "PacketDirection", "ClientToServer") && frame[2] == 0x35)
                    {
                        var candidate = M5BattleEvidenceRecovery.ParseCommand35ForEvidence(
                            frame[2],
                            frame.AsSpan(3, frame.Length - 4).ToArray());
                        if (candidate is not null)
                        {
                            commands.Add(new M5LiveBattleCommandObservation(
                                sequence,
                                observedAt,
                                candidate.PositionIndexCandidate,
                                candidate.ActionCodeCandidate,
                                candidate.Continuation,
                                DecodeTargetPositions(candidate),
                                candidate.BattleContextCandidate,
                                candidate.ActionParameterCandidate,
                                candidate.SemanticStatus));
                        }
                        continue;
                    }

                    if (!StringPropertyEquals(root, "PacketDirection", "ServerToClient"))
                    {
                        continue;
                    }

                    var records = ParseRecordStream(frame, runtimeCode, observedLengthTargets);
                    if (!records.Any(record => BattleOpcodes.Contains(record.Opcode)))
                    {
                        continue;
                    }

                    foreach (var record in records)
                    {
                        parsedCounts[record.Opcode] = parsedCounts.GetValueOrDefault(record.Opcode) + 1;
                        if (record.Opcode == 0x83)
                        {
                            effects.Add(ParseEffect83(sequence, record.Offset, frame.AsSpan(record.Offset, record.Length)));
                        }
                        else if (record.Opcode == 0x88)
                        {
                            var body = frame.AsSpan(record.Offset, record.Length);
                            var decoded = OfficialBattleVitalSnapshotWireCodec.DecodeLayout(
                                OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
                                GameplayProtocolState.Battle,
                                body);
                            if (!decoded.LayoutDecoded || decoded.Value is null)
                            {
                                throw new InvalidDataException(
                                    $"Attested 0x88 record failed exact layout decode: {decoded.FailureCode}");
                            }
                            var encoded = OfficialBattleVitalSnapshotWireCodec.EncodeLayout(
                                OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
                                GameplayProtocolState.Battle,
                                new OfficialBattleVitalSnapshotLayout(
                                    decoded.Value.RoundIndex,
                                    decoded.Value.Positions,
                                    decoded.Value.OpaqueTrailer));
                            var roundTripVerified = encoded.LayoutEncoded && encoded.Value is not null &&
                                body.SequenceEqual(encoded.Value);
                            if (!roundTripVerified)
                            {
                                throw new InvalidDataException(
                                    $"Attested 0x88 record failed exact layout byte round-trip: original={Convert.ToHexString(body)}, encoded={Convert.ToHexString(encoded.Value ?? [])}.");
                            }
                            var changedOffsets = previousSnapshot is null
                                ? Array.Empty<int>()
                                : ChangedOffsets(previousSnapshot, body);
                            snapshots.Add(new M5LiveBattleSnapshot88Candidate(
                                sequence,
                                record.Offset,
                                decoded.Value.RoundIndex,
                                decoded.Value.Positions,
                                decoded.Value.OpaqueTrailer,
                                decoded.Value.ConsumedTrailerByte118,
                                Hash(body),
                                roundTripVerified,
                                changedOffsets,
                                "HitPointsMagicPointsConsumerVerified_TrailerByte118ConsumerVerifiedMeaningBlocked"));
                            previousSnapshot = body.ToArray();
                        }
                    }

                    var consumed = records.Count == 0 ? 0 : records[^1].Offset + records[^1].Length - 2;
                    var trailingOffset = 2 + consumed;
                    var trailingLength = Math.Max(0, frame.Length - 1 - trailingOffset);
                    battleFrames.Add(new M5LiveBattleFrameObservation(
                        sequence,
                        observedAt,
                        frame.Length,
                        checksumValid,
                        records.Select(record => $"0x{record.Opcode:X2}").ToArray(),
                        consumed,
                        trailingLength,
                        trailingLength == 0 ? null : Hash(frame.AsSpan(trailingOffset, trailingLength))));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(frame);
                }
            }

            var lengthEvidence = semantic.HandlerInvocationCounts
                .OrderBy(pair => pair.Key)
                .Select(pair =>
                {
                    if (!observedLengthTargets.TryGetValue(pair.Key, out var resolved))
                    {
                        resolved = ResolveRecordLength(runtimeCode, pair.Key)
                            ?? throw new InvalidDataException($"No static record length for observed opcode 0x{pair.Key:X2}.");
                    }
                    var parsed = parsedCounts.GetValueOrDefault(pair.Key);
                    return new M5LiveBattleRecordLengthEvidence(
                        $"0x{pair.Key:X2}",
                        resolved.Length,
                        $"0x{resolved.TargetRva:X8}",
                        pair.Value,
                        parsed,
                        parsed == pair.Value);
                })
                .ToArray();

            var allRuntimeCountsMatch = lengthEvidence.All(item => item.RuntimeCountMatchesParsedCount);
            var allEffectRoundTrips = effects.All(effect => effect.LayoutRoundTripVerified);
            var allSnapshotRoundTrips = snapshots.All(snapshot => snapshot.LayoutRoundTripVerified);
            var snapshotsBySequence = snapshots
                .GroupBy(snapshot => snapshot.Sequence)
                .ToDictionary(group => group.Key, group => group.OrderBy(snapshot => snapshot.FrameOffset).First());
            var sharedFrameEffects = effects
                .Where(effect => snapshotsBySequence.ContainsKey(effect.Sequence))
                .ToArray();
            var effectsBeforeSameFrameSnapshot = sharedFrameEffects.Count(effect =>
                effect.FrameOffset < snapshotsBySequence[effect.Sequence].FrameOffset);
            var effectKindSummaries = effects
                .GroupBy(effect => effect.EffectKind)
                .OrderBy(group => group.Key)
                .Select(group => new M5LiveBattleEffectKindSummary(
                    group.Key,
                    group.Count(),
                    group.Count(effect => snapshotsBySequence.ContainsKey(effect.Sequence)),
                    group.Select(effect => effect.SourceBattlePosition).Distinct().Order().ToArray(),
                    group.Select(effect => effect.TargetBattlePositions.Count).Distinct().Order().ToArray(),
                    group.Select(effect => effect.SignedResultCandidate).Distinct().Order().ToArray(),
                    group.Select(effect => effect.AuxiliaryValue0).Distinct().Order().ToArray(),
                    group.Select(effect => effect.AuxiliaryValue1).Distinct().Order().ToArray(),
                    "ObservedExactBuildDistribution_NotAuthoritativeServerMeaning"))
                .ToArray();
            var effectKindStaticProfiles = Enumerable.Range(1, 23)
                .Select(kind => ResolveEffectKindDispatchProfile(runtimeCode, (byte)kind))
                .ToArray();
            var actorStateRouting = ResolveActorStateRoutingEvidence(runtimeCode, effects);
            var effectSnapshotOrdering = new M5LiveBattleEffectSnapshotOrderingEvidence(
                snapshots.Count,
                snapshotsBySequence.Keys.Count(sequence => effects.Any(effect => effect.Sequence == sequence)),
                sharedFrameEffects.Length,
                effectsBeforeSameFrameSnapshot,
                sharedFrameEffects.Length == effectsBeforeSameFrameSnapshot,
                CompleteSerializerOrderingProven: false,
                "ObservedSharedFrameOrderingVerified_UnobservedOrderingAndTrailerMeaningBlocked");
            var captureAttested = semantic.BuildIds.Count == 1 &&
                string.Equals(semantic.BuildIds.Single(), expectedOfficialClientSha256, StringComparison.OrdinalIgnoreCase);
            var status = checksumFailureCount == 0 && captureAttested && allRuntimeCountsMatch &&
                allEffectRoundTrips && allSnapshotRoundTrips
                ? "PASS_ATTESTED_BATTLE_RECORD_STREAM_REPLAYED_FORMULA_OPERANDS_BLOCKED"
                : "BLOCKED_BY_CAPTURE_OR_RECORD_REPLAY_MISMATCH";

            return new M5LiveBattleCaptureSnapshot(
                "god2-roadmap-m5-live-battle-capture-v6",
                status,
                expectedOfficialClientSha256.ToUpperInvariant(),
                runtimeCodeSha256,
                captureAttested,
                processIds.Count == 1 ? processIds.Single() : 0,
                decodedFrameCount,
                checksumFailureCount,
                lengthEvidence,
                commands,
                effects,
                snapshots,
                battleFrames,
                actorStateRouting,
                effectKindStaticProfiles,
                effectKindSummaries,
                effectSnapshotOrdering,
                "OutcomeRecordLayoutRecovered_ExactDamageFormulaStillRequiresAttackerAndDefenderOperands",
                [
                    $"The capture contains {commands.Count} checksum-valid C2S 0x35 Battle commands.",
                    $"The official record-length resolver and runtime hook agree on {lengthEvidence.Sum(item => item.ParsedRecordCount)} handler records.",
                    $"The capture contains {effects.Count} fixed 15-byte 0x83 effect records and {snapshots.Count} fixed 121-byte 0x88 snapshots.",
                    $"All {effects.Count} 0x83 records reproduce byte-for-byte through the evidence-only layout encoder.",
                    $"All {snapshots.Count} 0x88 records reproduce byte-for-byte through the evidence-only layout encoder; raw trailer bytes are retained without inventing their server meaning.",
                    "The exact-build two-stage effect-kind dispatch tables are recovered for all kinds 1..23. Kinds 7 and 9 directly copy the signed result into the affected actor delta slot; typed HP/MP routing still occurs through a later actor-state machine.",
                    "An exact projector path passes 0x83 record byte 12 (AuxiliaryValue0 high byte) through the 67-entry selector table at RVA 0x003F4F10 into actor+0x232. Selector indices 0x22/0x23/0x24 map to states 0x77/0x78/0x79, whose later consumer dispatches HP/MP/HP+MP mutations respectively.",
                    $"In {effectSnapshotOrdering.SnapshotsSharingFrameWithEffects} shared frames, all {effectSnapshotOrdering.EffectsSharingFrameWithSnapshot} observed 0x83 records precede the same-frame 0x88 snapshot.",
                    "The 0x83 consumer verifies an effect kind, source position, playback gate, two raw UInt16 mask fields whose low fourteen bits project side slots, one reserved byte, a signed result and two auxiliary UInt16 values; observed high mask bits are preserved but remain semantically unclassified.",
                    "The 0x88 consumer verifies fourteen current HP/MP pairs. For the selected actor, actor+0xDF0/+0xDF4 provide maximum HP/MP and the UI clamps, divides and renders both current/maximum pairs.",
                    "Three controlled position-6 Skill commands (sequences 2809/2920/3027) precede exact MP transitions 63->58->53->48 (snapshots 2867/2974/3049), matching the observed official skill's MP cost 5; the client CHARHPMP domain and complementary damage bar bind the first value to HP."
                ],
                [
                    "The signed 0x83 result is a server-supplied outcome projected through exact-build clamped HP/MP Client consumers; the authoritative server operands, formula and complete serializer remain evidence-blocked.",
                    "Attacker attack power, defender defense, random roll, critical state and elemental modifier are not present as typed operands in this capture.",
                    "The exact official damage formula cannot be promoted until repeated operand-to-result-to-state-mutation evidence is captured.",
                    "The existing attested captures observe actor-state selectors 0x00 and 0x1C only; none observes selector 0x22, 0x23 or 0x24, so the typed route is exact-build static reachability rather than an observed server emission.",
                    "The 0x88 trailer layout is byte-replayable, but only offset 118 has a verified exact-build Client consumer; its semantic meaning and authoritative server source remain blocked despite the observed constant.",
                    "Observed effect-before-snapshot ordering does not prove ordering for unobserved action families, multi-frame effects, rollback or settlement."
                ],
                RawPacketBodiesRetained: false,
                ClientWriteAccessUsed: false,
                NetworkBytesEmitted: false,
                FakeNetworkBytes: false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(runtimeCode);
        }
    }

    public static (int Length, uint TargetRva)? ResolveRecordLength(byte[] runtimeCode, byte opcode)
    {
        ArgumentNullException.ThrowIfNull(runtimeCode);
        var tableOffset = checked((int)RecordLengthJumpTableRva + opcode * sizeof(uint));
        if (tableOffset < 0 || tableOffset + sizeof(uint) > runtimeCode.Length)
        {
            return null;
        }

        var absoluteTarget = BinaryPrimitives.ReadUInt32LittleEndian(runtimeCode.AsSpan(tableOffset, sizeof(uint)));
        if (absoluteTarget < ImageBase)
        {
            return null;
        }

        var targetRva = absoluteTarget - ImageBase;
        if (targetRva + 5u > runtimeCode.Length || runtimeCode[targetRva] != 0xB8)
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(runtimeCode.AsSpan(checked((int)targetRva + 1), 4));
        return length > 0 ? (length, targetRva) : null;
    }

    public static M5LiveBattleEffectKindStaticProfile ResolveEffectKindDispatchProfile(
        byte[] runtimeCode,
        byte effectKind)
    {
        ArgumentNullException.ThrowIfNull(runtimeCode);
        if (effectKind is < 1 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(effectKind));
        }

        var projectorTarget = ResolveIndexedDispatchTarget(
            runtimeCode,
            effectKind,
            EffectProjectorIndexTableRva,
            EffectProjectorJumpTableRva);
        var actionQueueTarget = ResolveIndexedDispatchTarget(
            runtimeCode,
            effectKind,
            EffectActionQueueIndexTableRva,
            EffectActionQueueJumpTableRva);
        return new M5LiveBattleEffectKindStaticProfile(
            effectKind,
            $"0x{projectorTarget:X8}",
            $"0x{actionQueueTarget:X8}",
            effectKind is 7 or 9,
            "IntermediateActorStateMachineRequired_TypedHpMpMutatorNotEffectKindDirect");
    }

    public static M5LiveBattleActorStateRoutingEvidence ResolveActorStateRoutingEvidence(
        byte[] runtimeCode,
        IReadOnlyCollection<M5LiveBattleEffect83Candidate> effects)
    {
        ArgumentNullException.ThrowIfNull(runtimeCode);
        ArgumentNullException.ThrowIfNull(effects);

        RequireBytesAt(runtimeCode, 0x001556AA,
            "8BCF500FB6460B500FB6460A50FF7510FF750CE85EB60000");
        RequireBytesAt(runtimeCode, 0x00160D6B,
            "8A45143C43730E0FB6C08A80104F7F00884510");
        RequireBytesAt(runtimeCode, 0x00163319,
            "8A4514752F888632020000C6813202000000");
        RequireBytesAt(runtimeCode, 0x0015F16D,
            "8A450B3C77746B3C7874553C79742F");

        var selectorTableEnd = checked((int)ActorStateSelectorTableRva + 0x43);
        if (selectorTableEnd > runtimeCode.Length)
        {
            throw new InvalidDataException("Actor-state selector table falls outside the attested runtime snapshot.");
        }
        var observed = effects
            .Select(effect => (byte)(effect.AuxiliaryValue0 >> 8))
            .Distinct()
            .Order()
            .ToArray();
        var typedRoutes = new[]
        {
            (Selector: (byte)0x22, ExpectedState: (byte)0x77, Route: "ActorDelta286ToHitPointsMutatorRva0x001566A0"),
            (Selector: (byte)0x23, ExpectedState: (byte)0x78, Route: "ActorDelta286ToMagicPointsMutatorRva0x001566E0"),
            (Selector: (byte)0x24, ExpectedState: (byte)0x79, Route: "ActorDelta286ToHitPointsThenActorDelta288ToMagicPoints")
        }
        .Select(route =>
        {
            var actualState = runtimeCode[ActorStateSelectorTableRva + route.Selector];
            if (actualState != route.ExpectedState)
            {
                throw new InvalidDataException(
                    $"Actor-state selector 0x{route.Selector:X2} maps to 0x{actualState:X2}, expected 0x{route.ExpectedState:X2}.");
            }
            return new M5LiveBattleActorStateSelectorRoute(
                $"0x{route.Selector:X2}",
                $"0x{actualState:X2}",
                route.Route,
                observed.Contains(route.Selector));
        })
        .ToArray();

        return new M5LiveBattleActorStateRoutingEvidence(
            RecordSelectorOffset: 12,
            RecordSelectorField: "AuxiliaryValue0HighByte",
            ProjectorSourceRva: "0x001556AA",
            MapperRva: $"0x{ActorStateMapperRva:X8}",
            SelectorTableRva: $"0x{ActorStateSelectorTableRva:X8}",
            SetterRva: $"0x{ActorStateSetterRva:X8}",
            ActorStateOffset: "actor+0x232",
            MapperDirectCallers: ["0x001556BD", "0x001558AE", "0x00155A70", "0x0015F29D"],
            SetterDirectCallers: ["0x00160DAC", "0x00160E64"],
            ObservedSelectorIndices: observed.Select(value => $"0x{value:X2}").ToArray(),
            TypedRoutes: typedRoutes,
            EvidenceStatus: typedRoutes.Any(route => route.ObservedInCapture)
                ? "ExactBuildStaticRouteAndServerEmissionObserved"
                : "ExactBuildStaticRouteVerified_ServerEmissionNotObserved");
    }

    private static void RequireBytesAt(byte[] runtimeCode, int rva, string expectedHex)
    {
        var expected = Convert.FromHexString(expectedHex);
        if (rva < 0 || rva + expected.Length > runtimeCode.Length ||
            !runtimeCode.AsSpan(rva, expected.Length).SequenceEqual(expected))
        {
            throw new InvalidDataException($"Exact-build actor-state routing signature mismatch at RVA 0x{rva:X8}.");
        }
    }

    private static uint ResolveIndexedDispatchTarget(
        byte[] runtimeCode,
        byte effectKind,
        uint indexTableRva,
        uint jumpTableRva)
    {
        var indexOffset = checked((int)indexTableRva + effectKind - 1);
        if (indexOffset >= runtimeCode.Length)
        {
            throw new InvalidDataException("Effect-kind index table falls outside the attested runtime snapshot.");
        }
        var jumpIndex = runtimeCode[indexOffset];
        var jumpOffset = checked((int)jumpTableRva + jumpIndex * sizeof(uint));
        if (jumpOffset < 0 || jumpOffset + sizeof(uint) > runtimeCode.Length)
        {
            throw new InvalidDataException("Effect-kind jump table falls outside the attested runtime snapshot.");
        }
        var absoluteTarget = BinaryPrimitives.ReadUInt32LittleEndian(runtimeCode.AsSpan(jumpOffset));
        if (absoluteTarget < ImageBase)
        {
            throw new InvalidDataException("Effect-kind dispatch target is below the exact image base.");
        }
        var targetRva = absoluteTarget - ImageBase;
        if (targetRva >= runtimeCode.Length)
        {
            throw new InvalidDataException("Effect-kind dispatch target falls outside the attested runtime snapshot.");
        }
        return targetRva;
    }

    public static M5LiveBattleEffect83Candidate ParseEffect83(
        ulong sequence,
        int frameOffset,
        ReadOnlySpan<byte> record)
    {
        if (record.Length != 15 || record[0] != 0x83)
        {
            throw new InvalidDataException("M5 0x83 effect record must be exactly 15 bytes.");
        }

        var decoded = OfficialBattleEffectWireCodec.DecodeLayout(
            OfficialBattleEffectWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            record);
        if (!decoded.LayoutDecoded || decoded.Value is null)
        {
            throw new InvalidDataException($"M5 0x83 effect record failed exact layout decode: {decoded.FailureCode}");
        }

        var value = decoded.Value;
        var encoded = OfficialBattleEffectWireCodec.EncodeLayout(
            OfficialBattleEffectWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            new OfficialBattleEffectLayout(
                value.EffectKind,
                value.SourceBattlePosition,
                value.PlaybackGate,
                value.FriendlyTargetMask,
                value.EnemyTargetMask,
                value.ReservedByte8,
                value.SignedResult,
                value.AuxiliaryValue0,
                value.AuxiliaryValue1));
        var roundTripVerified = encoded.LayoutEncoded && encoded.Value is not null &&
            record.SequenceEqual(encoded.Value);
        if (!roundTripVerified)
        {
            throw new InvalidDataException(
                $"M5 0x83 effect record failed exact layout byte round-trip: original={Convert.ToHexString(record)}, encoded={Convert.ToHexString(encoded.Value ?? [])}.");
        }

        return new M5LiveBattleEffect83Candidate(
            sequence,
            frameOffset,
            value.EffectKind,
            value.SourceBattlePosition,
            value.SourceSide,
            value.SourceSlot,
            value.PlaybackGate,
            value.FriendlyTargetMask,
            value.EnemyTargetMask,
            value.ReservedByte8,
            value.TargetBattlePositions,
            value.SignedResult,
            value.AuxiliaryValue0,
            value.AuxiliaryValue1,
            value.RecordSha256,
            roundTripVerified,
            "StaticLengthRuntimeBoundaryConsumerAndLayoutRoundTripVerified_ResultAuthorityBlocked");
    }

    private static IReadOnlyList<RecordSlice> ParseRecordStream(
        byte[] frame,
        byte[] runtimeCode,
        IDictionary<byte, (int Length, uint TargetRva)> observedLengths)
    {
        var records = new List<RecordSlice>();
        var offset = 2;
        while (offset < frame.Length - 1)
        {
            var opcode = frame[offset];
            var resolved = ResolveRecordLength(runtimeCode, opcode);
            if (resolved is null || offset + resolved.Value.Length > frame.Length - 1)
            {
                break;
            }

            observedLengths[opcode] = resolved.Value;
            records.Add(new RecordSlice(opcode, offset, resolved.Value.Length));
            offset += resolved.Value.Length;
        }
        return records;
    }

    private static IReadOnlyList<int> DecodeTargetPositions(M5BattleCommand35Candidate command)
    {
        var result = new List<int>();
        var groups = new[] { command.TargetMaskGroup0, command.TargetMaskGroup1, command.TargetMaskGroup2 };
        for (var group = 0; group < groups.Length; group++)
        {
            for (var bit = 0; bit < 14; bit++)
            {
                if ((groups[group] & (1 << bit)) != 0)
                {
                    result.Add(group * 14 + bit);
                }
            }
        }
        return result;
    }

    private static IReadOnlyList<int> ChangedOffsets(byte[] previous, ReadOnlySpan<byte> current)
    {
        if (previous.Length != current.Length)
        {
            throw new InvalidDataException("M5 0x88 snapshot length changed within one capture.");
        }
        var result = new List<int>();
        for (var index = 0; index < current.Length; index++)
        {
            if (previous[index] != current[index])
            {
                result.Add(index);
            }
        }
        return result;
    }

    private static SemanticAttestation ReadSemanticAttestation(string path, string expectedBuildId)
    {
        var buildIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<byte, int>();
        foreach (var line in ReadBoundedLines(path, "M5 live semantic events"))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("ClientBuildId", out var buildElement) && buildElement.GetString() is { Length: > 0 } build)
            {
                buildIds.Add(build);
            }
            if (!StringPropertyEquals(root, "EventType", "HandlerArgument") ||
                !StringPropertyEquals(root, "SourceToken", "Battle.HandlerRecordLength") ||
                !root.TryGetProperty("Payload", out var payload) ||
                !payload.TryGetProperty("ArgumentValue", out var value))
            {
                continue;
            }
            var opcode = checked((byte)value.GetInt32());
            counts[opcode] = counts.GetValueOrDefault(opcode) + 1;
        }

        if (buildIds.Count != 1 || !buildIds.Contains(expectedBuildId))
        {
            throw new InvalidDataException("M5 live semantic events are not attested to the requested official Client build.");
        }
        return new SemanticAttestation(buildIds, counts);
    }

    private static IEnumerable<string> ReadBoundedLines(string path, string label)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > MaximumInputBytes)
        {
            throw new InvalidDataException($"{label} is missing, empty, or exceeds the input budget.");
        }
        return File.ReadLines(file.FullName);
    }

    private static void ValidateFrameEnvelope(byte[] frame)
    {
        if (frame.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(frame) != frame.Length)
        {
            throw new InvalidDataException("M5 live plaintext frame length is invalid.");
        }
    }

    private static bool StringPropertyEquals(JsonElement element, string propertyName, string expected) =>
        element.TryGetProperty(propertyName, out var value) &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static ulong ReadUInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value.GetUInt64() : 0;

    private static long ReadInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value.GetInt64() : 0;

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record RecordSlice(byte Opcode, int Offset, int Length);
    private sealed record SemanticAttestation(
        IReadOnlySet<string> BuildIds,
        IReadOnlyDictionary<byte, int> HandlerInvocationCounts);
}
