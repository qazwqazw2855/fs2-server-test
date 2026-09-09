using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialPortalWireResultCode
{
    Success,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    EvidenceMismatch,
    Malformed,
    UnsupportedDestination
}

public sealed record OfficialPortalWireResult<T>(
    OfficialPortalWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool Succeeded => Code == OfficialPortalWireResultCode.Success;

    public static OfficialPortalWireResult<T> Success(T value) =>
        new(OfficialPortalWireResultCode.Success, value, string.Empty);

    public static OfficialPortalWireResult<T> Failure(
        OfficialPortalWireResultCode code,
        string failureCode) =>
        new(code, default, failureCode);
}

public sealed record OfficialPortalActivateCommand(
    string ClientBuildId,
    byte Opcode,
    ReadOnlyMemory<byte> PreservedActivationRegion,
    string DecodedRequestSha256,
    string EvidenceId);

public sealed record OfficialPortalDestination(
    int RuntimeMapId,
    byte AreaId,
    ushort ClientMapId,
    ushort X,
    ushort Y,
    byte PositionMode,
    byte PreludeState,
    ushort PreservedOpaqueWord,
    string EvidenceId,
    bool MapTransitionFirst = false);

public sealed record OfficialPortalAuthoritativeResult(
    string ClientBuildId,
    int RuntimeMapId,
    int X,
    int Y,
    string ResultCode,
    string CorrelationId);

public sealed record OfficialPortalDecodedResult(
    byte PreludeState,
    byte AreaId,
    ushort ClientMapId,
    ushort X,
    ushort Y,
    byte PositionMode,
    ushort PreservedOpaqueWord);

public sealed record OfficialPortalSerializedFrames(
    ReadOnlyMemory<byte> PreludeDecodedFrame,
    ReadOnlyMemory<byte> MapTransitionDecodedFrame,
    OfficialPortalDestination Destination)
{
    public IReadOnlyList<ReadOnlyMemory<byte>> OrderedDecodedFrames =>
        Destination.MapTransitionFirst
            ? new ReadOnlyCollection<ReadOnlyMemory<byte>>([MapTransitionDecodedFrame, PreludeDecodedFrame])
            : new ReadOnlyCollection<ReadOnlyMemory<byte>>([PreludeDecodedFrame, MapTransitionDecodedFrame]);
}

/// <summary>
/// Build-locked semantic codec for the first verified post-world gameplay family.
/// Session encryption/framing remains owned by the existing Phase 1 world codec.
/// </summary>
public static class OfficialPortalWireCodec
{
    public const string ClientBuildId = "god2-opt-6b127086e0c0";
    public const string ClientExeSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
    public const string EvidenceId = "PortalMapTransfer-attempt-42950-trace-six-round-trips";
    public const string ClientReceiveHandlerEvidence = "God2_opt+rva-0x0008ED67->rva-0x0008D710";
    public const string ClientPositionConsumerEvidence =
        "God2_opt+rva-0x0008D710: mode=packed&3; x=(packed>>2)&0x7FFF; y=packed>>17";
    public const string ClientRequestBuilderEvidence = "God2_opt+rva-0x004AD5FC";
    public const byte ActivateOpcode = 0x0C;
    public const byte PreludeOpcode = 0xBB;
    public const byte MapTransitionOpcode = 0x61;
    public const ushort PreservedOpaqueWord = 0x0404;

    private static readonly byte[] ProvenActivationRegion = Convert.FromHexString("A82734FC9C");

    private static readonly IReadOnlyDictionary<(ushort ClientMapId, byte AreaId), OfficialPortalDestination> Destinations =
        new ReadOnlyDictionary<(ushort ClientMapId, byte AreaId), OfficialPortalDestination>(
            new Dictionary<(ushort ClientMapId, byte AreaId), OfficialPortalDestination>
            {
                [(0, 2)] = new(0, 2, 0, 283, 453, 1, 0x88, 0x0202,
                    "PortalCapture/portal-biyou-return-20260811-153414", MapTransitionFirst: true),
                [(0, 15)] = new(170015000, 15, 0, 248, 247, 1, 0x00, 0x0F0F,
                    "LiveRecovery/Stage3-source-world-identity-sequence-14880", MapTransitionFirst: true),
                [(3, 4)] = new(3, 4, 3, 196, 139, 0, 0x00, PreservedOpaqueWord, EvidenceId),
                [(7, 15)] = new(7, 15, 7, 48, 81, 0, 0x00, 0x0F0F,
                    "LiveRecovery/Stage3-CGC-S3-portal-map0-area15-to-map7-area15", MapTransitionFirst: true),
                [(19, 4)] = new(19, 4, 19, 28, 34, 2, 0x88, PreservedOpaqueWord, EvidenceId),
                [(43, 2)] = new(43, 2, 43, 52, 184, 3, 0x00, 0x0202,
                    "PortalCapture/portal-next-elevated-20260811-152341", MapTransitionFirst: true)
            });

    public static IReadOnlyCollection<OfficialPortalDestination> SupportedDestinations => Destinations.Values.ToArray();

    public static OfficialPortalWireResult<OfficialPortalActivateCommand> DecodeActivate(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialPortalWireResult<OfficialPortalActivateCommand>.Failure(
                OfficialPortalWireResultCode.BuildMismatch,
                "wire.portal.client_build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return OfficialPortalWireResult<OfficialPortalActivateCommand>.Failure(
                OfficialPortalWireResultCode.InvalidState,
                "wire.portal.state_invalid");
        }

        if (decodedFrame.Length != 8 || decodedFrame[0] != 8 || decodedFrame[1] != 0)
        {
            return OfficialPortalWireResult<OfficialPortalActivateCommand>.Failure(
                OfficialPortalWireResultCode.InvalidLength,
                "wire.portal.activate_length_invalid");
        }

        if (decodedFrame[2] != ActivateOpcode)
        {
            return OfficialPortalWireResult<OfficialPortalActivateCommand>.Failure(
                OfficialPortalWireResultCode.InvalidOpcode,
                "wire.portal.activate_opcode_invalid");
        }

        if (!decodedFrame[3..].SequenceEqual(ProvenActivationRegion))
        {
            return OfficialPortalWireResult<OfficialPortalActivateCommand>.Failure(
                OfficialPortalWireResultCode.EvidenceMismatch,
                "wire.portal.activate_preserved_region_mismatch");
        }

        return OfficialPortalWireResult<OfficialPortalActivateCommand>.Success(
            new OfficialPortalActivateCommand(
                ClientBuildId,
                ActivateOpcode,
                ProvenActivationRegion.ToArray(),
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                EvidenceId));
    }

    public static OfficialPortalWireResult<ReadOnlyMemory<byte>> SerializeActivate(
        OfficialPortalActivateCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!string.Equals(command.ClientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialPortalWireResult<ReadOnlyMemory<byte>>.Failure(
                OfficialPortalWireResultCode.BuildMismatch,
                "wire.portal.client_build_mismatch");
        }

        if (command.Opcode != ActivateOpcode ||
            !command.PreservedActivationRegion.Span.SequenceEqual(ProvenActivationRegion))
        {
            return OfficialPortalWireResult<ReadOnlyMemory<byte>>.Failure(
                OfficialPortalWireResultCode.EvidenceMismatch,
                "wire.portal.activate_model_evidence_mismatch");
        }

        var frame = new byte[8];
        frame[0] = 8;
        frame[2] = ActivateOpcode;
        ProvenActivationRegion.CopyTo(frame, 3);
        return OfficialPortalWireResult<ReadOnlyMemory<byte>>.Success(frame);
    }

    public static OfficialPortalWireResult<OfficialPortalSerializedFrames> SerializeResult(
        OfficialPortalAuthoritativeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!string.Equals(result.ClientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialPortalWireResult<OfficialPortalSerializedFrames>.Failure(
                OfficialPortalWireResultCode.BuildMismatch,
                "wire.portal.client_build_mismatch");
        }

        if (!string.Equals(result.ResultCode, "Success", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(result.CorrelationId))
        {
            return OfficialPortalWireResult<OfficialPortalSerializedFrames>.Failure(
                OfficialPortalWireResultCode.Malformed,
                "wire.portal.authoritative_result_invalid");
        }

        if (result.RuntimeMapId is < 0 or > ushort.MaxValue)
        {
            return OfficialPortalWireResult<OfficialPortalSerializedFrames>.Failure(
                OfficialPortalWireResultCode.UnsupportedDestination,
                "wire.portal.destination_not_verified");
        }

        return SerializeVerifiedClientDestination(
            (ushort)result.RuntimeMapId,
            4,
            result.X,
            result.Y);
    }

    public static OfficialPortalWireResult<OfficialPortalSerializedFrames> SerializeVerifiedClientDestination(
        ushort clientMapId,
        byte clientAreaId,
        int x,
        int y)
    {
        if (!Destinations.TryGetValue((clientMapId, clientAreaId), out var destination) ||
            x != destination.X || y != destination.Y)
        {
            return OfficialPortalWireResult<OfficialPortalSerializedFrames>.Failure(
                OfficialPortalWireResultCode.UnsupportedDestination,
                "wire.portal.destination_not_verified");
        }

        return BuildFrames(destination);
    }

    /// <summary>
    /// Projects an authoritative initial-world position through the statically recovered
    /// 0x61 Client consumer. Map/resource identity and resource bounds are verified by the
    /// production world projector before this build-locked wire boundary is reached.
    /// </summary>
    public static OfficialPortalWireResult<OfficialPortalSerializedFrames> SerializeWorldProjectionDestination(
        string clientBuildId,
        ushort clientMapId,
        byte clientAreaId,
        int x,
        int y)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialPortalWireResult<OfficialPortalSerializedFrames>.Failure(
                OfficialPortalWireResultCode.BuildMismatch,
                "wire.world_projection.client_build_mismatch");
        }

        if (!Destinations.TryGetValue((clientMapId, clientAreaId), out var baseline))
        {
            return OfficialPortalWireResult<OfficialPortalSerializedFrames>.Failure(
                OfficialPortalWireResultCode.UnsupportedDestination,
                "wire.world_projection.map_identity_not_verified");
        }

        if (x is < 0 or > 0x7FFF || y is < 0 or > 0x7FFF)
        {
            return OfficialPortalWireResult<OfficialPortalSerializedFrames>.Failure(
                OfficialPortalWireResultCode.Malformed,
                "wire.world_projection.position_range_invalid");
        }

        return BuildFrames(baseline with
        {
            X = checked((ushort)x),
            Y = checked((ushort)y),
            EvidenceId = ClientPositionConsumerEvidence
        });
    }

    /// <summary>
    /// Test-only serializer used to verify the statically recovered position consumer with
    /// the automated official client. Production portal serialization remains locked to the
    /// two historically observed destinations in <see cref="SerializeResult"/>.
    /// </summary>
    public static OfficialPortalWireResult<OfficialPortalSerializedFrames> SerializeTestOnlyMap19PositionEvidence(
        string clientBuildId,
        int x,
        int y)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialPortalWireResult<OfficialPortalSerializedFrames>.Failure(
                OfficialPortalWireResultCode.BuildMismatch,
                "wire.portal.client_build_mismatch");
        }

        if (x is < 0 or > 0x7FFF || y is < 0 or > 0x7FFF)
        {
            return OfficialPortalWireResult<OfficialPortalSerializedFrames>.Failure(
                OfficialPortalWireResultCode.Malformed,
                "wire.portal.position_evidence_range_invalid");
        }

        var baseline = Destinations[(19, 4)];
        return BuildFrames(baseline with
        {
            X = checked((ushort)x),
            Y = checked((ushort)y),
            EvidenceId = ClientPositionConsumerEvidence
        });
    }

    private static OfficialPortalWireResult<OfficialPortalSerializedFrames> BuildFrames(
        OfficialPortalDestination destination)
    {
        var prelude = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(prelude, 6);
        prelude[2] = PreludeOpcode;
        prelude[3] = destination.PreludeState;
        prelude[4] = 0;
        prelude[5] = AdditiveChecksum(prelude.AsSpan(0, 5));

        var transition = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(transition, 12);
        transition[2] = MapTransitionOpcode;
        var packedMap = checked((ushort)((destination.ClientMapId << 6) | destination.AreaId));
        BinaryPrimitives.WriteUInt16LittleEndian(transition.AsSpan(3), packedMap);
        BinaryPrimitives.WriteUInt16LittleEndian(transition.AsSpan(5), destination.PreservedOpaqueWord);
        var packedPosition = (uint)(destination.PositionMode |
            ((uint)destination.X << 2) |
            ((uint)destination.Y << 17));
        BinaryPrimitives.WriteUInt32LittleEndian(transition.AsSpan(7), packedPosition);
        transition[11] = AdditiveChecksum(transition.AsSpan(0, 11));

        return OfficialPortalWireResult<OfficialPortalSerializedFrames>.Success(
            new OfficialPortalSerializedFrames(prelude, transition, destination));
    }

    public static OfficialPortalWireResult<OfficialPortalDecodedResult> DecodeResult(
        ReadOnlySpan<byte> prelude,
        ReadOnlySpan<byte> transition)
    {
        if (prelude.Length != 6 || transition.Length != 12 ||
            prelude[0] != 6 || prelude[1] != 0 ||
            transition[0] != 12 || transition[1] != 0)
        {
            return OfficialPortalWireResult<OfficialPortalDecodedResult>.Failure(
                OfficialPortalWireResultCode.InvalidLength,
                "wire.portal.result_length_invalid");
        }

        if (prelude[2] != PreludeOpcode || transition[2] != MapTransitionOpcode)
        {
            return OfficialPortalWireResult<OfficialPortalDecodedResult>.Failure(
                OfficialPortalWireResultCode.InvalidOpcode,
                "wire.portal.result_opcode_invalid");
        }

        if (prelude[4] != 0 ||
            prelude[5] != AdditiveChecksum(prelude[..5]) ||
            transition[11] != AdditiveChecksum(transition[..11]))
        {
            return OfficialPortalWireResult<OfficialPortalDecodedResult>.Failure(
                OfficialPortalWireResultCode.Malformed,
                "wire.portal.result_checksum_invalid");
        }

        var packedMap = BinaryPrimitives.ReadUInt16LittleEndian(transition[3..]);
        var areaId = (byte)(packedMap & 0x3F);
        var clientMapId = (ushort)(packedMap >> 6);
        var opaque = BinaryPrimitives.ReadUInt16LittleEndian(transition[5..]);
        var packedPosition = BinaryPrimitives.ReadUInt32LittleEndian(transition[7..]);
        var mode = (byte)(packedPosition & 0x03);
        var x = (ushort)((packedPosition >> 2) & 0x7FFF);
        var y = (ushort)((packedPosition >> 17) & 0x7FFF);

        var preludeState = prelude[3];
        var destination = Destinations.Values.FirstOrDefault(value =>
            value.AreaId == areaId &&
            value.ClientMapId == clientMapId &&
            value.X == x &&
            value.Y == y &&
            value.PositionMode == mode &&
            value.PreludeState == preludeState &&
            value.PreservedOpaqueWord == opaque);
        if (destination is null)
        {
            return OfficialPortalWireResult<OfficialPortalDecodedResult>.Failure(
                OfficialPortalWireResultCode.EvidenceMismatch,
                "wire.portal.result_fields_not_verified");
        }

        return OfficialPortalWireResult<OfficialPortalDecodedResult>.Success(
            new OfficialPortalDecodedResult(preludeState, areaId, clientMapId, x, y, mode, opaque));
    }

    public static byte AdditiveChecksum(ReadOnlySpan<byte> bytes)
    {
        var checksum = 0;
        foreach (var value in bytes)
        {
            checksum = (checksum + value + 0x3C) & 0xFF;
        }

        return (byte)checksum;
    }
}
