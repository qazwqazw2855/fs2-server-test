using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialMonsterWorldWireResultCode
{
    LayoutDecoded,
    LayoutEncoded,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    InvalidCoordinate,
    InvalidCatalogKey,
    InvalidDirection,
    InvalidAppearance,
    SemanticEvidenceBlocked
}

public sealed record OfficialMonsterWorldWireResult<T>(
    OfficialMonsterWorldWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool LayoutDecoded => Code == OfficialMonsterWorldWireResultCode.LayoutDecoded;
    public bool LayoutEncoded => Code == OfficialMonsterWorldWireResultCode.LayoutEncoded;

    public static OfficialMonsterWorldWireResult<T> Decoded(T value) =>
        new(OfficialMonsterWorldWireResultCode.LayoutDecoded, value, string.Empty);

    public static OfficialMonsterWorldWireResult<T> Encoded(T value) =>
        new(OfficialMonsterWorldWireResultCode.LayoutEncoded, value, string.Empty);

    public static OfficialMonsterWorldWireResult<T> Failure(
        OfficialMonsterWorldWireResultCode code,
        string failureCode) => new(code, default, failureCode);
}

public sealed record OfficialMonsterWorldLayout(
    ushort ObjectId,
    byte RawClassByte,
    byte AppearanceSelector,
    byte DirectionSelector,
    byte IslandIndex,
    ushort AreaId,
    ushort RawControlWord7,
    ushort MapX,
    ushort MapY,
    byte PositionLowFlags,
    ushort RawControlWord17,
    ushort AreaEntryIndex,
    byte OrientationSelector);

public sealed record OfficialMonsterWorldCandidate(
    OfficialMonsterWorldLayout Layout,
    uint RawIdentityDword,
    ushort RawLookupWord,
    uint RawPackedPosition,
    ushort RawNameIndexWord,
    string RecordSha256,
    string FieldLayoutEvidence,
    bool RuntimeMutationAllowed);

/// <summary>
/// Current-build world monster spawn/update record 0x71. Exact current captures
/// retain the public-beta 21-byte layout. The hierarchical identity key is consumed
/// by the official EnyName resolver. Packed X/Y and the duplicate unpacked X/Y fields
/// are cross-checked so a malformed compatibility projection cannot be serialized.
/// The two unnamed control words remain caller-supplied raw values.
/// </summary>
public static class OfficialMonsterWorldWireCodec
{
    public const string ClientBuildId = OfficialBattleCommandWireCodec.ClientBuildId;
    public const byte Opcode = 0x71;
    public const int RecordLength = 21;
    public const bool RuntimeMutationEnabled = false;
    public const string FieldLayoutEvidence =
        "CurrentBuild-live-0x71/21-repeated-captures;" +
        "CurrentBuild-handler-rva-0x0008FF22;" +
        "FS2TW-public-beta-SC_DRAW_MONSTER-0x71/21+EnyName-resolver;" +
        "XJZ-V9-enemy_identity.csv-cross-version-name-corroboration";

    public static OfficialMonsterWorldWireResult<OfficialMonsterWorldCandidate> DecodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record)
    {
        var validation = ValidateCommon(clientBuildId, state, record);
        if (validation is not null)
        {
            return OfficialMonsterWorldWireResult<OfficialMonsterWorldCandidate>.Failure(
                validation.Value.Code,
                validation.Value.FailureCode);
        }

        var rawIdentity = BinaryPrimitives.ReadUInt32LittleEndian(record[1..]);
        var rawLookup = BinaryPrimitives.ReadUInt16LittleEndian(record[5..]);
        var rawPosition = BinaryPrimitives.ReadUInt32LittleEndian(record[9..]);
        var rawNameIndex = BinaryPrimitives.ReadUInt16LittleEndian(record[19..]);
        var mapX = (ushort)((rawPosition >> 2) & 0x7FFF);
        var mapY = (ushort)((rawPosition >> 17) & 0x7FFF);
        if (BinaryPrimitives.ReadUInt16LittleEndian(record[13..]) != mapX ||
            BinaryPrimitives.ReadUInt16LittleEndian(record[15..]) != mapY)
        {
            return OfficialMonsterWorldWireResult<OfficialMonsterWorldCandidate>.Failure(
                OfficialMonsterWorldWireResultCode.InvalidCoordinate,
                "wire.monster_world.coordinate_projection_mismatch");
        }

        var layout = new OfficialMonsterWorldLayout(
            (ushort)rawIdentity,
            record[3],
            (byte)(record[4] & 0x1F),
            (byte)(rawIdentity >> 29),
            (byte)(rawLookup & 0x3F),
            (ushort)(rawLookup >> 6),
            BinaryPrimitives.ReadUInt16LittleEndian(record[7..]),
            mapX,
            mapY,
            (byte)(rawPosition & 0x03),
            BinaryPrimitives.ReadUInt16LittleEndian(record[17..]),
            (ushort)(rawNameIndex >> 3),
            (byte)(rawNameIndex & 0x07));

        return OfficialMonsterWorldWireResult<OfficialMonsterWorldCandidate>.Decoded(
            new OfficialMonsterWorldCandidate(
                layout,
                rawIdentity,
                rawLookup,
                rawPosition,
                rawNameIndex,
                Convert.ToHexString(SHA256.HashData(record)),
                FieldLayoutEvidence,
                RuntimeMutationAllowed: false));
    }

    public static OfficialMonsterWorldWireResult<byte[]> EncodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialMonsterWorldLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return Failure<byte[]>(OfficialMonsterWorldWireResultCode.BuildMismatch, "wire.monster_world.client_build_mismatch");
        }
        if (state != GameplayProtocolState.World)
        {
            return Failure<byte[]>(OfficialMonsterWorldWireResultCode.InvalidState, "wire.monster_world.state_invalid");
        }
        if (layout.MapX > 0x7FFF || layout.MapY > 0x7FFF)
        {
            return Failure<byte[]>(OfficialMonsterWorldWireResultCode.InvalidCoordinate, "wire.monster_world.coordinate_invalid");
        }
        if (layout.PositionLowFlags > 3)
        {
            return Failure<byte[]>(OfficialMonsterWorldWireResultCode.InvalidCoordinate, "wire.monster_world.position_flags_invalid");
        }
        if (layout.IslandIndex > 0x3F || layout.AreaId > 0x03FF || layout.AreaEntryIndex > 0x1FFF)
        {
            return Failure<byte[]>(OfficialMonsterWorldWireResultCode.InvalidCatalogKey, "wire.monster_world.catalog_key_invalid");
        }
        if (layout.DirectionSelector > 7)
        {
            return Failure<byte[]>(OfficialMonsterWorldWireResultCode.InvalidDirection, "wire.monster_world.direction_invalid");
        }
        if (layout.AppearanceSelector > 0x1F)
        {
            return Failure<byte[]>(OfficialMonsterWorldWireResultCode.InvalidAppearance, "wire.monster_world.appearance_invalid");
        }
        if (layout.OrientationSelector > 7)
        {
            return Failure<byte[]>(OfficialMonsterWorldWireResultCode.InvalidDirection, "wire.monster_world.orientation_invalid");
        }

        var record = new byte[RecordLength];
        record[0] = Opcode;
        var rawIdentity = (uint)(
            layout.ObjectId |
            ((uint)layout.RawClassByte << 16) |
            ((uint)layout.AppearanceSelector << 24) |
            ((uint)layout.DirectionSelector << 29));
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(1), rawIdentity);
        BinaryPrimitives.WriteUInt16LittleEndian(
            record.AsSpan(5),
            (ushort)(layout.IslandIndex | (layout.AreaId << 6)));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(7), layout.RawControlWord7);
        var rawPosition = layout.PositionLowFlags | ((uint)layout.MapX << 2) | ((uint)layout.MapY << 17);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(9), rawPosition);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(13), layout.MapX);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(15), layout.MapY);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(17), layout.RawControlWord17);
        BinaryPrimitives.WriteUInt16LittleEndian(
            record.AsSpan(19),
            (ushort)((layout.AreaEntryIndex << 3) | layout.OrientationSelector));
        return OfficialMonsterWorldWireResult<byte[]>.Encoded(record);
    }

    public static OfficialMonsterWorldWireResult<byte[]> EncodeForRuntime(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialMonsterWorldLayout layout)
    {
        var encoded = EncodeLayout(clientBuildId, state, layout);
        return encoded.LayoutEncoded
            ? Failure<byte[]>(
                OfficialMonsterWorldWireResultCode.SemanticEvidenceBlocked,
                "wire.monster_world.runtime_identity_binding_required")
            : encoded;
    }

    private static (OfficialMonsterWorldWireResultCode Code, string FailureCode)? ValidateCommon(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return (OfficialMonsterWorldWireResultCode.BuildMismatch, "wire.monster_world.client_build_mismatch");
        }
        if (state != GameplayProtocolState.World)
        {
            return (OfficialMonsterWorldWireResultCode.InvalidState, "wire.monster_world.state_invalid");
        }
        if (record.Length != RecordLength)
        {
            return (OfficialMonsterWorldWireResultCode.InvalidLength, "wire.monster_world.length_invalid");
        }
        if (record[0] != Opcode)
        {
            return (OfficialMonsterWorldWireResultCode.InvalidOpcode, "wire.monster_world.opcode_invalid");
        }
        return null;
    }

    private static OfficialMonsterWorldWireResult<T> Failure<T>(
        OfficialMonsterWorldWireResultCode code,
        string failureCode) => OfficialMonsterWorldWireResult<T>.Failure(code, failureCode);
}
