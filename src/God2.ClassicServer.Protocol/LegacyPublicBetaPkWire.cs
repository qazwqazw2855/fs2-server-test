using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace God2.ClassicServer.Protocol;

public enum LegacyPublicBetaWireResultCode
{
    Decoded,
    InvalidLength,
    InvalidOpcode
}

public sealed record LegacyPublicBetaWireResult<T>(LegacyPublicBetaWireResultCode Code, T? Value, string FailureCode)
{
    public bool Succeeded => Code == LegacyPublicBetaWireResultCode.Decoded;
    public static LegacyPublicBetaWireResult<T> Success(T value) => new(LegacyPublicBetaWireResultCode.Decoded, value, string.Empty);
    public static LegacyPublicBetaWireResult<T> Failure(LegacyPublicBetaWireResultCode code, string failureCode) => new(code, default, failureCode);
}

public sealed record LegacyPublicBetaPkCursorTargetRequest(
    uint SourceActorId,
    uint TargetActorId,
    ReadOnlyMemory<byte> SourceLabelRaw,
    ReadOnlyMemory<byte> TargetLabelRaw,
    ReadOnlyMemory<byte> ReservedRaw,
    byte Mode);

public sealed record LegacyPublicBetaPlayerDerivedStats(
    short VitalityModifier,
    short StrengthModifier,
    short IntelligenceModifier,
    short SpeedModifier,
    IReadOnlyList<ushort> ElementsGoldWoodWaterFireEarth,
    ushort PositiveMask,
    ushort NegativeMask,
    ushort PhysicalDefense,
    ushort PhysicalAttack,
    ushort MagicalDefense,
    ushort MagicalAttack);

/// <summary>
/// Version-isolated public-beta application-record codecs recovered from the
/// 2006 static client. They are deliberately not registered for the current
/// official build, whose controlled assistance request is opcode 0x6A.
/// </summary>
public static class LegacyPublicBetaPkWireCodec
{
    public const string ProtocolVersion = "fs2tw-public-beta-static-20060601";
    public const string EvidenceArchiveSha256 = "75BA3B07058F83581B0E7DC40B8469ABF413ED5CF4271CBDC497D340D907598D";
    public const byte CursorTargetOpcode = 0x69;
    public const int CursorTargetLength = 45;
    public const byte PetTargetOpcode = 0xB8;
    public const int PetTargetLength = 5;
    public const byte PlayerDerivedStatsOpcode = 0x2C;
    public const int PlayerDerivedStatsLength = 31;

    public static LegacyPublicBetaWireResult<LegacyPublicBetaPkCursorTargetRequest> DecodeCursorTarget(ReadOnlySpan<byte> record)
    {
        if (record.Length != CursorTargetLength)
        {
            return LegacyPublicBetaWireResult<LegacyPublicBetaPkCursorTargetRequest>.Failure(
                LegacyPublicBetaWireResultCode.InvalidLength, "wire.legacy_beta.pk_cursor.length_invalid");
        }

        if (record[0] != CursorTargetOpcode)
        {
            return LegacyPublicBetaWireResult<LegacyPublicBetaPkCursorTargetRequest>.Failure(
                LegacyPublicBetaWireResultCode.InvalidOpcode, "wire.legacy_beta.pk_cursor.opcode_invalid");
        }

        return LegacyPublicBetaWireResult<LegacyPublicBetaPkCursorTargetRequest>.Success(
            new LegacyPublicBetaPkCursorTargetRequest(
                BinaryPrimitives.ReadUInt32LittleEndian(record[1..]),
                BinaryPrimitives.ReadUInt32LittleEndian(record[5..]),
                record.Slice(9, 16).ToArray(),
                record.Slice(25, 16).ToArray(),
                record.Slice(41, 3).ToArray(),
                record[44]));
    }

    public static ReadOnlyMemory<byte> EncodeCursorTarget(LegacyPublicBetaPkCursorTargetRequest request)
    {
        var record = new byte[CursorTargetLength];
        record[0] = CursorTargetOpcode;
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(1), request.SourceActorId);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(5), request.TargetActorId);
        CopyExact(request.SourceLabelRaw, record.AsSpan(9, 16), nameof(request.SourceLabelRaw));
        CopyExact(request.TargetLabelRaw, record.AsSpan(25, 16), nameof(request.TargetLabelRaw));
        CopyExact(request.ReservedRaw, record.AsSpan(41, 3), nameof(request.ReservedRaw));
        record[44] = request.Mode;
        return record;
    }

    public static LegacyPublicBetaWireResult<uint> DecodePetTarget(ReadOnlySpan<byte> record)
    {
        if (record.Length != PetTargetLength)
        {
            return LegacyPublicBetaWireResult<uint>.Failure(LegacyPublicBetaWireResultCode.InvalidLength, "wire.legacy_beta.pet_pk.length_invalid");
        }

        return record[0] == PetTargetOpcode
            ? LegacyPublicBetaWireResult<uint>.Success(BinaryPrimitives.ReadUInt32LittleEndian(record[1..]))
            : LegacyPublicBetaWireResult<uint>.Failure(LegacyPublicBetaWireResultCode.InvalidOpcode, "wire.legacy_beta.pet_pk.opcode_invalid");
    }

    public static ReadOnlyMemory<byte> EncodePetTarget(uint targetActorId)
    {
        var record = new byte[PetTargetLength];
        record[0] = PetTargetOpcode;
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(1), targetActorId);
        return record;
    }

    public static LegacyPublicBetaWireResult<LegacyPublicBetaPlayerDerivedStats> DecodePlayerDerivedStats(ReadOnlySpan<byte> record)
    {
        if (record.Length != PlayerDerivedStatsLength)
        {
            return LegacyPublicBetaWireResult<LegacyPublicBetaPlayerDerivedStats>.Failure(
                LegacyPublicBetaWireResultCode.InvalidLength, "wire.legacy_beta.derived_stats.length_invalid");
        }

        if (record[0] != PlayerDerivedStatsOpcode)
        {
            return LegacyPublicBetaWireResult<LegacyPublicBetaPlayerDerivedStats>.Failure(
                LegacyPublicBetaWireResultCode.InvalidOpcode, "wire.legacy_beta.derived_stats.opcode_invalid");
        }

        var elements = new ushort[5];
        for (var index = 0; index < elements.Length; index++)
        {
            elements[index] = BinaryPrimitives.ReadUInt16LittleEndian(record[(9 + index * 2)..]);
        }

        return LegacyPublicBetaWireResult<LegacyPublicBetaPlayerDerivedStats>.Success(
            new LegacyPublicBetaPlayerDerivedStats(
                BinaryPrimitives.ReadInt16LittleEndian(record[1..]),
                BinaryPrimitives.ReadInt16LittleEndian(record[3..]),
                BinaryPrimitives.ReadInt16LittleEndian(record[5..]),
                BinaryPrimitives.ReadInt16LittleEndian(record[7..]),
                new ReadOnlyCollection<ushort>(elements),
                BinaryPrimitives.ReadUInt16LittleEndian(record[19..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[21..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[23..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[25..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[27..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[29..])));
    }

    public static ReadOnlyMemory<byte> EncodePlayerDerivedStats(LegacyPublicBetaPlayerDerivedStats stats)
    {
        if (stats.ElementsGoldWoodWaterFireEarth.Count != 5)
        {
            throw new ArgumentException("The public-beta derived-stat record requires exactly five elemental values.", nameof(stats));
        }

        var record = new byte[PlayerDerivedStatsLength];
        record[0] = PlayerDerivedStatsOpcode;
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(1), stats.VitalityModifier);
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(3), stats.StrengthModifier);
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(5), stats.IntelligenceModifier);
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(7), stats.SpeedModifier);
        for (var index = 0; index < stats.ElementsGoldWoodWaterFireEarth.Count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(9 + index * 2), stats.ElementsGoldWoodWaterFireEarth[index]);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(19), stats.PositiveMask);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(21), stats.NegativeMask);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(23), stats.PhysicalDefense);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(25), stats.PhysicalAttack);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(27), stats.MagicalDefense);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(29), stats.MagicalAttack);
        return record;
    }

    private static void CopyExact(ReadOnlyMemory<byte> source, Span<byte> destination, string parameterName)
    {
        if (source.Length != destination.Length)
        {
            throw new ArgumentException($"{parameterName} must contain exactly {destination.Length} bytes.", parameterName);
        }

        source.Span.CopyTo(destination);
    }
}
