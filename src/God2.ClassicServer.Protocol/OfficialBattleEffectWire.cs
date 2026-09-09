using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialBattleEffectWireResultCode
{
    LayoutDecoded,
    LayoutEncoded,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    InvalidSourceBattlePosition,
    SemanticEvidenceBlocked
}

public sealed record OfficialBattleEffectWireResult<T>(
    OfficialBattleEffectWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool LayoutDecoded => Code == OfficialBattleEffectWireResultCode.LayoutDecoded;
    public bool LayoutEncoded => Code == OfficialBattleEffectWireResultCode.LayoutEncoded;
    public bool Succeeded => LayoutDecoded || LayoutEncoded;

    public static OfficialBattleEffectWireResult<T> Success(T value) =>
        new(OfficialBattleEffectWireResultCode.LayoutDecoded, value, string.Empty);

    public static OfficialBattleEffectWireResult<T> Encoded(T value) =>
        new(OfficialBattleEffectWireResultCode.LayoutEncoded, value, string.Empty);

    public static OfficialBattleEffectWireResult<T> Failure(
        OfficialBattleEffectWireResultCode code,
        string failureCode) =>
        new(code, default, failureCode);
}

public sealed record OfficialBattleEffectLayout(
    byte EffectKind,
    byte SourceBattlePosition,
    byte PlaybackGate,
    ushort FriendlyTargetMask,
    ushort EnemyTargetMask,
    byte ReservedByte8,
    short SignedResult,
    ushort AuxiliaryValue0,
    ushort AuxiliaryValue1);

public sealed record OfficialBattleEffectCandidate(
    byte EffectKind,
    byte SourceBattlePosition,
    byte SourceSide,
    byte SourceSlot,
    byte PlaybackGate,
    ushort FriendlyTargetMask,
    ushort EnemyTargetMask,
    byte ReservedByte8,
    IReadOnlyList<int> TargetBattlePositions,
    short SignedResult,
    ushort AuxiliaryValue0,
    ushort AuxiliaryValue1,
    string RecordSha256,
    string FieldLayoutEvidence,
    string SemanticEvidenceStatus,
    bool RuntimeMutationAllowed);

/// <summary>
/// Exact-build decoder for the official fixed 15-byte S2C 0x83 Battle effect record.
/// The field boundaries are verified by static record-length recovery and live handler
/// replay. Exact-build consumers also verify that record bytes 9..10 flow through signed
/// action-delta slots into the clamped HP/MP mutators. Runtime output remains disabled:
/// a client result consumer does not prove the authoritative server formula or the complete
/// server-to-client serializer and ordering contract.
/// </summary>
public static class OfficialBattleEffectWireCodec
{
    public const string ClientBuildId = OfficialBattleCommandWireCodec.ClientBuildId;
    public const string ClientSha256 = OfficialBattleCommandWireCodec.ClientSha256;
    public const string FieldLayoutEvidence =
        "God2_opt+rva-0x0014AD98+0x00155550+0x00160D20+0x00163240+0x0015F1AB+0x001566A0;BattleActorStateRoutingEvidence.20260813;" +
        "FS2TW-public-beta-0x83-action-source-prefix-only-target-mask-offsets-changed";
    public const byte EffectOpcode = 0x83;
    public const int RecordLength = 15;
    public const bool RuntimeMutationEnabled = false;

    public static OfficialBattleEffectWireResult<OfficialBattleEffectCandidate> DecodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialBattleEffectWireResult<OfficialBattleEffectCandidate>.Failure(
                OfficialBattleEffectWireResultCode.BuildMismatch,
                "wire.battle.effect_client_build_mismatch");
        }
        if (state != GameplayProtocolState.Battle)
        {
            return OfficialBattleEffectWireResult<OfficialBattleEffectCandidate>.Failure(
                OfficialBattleEffectWireResultCode.InvalidState,
                "wire.battle.effect_state_invalid");
        }
        if (record.Length != RecordLength)
        {
            return OfficialBattleEffectWireResult<OfficialBattleEffectCandidate>.Failure(
                OfficialBattleEffectWireResultCode.InvalidLength,
                "wire.battle.effect_length_invalid");
        }
        if (record[0] != EffectOpcode)
        {
            return OfficialBattleEffectWireResult<OfficialBattleEffectCandidate>.Failure(
                OfficialBattleEffectWireResultCode.InvalidOpcode,
                "wire.battle.effect_opcode_invalid");
        }
        if (record[2] >= 28)
        {
            return OfficialBattleEffectWireResult<OfficialBattleEffectCandidate>.Failure(
                OfficialBattleEffectWireResultCode.InvalidSourceBattlePosition,
                "wire.battle.effect_source_position_invalid");
        }

        var friendlyTargetMask = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        var enemyTargetMask = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        var targets = new List<int>();
        for (var slot = 0; slot < 14; slot++)
        {
            if ((friendlyTargetMask & (1 << slot)) != 0)
            {
                targets.Add(slot);
            }
            if ((enemyTargetMask & (1 << slot)) != 0)
            {
                targets.Add(14 + slot);
            }
        }

        return OfficialBattleEffectWireResult<OfficialBattleEffectCandidate>.Success(
            new OfficialBattleEffectCandidate(
                record[1],
                record[2],
                (byte)(record[2] / 14),
                (byte)(record[2] % 14),
                record[3],
                friendlyTargetMask,
                enemyTargetMask,
                record[8],
                targets,
                BinaryPrimitives.ReadInt16LittleEndian(record[9..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[11..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[13..]),
                Convert.ToHexString(SHA256.HashData(record)),
                FieldLayoutEvidence,
                "RecordBoundaryVerified_SignedResultProjectionAndTypedSelectorRoutingConsumerVerified_ServerFormulaAndSerializerBlocked",
                RuntimeMutationAllowed: false));
    }

    public static OfficialBattleEffectWireResult<OfficialBattleEffectCandidate> DecodeForRuntime(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record)
    {
        var layout = DecodeLayout(clientBuildId, state, record);
        if (!layout.LayoutDecoded)
        {
            return layout;
        }
        return OfficialBattleEffectWireResult<OfficialBattleEffectCandidate>.Failure(
            OfficialBattleEffectWireResultCode.SemanticEvidenceBlocked,
            "wire.battle.effect_server_serializer_evidence_blocked");
    }

    /// <summary>
    /// Produces the exact fixed record layout for offline evidence replay and byte-round-trip
    /// verification. This is not the production Battle serializer: action authority, record
    /// ordering and the semantic/server authority of the 0x88 trailer remain evidence-blocked.
    /// </summary>
    public static OfficialBattleEffectWireResult<byte[]> EncodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialBattleEffectLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialBattleEffectWireResult<byte[]>.Failure(
                OfficialBattleEffectWireResultCode.BuildMismatch,
                "wire.battle.effect_client_build_mismatch");
        }
        if (state != GameplayProtocolState.Battle)
        {
            return OfficialBattleEffectWireResult<byte[]>.Failure(
                OfficialBattleEffectWireResultCode.InvalidState,
                "wire.battle.effect_state_invalid");
        }
        if (layout.SourceBattlePosition >= 28)
        {
            return OfficialBattleEffectWireResult<byte[]>.Failure(
                OfficialBattleEffectWireResultCode.InvalidSourceBattlePosition,
                "wire.battle.effect_source_position_invalid");
        }
        var record = new byte[RecordLength];
        record[0] = EffectOpcode;
        record[1] = layout.EffectKind;
        record[2] = layout.SourceBattlePosition;
        record[3] = layout.PlaybackGate;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), layout.FriendlyTargetMask);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), layout.EnemyTargetMask);
        record[8] = layout.ReservedByte8;
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(9), layout.SignedResult);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(11), layout.AuxiliaryValue0);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(13), layout.AuxiliaryValue1);
        return OfficialBattleEffectWireResult<byte[]>.Encoded(record);
    }

    public static OfficialBattleEffectWireResult<byte[]> EncodeForRuntime(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialBattleEffectLayout layout)
    {
        var encoded = EncodeLayout(clientBuildId, state, layout);
        if (!encoded.LayoutEncoded)
        {
            return encoded;
        }
        return OfficialBattleEffectWireResult<byte[]>.Failure(
            OfficialBattleEffectWireResultCode.SemanticEvidenceBlocked,
            "wire.battle.effect_server_serializer_evidence_blocked");
    }
}
