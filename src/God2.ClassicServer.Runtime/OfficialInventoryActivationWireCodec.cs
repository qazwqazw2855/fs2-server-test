using System.Buffers.Binary;
using System.Security.Cryptography;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum OfficialInventoryActivationWireResultCode
{
    Success,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    Malformed,
    SlotOutOfRange,
    UnsupportedQuantity
}

public sealed record OfficialInventoryActivationWireResult<T>(
    OfficialInventoryActivationWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool Succeeded => Code == OfficialInventoryActivationWireResultCode.Success;

    public static OfficialInventoryActivationWireResult<T> Success(T value) =>
        new(OfficialInventoryActivationWireResultCode.Success, value, string.Empty);

    public static OfficialInventoryActivationWireResult<T> Failure(
        OfficialInventoryActivationWireResultCode code,
        string failureCode) =>
        new(code, default, failureCode);
}

public sealed record OfficialInventoryActivationRequest(
    ushort ClientItemId,
    byte SlotIndex,
    byte Quantity,
    string DecodedFrameSha256,
    string EvidenceId);

/// <summary>
/// Read-only decoder for the exact-build C2S 0x28 inventory-activation request.
/// Static callers prove a four-byte item/slot/quantity payload and always request
/// quantity one. The operation can be an equipment transition or another item-use
/// path, so this codec intentionally exposes no serializer or runtime mutation.
/// The public-beta 0x28 producer independently corroborates the u16+u8+u8 shape,
/// but the exact-current static callers remain the semantic authority.
/// </summary>
public static class OfficialInventoryActivationWireCodec
{
    public const string ClientBuildId = OfficialNpcInteractionWireCodec.ClientBuildId;
    public const string EvidenceId =
        "EquipmentProtocolEvidenceRecovery/20260813;" +
        "Artifacts/OfflineClientReverseEngineering/equipment-outbound-static/all-outbound-callsites.json;" +
        "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
        ":legacy_mission_protocol/interaction28-u16-u8-u8-corroboration";
    public const byte Opcode = 0x28;
    public const int FrameLength = 8;
    public const byte MaximumSlotIndex = 49;
    public const byte VerifiedCallerQuantity = 1;

    public static OfficialInventoryActivationWireResult<OfficialInventoryActivationRequest> Decode(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return Failure(
                OfficialInventoryActivationWireResultCode.BuildMismatch,
                "wire.inventory_activation.build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return Failure(
                OfficialInventoryActivationWireResultCode.InvalidState,
                "wire.inventory_activation.state_invalid");
        }

        if (decodedFrame.Length != FrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != FrameLength)
        {
            return Failure(
                OfficialInventoryActivationWireResultCode.InvalidLength,
                "wire.inventory_activation.length_invalid");
        }

        if (decodedFrame[2] != Opcode)
        {
            return Failure(
                OfficialInventoryActivationWireResultCode.InvalidOpcode,
                "wire.inventory_activation.opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return Failure(
                OfficialInventoryActivationWireResultCode.Malformed,
                "wire.inventory_activation.integrity_invalid");
        }

        var clientItemId = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[3..]);
        var slotIndex = decodedFrame[5];
        var quantity = decodedFrame[6];
        if (clientItemId == 0)
        {
            return Failure(
                OfficialInventoryActivationWireResultCode.Malformed,
                "wire.inventory_activation.item_invalid");
        }

        if (slotIndex > MaximumSlotIndex)
        {
            return Failure(
                OfficialInventoryActivationWireResultCode.SlotOutOfRange,
                "wire.inventory_activation.slot_out_of_range");
        }

        if (quantity != VerifiedCallerQuantity)
        {
            return Failure(
                OfficialInventoryActivationWireResultCode.UnsupportedQuantity,
                "wire.inventory_activation.quantity_unsupported");
        }

        return OfficialInventoryActivationWireResult<OfficialInventoryActivationRequest>.Success(
            new OfficialInventoryActivationRequest(
                clientItemId,
                slotIndex,
                quantity,
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                EvidenceId));
    }

    private static OfficialInventoryActivationWireResult<OfficialInventoryActivationRequest> Failure(
        OfficialInventoryActivationWireResultCode code,
        string failureCode) =>
        OfficialInventoryActivationWireResult<OfficialInventoryActivationRequest>.Failure(code, failureCode);
}
