using System.Buffers.Binary;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Protocol;

public enum ProtocolConfidence
{
    Unknown,
    Inferred,
    Recovered,
    Verified
}

public enum PacketDirection
{
    Unknown,
    ClientToServer,
    ServerToClient
}

public enum PacketRecoveryStatus
{
    Unknown,
    Recovered,
    Verified,
    EvidenceOnly,
    NeedsRecovery
}

public readonly record struct Opcode(ushort Value)
{
    public string ToHex() => Value.ToString("X4");
}

public sealed record PacketHeader(Opcode Opcode, int Length, uint Sequence, string OpcodeCandidate = "");

public sealed record PacketEnvelope(
    PacketHeader Header,
    ReadOnlyMemory<byte> Payload,
    ProtocolConfidence Confidence,
    PacketKnowledge? Knowledge = null);

public sealed record ProtocolEvidenceSource(string Path, string EvidenceType, ProtocolConfidence Confidence);

public sealed record ProtocolField(
    string Name,
    int Offset,
    int Length,
    string Encoding,
    PacketRecoveryStatus Status,
    string Note = "");

public sealed record PacketKnowledge(
    string Id,
    string Family,
    PacketDirection Direction,
    int Length,
    string? OpcodeCandidate,
    ProtocolConfidence Confidence,
    PacketRecoveryStatus RecoveryStatus,
    bool Recovered,
    bool Verified,
    IReadOnlyList<ProtocolEvidenceSource> EvidenceSources,
    IReadOnlyList<ProtocolField> KnownFields,
    IReadOnlyList<ProtocolField> UnknownFields,
    IReadOnlyList<string> Samples,
    IReadOnlyList<string> FixedPrefixes);

public sealed record ProtocolKnowledgeBase(
    string Version,
    IReadOnlyList<PacketKnowledge> Packets,
    IReadOnlyList<string> Remaining,
    string VisibilityBoundary);

public interface IPacketReader
{
    OperationResult<PacketEnvelope> Read(ReadOnlyMemory<byte> frame);
}

public interface IPacketWriter
{
    OperationResult<ReadOnlyMemory<byte>> Write(PacketEnvelope envelope);
}

public interface IPacketHandler
{
    Opcode Opcode { get; }

    ProtocolConfidence Confidence { get; }

    Task<OperationResult> HandleAsync(PacketEnvelope packet, CancellationToken cancellationToken);
}

public interface IPacketRouter
{
    void Register(IPacketHandler handler);

    Task<OperationResult> RouteAsync(PacketEnvelope packet, CancellationToken cancellationToken);
}

public sealed class ProtocolRegistry
{
    private readonly Dictionary<string, PacketKnowledge> _packetsById;
    private readonly Dictionary<string, List<PacketKnowledge>> _packetsByFamily;

    public ProtocolRegistry(ProtocolKnowledgeBase knowledgeBase)
    {
        KnowledgeBase = knowledgeBase;
        _packetsById = knowledgeBase.Packets.ToDictionary(packet => packet.Id, StringComparer.Ordinal);
        _packetsByFamily = knowledgeBase.Packets
            .GroupBy(packet => packet.Family, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
    }

    public ProtocolKnowledgeBase KnowledgeBase { get; }

    public static ProtocolRegistry Official { get; } = new(OfficialProtocolKnowledge.CreateDefault());

    public IReadOnlyList<PacketKnowledge> Packets => KnowledgeBase.Packets;

    public bool TryGetPacket(string id, out PacketKnowledge? packet) => _packetsById.TryGetValue(id, out packet);

    public IReadOnlyList<PacketKnowledge> FindByFamily(string family) =>
        _packetsByFamily.TryGetValue(family, out var packets) ? packets : [];
}

public sealed class OpcodeRegistry
{
    private readonly Dictionary<string, PacketKnowledge> _packetsByOpcodeCandidate;

    public OpcodeRegistry(ProtocolRegistry protocolRegistry)
    {
        _packetsByOpcodeCandidate = protocolRegistry.Packets
            .Where(packet => !string.IsNullOrWhiteSpace(packet.OpcodeCandidate))
            .GroupBy(packet => packet.OpcodeCandidate!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => NormalizeHex(group.Key), group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string opcodeCandidate, out PacketKnowledge? packet) =>
        _packetsByOpcodeCandidate.TryGetValue(NormalizeHex(opcodeCandidate), out packet);

    public static string NormalizeHex(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}

public sealed class PacketRegistry
{
    private readonly Dictionary<Opcode, IPacketHandler> _handlers = [];
    private readonly ProtocolRegistry _protocolRegistry;

    public PacketRegistry()
        : this(ProtocolRegistry.Official)
    {
    }

    public PacketRegistry(ProtocolRegistry protocolRegistry)
    {
        _protocolRegistry = protocolRegistry;
    }

    public IReadOnlyList<PacketKnowledge> KnownPackets => _protocolRegistry.Packets;

    public void Register(IPacketHandler handler)
    {
        if (handler.Confidence == ProtocolConfidence.Unknown)
        {
            throw new InvalidOperationException("Unknown packets cannot be registered as verified handlers.");
        }

        _handlers[handler.Opcode] = handler;
    }

    public bool TryGet(Opcode opcode, out IPacketHandler? handler) => _handlers.TryGetValue(opcode, out handler);

    public bool TryGetKnowledge(string id, out PacketKnowledge? packet) => _protocolRegistry.TryGetPacket(id, out packet);
}

public sealed class PacketFactory
{
    private readonly PacketDeserializer _deserializer;

    public PacketFactory(PacketDeserializer deserializer)
    {
        _deserializer = deserializer;
    }

    public OperationResult<PacketEnvelope> CreateFromFrame(ReadOnlyMemory<byte> frame) => _deserializer.Read(frame);

    public PacketEnvelope CreateKnown(PacketKnowledge knowledge, ReadOnlyMemory<byte> payload, uint sequence = 0)
    {
        var opcode = ParseOpcode(knowledge.OpcodeCandidate);
        var header = new PacketHeader(opcode, knowledge.Length, sequence, knowledge.OpcodeCandidate ?? string.Empty);
        return new PacketEnvelope(header, payload, knowledge.Confidence, knowledge);
    }

    private static Opcode ParseOpcode(string? opcodeCandidate)
    {
        if (string.IsNullOrWhiteSpace(opcodeCandidate) || opcodeCandidate.Length < 4)
        {
            return new Opcode(0);
        }

        return ushort.TryParse(OpcodeRegistry.NormalizeHex(opcodeCandidate)[..4], System.Globalization.NumberStyles.HexNumber, null, out var value)
            ? new Opcode(value)
            : new Opcode(0);
    }
}

public sealed class PacketSerializer : IPacketWriter
{
    public OperationResult<ReadOnlyMemory<byte>> Write(PacketEnvelope envelope)
    {
        if (envelope.Header.Length < 2)
        {
            return OperationResult<ReadOnlyMemory<byte>>.Failure("packet.length_invalid", "Packet length must include the two-byte length prefix.", envelope.Knowledge?.Id ?? string.Empty);
        }

        if (envelope.Payload.Length != envelope.Header.Length - 2)
        {
            return OperationResult<ReadOnlyMemory<byte>>.Failure("packet.payload_length_mismatch", "Payload length does not match packet header length.", envelope.Knowledge?.Id ?? string.Empty);
        }

        var buffer = new byte[envelope.Header.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), (ushort)envelope.Header.Length);
        envelope.Payload.Span.CopyTo(buffer.AsSpan(2));
        return OperationResult<ReadOnlyMemory<byte>>.Success(buffer);
    }
}

public sealed class PacketDeserializer : IPacketReader
{
    private readonly ProtocolRegistry _protocolRegistry;

    public PacketDeserializer(ProtocolRegistry protocolRegistry)
    {
        _protocolRegistry = protocolRegistry;
    }

    public OperationResult<PacketEnvelope> Read(ReadOnlyMemory<byte> frame)
    {
        if (frame.Length < 2)
        {
            return OperationResult<PacketEnvelope>.Failure("packet.frame_too_short", "Packet frame must contain a two-byte length prefix.");
        }

        var declaredLength = BinaryPrimitives.ReadUInt16LittleEndian(frame.Span[..2]);
        if (declaredLength != frame.Length)
        {
            return OperationResult<PacketEnvelope>.Failure("packet.length_mismatch", $"Declared length {declaredLength} does not match frame length {frame.Length}.");
        }

        var knowledge = Classify(frame.Span);
        var opcodeCandidate = frame.Length >= 4
            ? Convert.ToHexString(frame.Span.Slice(2, 2))
            : string.Empty;
        var opcode = PacketFactoryOpcode(opcodeCandidate);
        var header = new PacketHeader(opcode, declaredLength, 0, opcodeCandidate);
        var confidence = knowledge?.Confidence ?? ProtocolConfidence.Unknown;
        return OperationResult<PacketEnvelope>.Success(new PacketEnvelope(header, frame[2..], confidence, knowledge));
    }

    private PacketKnowledge? Classify(ReadOnlySpan<byte> frame)
    {
        if (OfficialLoginEvidenceCatalog.TryIdentify(frame, out var knowledgeId) &&
            _protocolRegistry.TryGetPacket(knowledgeId, out var exactKnowledge))
        {
            return exactKnowledge;
        }

        foreach (var packet in _protocolRegistry.Packets)
        {
            foreach (var sample in packet.Samples)
            {
                if (HexEquals(frame, sample))
                {
                    return packet;
                }
            }

            foreach (var prefix in packet.FixedPrefixes)
            {
                if (HexStartsWith(frame, prefix))
                {
                    return packet;
                }
            }
        }

        return null;
    }

    private static bool HexEquals(ReadOnlySpan<byte> value, string hexadecimal)
    {
        if (hexadecimal.Length != value.Length * 2)
        {
            return false;
        }

        return HexStartsWith(value, hexadecimal);
    }

    private static bool HexStartsWith(ReadOnlySpan<byte> value, string hexadecimal)
    {
        if ((hexadecimal.Length & 1) != 0 || hexadecimal.Length / 2 > value.Length)
        {
            return false;
        }

        for (var index = 0; index < hexadecimal.Length / 2; index++)
        {
            var high = HexValue(hexadecimal[index * 2]);
            var low = HexValue(hexadecimal[(index * 2) + 1]);
            if (high < 0 || low < 0 || value[index] != (byte)((high << 4) | low))
            {
                return false;
            }
        }

        return true;
    }

    private static int HexValue(char value) =>
        value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'A' and <= 'F' => value - 'A' + 10,
            >= 'a' and <= 'f' => value - 'a' + 10,
            _ => -1
        };

    private static Opcode PacketFactoryOpcode(string opcodeCandidate)
    {
        if (opcodeCandidate.Length != 4 ||
            !ushort.TryParse(opcodeCandidate, System.Globalization.NumberStyles.HexNumber, null, out var value))
        {
            return new Opcode(0);
        }

        return new Opcode(value);
    }
}

public sealed class InMemoryPacketRouter : IPacketRouter
{
    private readonly PacketRegistry _registry;

    public InMemoryPacketRouter(PacketRegistry registry)
    {
        _registry = registry;
    }

    public void Register(IPacketHandler handler) => _registry.Register(handler);

    public async Task<OperationResult> RouteAsync(PacketEnvelope packet, CancellationToken cancellationToken)
    {
        if (!_registry.TryGet(packet.Header.Opcode, out var handler) || handler is null)
        {
            return OperationResult.Failure("packet.handler_missing", "No packet handler is registered for this opcode.", packet.Header.OpcodeCandidate);
        }

        return await handler.HandleAsync(packet, cancellationToken);
    }
}

public static class OfficialProtocolKnowledge
{
    public static ProtocolKnowledgeBase CreateDefault()
    {
        var heartbeatEvidence = Evidence("Artifacts/GameplayPacketRecovery/Latest/action-correlations/idle-baseline.json", ProtocolConfidence.Verified);
        var movementEvidence =
            new[]
            {
                Evidence("Artifacts/GameplayPacketRecovery/Latest/action-correlations/movement-step-1.json", ProtocolConfidence.Recovered),
                Evidence("Artifacts/GameplayPacketRecovery/Latest/action-correlations/movement-step-2.json", ProtocolConfidence.Recovered)
            };
        var loginEvidence = Evidence(
            "src/God2.ClassicServer.Protocol/Evidence/ProtocolEvidenceRecovery/source-manifest.json",
            ProtocolConfidence.Verified);
        var capture20260806Evidence = Evidence(
            "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.json",
            ProtocolConfidence.Inferred);
        var capture20260806Batch2Evidence = Evidence(
            "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/CapturedPacketEvidence.20260806.Batch2.json",
            ProtocolConfidence.Inferred);
        var evidencePackage20260806 = Evidence(
            OfficialEvidencePackage20260806Catalog.EvidencePath,
            ProtocolConfidence.Recovered);
        var pkLateJoinEvidence = Evidence(
            "Artifacts/ClientInstrumentation/OfficialEvidenceLauncher/PKLateJoinAnalysis-20260815/late-join-result.md",
            ProtocolConfidence.Recovered);

        return new ProtocolKnowledgeBase(
            "official-protocol-current-build-evidence-20260815-v4",
            [
                new PacketKnowledge(
                    OfficialLoginEvidenceCatalog.LoginRequestKnowledgeId,
                    "Login",
                    PacketDirection.ClientToServer,
                    208,
                    null,
                    ProtocolConfidence.Verified,
                    PacketRecoveryStatus.Verified,
                    Recovered: true,
                    Verified: true,
                    [loginEvidence],
                    [KnownLengthField(208)],
                    [UnknownField("opaqueLoginRequestPayload", 2, 206, "Packet role is verified; credential, command, sequence, nonce, checksum and encryption semantics remain unknown.")],
                    [],
                    []),
                new PacketKnowledge(
                    OfficialLoginEvidenceCatalog.LoginSuccessResponseKnowledgeId,
                    "Login",
                    PacketDirection.ServerToClient,
                    417,
                    null,
                    ProtocolConfidence.Verified,
                    PacketRecoveryStatus.Verified,
                    Recovered: true,
                    Verified: true,
                    [loginEvidence],
                    [KnownLengthField(417)],
                    [UnknownField("opaqueLoginSuccessAndCharacterBootstrapPayload", 2, 415, "Packet role is verified as successful Login plus Character bootstrap; result and character field semantics remain unknown.")],
                    [],
                    []),
                new PacketKnowledge(
                    "world-heartbeat-05007ACCEC",
                    "Heartbeat",
                    PacketDirection.ClientToServer,
                    5,
                    "7ACC",
                    ProtocolConfidence.Verified,
                    PacketRecoveryStatus.Verified,
                    Recovered: true,
                    Verified: true,
                    [heartbeatEvidence],
                    [KnownLengthField(5)],
                    [UnknownField("payload", 2, 3, "Encrypted/obfuscated heartbeat payload not decoded.")],
                    ["05007ACCEC"],
                    ["05007ACCEC"]),
                new PacketKnowledge(
                    "world-heartbeat-05003D09A5",
                    "Heartbeat",
                    PacketDirection.ClientToServer,
                    5,
                    "3D09",
                    ProtocolConfidence.Recovered,
                    PacketRecoveryStatus.Recovered,
                    Recovered: true,
                    Verified: false,
                    [heartbeatEvidence],
                    [KnownLengthField(5)],
                    [UnknownField("payload", 2, 3, "Alternate idle payload not decoded.")],
                    ["05003D09A5"],
                    ["05003D09A5"]),
                new PacketKnowledge(
                    "world-movement-client-10-byte-family",
                    "Movement",
                    PacketDirection.ClientToServer,
                    10,
                    "80BA",
                    ProtocolConfidence.Recovered,
                    PacketRecoveryStatus.Recovered,
                    Recovered: true,
                    Verified: false,
                    movementEvidence,
                    [
                        KnownLengthField(10),
                        new("familyPrefixCandidate", 2, 3, "bytes", PacketRecoveryStatus.Recovered, "Fixed across isolated movement clicks: 80BAD7.")
                    ],
                    [
                        UnknownField("opcodeCandidate", 2, 2, "Opcode candidate only; encryption/obfuscation not decoded."),
                        UnknownField("movementPayload", 5, 5, "Likely movement state; coordinates/direction/sequence are not proven.")
                    ],
                    ["0A0080BAD7C34DA69488", "0A0080BAD7C44EA79586"],
                    ["0A0080BAD7"]),
                new PacketKnowledge(
                    "world-npc-interaction-client-8-byte-candidate",
                    "NPC",
                    PacketDirection.ClientToServer,
                    8,
                    "7758",
                    ProtocolConfidence.Inferred,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [
                        Evidence("Artifacts/GameplayPacketRecovery/Latest/action-correlations/npc-interact-1.json", ProtocolConfidence.Inferred),
                        Evidence("Artifacts/GameplayPacketRecovery/Latest/action-correlations/npc-interact-2.json", ProtocolConfidence.Inferred)
                    ],
                    [KnownLengthField(8)],
                    [UnknownField("npcIdOrObjectId", 2, 6, "Same NPC produced repeat frame; field location not decoded.")],
                    ["0800775884CB3F09"],
                    ["0800775884CB3F09"]),
                new PacketKnowledge(
                    "world-merchant-interaction-client-8-byte-candidate",
                    "Merchant",
                    PacketDirection.ClientToServer,
                    8,
                    "77B5",
                    ProtocolConfidence.Inferred,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [Evidence("Artifacts/GameplayPacketRecovery/Latest/action-correlations/shop-open-2.json", ProtocolConfidence.Inferred)],
                    [KnownLengthField(8)],
                    [UnknownField("merchantOrShopPayload", 2, 6, "Merchant id/menu/shop fields not decoded.")],
                    ["080077B5F3D73FB8"],
                    ["080077B5F3D73FB8"]),
                new PacketKnowledge(
                    "world-logout-client-5-byte-candidate",
                    "Logout",
                    PacketDirection.ClientToServer,
                    5,
                    "AC9D",
                    ProtocolConfidence.Inferred,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [Evidence("Artifacts/GameplayPacketRecovery/Latest/action-correlations/logout-1.json", ProtocolConfidence.Inferred)],
                    [KnownLengthField(5)],
                    [UnknownField("logoutPayload", 2, 3, "Logout mode field is not decoded.")],
                    ["0500AC9D30"],
                    ["0500AC9D30"]),
                new PacketKnowledge(
                    "evidence-package-20260806-c2s-opcode-2e-10",
                    "DecodedOpcode",
                    PacketDirection.ClientToServer,
                    10,
                    "2E",
                    ProtocolConfidence.Recovered,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [evidencePackage20260806],
                    [KnownLengthField(10), RecoveredOpcodeByteField(0x2E, 367)],
                    [UnknownField("decodedPayload", 3, 7, "Opcode 0x2E is structurally recovered from PreEncrypt evidence; gameplay fields and handler semantics remain unknown.")],
                    [],
                    ["0A002E"]),
                new PacketKnowledge(
                    "evidence-package-20260806-c2s-opcode-30-5",
                    "DecodedOpcode",
                    PacketDirection.ClientToServer,
                    5,
                    "30",
                    ProtocolConfidence.Recovered,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [evidencePackage20260806],
                    [KnownLengthField(5), RecoveredOpcodeByteField(0x30, 136)],
                    [UnknownField("decodedPayload", 3, 2, "The byte-identical 0x30 family repeated 136 times; heartbeat or gameplay semantics are not assumed.")],
                    [],
                    ["050030"]),
                new PacketKnowledge(
                    "evidence-package-20260806-c2s-opcode-35-20",
                    "DecodedOpcode",
                    PacketDirection.ClientToServer,
                    20,
                    "35",
                    ProtocolConfidence.Recovered,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [evidencePackage20260806],
                    [KnownLengthField(20), RecoveredOpcodeByteField(0x35, 112)],
                    [UnknownField("decodedPayload", 3, 17, "Opcode 0x35 has 112 observations and 46 distinct payloads; movement, battle, skill and inventory semantics remain unknown.")],
                    [],
                    ["140035"]),
                new PacketKnowledge(
                    "evidence-package-20260806-c2s-opcode-6d-5",
                    "DecodedOpcode",
                    PacketDirection.ClientToServer,
                    5,
                    "6D",
                    ProtocolConfidence.Recovered,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [evidencePackage20260806],
                    [KnownLengthField(5), RecoveredOpcodeByteField(0x6D, 33)],
                    [UnknownField("decodedPayload", 3, 2, "The byte-identical 0x6D family repeated 33 times; gameplay semantics remain unknown.")],
                    [],
                    ["05006D"]),
                new PacketKnowledge(
                    "evidence-package-20260806-c2s-opcode-36-12",
                    "DecodedOpcode",
                    PacketDirection.ClientToServer,
                    12,
                    "36",
                    ProtocolConfidence.Recovered,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [evidencePackage20260806],
                    [KnownLengthField(12), RecoveredOpcodeByteField(0x36, 33)],
                    [UnknownField("decodedPayload", 3, 9, "Opcode 0x36 repeated 33 times across three payloads; acknowledgement and gameplay semantics remain unknown.")],
                    [],
                    ["0C0036"]),
                new PacketKnowledge(
                    "official-20260815-c2s-battle-assistance-join-6a-12",
                    "Battle",
                    PacketDirection.ClientToServer,
                    OfficialBattleAssistanceJoinWireCodec.DecodedFrameLength,
                    "6A",
                    ProtocolConfidence.Recovered,
                    PacketRecoveryStatus.Recovered,
                    Recovered: true,
                    Verified: false,
                    [pkLateJoinEvidence],
                    [
                        KnownLengthField(OfficialBattleAssistanceJoinWireCodec.DecodedFrameLength),
                        new("decodedOpcodeByte", 2, 1, "uint8", PacketRecoveryStatus.Verified, "Controlled current-build trace observed opcode 0x6A immediately before the three-client battle join."),
                        new("joiningEntityId", 3, 4, "uint32le", PacketRecoveryStatus.Recovered, "Value 254 binds to Kero's actor snapshot and the newly added roster descriptor."),
                        new("battleAnchorEntityId", 7, 4, "uint32le", PacketRecoveryStatus.Recovered, "Value 242 binds to Xia's actor snapshot in the already active battle."),
                        new("checksum", 11, 1, "uint8", PacketRecoveryStatus.Verified, "Matches the current-build decoded-frame checksum boundary.")
                    ],
                    [],
                    ["0C006AFE000000F2000000FA"],
                    ["0C006A"]),
                new PacketKnowledge(
                    "evidence-package-20260806-c2s-opcode-66-7",
                    "DecodedOpcode",
                    PacketDirection.ClientToServer,
                    7,
                    "66",
                    ProtocolConfidence.Recovered,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [evidencePackage20260806],
                    [KnownLengthField(7), RecoveredOpcodeByteField(0x66, 17)],
                    [UnknownField("decodedPayload", 3, 4, "Opcode 0x66 repeated 17 times across 14 payloads; gameplay semantics remain unknown.")],
                    [],
                    ["070066"]),
                new PacketKnowledge(
                    "capture-20260806-login-handshake-c2s-208-candidate",
                    "Login",
                    PacketDirection.ClientToServer,
                    208,
                    null,
                    ProtocolConfidence.Inferred,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [capture20260806Evidence, capture20260806Batch2Evidence],
                    [KnownLengthField(208)],
                    [UnknownField("encryptedOrObfuscatedLoginHandshakePayload", 2, 206, "Observed in two x86 God2_opt.exe Winsock capture sessions as 208-byte ClientToServer candidate frames; opcode and field semantics vary and remain unknown.")],
                    [],
                    ["D00094EDF9D04733", "D00075CC9C8CA49F", "D00002183B24DED0", "D0007E5BD4572F8A"]),
                new PacketKnowledge(
                    "capture-20260806-client-keepalive-c2s-5-candidate",
                    "Heartbeat",
                    PacketDirection.ClientToServer,
                    5,
                    null,
                    ProtocolConfidence.Inferred,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [capture20260806Evidence, capture20260806Batch2Evidence],
                    [KnownLengthField(5)],
                    [UnknownField("keepAliveOrTickPayload", 2, 3, "High-frequency 5-byte ClientToServer candidates observed 284 times across two sessions; encrypted/obfuscated payload semantics remain unknown.")],
                    ["0500452B7B", "0500926836", "0500481E8D", "050012405B", "05005D0316", "05008FCE66"],
                    []),
                new PacketKnowledge(
                    "capture-20260806-client-action-c2s-20-candidate",
                    "Movement",
                    PacketDirection.ClientToServer,
                    20,
                    null,
                    ProtocolConfidence.Inferred,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [capture20260806Evidence, capture20260806Batch2Evidence],
                    [KnownLengthField(20)],
                    [UnknownField("clientActionOrMovementPayload", 2, 18, "Repeated 20-byte ClientToServer movement/action candidates were observed, but coordinate, direction, mount, skill, and sequence fields are not proven.")],
                    [
                        "14004A314E713F59075F64DE02F0874D408536D8",
                        "14004A2661664059075F64DE02F087737675362A",
                        "140092D94A100808DE07FD78E530080A45BB0671"
                    ],
                    ["140092D94A100808"]),
                new PacketKnowledge(
                    "capture-20260806-server-world-delta-s2c-20-candidate",
                    "WorldState",
                    PacketDirection.ServerToClient,
                    20,
                    null,
                    ProtocolConfidence.Inferred,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [capture20260806Evidence, capture20260806Batch2Evidence],
                    [KnownLengthField(20)],
                    [UnknownField("serverWorldStateDeltaPayload", 2, 18, "Repeated 20-byte ServerToClient world-state candidates were observed, but entity/component field ownership is not proven.")],
                    [
                        "1400FB805A673F59073F43DF02F0878545C60CDB",
                        "14009675B4C2EE07CF710D59068EB0139EC3ECCF",
                        "140045254A0B0808DE07FE79E53008D25090DC58"
                    ],
                    ["140045254A0B0808"]),
                new PacketKnowledge(
                    "capture-20260806-server-world-delta-s2c-14-candidate",
                    "WorldState",
                    PacketDirection.ServerToClient,
                    14,
                    null,
                    ProtocolConfidence.Inferred,
                    PacketRecoveryStatus.EvidenceOnly,
                    Recovered: true,
                    Verified: false,
                    [capture20260806Evidence, capture20260806Batch2Evidence],
                    [KnownLengthField(14)],
                    [UnknownField("serverWorldStateDeltaPayload", 2, 12, "Repeated 14-byte ServerToClient world-state candidates were observed, but NPC, monster, player, pet, immortal, and mount semantics remain unverified.")],
                    [
                        "0E004B3158663F48E76410DE02FC",
                        "0E004624B2C1EE1A1F77E3580681",
                        "0E005B0939888A78DAA5873EEAEC"
                    ],
                    ["0E005B0939888A78"])
            ],
            [
                "CharacterDelete payload field decoder and accepted response serializer",
                "CharacterCreate payload field decoder and accepted response serializer",
                "CharacterRename raw request/response",
                "Portal/MapTransfer request field decoder and accepted response serializer",
                "Merchant successful-buy differential plus result field decoder and serializer",
                "InventoryMove raw request/response",
                "QuestAccept/QuestComplete per-action semantic markers and field decoder",
                "Combat action-specific command mapping and accepted response serializers",
                "DropItem and PickUpItem raw request/response"
            ],
            "Visual official-client success is evidence, but only repeated raw frame evidence or source-backed frozen startup evidence may promote an opcode to Verified.");
    }

    private static ProtocolEvidenceSource Evidence(string path, ProtocolConfidence confidence) => new(path, "OfficialRecoveryEvidence", confidence);

    private static ProtocolField KnownLengthField(int length) => new("frameLength", 0, 2, "uint16le", PacketRecoveryStatus.Verified, $"Little-endian frame length equals {length}.");

    private static ProtocolField RecoveredOpcodeByteField(byte opcode, int observations) =>
        new("decodedOpcodeByte", 2, 1, "uint8", PacketRecoveryStatus.Recovered,
            $"PreEncrypt capture observed decoded opcode byte 0x{opcode:X2} in {observations} structurally valid frames; semantic role is not assigned.");

    private static ProtocolField UnknownField(string name, int offset, int length, string note) => new(name, offset, length, "unknown", PacketRecoveryStatus.NeedsRecovery, note);
}
