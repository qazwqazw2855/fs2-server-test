using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialBattleBatchWireResultCode
{
    LayoutDecoded,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidChecksum,
    UnsupportedOuterOpcode,
    UnsupportedInnerOpcode,
    TruncatedInnerRecord,
    InvalidBootstrapHeader,
    EmptyBatch,
    SerializerEvidenceBlocked
}

public sealed record OfficialBattleInnerRecord(byte Opcode, ReadOnlyMemory<byte> Bytes);

public sealed record OfficialBattleBatch(
    byte OuterOpcode,
    ReadOnlyMemory<byte> BootstrapHeader,
    IReadOnlyList<OfficialBattleInnerRecord> Records,
    string DecodedFrameSha256,
    bool RuntimeSerializerAllowed);

public sealed record OfficialBattleTypedBatch(
    OfficialBattleBatch Container,
    IReadOnlyList<OfficialBattleRosterRecord> Rosters,
    IReadOnlyList<OfficialBattleControlRecord> Controls,
    IReadOnlyList<OfficialBattleEffectCandidate> Effects,
    IReadOnlyList<OfficialBattleVitalSnapshotCandidate> VitalSnapshots,
    IReadOnlyList<OfficialBattleBoundaryRecord> Boundaries,
    IReadOnlyList<OfficialBattlePackedControl87> PackedControls,
    IReadOnlyList<OfficialBattleSettlementCandidate> Settlements,
    bool FullyTyped);

public sealed record OfficialBattleBatchWireResult<T>(
    OfficialBattleBatchWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool LayoutDecoded => Code == OfficialBattleBatchWireResultCode.LayoutDecoded;

    public static OfficialBattleBatchWireResult<T> Success(T value) =>
        new(OfficialBattleBatchWireResultCode.LayoutDecoded, value, string.Empty);

    public static OfficialBattleBatchWireResult<T> Failure(OfficialBattleBatchWireResultCode code, string failureCode) =>
        new(code, default, failureCode);
}

/// <summary>
/// Structural codec for current-build S2C battle containers observed during
/// controlled late participation. 0x86 containers begin directly with their
/// first 17-byte inner record. 0x82 bootstraps contain an eight-byte opaque,
/// losslessly preserved header before the inner record stream. This codec does
/// not invent the header fields or grant production serializer authority.
/// </summary>
public static class OfficialBattleBatchWireCodec
{
    public const string ClientBuildId = OfficialBattleCommandWireCodec.ClientBuildId;
    public const byte BootstrapOpcode = 0x82;
    public const byte IncrementalSyncOpcode = 0x86;
    public const int BootstrapHeaderLength = 8;
    public const bool RuntimeSerializerEnabled = false;

    private static readonly IReadOnlyDictionary<byte, int> InnerRecordLengths =
        new ReadOnlyDictionary<byte, int>(new Dictionary<byte, int>
        {
            [0x1C] = 45,
            [0x38] = 2,
            [0x83] = 15,
            [0x85] = 2,
            [0x86] = 17,
            [0x87] = 3,
            [0x88] = 121,
            [0x89] = 57
        });

    public static OfficialBattleBatchWireResult<OfficialBattleBatch> DecodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return Failure(OfficialBattleBatchWireResultCode.BuildMismatch, "wire.battle_batch.client_build_mismatch");
        }

        if (state is not (GameplayProtocolState.World or GameplayProtocolState.Battle))
        {
            return Failure(OfficialBattleBatchWireResultCode.InvalidState, "wire.battle_batch.state_invalid");
        }

        if (decodedFrame.Length < 5 || BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != decodedFrame.Length)
        {
            return Failure(OfficialBattleBatchWireResultCode.InvalidLength, "wire.battle_batch.length_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return Failure(OfficialBattleBatchWireResultCode.InvalidChecksum, "wire.battle_batch.checksum_invalid");
        }

        var outerOpcode = decodedFrame[2];
        var cursor = 2;
        ReadOnlyMemory<byte> bootstrapHeader = ReadOnlyMemory<byte>.Empty;
        if (outerOpcode == BootstrapOpcode)
        {
            if (decodedFrame.Length < 2 + 1 + BootstrapHeaderLength + 1)
            {
                return Failure(OfficialBattleBatchWireResultCode.InvalidBootstrapHeader, "wire.battle_batch.bootstrap_header_invalid");
            }

            bootstrapHeader = decodedFrame.Slice(3, BootstrapHeaderLength).ToArray();
            cursor = 3 + BootstrapHeaderLength;
        }
        else if (outerOpcode != IncrementalSyncOpcode)
        {
            return Failure(OfficialBattleBatchWireResultCode.UnsupportedOuterOpcode, "wire.battle_batch.outer_opcode_unsupported");
        }

        var records = new List<OfficialBattleInnerRecord>();
        var payloadEnd = decodedFrame.Length - 1;
        while (cursor < payloadEnd)
        {
            var opcode = decodedFrame[cursor];
            if (!InnerRecordLengths.TryGetValue(opcode, out var recordLength))
            {
                return Failure(OfficialBattleBatchWireResultCode.UnsupportedInnerOpcode, $"wire.battle_batch.inner_opcode_unsupported:{opcode:X2}");
            }

            if (cursor + recordLength > payloadEnd)
            {
                return Failure(OfficialBattleBatchWireResultCode.TruncatedInnerRecord, $"wire.battle_batch.inner_record_truncated:{opcode:X2}");
            }

            records.Add(new OfficialBattleInnerRecord(opcode, decodedFrame.Slice(cursor, recordLength).ToArray()));
            cursor += recordLength;
        }

        if (records.Count == 0)
        {
            return Failure(OfficialBattleBatchWireResultCode.EmptyBatch, "wire.battle_batch.empty");
        }

        return OfficialBattleBatchWireResult<OfficialBattleBatch>.Success(
            new OfficialBattleBatch(
                outerOpcode,
                bootstrapHeader,
                records.AsReadOnly(),
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                RuntimeSerializerAllowed: false));
    }

    public static OfficialBattleBatchWireResult<ReadOnlyMemory<byte>> EncodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        byte outerOpcode,
        ReadOnlySpan<byte> bootstrapHeader,
        IReadOnlyList<OfficialBattleInnerRecord> records)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialBattleBatchWireResult<ReadOnlyMemory<byte>>.Failure(
                OfficialBattleBatchWireResultCode.BuildMismatch, "wire.battle_batch.client_build_mismatch");
        }

        if (state is not (GameplayProtocolState.World or GameplayProtocolState.Battle))
        {
            return OfficialBattleBatchWireResult<ReadOnlyMemory<byte>>.Failure(
                OfficialBattleBatchWireResultCode.InvalidState, "wire.battle_batch.state_invalid");
        }

        if (outerOpcode is not (BootstrapOpcode or IncrementalSyncOpcode))
        {
            return OfficialBattleBatchWireResult<ReadOnlyMemory<byte>>.Failure(
                OfficialBattleBatchWireResultCode.UnsupportedOuterOpcode, "wire.battle_batch.outer_opcode_unsupported");
        }

        if ((outerOpcode == BootstrapOpcode && bootstrapHeader.Length != BootstrapHeaderLength) ||
            (outerOpcode == IncrementalSyncOpcode && bootstrapHeader.Length != 0))
        {
            return OfficialBattleBatchWireResult<ReadOnlyMemory<byte>>.Failure(
                OfficialBattleBatchWireResultCode.InvalidBootstrapHeader, "wire.battle_batch.bootstrap_header_invalid");
        }

        if (records.Count == 0 || (outerOpcode == IncrementalSyncOpcode && records[0].Opcode != IncrementalSyncOpcode))
        {
            return OfficialBattleBatchWireResult<ReadOnlyMemory<byte>>.Failure(
                OfficialBattleBatchWireResultCode.EmptyBatch, "wire.battle_batch.empty_or_outer_mismatch");
        }

        var recordBytes = 0;
        foreach (var record in records)
        {
            if (!InnerRecordLengths.TryGetValue(record.Opcode, out var expectedLength))
            {
                return OfficialBattleBatchWireResult<ReadOnlyMemory<byte>>.Failure(
                    OfficialBattleBatchWireResultCode.UnsupportedInnerOpcode, $"wire.battle_batch.inner_opcode_unsupported:{record.Opcode:X2}");
            }

            if (record.Bytes.Length != expectedLength || record.Bytes.Span[0] != record.Opcode)
            {
                return OfficialBattleBatchWireResult<ReadOnlyMemory<byte>>.Failure(
                    OfficialBattleBatchWireResultCode.TruncatedInnerRecord, $"wire.battle_batch.inner_record_invalid:{record.Opcode:X2}");
            }

            recordBytes = checked(recordBytes + record.Bytes.Length);
        }

        var prefixLength = outerOpcode == BootstrapOpcode ? 3 + BootstrapHeaderLength : 2;
        var frame = new byte[checked(prefixLength + recordBytes + 1)];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        var cursor = 2;
        if (outerOpcode == BootstrapOpcode)
        {
            frame[cursor++] = BootstrapOpcode;
            bootstrapHeader.CopyTo(frame.AsSpan(cursor));
            cursor += BootstrapHeaderLength;
        }

        foreach (var record in records)
        {
            record.Bytes.Span.CopyTo(frame.AsSpan(cursor));
            cursor += record.Bytes.Length;
        }

        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return OfficialBattleBatchWireResult<ReadOnlyMemory<byte>>.Success(frame);
    }

    public static OfficialBattleBatchWireResult<ReadOnlyMemory<byte>> EncodeForRuntime(
        string clientBuildId,
        GameplayProtocolState state,
        byte outerOpcode,
        ReadOnlySpan<byte> bootstrapHeader,
        IReadOnlyList<OfficialBattleInnerRecord> records)
    {
        var encoded = EncodeLayout(clientBuildId, state, outerOpcode, bootstrapHeader, records);
        return encoded.LayoutDecoded
            ? OfficialBattleBatchWireResult<ReadOnlyMemory<byte>>.Failure(
                OfficialBattleBatchWireResultCode.SerializerEvidenceBlocked,
                "wire.battle_batch.serializer_evidence_blocked")
            : encoded;
    }

    public static OfficialBattleBatchWireResult<OfficialBattleTypedBatch> DecodeTypedLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        var container = DecodeLayout(clientBuildId, state, decodedFrame);
        if (!container.LayoutDecoded || container.Value is null)
        {
            return OfficialBattleBatchWireResult<OfficialBattleTypedBatch>.Failure(container.Code, container.FailureCode);
        }

        var rosters = new List<OfficialBattleRosterRecord>();
        var controls = new List<OfficialBattleControlRecord>();
        var effects = new List<OfficialBattleEffectCandidate>();
        var snapshots = new List<OfficialBattleVitalSnapshotCandidate>();
        var boundaries = new List<OfficialBattleBoundaryRecord>();
        var packed = new List<OfficialBattlePackedControl87>();
        var settlements = new List<OfficialBattleSettlementCandidate>();
        foreach (var inner in container.Value.Records)
        {
            switch (inner.Opcode)
            {
                case 0x1C:
                {
                    var value = OfficialBattleRosterControlWireCodec.DecodeRoster(clientBuildId, GameplayProtocolState.Battle, inner.Bytes.Span);
                    if (!value.LayoutDecoded || value.Value is null) return TypedFailure(inner.Opcode, value.FailureCode);
                    rosters.Add(value.Value);
                    break;
                }
                case 0x38:
                case 0x85:
                {
                    var value = OfficialBattleRosterControlWireCodec.DecodeBoundary(clientBuildId, GameplayProtocolState.Battle, inner.Bytes.Span);
                    if (!value.LayoutDecoded || value.Value is null) return TypedFailure(inner.Opcode, value.FailureCode);
                    boundaries.Add(value.Value);
                    break;
                }
                case 0x83:
                {
                    var value = OfficialBattleEffectWireCodec.DecodeLayout(clientBuildId, GameplayProtocolState.Battle, inner.Bytes.Span);
                    if (!value.LayoutDecoded || value.Value is null) return TypedFailure(inner.Opcode, value.FailureCode);
                    effects.Add(value.Value);
                    break;
                }
                case 0x86:
                {
                    var value = OfficialBattleRosterControlWireCodec.DecodeControl(clientBuildId, GameplayProtocolState.Battle, inner.Bytes.Span);
                    if (!value.LayoutDecoded || value.Value is null) return TypedFailure(inner.Opcode, value.FailureCode);
                    controls.Add(value.Value);
                    break;
                }
                case 0x87:
                {
                    var value = OfficialBattleRosterControlWireCodec.DecodePackedControl87(clientBuildId, GameplayProtocolState.Battle, inner.Bytes.Span);
                    if (!value.LayoutDecoded || value.Value is null) return TypedFailure(inner.Opcode, value.FailureCode);
                    packed.Add(value.Value);
                    break;
                }
                case 0x88:
                {
                    var value = OfficialBattleVitalSnapshotWireCodec.DecodeLayout(clientBuildId, GameplayProtocolState.Battle, inner.Bytes.Span);
                    if (!value.LayoutDecoded || value.Value is null) return TypedFailure(inner.Opcode, value.FailureCode);
                    snapshots.Add(value.Value);
                    break;
                }
                case 0x89:
                {
                    var value = OfficialBattleSettlementWireCodec.DecodeLayout(
                        clientBuildId,
                        GameplayProtocolState.Battle,
                        inner.Bytes.Span);
                    if (!value.LayoutDecoded || value.Value is null) return TypedFailure(inner.Opcode, value.FailureCode);
                    settlements.Add(value.Value);
                    break;
                }
                default:
                    return TypedFailure(inner.Opcode, "wire.battle_batch.typed_opcode_unsupported");
            }
        }

        return OfficialBattleBatchWireResult<OfficialBattleTypedBatch>.Success(
            new OfficialBattleTypedBatch(
                container.Value,
                rosters.AsReadOnly(),
                controls.AsReadOnly(),
                effects.AsReadOnly(),
                snapshots.AsReadOnly(),
                boundaries.AsReadOnly(),
                packed.AsReadOnly(),
                settlements.AsReadOnly(),
                FullyTyped: true));
    }

    private static OfficialBattleBatchWireResult<OfficialBattleTypedBatch> TypedFailure(byte opcode, string failureCode) =>
        OfficialBattleBatchWireResult<OfficialBattleTypedBatch>.Failure(
            OfficialBattleBatchWireResultCode.UnsupportedInnerOpcode,
            $"{failureCode}:{opcode:X2}");

    private static OfficialBattleBatchWireResult<OfficialBattleBatch> Failure(
        OfficialBattleBatchWireResultCode code,
        string failureCode) => OfficialBattleBatchWireResult<OfficialBattleBatch>.Failure(code, failureCode);
}
