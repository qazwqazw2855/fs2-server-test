using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialBattleSettlementWireResultCode
{
    LayoutDecoded,
    LayoutEncoded,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    InvalidEntryCount,
    InvalidPresentationKind,
    SemanticEvidenceBlocked
}

public sealed record OfficialBattleSettlementWireResult<T>(
    OfficialBattleSettlementWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool LayoutDecoded => Code == OfficialBattleSettlementWireResultCode.LayoutDecoded;
    public bool LayoutEncoded => Code == OfficialBattleSettlementWireResultCode.LayoutEncoded;

    public static OfficialBattleSettlementWireResult<T> Decoded(T value) =>
        new(OfficialBattleSettlementWireResultCode.LayoutDecoded, value, string.Empty);

    public static OfficialBattleSettlementWireResult<T> Encoded(T value) =>
        new(OfficialBattleSettlementWireResultCode.LayoutEncoded, value, string.Empty);

    public static OfficialBattleSettlementWireResult<T> Failure(
        OfficialBattleSettlementWireResultCode code,
        string failureCode) => new(code, default, failureCode);
}

public sealed record OfficialBattleSettlementEntry(int CatalogValue, uint RawMetadata);

public sealed record OfficialBattleSettlementLayout(
    int RawCaseValue,
    uint ExperienceBase,
    uint ExperienceCredited,
    uint RawSummaryValue2,
    uint RawSummaryValue3,
    IReadOnlyList<OfficialBattleSettlementEntry> Entries,
    uint PresentationKind);

public sealed record OfficialBattleSettlementCandidate(
    int RawCaseValue,
    uint ExperienceBase,
    uint ExperienceCredited,
    uint RawSummaryValue2,
    uint RawSummaryValue3,
    IReadOnlyList<OfficialBattleSettlementEntry> Entries,
    uint PresentationKind,
    string RecordSha256,
    string FieldLayoutEvidence,
    string SemanticEvidenceStatus,
    bool RuntimeMutationAllowed);

/// <summary>
/// Exact current-build 57-byte S2C 0x89 settlement layout. Controlled single- and
/// multi-monster captures bind offsets 5 and 9 to base and credited experience.
/// Public-beta evidence supplies the stable case/entry/presentation field family;
/// current captures establish the inserted fourth summary value and the resulting
/// four-byte shift of the entry and presentation offsets. Unnamed values remain raw.
/// </summary>
public static class OfficialBattleSettlementWireCodec
{
    public const string ClientBuildId = OfficialBattleCommandWireCodec.ClientBuildId;
    public const byte SettlementOpcode = 0x89;
    public const int RecordLength = 57;
    public const int EntryCount = 4;
    public const int EntriesOffset = 21;
    public const int EntryStride = 8;
    public const int PresentationKindOffset = 53;
    public const bool RuntimeMutationEnabled = false;
    public const string FieldLayoutEvidence =
        "CurrentBuild-0x89/57-live-controlled-settlements;" +
        "Test-LimitedOfficialBattleCapture-experience-offsets-5-and-9;" +
        "FS2TW-public-beta-0x89/53-stable-case-entry-presentation-family-current-added-summary-u32";

    public static OfficialBattleSettlementWireResult<OfficialBattleSettlementCandidate> DecodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record)
    {
        var validation = Validate(clientBuildId, state, record);
        if (validation is not null)
        {
            return OfficialBattleSettlementWireResult<OfficialBattleSettlementCandidate>.Failure(
                validation.Value.Code,
                validation.Value.FailureCode);
        }

        var presentationKind = BinaryPrimitives.ReadUInt32LittleEndian(record[PresentationKindOffset..]);
        if (presentationKind > 3)
        {
            return OfficialBattleSettlementWireResult<OfficialBattleSettlementCandidate>.Failure(
                OfficialBattleSettlementWireResultCode.InvalidPresentationKind,
                "wire.battle.settlement_presentation_kind_invalid");
        }

        var entries = new OfficialBattleSettlementEntry[EntryCount];
        for (var index = 0; index < EntryCount; index++)
        {
            var offset = EntriesOffset + index * EntryStride;
            entries[index] = new OfficialBattleSettlementEntry(
                BinaryPrimitives.ReadInt32LittleEndian(record[offset..]),
                BinaryPrimitives.ReadUInt32LittleEndian(record[(offset + sizeof(int))..]));
        }

        return OfficialBattleSettlementWireResult<OfficialBattleSettlementCandidate>.Decoded(
            new OfficialBattleSettlementCandidate(
                BinaryPrimitives.ReadInt32LittleEndian(record[1..]),
                BinaryPrimitives.ReadUInt32LittleEndian(record[5..]),
                BinaryPrimitives.ReadUInt32LittleEndian(record[9..]),
                BinaryPrimitives.ReadUInt32LittleEndian(record[13..]),
                BinaryPrimitives.ReadUInt32LittleEndian(record[17..]),
                entries,
                presentationKind,
                Convert.ToHexString(SHA256.HashData(record)),
                FieldLayoutEvidence,
                "ExperienceBaseAndCreditedVerified_OtherSummaryAndEntryMeaningCompatibilityDerived",
                RuntimeMutationAllowed: false));
    }

    public static OfficialBattleSettlementWireResult<byte[]> EncodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialBattleSettlementLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(layout.Entries);
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialBattleSettlementWireResult<byte[]>.Failure(
                OfficialBattleSettlementWireResultCode.BuildMismatch,
                "wire.battle.settlement_client_build_mismatch");
        }
        if (state != GameplayProtocolState.Battle)
        {
            return OfficialBattleSettlementWireResult<byte[]>.Failure(
                OfficialBattleSettlementWireResultCode.InvalidState,
                "wire.battle.settlement_state_invalid");
        }
        if (layout.Entries.Count != EntryCount)
        {
            return OfficialBattleSettlementWireResult<byte[]>.Failure(
                OfficialBattleSettlementWireResultCode.InvalidEntryCount,
                "wire.battle.settlement_entry_count_invalid");
        }
        if (layout.PresentationKind > 3)
        {
            return OfficialBattleSettlementWireResult<byte[]>.Failure(
                OfficialBattleSettlementWireResultCode.InvalidPresentationKind,
                "wire.battle.settlement_presentation_kind_invalid");
        }

        var record = new byte[RecordLength];
        record[0] = SettlementOpcode;
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(1), layout.RawCaseValue);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(5), layout.ExperienceBase);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(9), layout.ExperienceCredited);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(13), layout.RawSummaryValue2);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(17), layout.RawSummaryValue3);
        for (var index = 0; index < EntryCount; index++)
        {
            var offset = EntriesOffset + index * EntryStride;
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(offset), layout.Entries[index].CatalogValue);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(offset + sizeof(int)), layout.Entries[index].RawMetadata);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(PresentationKindOffset), layout.PresentationKind);
        return OfficialBattleSettlementWireResult<byte[]>.Encoded(record);
    }

    public static OfficialBattleSettlementWireResult<byte[]> EncodeForRuntime(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialBattleSettlementLayout layout)
    {
        var encoded = EncodeLayout(clientBuildId, state, layout);
        return encoded.LayoutEncoded
            ? OfficialBattleSettlementWireResult<byte[]>.Failure(
                OfficialBattleSettlementWireResultCode.SemanticEvidenceBlocked,
                "wire.battle.settlement_server_authority_evidence_blocked")
            : encoded;
    }

    private static (OfficialBattleSettlementWireResultCode Code, string FailureCode)? Validate(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return (OfficialBattleSettlementWireResultCode.BuildMismatch, "wire.battle.settlement_client_build_mismatch");
        }
        if (state != GameplayProtocolState.Battle)
        {
            return (OfficialBattleSettlementWireResultCode.InvalidState, "wire.battle.settlement_state_invalid");
        }
        if (record.Length != RecordLength)
        {
            return (OfficialBattleSettlementWireResultCode.InvalidLength, "wire.battle.settlement_length_invalid");
        }
        if (record[0] != SettlementOpcode)
        {
            return (OfficialBattleSettlementWireResultCode.InvalidOpcode, "wire.battle.settlement_opcode_invalid");
        }
        return null;
    }
}
