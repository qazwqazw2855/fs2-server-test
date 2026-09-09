using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialPlayerSnapshotWireResultCode
{
    LayoutDecoded,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    InvalidAttributeCode,
    SemanticEvidenceBlocked
}

public sealed record OfficialPlayerSnapshotWireResult<T>(
    OfficialPlayerSnapshotWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool LayoutDecoded => Code == OfficialPlayerSnapshotWireResultCode.LayoutDecoded;

    public static OfficialPlayerSnapshotWireResult<T> Success(T value) =>
        new(OfficialPlayerSnapshotWireResultCode.LayoutDecoded, value, string.Empty);

    public static OfficialPlayerSnapshotWireResult<T> Failure(
        OfficialPlayerSnapshotWireResultCode code,
        string failureCode) =>
        new(code, default, failureCode);
}

public enum OfficialPlayerAttributeCandidate : byte
{
    Intelligence = 1,
    Vitality = 2,
    Strength = 3,
    Speed = 4
}

public sealed record OfficialAttributeIncrementCandidate(
    byte RawAttributeCode,
    OfficialPlayerAttributeCandidate PublicBetaCandidate,
    string RecordSha256,
    string EvidenceStatus,
    bool RuntimeMutationAllowed);

public sealed record OfficialPlayerStatusLayoutCandidate(
    sbyte Level,
    byte ReservedAfterLevel,
    ushort UnspentAttributePoints,
    ushort Vitality,
    ushort Strength,
    ushort Intelligence,
    ushort Speed,
    ushort ExperienceBasisPoints,
    ushort ReservedBeforeMaximumVitals,
    int MaximumHitPoints,
    int MaximumMagicPoints,
    string RecordSha256,
    string EvidenceStatus,
    bool RuntimeMutationAllowed);

public sealed record OfficialPlayerVitalsLayoutCandidate(
    int CurrentHitPoints,
    int CurrentMagicPoints,
    int MaximumHitPoints,
    int MaximumMagicPoints,
    string RecordSha256,
    string EvidenceStatus,
    bool RuntimeMutationAllowed);

/// <summary>
/// Exact-current structural decoders cross-referenced with the public-beta
/// client consumers. Current captures independently prove C2S 0x21/2,
/// S2C 0x22/25 and S2C 0x24/17 application-record boundaries. The field
/// 0x21 attribute mapping remains a cross-version candidate. Current-build
/// dispatchers/consumers now independently confirm 0x22 and 0x24 fields and
/// permit their bounded server-output projections.
/// </summary>
public static class OfficialPlayerSnapshotCandidateWireCodec
{
    public const string ClientBuildId = OfficialBattleCommandWireCodec.ClientBuildId;
    public const string CurrentStructuralEvidence =
        "OfficialEvidencePackage20260806:C2S-0x21/frame5x6+S2C-0x22/frame28x9;" +
        "OfficialEvidencePackage20260807Supplement:S2C-0x24/frame20x7;" +
        "current-build/client-dispatch-registry.json";
    public const string PublicBetaConsumerEvidence =
        "FS2TW-research-evidence-20260815.zip@" +
        OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
        ":legacy_combat_protocol/FUN_004B4B90+FUN_004B5260+producer_0047A000";
    public const byte AttributeIncrementOpcode = 0x21;
    public const byte StatusOpcode = 0x22;
    public const byte VitalsOpcode = 0x24;
    public const int AttributeIncrementRecordLength = 2;
    public const int StatusRecordLength = 25;
    public const int VitalsRecordLength = 17;
    public const bool RuntimeMutationEnabled = false;
    public const bool StatusRuntimeProjectionEnabled = true;
    public const bool VitalsRuntimeProjectionEnabled = true;

    public static OfficialPlayerSnapshotWireResult<OfficialAttributeIncrementCandidate> DecodeAttributeIncrementLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> applicationRecord)
    {
        var boundary = Validate(clientBuildId, state, applicationRecord,
            AttributeIncrementOpcode, AttributeIncrementRecordLength, clientToServer: true);
        if (boundary is not null)
        {
            return OfficialPlayerSnapshotWireResult<OfficialAttributeIncrementCandidate>.Failure(
                boundary.Value.Code, boundary.Value.FailureCode);
        }

        var rawCode = applicationRecord[1];
        if (rawCode is < 1 or > 4)
        {
            return OfficialPlayerSnapshotWireResult<OfficialAttributeIncrementCandidate>.Failure(
                OfficialPlayerSnapshotWireResultCode.InvalidAttributeCode,
                "wire.player.attribute_increment_code_invalid");
        }

        return OfficialPlayerSnapshotWireResult<OfficialAttributeIncrementCandidate>.Success(
            new OfficialAttributeIncrementCandidate(
                rawCode,
                (OfficialPlayerAttributeCandidate)rawCode,
                Hash(applicationRecord),
                "CurrentOpcodeLengthObserved_PublicBetaCodeMeaningCandidate_CurrentConsumerConfirmationRequired",
                RuntimeMutationAllowed: false));
    }

    public static OfficialPlayerSnapshotWireResult<OfficialPlayerStatusLayoutCandidate> DecodeStatusLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> applicationRecord)
    {
        var boundary = Validate(clientBuildId, state, applicationRecord,
            StatusOpcode, StatusRecordLength, clientToServer: false);
        if (boundary is not null)
        {
            return OfficialPlayerSnapshotWireResult<OfficialPlayerStatusLayoutCandidate>.Failure(
                boundary.Value.Code, boundary.Value.FailureCode);
        }

        return OfficialPlayerSnapshotWireResult<OfficialPlayerStatusLayoutCandidate>.Success(
            new OfficialPlayerStatusLayoutCandidate(
                unchecked((sbyte)applicationRecord[1]),
                applicationRecord[2],
                ReadU16(applicationRecord, 3),
                ReadU16(applicationRecord, 5),
                ReadU16(applicationRecord, 7),
                ReadU16(applicationRecord, 9),
                ReadU16(applicationRecord, 11),
                ReadU16(applicationRecord, 13),
                ReadU16(applicationRecord, 15),
                ReadI32(applicationRecord, 17),
                ReadI32(applicationRecord, 21),
                Hash(applicationRecord),
                "CurrentWorldBattleDispatcherAndConsumerVerified_FieldBinding_RuntimeProjectionEnabled",
                RuntimeMutationAllowed: StatusRuntimeProjectionEnabled));
    }

    public static OfficialPlayerSnapshotWireResult<OfficialPlayerVitalsLayoutCandidate> DecodeVitalsLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> applicationRecord)
    {
        var boundary = Validate(clientBuildId, state, applicationRecord,
            VitalsOpcode, VitalsRecordLength, clientToServer: false);
        if (boundary is not null)
        {
            return OfficialPlayerSnapshotWireResult<OfficialPlayerVitalsLayoutCandidate>.Failure(
                boundary.Value.Code, boundary.Value.FailureCode);
        }

        return OfficialPlayerSnapshotWireResult<OfficialPlayerVitalsLayoutCandidate>.Success(
            new OfficialPlayerVitalsLayoutCandidate(
                ReadI32(applicationRecord, 1),
                ReadI32(applicationRecord, 5),
                ReadI32(applicationRecord, 9),
                ReadI32(applicationRecord, 13),
                Hash(applicationRecord),
                "CurrentDispatcherAndConsumerVerified_HpMpFieldBinding_RuntimeProjectionEnabled",
                RuntimeMutationAllowed: VitalsRuntimeProjectionEnabled));
    }

    public static OfficialPlayerSnapshotWireResult<T> RejectRuntimeMutation<T>() =>
        OfficialPlayerSnapshotWireResult<T>.Failure(
            OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked,
            "wire.player.cross_version_candidate_runtime_mutation_blocked");

    private static (OfficialPlayerSnapshotWireResultCode Code, string FailureCode)? Validate(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> applicationRecord,
        byte opcode,
        int length,
        bool clientToServer)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return (OfficialPlayerSnapshotWireResultCode.BuildMismatch,
                "wire.player.cross_version_candidate_build_mismatch");
        }

        if (state is not (GameplayProtocolState.World or GameplayProtocolState.Battle) ||
            (clientToServer && state != GameplayProtocolState.World))
        {
            return (OfficialPlayerSnapshotWireResultCode.InvalidState,
                "wire.player.cross_version_candidate_state_invalid");
        }

        if (applicationRecord.Length != length)
        {
            return (OfficialPlayerSnapshotWireResultCode.InvalidLength,
                "wire.player.cross_version_candidate_length_invalid");
        }

        if (applicationRecord[0] != opcode)
        {
            return (OfficialPlayerSnapshotWireResultCode.InvalidOpcode,
                "wire.player.cross_version_candidate_opcode_invalid");
        }

        return null;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);

    private static int ReadI32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
