using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum OfficialInventoryBootstrapWireResultCode
{
    Success,
    BuildMismatch,
    UnsupportedSnapshot,
    ProjectionFailed
}

public sealed record OfficialInventoryBootstrapWireResult(
    OfficialInventoryBootstrapWireResultCode Code,
    IReadOnlyList<ReadOnlyMemory<byte>> DecodedFrames,
    string FailureCode)
{
    public bool Succeeded => Code == OfficialInventoryBootstrapWireResultCode.Success;

    public static OfficialInventoryBootstrapWireResult Success(IReadOnlyList<ReadOnlyMemory<byte>> frames) =>
        new(OfficialInventoryBootstrapWireResultCode.Success, frames, string.Empty);

    public static OfficialInventoryBootstrapWireResult Failure(
        OfficialInventoryBootstrapWireResultCode code,
        string failureCode) =>
        new(code, [], failureCode);
}

/// <summary>
/// Restores only the exact inventory state proven by the controlled merchant
/// purchase and sale evidence. The official Client places the Stage-5 purchase
/// result in its visible slot 4 while the authoritative empty-inventory insert
/// is persisted as slot 0; Stage 6 then reports that visible slot as 4.
/// Unknown item, quantity, slot, or multi-item layouts remain fail closed.
/// </summary>
public static class OfficialInventoryBootstrapWireCodec
{
    public const string ClientBuildId = OfficialMerchantTransactionWireCodec.ClientBuildId;
    public const string EvidenceId = "LiveRecovery/Stages5-6-attempt-759-trace";
    public const int VerifiedAuthoritySlot = 0;
    public const ushort VerifiedClientSaleSlot = 4;
    public const ushort VerifiedMerchantCatalogIndex = 7;

    public static string? Validate(PlayerInventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Slots.Count == 0)
        {
            return null;
        }

        if (snapshot.Slots.Count != 1)
        {
            return "wire.inventory_bootstrap.layout_evidence_blocked";
        }

        var slot = snapshot.Slots[0];
        return slot.SlotIndex == VerifiedAuthoritySlot &&
               slot.ItemTemplateId == OfficialMerchantTransactionWireCodec.LiveStageCanonicalItemId &&
               slot.Quantity == 1
            ? null
            : "wire.inventory_bootstrap.slot_evidence_blocked";
    }

    public static OfficialInventoryBootstrapWireResult Serialize(
        string clientBuildId,
        PlayerInventorySnapshot snapshot,
        uint walletBalance)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialInventoryBootstrapWireResult.Failure(
                OfficialInventoryBootstrapWireResultCode.BuildMismatch,
                "wire.inventory_bootstrap.build_mismatch");
        }

        var validation = Validate(snapshot);
        if (validation is not null)
        {
            return OfficialInventoryBootstrapWireResult.Failure(
                OfficialInventoryBootstrapWireResultCode.UnsupportedSnapshot,
                validation);
        }

        if (snapshot.Slots.Count == 0)
        {
            return OfficialInventoryBootstrapWireResult.Success([]);
        }

        var request = new OfficialMerchantTransactionRequest(
            OfficialMerchantTransactionWireCodec.LiveStageMerchantHandle,
            OfficialMerchantTransactionWireCodec.LiveStageClientItemId,
            Quantity: 1,
            OfficialMerchantOperation.Buy,
            VerifiedMerchantCatalogIndex,
            DecodedFrameSha256: string.Empty,
            EvidenceId);
        var result = new InventoryTransactionResult(
            InventoryTransactionResultCode.Success,
            Guid.Empty,
            "inventory-bootstrap",
            snapshot.Version,
            snapshot.Version,
            walletBalance,
            walletBalance,
            InventorySnapshot: snapshot);
        var projection = OfficialMerchantTransactionWireCodec.SerializeSuccess(
            clientBuildId,
            request,
            result);
        if (!projection.Succeeded || projection.Value is null)
        {
            return OfficialInventoryBootstrapWireResult.Failure(
                OfficialInventoryBootstrapWireResultCode.ProjectionFailed,
                projection.FailureCode);
        }

        return OfficialInventoryBootstrapWireResult.Success(
            [projection.Value.DecodedFrame.ToArray()]);
    }

    public static bool TryMapClientSaleSlotToAuthority(
        ushort clientSlot,
        out int authoritySlot)
    {
        authoritySlot = clientSlot == VerifiedClientSaleSlot
            ? VerifiedAuthoritySlot
            : -1;
        return authoritySlot >= 0;
    }
}
