using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialBattleVitalSnapshotWireResultCode
{
    LayoutDecoded,
    LayoutEncoded,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    InvalidPositionCount,
    InvalidBattlePosition,
    SemanticEvidenceBlocked
}

public sealed record OfficialBattleVitalSnapshotWireResult<T>(
    OfficialBattleVitalSnapshotWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool LayoutDecoded => Code == OfficialBattleVitalSnapshotWireResultCode.LayoutDecoded;
    public bool LayoutEncoded => Code == OfficialBattleVitalSnapshotWireResultCode.LayoutEncoded;
    public bool Succeeded => LayoutDecoded || LayoutEncoded;

    public static OfficialBattleVitalSnapshotWireResult<T> Success(T value) =>
        new(OfficialBattleVitalSnapshotWireResultCode.LayoutDecoded, value, string.Empty);

    public static OfficialBattleVitalSnapshotWireResult<T> Encoded(T value) =>
        new(OfficialBattleVitalSnapshotWireResultCode.LayoutEncoded, value, string.Empty);

    public static OfficialBattleVitalSnapshotWireResult<T> Failure(
        OfficialBattleVitalSnapshotWireResultCode code,
        string failureCode) =>
        new(code, default, failureCode);
}

public sealed record OfficialBattleCurrentVitals(
    byte BattlePosition,
    int CurrentHitPoints,
    int CurrentMagicPoints);

public sealed record OfficialBattleVitalSnapshotLayout(
    int RoundIndex,
    IReadOnlyList<OfficialBattleCurrentVitals> Positions,
    uint RawTrailer);

public sealed record OfficialBattleVitalSnapshotCandidate(
    int RoundIndex,
    IReadOnlyList<OfficialBattleCurrentVitals> Positions,
    uint OpaqueTrailer,
    string RecordSha256,
    string FieldLayoutEvidence,
    string HitPointMagicPointConsumerEvidence,
    string SemanticEvidenceStatus,
    bool RuntimeMutationAllowed)
{
    public byte ConsumedTrailerByte118 => (byte)(OpaqueTrailer >> 8);
    public string TrailerEvidenceStatus =>
        "RawBytesPreserved_Offset118ClientConsumerVerified_SemanticMeaningBlocked";
}

/// <summary>
/// Exact-build layout decoder for the fixed 121-byte S2C 0x88 battle snapshot.
/// The official client reads 14 pairs at record offsets 5+8*n and 9+8*n.
/// For the locally selected actor it combines those two current values with
/// actor+0xDF0 and actor+0xDF4, then the UI clamps current to maximum and renders
/// current/maximum percentages and "%d/%d" text. Controlled official-client
/// skill actions then bind the second value to MP by exact repeated cost-five
/// transitions; the CHARHPMP client domain and the complementary damage bar bind
/// the first value to HP.
/// </summary>
public static class OfficialBattleVitalSnapshotWireCodec
{
    public const string ClientBuildId = OfficialBattleCommandWireCodec.ClientBuildId;
    public const string ClientSha256 = OfficialBattleCommandWireCodec.ClientSha256;
    public const string FieldLayoutEvidence =
        "God2_opt+rva-0x0014BC60+0x0014BD0D+0x0014BD37-0x0014BDA8+0x00160450-0x001604D1;" +
        "live-basic-formula-20260811-1622+live-actor-mutation-20260811-172703;" +
        "FS2TW-public-beta-0x88/121-consumer_004F7A30-cross-version-corroboration";
    public const string HitPointMagicPointConsumerEvidence =
        "God2_opt+rva-0x001177C0-0x0011783B+0x000D98F0-0x000D9A67+0x003F0F18;" +
        "live-actor-mutation-20260811-172703:skill-mp5-sequences-2809/2920/3027-to-2867/2974/3049";
    public const byte SnapshotOpcode = 0x88;
    public const int RecordLength = 121;
    public const int PositionCount = 14;
    public const int PositionStride = 8;
    public const int PositionsOffset = 5;
    public const int TrailerOffset = 117;
    public const bool RuntimeMutationEnabled = false;

    public static OfficialBattleVitalSnapshotWireResult<OfficialBattleVitalSnapshotCandidate> DecodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialBattleVitalSnapshotWireResult<OfficialBattleVitalSnapshotCandidate>.Failure(
                OfficialBattleVitalSnapshotWireResultCode.BuildMismatch,
                "wire.battle.vital_snapshot_client_build_mismatch");
        }
        if (state != GameplayProtocolState.Battle)
        {
            return OfficialBattleVitalSnapshotWireResult<OfficialBattleVitalSnapshotCandidate>.Failure(
                OfficialBattleVitalSnapshotWireResultCode.InvalidState,
                "wire.battle.vital_snapshot_state_invalid");
        }
        if (record.Length != RecordLength)
        {
            return OfficialBattleVitalSnapshotWireResult<OfficialBattleVitalSnapshotCandidate>.Failure(
                OfficialBattleVitalSnapshotWireResultCode.InvalidLength,
                "wire.battle.vital_snapshot_length_invalid");
        }
        if (record[0] != SnapshotOpcode)
        {
            return OfficialBattleVitalSnapshotWireResult<OfficialBattleVitalSnapshotCandidate>.Failure(
                OfficialBattleVitalSnapshotWireResultCode.InvalidOpcode,
                "wire.battle.vital_snapshot_opcode_invalid");
        }

        var positions = new OfficialBattleCurrentVitals[PositionCount];
        for (byte position = 0; position < PositionCount; position++)
        {
            var offset = PositionsOffset + position * PositionStride;
            positions[position] = new OfficialBattleCurrentVitals(
                position,
                BinaryPrimitives.ReadInt32LittleEndian(record[offset..]),
                BinaryPrimitives.ReadInt32LittleEndian(record[(offset + sizeof(int))..]));
        }

        return OfficialBattleVitalSnapshotWireResult<OfficialBattleVitalSnapshotCandidate>.Success(
            new OfficialBattleVitalSnapshotCandidate(
                BinaryPrimitives.ReadInt32LittleEndian(record[1..]),
                positions,
                BinaryPrimitives.ReadUInt32LittleEndian(record[TrailerOffset..]),
                Convert.ToHexString(SHA256.HashData(record)),
                FieldLayoutEvidence,
                HitPointMagicPointConsumerEvidence,
                "HitPointsMagicPointsConsumerVerified_TrailerByte118ConsumerVerifiedMeaningBlocked",
                RuntimeMutationAllowed: false));
    }

    /// <summary>
    /// Reproduces the exact fixed record for offline evidence replay. The four trailer
    /// bytes are preserved as raw captured data: only byte 118 has a verified Client
    /// consumer, and its gameplay meaning and authoritative server source remain unknown.
    /// </summary>
    public static OfficialBattleVitalSnapshotWireResult<byte[]> EncodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialBattleVitalSnapshotLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(layout.Positions);

        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialBattleVitalSnapshotWireResult<byte[]>.Failure(
                OfficialBattleVitalSnapshotWireResultCode.BuildMismatch,
                "wire.battle.vital_snapshot_client_build_mismatch");
        }
        if (state != GameplayProtocolState.Battle)
        {
            return OfficialBattleVitalSnapshotWireResult<byte[]>.Failure(
                OfficialBattleVitalSnapshotWireResultCode.InvalidState,
                "wire.battle.vital_snapshot_state_invalid");
        }
        if (layout.Positions.Count != PositionCount)
        {
            return OfficialBattleVitalSnapshotWireResult<byte[]>.Failure(
                OfficialBattleVitalSnapshotWireResultCode.InvalidPositionCount,
                "wire.battle.vital_snapshot_position_count_invalid");
        }

        var record = new byte[RecordLength];
        record[0] = SnapshotOpcode;
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(1), layout.RoundIndex);
        for (byte position = 0; position < PositionCount; position++)
        {
            var current = layout.Positions[position];
            if (current.BattlePosition != position)
            {
                return OfficialBattleVitalSnapshotWireResult<byte[]>.Failure(
                    OfficialBattleVitalSnapshotWireResultCode.InvalidBattlePosition,
                    "wire.battle.vital_snapshot_position_order_invalid");
            }
            var offset = PositionsOffset + position * PositionStride;
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(offset), current.CurrentHitPoints);
            BinaryPrimitives.WriteInt32LittleEndian(
                record.AsSpan(offset + sizeof(int)),
                current.CurrentMagicPoints);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(TrailerOffset), layout.RawTrailer);
        return OfficialBattleVitalSnapshotWireResult<byte[]>.Encoded(record);
    }

    public static OfficialBattleVitalSnapshotWireResult<byte[]> EncodeForRuntime(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialBattleVitalSnapshotLayout layout)
    {
        var encoded = EncodeLayout(clientBuildId, state, layout);
        if (!encoded.LayoutEncoded)
        {
            return encoded;
        }
        return OfficialBattleVitalSnapshotWireResult<byte[]>.Failure(
            OfficialBattleVitalSnapshotWireResultCode.SemanticEvidenceBlocked,
            "wire.battle.vital_snapshot_server_serializer_evidence_blocked");
    }

    public static OfficialBattleVitalSnapshotWireResult<OfficialBattleVitalSnapshotCandidate> DecodeForRuntime(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record)
    {
        var layout = DecodeLayout(clientBuildId, state, record);
        if (!layout.LayoutDecoded)
        {
            return layout;
        }
        return OfficialBattleVitalSnapshotWireResult<OfficialBattleVitalSnapshotCandidate>.Failure(
            OfficialBattleVitalSnapshotWireResultCode.SemanticEvidenceBlocked,
            "wire.battle.vital_snapshot_server_serializer_evidence_blocked");
    }
}
