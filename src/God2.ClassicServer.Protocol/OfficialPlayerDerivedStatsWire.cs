using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialPlayerDerivedStatPolarity
{
    Neutral,
    Positive,
    Negative
}

public sealed record OfficialPlayerDerivedStatsLayout(
    ushort VitalityMagnitude,
    ushort StrengthMagnitude,
    ushort IntelligenceMagnitude,
    ushort SpeedMagnitude,
    uint Gold,
    uint Wood,
    uint Water,
    uint Fire,
    uint Earth,
    ushort PositiveMask,
    ushort NegativeMask,
    uint PhysicalDefense,
    uint PhysicalAttack,
    uint MagicalDefense,
    uint MagicalAttack,
    string RecordSha256,
    string EvidenceStatus,
    bool RuntimeMutationAllowed)
{
    public int VitalityModifier => ApplySign(VitalityMagnitude, 1);
    public int StrengthModifier => ApplySign(StrengthMagnitude, 2);
    public int IntelligenceModifier => ApplySign(IntelligenceMagnitude, 0);
    public int SpeedModifier => ApplySign(SpeedMagnitude, 3);

    public OfficialPlayerDerivedStatPolarity GoldPolarity => Polarity(4);
    public OfficialPlayerDerivedStatPolarity WoodPolarity => Polarity(5);
    public OfficialPlayerDerivedStatPolarity WaterPolarity => Polarity(6);
    public OfficialPlayerDerivedStatPolarity FirePolarity => Polarity(7);
    public OfficialPlayerDerivedStatPolarity EarthPolarity => Polarity(8);

    private int ApplySign(ushort magnitude, int bit) =>
        (NegativeMask & (1 << bit)) != 0 ? -magnitude : magnitude;

    private OfficialPlayerDerivedStatPolarity Polarity(int bit)
    {
        var flag = 1 << bit;
        if ((PositiveMask & flag) != 0)
        {
            return OfficialPlayerDerivedStatPolarity.Positive;
        }

        return (NegativeMask & flag) != 0
            ? OfficialPlayerDerivedStatPolarity.Negative
            : OfficialPlayerDerivedStatPolarity.Neutral;
    }
}

/// <summary>
/// Exact-current static layout for S2C 0x2C. Both the World and Battle
/// dispatchers pass payload+0 to the same current-build consumer at RVA
/// 0x00117CD0. The consumer reads through payload offset 0x3B, establishing
/// this 61-byte canonical application record. Public-beta evidence supplies
/// the field names only; all current offsets and widths come from the exact
/// God2_opt build.
/// </summary>
public static class OfficialPlayerDerivedStatsWireCodec
{
    public const string ClientBuildId = OfficialBattleCommandWireCodec.ClientBuildId;
    public const byte Opcode = 0x2C;
    public const int ApplicationRecordLength = 61;
    public const ushort KnownMaskBits = 0x01FF;
    public const bool RuntimeMutationEnabled = false;
    public const string CurrentConsumerEvidence =
        "God2_opt-6B127086E0C0:WorldDispatchRva-0x0008FA51+" +
        "BattleDispatchRva-0x00146F5E+SharedConsumerRva-0x00117CD0+" +
        "ProjectionRva-0x001178C0";
    public const string PublicBetaSemanticEvidence =
        "FS2TW-research-evidence-20260815.zip@" +
        OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
        ":legacy_combat_protocol/FUN_004B4620";

    public static OfficialPlayerSnapshotWireResult<OfficialPlayerDerivedStatsLayout> DecodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> applicationRecord)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return Failure(OfficialPlayerSnapshotWireResultCode.BuildMismatch,
                "wire.player.derived_stats.build_mismatch");
        }

        if (state is not (GameplayProtocolState.World or GameplayProtocolState.Battle))
        {
            return Failure(OfficialPlayerSnapshotWireResultCode.InvalidState,
                "wire.player.derived_stats.state_invalid");
        }

        if (applicationRecord.Length != ApplicationRecordLength)
        {
            return Failure(OfficialPlayerSnapshotWireResultCode.InvalidLength,
                "wire.player.derived_stats.length_invalid");
        }

        if (applicationRecord[0] != Opcode)
        {
            return Failure(OfficialPlayerSnapshotWireResultCode.InvalidOpcode,
                "wire.player.derived_stats.opcode_invalid");
        }

        var positiveMask = ReadU16(applicationRecord, 37);
        var negativeMask = ReadU16(applicationRecord, 41);
        return OfficialPlayerSnapshotWireResult<OfficialPlayerDerivedStatsLayout>.Success(
            new OfficialPlayerDerivedStatsLayout(
                ReadU16(applicationRecord, 1),
                ReadU16(applicationRecord, 5),
                ReadU16(applicationRecord, 9),
                ReadU16(applicationRecord, 13),
                ReadU32(applicationRecord, 17),
                ReadU32(applicationRecord, 21),
                ReadU32(applicationRecord, 25),
                ReadU32(applicationRecord, 29),
                ReadU32(applicationRecord, 33),
                positiveMask,
                negativeMask,
                ReadU32(applicationRecord, 45),
                ReadU32(applicationRecord, 49),
                ReadU32(applicationRecord, 53),
                ReadU32(applicationRecord, 57),
                Convert.ToHexString(SHA256.HashData(applicationRecord)),
                "ExactCurrentConsumerOffsets_PublicBetaNamesCorroborated_RuntimeRegistrationPendingLiveTest",
                RuntimeMutationAllowed: false));
    }

    public static OfficialPlayerSnapshotWireResult<byte[]> EncodeCanonicalLayout(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialPlayerDerivedStatsLayout value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return EncodeFailure(OfficialPlayerSnapshotWireResultCode.BuildMismatch,
                "wire.player.derived_stats.build_mismatch");
        }

        if (state is not (GameplayProtocolState.World or GameplayProtocolState.Battle))
        {
            return EncodeFailure(OfficialPlayerSnapshotWireResultCode.InvalidState,
                "wire.player.derived_stats.state_invalid");
        }

        if ((value.PositiveMask & ~KnownMaskBits) != 0 ||
            (value.NegativeMask & ~KnownMaskBits) != 0 ||
            (value.PositiveMask & value.NegativeMask) != 0)
        {
            return EncodeFailure(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked,
                "wire.player.derived_stats.mask_invalid");
        }

        if (value.VitalityMagnitude > short.MaxValue ||
            value.StrengthMagnitude > short.MaxValue ||
            value.IntelligenceMagnitude > short.MaxValue ||
            value.SpeedMagnitude > short.MaxValue)
        {
            return EncodeFailure(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked,
                "wire.player.derived_stats.modifier_out_of_range");
        }

        var record = new byte[ApplicationRecordLength];
        record[0] = Opcode;
        WriteU16(record, 1, value.VitalityMagnitude);
        WriteU16(record, 5, value.StrengthMagnitude);
        WriteU16(record, 9, value.IntelligenceMagnitude);
        WriteU16(record, 13, value.SpeedMagnitude);
        WriteU32(record, 17, value.Gold);
        WriteU32(record, 21, value.Wood);
        WriteU32(record, 25, value.Water);
        WriteU32(record, 29, value.Fire);
        WriteU32(record, 33, value.Earth);
        WriteU16(record, 37, value.PositiveMask);
        WriteU16(record, 41, value.NegativeMask);
        WriteU32(record, 45, value.PhysicalDefense);
        WriteU32(record, 49, value.PhysicalAttack);
        WriteU32(record, 53, value.MagicalDefense);
        WriteU32(record, 57, value.MagicalAttack);
        return OfficialPlayerSnapshotWireResult<byte[]>.Success(record);
    }

    public static OfficialPlayerSnapshotWireResult<T> RejectRuntimeMutation<T>() =>
        OfficialPlayerSnapshotWireResult<T>.Failure(
            OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked,
            "wire.player.derived_stats.runtime_registration_requires_live_test");

    private static OfficialPlayerSnapshotWireResult<OfficialPlayerDerivedStatsLayout> Failure(
        OfficialPlayerSnapshotWireResultCode code,
        string failureCode) =>
        OfficialPlayerSnapshotWireResult<OfficialPlayerDerivedStatsLayout>.Failure(code, failureCode);

    private static OfficialPlayerSnapshotWireResult<byte[]> EncodeFailure(
        OfficialPlayerSnapshotWireResultCode code,
        string failureCode) =>
        OfficialPlayerSnapshotWireResult<byte[]>.Failure(code, failureCode);

    private static ushort ReadU16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);

    private static uint ReadU32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);

    private static void WriteU16(Span<byte> bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[offset..], value);

    private static void WriteU32(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
}
