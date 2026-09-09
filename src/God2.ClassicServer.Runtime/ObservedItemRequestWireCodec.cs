using System.Buffers.Binary;
using System.Security.Cryptography;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum ObservedItemRequestWireResultCode
{
    Success,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    Malformed,
    SlotOutOfRange
}

public sealed record ObservedItemRequestWireResult<T>(
    ObservedItemRequestWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool Succeeded => Code == ObservedItemRequestWireResultCode.Success;

    public static ObservedItemRequestWireResult<T> Success(T value) =>
        new(ObservedItemRequestWireResultCode.Success, value, string.Empty);

    public static ObservedItemRequestWireResult<T> Failure(
        ObservedItemRequestWireResultCode code,
        string failureCode) =>
        new(code, default, failureCode);
}

public sealed record ObservedItemRequest(
    ushort ClientItemId,
    byte SlotIndex,
    byte RawTail,
    string DecodedFrameSha256,
    string EvidenceClassification,
    string EvidenceId);

/// <summary>
/// Read-only decoder for the exact-build-static and exact-build-observed C2S
/// 0x25 request shape. Exact senders prove item_id:u16le and slot:u8. They do
/// not consistently write the fourth payload byte, so RawTail stays opaque.
/// The cross-version self-use interpretation is corroboration only. This codec
/// deliberately exposes neither a response serializer nor a mutation path.
/// </summary>
public static class ObservedItemRequestWireCodec
{
    public const string ClientBuildId = OfficialNpcInteractionWireCodec.ClientBuildId;
    public const string EvidenceClassification =
        "ExactBuildStaticAndObserved_CrossVersionSemanticCorroboration";
    public const string EvidenceId =
        "ItemRequestExactBuildStaticEvidence/20260813:0x25-rva-AEF80-AF7C1;" +
        "OfficialProtocolCompletion/God2Evidence.20260807.112752:0x25-count3-length8-response0x3F;" +
        "XJZ_Evidence_Package_20260813_FINAL_V4:item-request-dataflow-evidence.json;" +
        "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
        ":legacy_mission_protocol/interaction25-u16-byte-opaque-tail-corroboration";
    public const byte Opcode = 0x25;
    public const int FrameLength = 8;
    public const byte MaximumSlotIndex = 49;

    public static ObservedItemRequestWireResult<ObservedItemRequest> Decode(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return Failure(
                ObservedItemRequestWireResultCode.BuildMismatch,
                "wire.item_request_observed.build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return Failure(
                ObservedItemRequestWireResultCode.InvalidState,
                "wire.item_request_observed.state_invalid");
        }

        if (decodedFrame.Length != FrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != FrameLength)
        {
            return Failure(
                ObservedItemRequestWireResultCode.InvalidLength,
                "wire.item_request_observed.length_invalid");
        }

        if (decodedFrame[2] != Opcode)
        {
            return Failure(
                ObservedItemRequestWireResultCode.InvalidOpcode,
                "wire.item_request_observed.opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return Failure(
                ObservedItemRequestWireResultCode.Malformed,
                "wire.item_request_observed.integrity_invalid");
        }

        var clientItemId = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[3..]);
        var slotIndex = decodedFrame[5];
        var rawTail = decodedFrame[6];
        if (clientItemId == 0)
        {
            return Failure(
                ObservedItemRequestWireResultCode.Malformed,
                "wire.item_request_observed.item_invalid");
        }

        if (slotIndex > MaximumSlotIndex)
        {
            return Failure(
                ObservedItemRequestWireResultCode.SlotOutOfRange,
                "wire.item_request_observed.slot_out_of_range");
        }

        return ObservedItemRequestWireResult<ObservedItemRequest>.Success(
            new ObservedItemRequest(
                clientItemId,
                slotIndex,
                rawTail,
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                EvidenceClassification,
                EvidenceId));
    }

    private static ObservedItemRequestWireResult<ObservedItemRequest> Failure(
        ObservedItemRequestWireResultCode code,
        string failureCode) =>
        ObservedItemRequestWireResult<ObservedItemRequest>.Failure(code, failureCode);
}
