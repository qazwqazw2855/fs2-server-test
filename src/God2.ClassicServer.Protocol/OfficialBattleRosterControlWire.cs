using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialBattleRosterControlWireResultCode
{
    LayoutDecoded,
    LayoutEncoded,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    InvalidBattlePosition,
    InvalidIdentityPayload,
    SemanticEvidenceBlocked
}

public sealed record OfficialBattleRosterControlWireResult<T>(
    OfficialBattleRosterControlWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool LayoutDecoded => Code == OfficialBattleRosterControlWireResultCode.LayoutDecoded;
    public bool LayoutEncoded => Code == OfficialBattleRosterControlWireResultCode.LayoutEncoded;
    public static OfficialBattleRosterControlWireResult<T> Decoded(T value) => new(OfficialBattleRosterControlWireResultCode.LayoutDecoded, value, string.Empty);
    public static OfficialBattleRosterControlWireResult<T> Encoded(T value) => new(OfficialBattleRosterControlWireResultCode.LayoutEncoded, value, string.Empty);
    public static OfficialBattleRosterControlWireResult<T> Failure(OfficialBattleRosterControlWireResultCode code, string failureCode) => new(code, default, failureCode);
}

public sealed record OfficialBattleRosterRecord(
    byte BattlePosition,
    byte DisplayLevel,
    ushort EntityId,
    ushort ActorKindFlags,
    ushort EncounterLocalId,
    ReadOnlyMemory<byte> IdentityPayload,
    ReadOnlyMemory<byte> NameFieldRaw,
    string RecordSha256,
    string CrossVersionEvidence,
    bool RuntimeMutationAllowed);

public sealed record OfficialBattleControlRecord(
    byte Subtype,
    byte BattlePosition,
    byte ControlByte3,
    byte ControlByte4,
    uint Value0,
    uint Value1,
    uint Value2,
    string RecordSha256);

public sealed record OfficialBattleBoundaryRecord(byte Opcode, byte RawControl, bool ConsumerReadsControl);

public sealed record OfficialBattlePackedControl87(
    ushort RawWord,
    byte LowSelector,
    byte HighSelector,
    bool ModeBit14,
    bool StateBit15,
    string RecordSha256);

/// <summary>
/// Current-build battle roster and compact control records recovered by joining
/// a third controlled client to an active PK. Public-beta consumers establish
/// the stable 0x1C identity prefix and 0x87 bit split; current-build handler
/// traces independently prove the present record boundaries and actor binding.
/// Changed record sizes are represented by current-build layouts only.
/// </summary>
public static class OfficialBattleRosterControlWireCodec
{
    public const string ClientBuildId = OfficialBattleCommandWireCodec.ClientBuildId;
    public const byte RosterOpcode = 0x1C;
    public const int RosterLength = 45;
    public const int RosterIdentityPayloadOffset = 9;
    public const int RosterIdentityPayloadLength = 36;
    public const int RosterNameFieldLength = 16;
    public const byte ControlOpcode = 0x86;
    public const int ControlLength = 17;
    public const byte ControlBoundaryOpcode = 0x38;
    public const byte SequenceBoundaryOpcode = 0x85;
    public const int BoundaryLength = 2;
    public const byte PackedControlOpcode = 0x87;
    public const int PackedControlLength = 3;
    public const bool RuntimeMutationEnabled = false;

    public static OfficialBattleRosterControlWireResult<OfficialBattleRosterRecord> DecodeRoster(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record)
    {
        var common = Validate(clientBuildId, state, record, RosterOpcode, RosterLength, "roster");
        if (common is not null)
        {
            return OfficialBattleRosterControlWireResult<OfficialBattleRosterRecord>.Failure(common.Value.Code, common.Value.FailureCode);
        }

        if (record[1] >= 28)
        {
            return OfficialBattleRosterControlWireResult<OfficialBattleRosterRecord>.Failure(
                OfficialBattleRosterControlWireResultCode.InvalidBattlePosition,
                "wire.battle_roster.position_invalid");
        }

        var identity = record.Slice(RosterIdentityPayloadOffset, RosterIdentityPayloadLength).ToArray();
        return OfficialBattleRosterControlWireResult<OfficialBattleRosterRecord>.Decoded(
            new OfficialBattleRosterRecord(
                record[1],
                record[2],
                BinaryPrimitives.ReadUInt16LittleEndian(record[3..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[5..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[7..]),
                identity,
                identity.AsMemory(0, RosterNameFieldLength),
                Convert.ToHexString(SHA256.HashData(record)),
                "PublicBeta-0x1C/29-byte-prefix[1..8]+CurrentBuild-0x1C/45-byte-live-actor-binding",
                RuntimeMutationAllowed: false));
    }

    public static OfficialBattleRosterControlWireResult<byte[]> EncodeRosterLayout(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialBattleRosterRecord roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialBattleRosterControlWireResult<byte[]>.Failure(OfficialBattleRosterControlWireResultCode.BuildMismatch, "wire.battle_roster.client_build_mismatch");
        }
        if (state != GameplayProtocolState.Battle)
        {
            return OfficialBattleRosterControlWireResult<byte[]>.Failure(OfficialBattleRosterControlWireResultCode.InvalidState, "wire.battle_roster.state_invalid");
        }
        if (roster.BattlePosition >= 28)
        {
            return OfficialBattleRosterControlWireResult<byte[]>.Failure(OfficialBattleRosterControlWireResultCode.InvalidBattlePosition, "wire.battle_roster.position_invalid");
        }
        if (roster.IdentityPayload.Length != RosterIdentityPayloadLength)
        {
            return OfficialBattleRosterControlWireResult<byte[]>.Failure(OfficialBattleRosterControlWireResultCode.InvalidIdentityPayload, "wire.battle_roster.identity_payload_invalid");
        }

        var record = new byte[RosterLength];
        record[0] = RosterOpcode;
        record[1] = roster.BattlePosition;
        record[2] = roster.DisplayLevel;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(3), roster.EntityId);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(5), roster.ActorKindFlags);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(7), roster.EncounterLocalId);
        roster.IdentityPayload.Span.CopyTo(record.AsSpan(RosterIdentityPayloadOffset));
        return OfficialBattleRosterControlWireResult<byte[]>.Encoded(record);
    }

    public static OfficialBattleRosterControlWireResult<OfficialBattleControlRecord> DecodeControl(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record)
    {
        var common = Validate(clientBuildId, state, record, ControlOpcode, ControlLength, "control");
        if (common is not null)
        {
            return OfficialBattleRosterControlWireResult<OfficialBattleControlRecord>.Failure(common.Value.Code, common.Value.FailureCode);
        }
        if (record[2] >= 28)
        {
            return OfficialBattleRosterControlWireResult<OfficialBattleControlRecord>.Failure(
                OfficialBattleRosterControlWireResultCode.InvalidBattlePosition,
                "wire.battle_control.position_invalid");
        }

        return OfficialBattleRosterControlWireResult<OfficialBattleControlRecord>.Decoded(
            new OfficialBattleControlRecord(
                record[1], record[2], record[3], record[4],
                BinaryPrimitives.ReadUInt32LittleEndian(record[5..]),
                BinaryPrimitives.ReadUInt32LittleEndian(record[9..]),
                BinaryPrimitives.ReadUInt32LittleEndian(record[13..]),
                Convert.ToHexString(SHA256.HashData(record))));
    }

    public static OfficialBattleRosterControlWireResult<byte[]> EncodeControlLayout(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialBattleControlRecord control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var common = ValidateEncode(clientBuildId, state, control.BattlePosition, "control");
        if (common is not null)
        {
            return OfficialBattleRosterControlWireResult<byte[]>.Failure(common.Value.Code, common.Value.FailureCode);
        }

        var record = new byte[ControlLength];
        record[0] = ControlOpcode;
        record[1] = control.Subtype;
        record[2] = control.BattlePosition;
        record[3] = control.ControlByte3;
        record[4] = control.ControlByte4;
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(5), control.Value0);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(9), control.Value1);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(13), control.Value2);
        return OfficialBattleRosterControlWireResult<byte[]>.Encoded(record);
    }

    public static OfficialBattleRosterControlWireResult<OfficialBattleBoundaryRecord> DecodeBoundary(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialBattleRosterControlWireResult<OfficialBattleBoundaryRecord>.Failure(OfficialBattleRosterControlWireResultCode.BuildMismatch, "wire.battle_boundary.client_build_mismatch");
        }
        if (state != GameplayProtocolState.Battle)
        {
            return OfficialBattleRosterControlWireResult<OfficialBattleBoundaryRecord>.Failure(OfficialBattleRosterControlWireResultCode.InvalidState, "wire.battle_boundary.state_invalid");
        }
        if (record.Length != BoundaryLength)
        {
            return OfficialBattleRosterControlWireResult<OfficialBattleBoundaryRecord>.Failure(OfficialBattleRosterControlWireResultCode.InvalidLength, "wire.battle_boundary.length_invalid");
        }
        if (record[0] is not (ControlBoundaryOpcode or SequenceBoundaryOpcode))
        {
            return OfficialBattleRosterControlWireResult<OfficialBattleBoundaryRecord>.Failure(OfficialBattleRosterControlWireResultCode.InvalidOpcode, "wire.battle_boundary.opcode_invalid");
        }

        return OfficialBattleRosterControlWireResult<OfficialBattleBoundaryRecord>.Decoded(
            new OfficialBattleBoundaryRecord(record[0], record[1], ConsumerReadsControl: false));
    }

    public static OfficialBattleRosterControlWireResult<byte[]> EncodeBoundaryLayout(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialBattleBoundaryRecord boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialBattleRosterControlWireResult<byte[]>.Failure(
                OfficialBattleRosterControlWireResultCode.BuildMismatch,
                "wire.battle_boundary.client_build_mismatch");
        }
        if (state != GameplayProtocolState.Battle)
        {
            return OfficialBattleRosterControlWireResult<byte[]>.Failure(
                OfficialBattleRosterControlWireResultCode.InvalidState,
                "wire.battle_boundary.state_invalid");
        }
        if (boundary.Opcode is not (ControlBoundaryOpcode or SequenceBoundaryOpcode))
        {
            return OfficialBattleRosterControlWireResult<byte[]>.Failure(
                OfficialBattleRosterControlWireResultCode.InvalidOpcode,
                "wire.battle_boundary.opcode_invalid");
        }

        return OfficialBattleRosterControlWireResult<byte[]>.Encoded([boundary.Opcode, boundary.RawControl]);
    }

    public static OfficialBattleRosterControlWireResult<OfficialBattlePackedControl87> DecodePackedControl87(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record)
    {
        var common = Validate(clientBuildId, state, record, PackedControlOpcode, PackedControlLength, "packed_control_87");
        if (common is not null)
        {
            return OfficialBattleRosterControlWireResult<OfficialBattlePackedControl87>.Failure(common.Value.Code, common.Value.FailureCode);
        }

        var word = BinaryPrimitives.ReadUInt16LittleEndian(record[1..]);
        return OfficialBattleRosterControlWireResult<OfficialBattlePackedControl87>.Decoded(
            new OfficialBattlePackedControl87(
                word,
                (byte)word,
                (byte)((word >> 8) & 0x3F),
                (word & 0x4000) != 0,
                (word & 0x8000) != 0,
                Convert.ToHexString(SHA256.HashData(record))));
    }

    public static OfficialBattleRosterControlWireResult<byte[]> EncodePackedControl87Layout(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialBattlePackedControl87 control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialBattleRosterControlWireResult<byte[]>.Failure(
                OfficialBattleRosterControlWireResultCode.BuildMismatch,
                "wire.battle_packed_control_87.client_build_mismatch");
        }
        if (state != GameplayProtocolState.Battle)
        {
            return OfficialBattleRosterControlWireResult<byte[]>.Failure(
                OfficialBattleRosterControlWireResultCode.InvalidState,
                "wire.battle_packed_control_87.state_invalid");
        }

        var expectedWord = (ushort)(
            control.LowSelector |
            (control.HighSelector << 8) |
            (control.ModeBit14 ? 0x4000 : 0) |
            (control.StateBit15 ? 0x8000 : 0));
        if (control.HighSelector > 0x3F || expectedWord != control.RawWord)
        {
            return OfficialBattleRosterControlWireResult<byte[]>.Failure(
                OfficialBattleRosterControlWireResultCode.SemanticEvidenceBlocked,
                "wire.battle_packed_control_87.bit_projection_mismatch");
        }

        var record = new byte[PackedControlLength];
        record[0] = PackedControlOpcode;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1), control.RawWord);
        return OfficialBattleRosterControlWireResult<byte[]>.Encoded(record);
    }

    private static (OfficialBattleRosterControlWireResultCode Code, string FailureCode)? ValidateEncode(
        string clientBuildId,
        GameplayProtocolState state,
        byte battlePosition,
        string family)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return (OfficialBattleRosterControlWireResultCode.BuildMismatch, $"wire.battle_{family}.client_build_mismatch");
        }
        if (state != GameplayProtocolState.Battle)
        {
            return (OfficialBattleRosterControlWireResultCode.InvalidState, $"wire.battle_{family}.state_invalid");
        }
        if (battlePosition >= 28)
        {
            return (OfficialBattleRosterControlWireResultCode.InvalidBattlePosition, $"wire.battle_{family}.position_invalid");
        }
        return null;
    }

    private static (OfficialBattleRosterControlWireResultCode Code, string FailureCode)? Validate(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> record,
        byte opcode,
        int length,
        string family)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return (OfficialBattleRosterControlWireResultCode.BuildMismatch, $"wire.battle_{family}.client_build_mismatch");
        }
        if (state != GameplayProtocolState.Battle)
        {
            return (OfficialBattleRosterControlWireResultCode.InvalidState, $"wire.battle_{family}.state_invalid");
        }
        if (record.Length != length)
        {
            return (OfficialBattleRosterControlWireResultCode.InvalidLength, $"wire.battle_{family}.length_invalid");
        }
        if (record[0] != opcode)
        {
            return (OfficialBattleRosterControlWireResultCode.InvalidOpcode, $"wire.battle_{family}.opcode_invalid");
        }
        return null;
    }
}
