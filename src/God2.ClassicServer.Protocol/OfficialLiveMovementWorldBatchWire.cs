using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialLiveMovementWorldBatchResultCode
{
    LayoutDecoded,
    InvalidLength,
    InvalidOpcode,
    InvalidChecksum,
    InvalidChildOpcode,
    TruncatedChild
}

public sealed record OfficialLiveMovementWorldBatchResult<T>(
    OfficialLiveMovementWorldBatchResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool LayoutDecoded => Code == OfficialLiveMovementWorldBatchResultCode.LayoutDecoded;

    public static OfficialLiveMovementWorldBatchResult<T> Success(T value) =>
        new(OfficialLiveMovementWorldBatchResultCode.LayoutDecoded, value, string.Empty);

    public static OfficialLiveMovementWorldBatchResult<T> Failure(
        OfficialLiveMovementWorldBatchResultCode code,
        string failureCode) =>
        new(code, default, failureCode);
}

public sealed record OfficialLiveClientMovementCandidate(
    ushort CandidateXOrPathField,
    ushort CandidateYOrPathField,
    ushort CandidateSequenceOrFlags,
    byte FixedByte4,
    byte FixedByte6,
    byte FixedByte8,
    string DecodedFrameSha256,
    string RawFrameHex,
    string FieldLayoutEvidence,
    string SemanticEvidenceStatus,
    bool RuntimeMutationAllowed);

public sealed record OfficialLiveMovementSidebandCandidate(
    byte Opcode,
    int Length,
    string DecodedFrameSha256,
    string RawFrameHex,
    string FieldLayoutEvidence,
    string SemanticEvidenceStatus,
    bool RuntimeMutationAllowed);

public sealed record OfficialLiveWorldApplicationChildRecord(
    byte Opcode,
    int Offset,
    int Length,
    string RawHex,
    string SemanticEvidenceStatus);

public sealed record OfficialLiveWorldApplicationBatchCandidate(
    byte TopLevelOpcodeCandidate,
    IReadOnlyList<OfficialLiveWorldApplicationChildRecord> Children,
    string DecodedFrameSha256,
    string RawFrameHex,
    string FieldLayoutEvidence,
    string SemanticEvidenceStatus,
    bool RuntimeMutationAllowed);

/// <summary>
/// Parser-only boundary for official live 2026-08-18 town movement captures.
/// This codec deliberately preserves raw frames and child records without applying
/// movement, spawn, position, despawn, acknowledgement, or world-state mutation.
/// Sweep9-13 displacement-threshold evidence shows that elevated click markers
/// are not sufficient proof of movement: preserve actual 0x2E/0x33 frames and
/// never infer movement intent from automation markers alone.
/// </summary>
public static class OfficialLiveMovementWorldBatchWireCodec
{
    public const string ClientBuildId = "god2-opt-6b127086e0c0";
    public const string ClientSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
    public const string EvidenceManifest =
        "Artifacts/ClientInstrumentation/OfficialEvidenceLauncher/8cb0c67c-740e-4246-bd4e-810b703e8d87/auto-campaign-20260818-151602/official-live-movement-evidence-manifest-20260818.json";
    public const string FieldLayoutEvidence =
        "official-live-town-ground-move2/sweep3/sweep4-20260818;" +
        "server-parser-cut-from-official-live-movement-20260818.md;" +
        "official-live-movement-server-gap-matrix-20260818.md;" +
        "official-live-safe-input-sweep14-31-standalone-0x2e-rollup-20260818.md;" +
        "safe-town-ground-sweep32-20260818-1749-summary.json";
    public const byte ClientMovementOpcode = 0x2E;
    public const byte ClientMovementCompanionOpcode = 0x33;
    public const byte ClientStableSideband1DOpcode = 0x1D;
    public const byte ClientMovementSidebandOpcode = 0x6D;
    public const byte ClientStableSideband72Opcode = 0x72;
    public const byte ClientHeartbeatOrTick30Opcode = 0x30;
    public const byte EvidenceOnlyServerChild5COpcode = 0x5C;
    public const byte EvidenceOnlyServerChild73Opcode = 0x73;
    public const byte EvidenceOnlyServerChild5DOpcode = 0x5D;
    public const byte EvidenceOnlyServerChild5FOpcode = 0x5F;
    public const byte EvidenceOnlyServerChild71Opcode = 0x71;
    public const byte EvidenceOnlyServerChild72Opcode = 0x72;
    public const byte EvidenceOnlyServerChild74Opcode = 0x74;
    public const byte EvidenceOnlyServerChild75Opcode = 0x75;
    public const byte EvidenceOnlyServerChild82Opcode = 0x82;
    public const byte EvidenceOnlyServerChild86Opcode = 0x86;
    public const int ClientMovementFrameLength = 10;
    public const int ClientStableSidebandFrameLength = 5;
    public const int ClientMovementSidebandFrameLength = 5;
    public const bool RuntimeMutationEnabled = false;

    public static OfficialLiveMovementWorldBatchResult<OfficialLiveClientMovementCandidate> DecodeClientMovementLayout(
        ReadOnlySpan<byte> decodedFrame)
    {
        var validation = ValidateFrame(decodedFrame, ClientMovementFrameLength, ClientMovementOpcode, "movement");
        if (validation is not null)
        {
            return OfficialLiveMovementWorldBatchResult<OfficialLiveClientMovementCandidate>.Failure(
                validation.Value.Code,
                validation.Value.FailureCode);
        }

        return OfficialLiveMovementWorldBatchResult<OfficialLiveClientMovementCandidate>.Success(
            new OfficialLiveClientMovementCandidate(
                BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[3..]),
                BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[5..]),
                BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[7..]),
                decodedFrame[4],
                decodedFrame[6],
                decodedFrame[8],
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                Convert.ToHexString(decodedFrame),
                FieldLayoutEvidence,
                "FieldLayoutRecovered_FieldSemanticsEvidenceBlocked",
                RuntimeMutationAllowed: false));
    }

    public static OfficialLiveMovementWorldBatchResult<OfficialLiveMovementSidebandCandidate> DecodeClientMovementSidebandLayout(
        ReadOnlySpan<byte> decodedFrame)
    {
        if (decodedFrame.Length < 3)
        {
            return OfficialLiveMovementWorldBatchResult<OfficialLiveMovementSidebandCandidate>.Failure(
                OfficialLiveMovementWorldBatchResultCode.InvalidLength,
                "wire.live_movement_sideband.length_invalid");
        }

        var opcode = decodedFrame[2];
        var expectedLength = GetClientParserOnlyFrameLength(opcode);
        if (expectedLength == 0)
        {
            return OfficialLiveMovementWorldBatchResult<OfficialLiveMovementSidebandCandidate>.Failure(
                OfficialLiveMovementWorldBatchResultCode.InvalidOpcode,
                "wire.live_movement_sideband.opcode_invalid");
        }

        var validation = ValidateFrame(decodedFrame, expectedLength, opcode, "movement_sideband");
        if (validation is not null)
        {
            return OfficialLiveMovementWorldBatchResult<OfficialLiveMovementSidebandCandidate>.Failure(
                validation.Value.Code,
                validation.Value.FailureCode);
        }

        return OfficialLiveMovementWorldBatchResult<OfficialLiveMovementSidebandCandidate>.Success(
            new OfficialLiveMovementSidebandCandidate(
                opcode,
                expectedLength,
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                Convert.ToHexString(decodedFrame),
                FieldLayoutEvidence,
                "SidebandLayoutRecovered_SemanticsEvidenceBlocked",
                RuntimeMutationAllowed: false));
    }

    public static OfficialLiveMovementWorldBatchResult<OfficialLiveWorldApplicationBatchCandidate> DecodeServerWorldApplicationBatchLayout(
        ReadOnlySpan<byte> decodedFrame)
    {
        if (decodedFrame.Length < 5 ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != decodedFrame.Length)
        {
            return OfficialLiveMovementWorldBatchResult<OfficialLiveWorldApplicationBatchCandidate>.Failure(
                OfficialLiveMovementWorldBatchResultCode.InvalidLength,
                "wire.live_world_batch.length_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return OfficialLiveMovementWorldBatchResult<OfficialLiveWorldApplicationBatchCandidate>.Failure(
                OfficialLiveMovementWorldBatchResultCode.InvalidChecksum,
                "wire.live_world_batch.checksum_invalid");
        }

        var topLevelOpcode = decodedFrame[2];
        if (IsKnownEvidenceOnlyServerChildOpcode(topLevelOpcode))
        {
            var topLevelChild = new OfficialLiveWorldApplicationChildRecord(
                topLevelOpcode,
                Offset: 0,
                decodedFrame.Length,
                Convert.ToHexString(decodedFrame),
                "LengthPrefixedTopLevelFrameRecovered_SemanticsEvidenceBlocked");
            return OfficialLiveMovementWorldBatchResult<OfficialLiveWorldApplicationBatchCandidate>.Success(
                new OfficialLiveWorldApplicationBatchCandidate(
                    topLevelOpcode,
                    new ReadOnlyCollection<OfficialLiveWorldApplicationChildRecord>([topLevelChild]),
                    Convert.ToHexString(SHA256.HashData(decodedFrame)),
                    Convert.ToHexString(decodedFrame),
                    FieldLayoutEvidence,
                    "WorldApplicationBatchLayoutRecovered_AllMutationEvidenceBlocked",
                    RuntimeMutationAllowed: false));
        }

        var children = new List<OfficialLiveWorldApplicationChildRecord>();
        var cursor = 2;
        var payloadEnd = decodedFrame.Length - 1;
        while (cursor < payloadEnd)
        {
            if (TryReadLengthPrefixedServerChild(decodedFrame, cursor, payloadEnd, out var lengthPrefixedChild))
            {
                children.Add(lengthPrefixedChild);
                cursor += lengthPrefixedChild.Length;
                continue;
            }

            var opcode = decodedFrame[cursor];
            var childLength = GetServerChildLength(opcode);
            if (childLength == 0)
            {
                return OfficialLiveMovementWorldBatchResult<OfficialLiveWorldApplicationBatchCandidate>.Failure(
                    OfficialLiveMovementWorldBatchResultCode.InvalidChildOpcode,
                    "wire.live_world_batch.child_opcode_invalid");
            }

            if (cursor + childLength > payloadEnd)
            {
                return OfficialLiveMovementWorldBatchResult<OfficialLiveWorldApplicationBatchCandidate>.Failure(
                    OfficialLiveMovementWorldBatchResultCode.TruncatedChild,
                    "wire.live_world_batch.child_truncated");
            }

            children.Add(new OfficialLiveWorldApplicationChildRecord(
                opcode,
                cursor,
                childLength,
                Convert.ToHexString(decodedFrame.Slice(cursor, childLength)),
                "ChildLayoutRecovered_SemanticsEvidenceBlocked"));
            cursor += childLength;
        }

        return OfficialLiveMovementWorldBatchResult<OfficialLiveWorldApplicationBatchCandidate>.Success(
            new OfficialLiveWorldApplicationBatchCandidate(
                children.Count == 0 ? decodedFrame[2] : children[0].Opcode,
                new ReadOnlyCollection<OfficialLiveWorldApplicationChildRecord>(children),
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                Convert.ToHexString(decodedFrame),
                FieldLayoutEvidence,
                "WorldApplicationBatchLayoutRecovered_AllMutationEvidenceBlocked",
                RuntimeMutationAllowed: false));
    }

    private static (OfficialLiveMovementWorldBatchResultCode Code, string FailureCode)? ValidateFrame(
        ReadOnlySpan<byte> decodedFrame,
        int expectedLength,
        byte expectedOpcode,
        string family)
    {
        if (decodedFrame.Length != expectedLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != expectedLength)
        {
            return (OfficialLiveMovementWorldBatchResultCode.InvalidLength, $"wire.live_{family}.length_invalid");
        }

        if (decodedFrame[2] != expectedOpcode)
        {
            return (OfficialLiveMovementWorldBatchResultCode.InvalidOpcode, $"wire.live_{family}.opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return (OfficialLiveMovementWorldBatchResultCode.InvalidChecksum, $"wire.live_{family}.checksum_invalid");
        }

        return null;
    }

    private static int GetServerChildLength(byte opcode) =>
        opcode switch
        {
            EvidenceOnlyServerChild75Opcode => 5,
            EvidenceOnlyServerChild5COpcode => 32,
            EvidenceOnlyServerChild73Opcode => 8,
            EvidenceOnlyServerChild72Opcode => 21,
            0x60 => 21,
            EvidenceOnlyServerChild5DOpcode => 2,
            0x36 => 11,
            _ => 0
        };

    private static bool TryReadLengthPrefixedServerChild(
        ReadOnlySpan<byte> decodedFrame,
        int cursor,
        int payloadEnd,
        out OfficialLiveWorldApplicationChildRecord child)
    {
        child = default!;
        if (cursor + 4 > payloadEnd)
        {
            return false;
        }

        var childLength = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[cursor..]);
        if (childLength < 4 ||
            cursor + childLength > payloadEnd)
        {
            return false;
        }

        var childFrame = decodedFrame.Slice(cursor, childLength);
        if (childFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(childFrame))
        {
            return false;
        }

        var opcode = childFrame[2];
        if (!IsKnownEvidenceOnlyServerChildOpcode(opcode))
        {
            return false;
        }

        child = new OfficialLiveWorldApplicationChildRecord(
            opcode,
            cursor,
            childLength,
            Convert.ToHexString(childFrame),
            "LengthPrefixedChildLayoutRecovered_SemanticsEvidenceBlocked");
        return true;
    }

    private static bool IsKnownEvidenceOnlyServerChildOpcode(byte opcode) =>
        opcode is 0x36 or
            EvidenceOnlyServerChild5COpcode or
            EvidenceOnlyServerChild5DOpcode or
            EvidenceOnlyServerChild5FOpcode or
            0x60 or
            EvidenceOnlyServerChild71Opcode or
            EvidenceOnlyServerChild72Opcode or
            EvidenceOnlyServerChild73Opcode or
            EvidenceOnlyServerChild74Opcode or
            EvidenceOnlyServerChild75Opcode or
            EvidenceOnlyServerChild82Opcode or
            EvidenceOnlyServerChild86Opcode;

    private static int GetClientParserOnlyFrameLength(byte opcode) =>
        opcode switch
        {
            ClientMovementCompanionOpcode => ClientMovementFrameLength,
            ClientStableSideband1DOpcode => ClientStableSidebandFrameLength,
            ClientStableSideband72Opcode => ClientStableSidebandFrameLength,
            ClientMovementSidebandOpcode => ClientMovementSidebandFrameLength,
            ClientHeartbeatOrTick30Opcode => ClientStableSidebandFrameLength,
            _ => 0
        };

    public static OfficialLiveMovementEvidenceRecord CreateClientMovementEvidenceRecord(
        OfficialLiveClientMovementCandidate candidate,
        string runId,
        string markerName,
        long observedAtUnixMs,
        string sourceFrameId,
        string? pairingKey = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return OfficialLiveMovementEvidenceRecordFactory.CreateClientFrame(
            ClientMovementOpcode,
            Convert.FromHexString(candidate.RawFrameHex),
            runId,
            markerName,
            observedAtUnixMs,
            sourceFrameId,
            pairingKey);
    }

    public static OfficialLiveMovementEvidenceRecord CreateClientSidebandEvidenceRecord(
        OfficialLiveMovementSidebandCandidate candidate,
        string runId,
        string markerName,
        long observedAtUnixMs,
        string sourceFrameId,
        string? pairingKey = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return OfficialLiveMovementEvidenceRecordFactory.CreateClientFrame(
            candidate.Opcode,
            Convert.FromHexString(candidate.RawFrameHex),
            runId,
            markerName,
            observedAtUnixMs,
            sourceFrameId,
            pairingKey);
    }

    public static IReadOnlyList<OfficialLiveMovementEvidenceRecord> CreateServerChildEvidenceRecords(
        OfficialLiveWorldApplicationBatchCandidate candidate,
        string runId,
        string markerName,
        long observedAtUnixMs,
        string sourceFrameId)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var decodedFrame = Convert.FromHexString(candidate.RawFrameHex);
        var records = new List<OfficialLiveMovementEvidenceRecord>(candidate.Children.Count);
        foreach (var child in candidate.Children)
        {
            records.Add(OfficialLiveMovementEvidenceRecordFactory.CreateServerChild(
                candidate.TopLevelOpcodeCandidate,
                child.Opcode,
                child.Offset,
                Convert.FromHexString(child.RawHex),
                decodedFrame,
                runId,
                markerName,
                observedAtUnixMs,
                sourceFrameId));
        }

        return records;
    }
}
