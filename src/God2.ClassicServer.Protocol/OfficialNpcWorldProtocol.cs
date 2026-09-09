using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Protocol;

public enum OfficialWorldEntityType
{
    Unknown,
    Player,
    NPC,
    Monster
}

public enum OfficialNpcPacketKind
{
    Spawn,
    Update,
    Despawn,
    Interaction
}

public sealed record OfficialWorldEntityCatalogEntry(
    OfficialWorldEntityType EntityType,
    string PacketFamily,
    PacketRecoveryStatus RecoveryStatus,
    string Evidence);

public sealed record OfficialNpcInteractionCandidate(
    string EncodedHex,
    string OpcodeCandidate,
    int Length,
    string EvidenceStatus);

public sealed class EvidenceGatedOfficialNpcWorldProtocol
{
    public const string NpcInteractionKnowledgeId = "world-npc-interaction-client-8-byte-candidate";
    public const string NpcS2CEvidenceRequiredCode = "protocol.npc_s2c_evidence_required";

    public IReadOnlyList<OfficialWorldEntityCatalogEntry> EntityCatalog { get; } =
    [
        new(OfficialWorldEntityType.Player, "PlayerSpawn", PacketRecoveryStatus.Recovered, "PlayerSpawnS2C128 from frozen world bootstrap"),
        new(OfficialWorldEntityType.NPC, "NPC", PacketRecoveryStatus.NeedsRecovery, "No NPC Spawn/Update/Despawn S2C raw packet recovered"),
        new(OfficialWorldEntityType.Monster, "Monster", PacketRecoveryStatus.NeedsRecovery, "Deferred; out of Phase 4 scope"),
        new(OfficialWorldEntityType.Unknown, "Unknown", PacketRecoveryStatus.NeedsRecovery, "Fallback for unclassified entity packets")
    ];

    public OfficialNpcInteractionCandidate GetKnownInteractionCandidate() =>
        new("0800775884CB3F09", "7758", 8, "C2S evidence-only; purpose and fields not decoded");

    public OperationResult<ReadOnlyMemory<byte>> BuildNpcSpawn()
    {
        return MissingS2CEvidence(OfficialNpcPacketKind.Spawn);
    }

    public OperationResult<ReadOnlyMemory<byte>> BuildNpcUpdate()
    {
        return MissingS2CEvidence(OfficialNpcPacketKind.Update);
    }

    public OperationResult<ReadOnlyMemory<byte>> BuildNpcDespawn()
    {
        return MissingS2CEvidence(OfficialNpcPacketKind.Despawn);
    }

    private static OperationResult<ReadOnlyMemory<byte>> MissingS2CEvidence(OfficialNpcPacketKind packetKind) =>
        OperationResult<ReadOnlyMemory<byte>>.Failure(
            NpcS2CEvidenceRequiredCode,
            $"Official NPC {packetKind} S2C raw packet evidence is required before a builder can emit bytes.",
            "OfficialNpcWorldProtocol");
}
