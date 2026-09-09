namespace God2.ClassicServer.Runtime;

public enum SerializerEvidenceStatus
{
    Verified,
    PartiallyVerified,
    BlockedByEvidence,
    NotRequiredThisSprint
}

public enum RuntimeSerializerKind
{
    Player,
    Npc,
    Monster,
    Portal,
    Merchant,
    Heartbeat,
    Movement
}

public sealed record RuntimeSerializationResult(
    RuntimeSerializerKind Serializer,
    OfficialSerializerStatus Status,
    byte[] Frame,
    string Reason)
{
    public static RuntimeSerializationResult Success(RuntimeSerializerKind serializer, byte[] frame, string reason)
    {
        if (frame.Length == 0)
        {
            throw new ArgumentException("Verified serializer output must not be empty.", nameof(frame));
        }

        return new RuntimeSerializationResult(serializer, OfficialSerializerStatus.Ready, frame.ToArray(), reason);
    }

    public static RuntimeSerializationResult Blocked(RuntimeSerializerKind serializer, string reason) =>
        new(serializer, OfficialSerializerStatus.SerializerBlockedByEvidence, [], reason);
}

public sealed record RuntimeSerializerStatusSnapshot(
    string Serializer,
    OfficialSerializerStatus Status,
    string Reason,
    string MessageType = "",
    string Opcode = "Unknown",
    int? ExpectedLength = null,
    IReadOnlyList<string>? VerifiedFields = null,
    IReadOnlyList<string>? UnknownFields = null,
    string GoldenArtifact = "",
    string Source = "",
    string Confidence = "",
    SerializerEvidenceStatus CurrentStatus = SerializerEvidenceStatus.BlockedByEvidence);

public interface IRuntimeStateSerializer<in TState>
    where TState : IRuntimeState
{
    RuntimeSerializerKind Serializer { get; }

    RuntimeSerializationResult Serialize(TState state);
}

public abstract class EvidenceBlockedStateSerializer<TState> : IRuntimeStateSerializer<TState>
    where TState : IRuntimeState
{
    protected EvidenceBlockedStateSerializer(RuntimeSerializerKind serializer, string blockedReason)
    {
        Serializer = serializer;
        BlockedReason = blockedReason;
    }

    public RuntimeSerializerKind Serializer { get; }

    public string BlockedReason { get; }

    public RuntimeSerializationResult Serialize(TState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return RuntimeSerializationResult.Blocked(Serializer, BlockedReason);
    }
}

public sealed class PlayerSerializer : IRuntimeStateSerializer<PlayerState>
{
    public RuntimeSerializerKind Serializer => RuntimeSerializerKind.Player;

    public RuntimeSerializationResult Serialize(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            return RuntimeSerializationResult.Success(
                Serializer,
                OfficialClientWorldProtocolFrames.BuildPlayerSpawnFrame128(state.CharacterId, state.Name),
                "PlayerSpawnS2C128 uses verified dynamic character id/name fields and preserves the remaining build-locked opaque region.");
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return RuntimeSerializationResult.Blocked(
                Serializer,
                $"Player state cannot be represented by the verified PlayerSpawnS2C128 identity fields: {exception.GetType().Name}.");
        }
    }
}

public sealed class NpcSerializer : IRuntimeStateSerializer<NpcState>
{
    public RuntimeSerializerKind Serializer => RuntimeSerializerKind.Npc;

    public RuntimeSerializationResult Serialize(NpcState state) =>
        OfficialNpcReplicationWireCodec.SerializeSpawn(state);
}

public sealed class NpcPositionUpdateSerializer : IRuntimeStateSerializer<NpcState>
{
    public RuntimeSerializerKind Serializer => RuntimeSerializerKind.Npc;

    public RuntimeSerializationResult Serialize(NpcState state) =>
        OfficialNpcReplicationWireCodec.SerializePositionUpdate(state);
}

public sealed class NpcDespawnSerializer : IRuntimeStateSerializer<NpcState>
{
    public RuntimeSerializerKind Serializer => RuntimeSerializerKind.Npc;

    public RuntimeSerializationResult Serialize(NpcState state) =>
        OfficialNpcReplicationWireCodec.SerializeDespawn(state);
}

public sealed class MonsterSerializer : EvidenceBlockedStateSerializer<MonsterState>
{
    public MonsterSerializer()
        : base(
            RuntimeSerializerKind.Monster,
            "Monster serializer is blocked until official server-to-client monster spawn/update/despawn evidence exists.")
    {
    }
}

public sealed class PortalSerializer : EvidenceBlockedStateSerializer<PortalState>
{
    public PortalSerializer()
        : base(
            RuntimeSerializerKind.Portal,
            "Portal serializer is blocked until official portal visibility and transfer packet evidence exists.")
    {
    }
}

public sealed class MerchantSerializer : EvidenceBlockedStateSerializer<MerchantState>
{
    public MerchantSerializer()
        : base(
            RuntimeSerializerKind.Merchant,
            "Merchant serializer is blocked until official merchant open/list/update server-to-client evidence exists.")
    {
    }
}

public sealed record HeartbeatState(DateTimeOffset ObservedAtUtc, int Sequence) : IRuntimeState;

public sealed class HeartbeatSerializer : EvidenceBlockedStateSerializer<HeartbeatState>
{
    public HeartbeatSerializer()
        : base(
            RuntimeSerializerKind.Heartbeat,
            "Heartbeat keepalive classification is recovered, but no runtime-driven server-to-client heartbeat serializer is required by current evidence.")
    {
    }
}

public sealed record MovementState(
    long RuntimeObjectId,
    int MapId,
    WorldPosition3 Position,
    WorldDirection Direction,
    string MovementMode) : IMapPositionedRuntimeState;

public sealed class MovementSerializer : EvidenceBlockedStateSerializer<MovementState>
{
    public MovementSerializer()
        : base(
            RuntimeSerializerKind.Movement,
            "Movement serializer is blocked until official walking, coordinate scale, and mount-state evidence are complete.")
    {
    }
}

public static class RuntimeSerializerCatalog
{
    public static IReadOnlyList<RuntimeSerializerStatusSnapshot> CurrentStatus { get; } =
    [
        new(
            RuntimeSerializerKind.Player.ToString(),
            OfficialSerializerStatus.Ready,
            "Frozen PlayerSpawnS2C128 is byte-backed for current golden player; dynamic player fields remain partially verified.",
            "PlayerSpawn",
            "0x1F",
            128,
            ["length", "opcode", "characterName", "characterIdCandidate", "appearanceModeCandidate", "stateFlagsCandidate"],
            ["appearanceEquipmentMapHpMpStateReserved", "dynamic entity field semantics"],
            "OfficialClientWorldProtocolFrames.BuildPlayerSpawnFrame128",
            "Frozen world bootstrap",
            "High for frozen bytes; Medium for field semantics",
            SerializerEvidenceStatus.PartiallyVerified),
        new(
            RuntimeSerializerKind.Npc.ToString(),
            OfficialSerializerStatus.Ready,
            "NPC spawn is raw-message pinned for two unique MariaDB identities; update and despawn consumers are statically verified for the same build. Other NPC identities fail closed.",
            "NpcSpawn/PositionUpdate/Despawn",
            "0x72/0x60/0x75",
            24,
            ["entityHandle", "resourceType", "resourceOrdinal", "selectorHighBits", "directionCode", "stateCode", "packedX", "packedY", "interpolation", "opaqueTemplate"],
            ["additional NPC resource selectors", "monster selector family", "raw 0x75 golden"],
            "Artifacts/RoadMap/M3/npc-wire-evidence.json",
            "Current official Client static receive branches plus frozen world bootstrap application records",
            "High for two NPC spawn profiles; Medium for statically derived update/despawn",
            SerializerEvidenceStatus.PartiallyVerified),
        Snapshot(new MonsterSerializer(), "MonsterSpawn", "Unknown", null, "VerifiedPacketCatalog.json", "No monster S2C raw spawn evidence in current catalog"),
        Snapshot(new PortalSerializer(), "PortalStaticObject", "Unknown", null, "WorldProtocolRecovery.Phase4.NpcWorldRecovery.json", "Portal transfer/spawn S2C layout not recovered"),
        Snapshot(new MerchantSerializer(), "MerchantState", "Unknown", null, "VerifiedPacketCatalog.json", "Merchant open/list/update S2C evidence missing"),
        new(
            RuntimeSerializerKind.Heartbeat.ToString(),
            OfficialSerializerStatus.SerializerBlockedByEvidence,
            "Heartbeat is verified as client-to-server keepalive and accepted at protocol boundary; server-to-client runtime serializer is not required this sprint.",
            "Heartbeat",
            "7ACC/3D09 candidates",
            5,
            ["length"],
            ["payload"],
            "VerifiedRaw/world-heartbeat-05007ACCEC.bin",
            "VerifiedPacketCatalog.json",
            "High",
            SerializerEvidenceStatus.NotRequiredThisSprint),
        Snapshot(new MovementSerializer(), "Movement", "Unknown", 10, "VerifiedPacketCatalog.json", "Movement is C2S-classified; walking/coordinate/mount evidence is deferred")
    ];

    private static RuntimeSerializerStatusSnapshot Snapshot<TState>(
        EvidenceBlockedStateSerializer<TState> serializer,
        string messageType,
        string opcode,
        int? expectedLength,
        string goldenArtifact,
        string source)
        where TState : IRuntimeState =>
        new(
            serializer.Serializer.ToString(),
            OfficialSerializerStatus.SerializerBlockedByEvidence,
            serializer.BlockedReason,
            messageType,
            opcode,
            expectedLength,
            ["none"],
            ["all required runtime-to-protocol fields"],
            goldenArtifact,
            source,
            "None for S2C spawn bytes",
            SerializerEvidenceStatus.BlockedByEvidence);
}
