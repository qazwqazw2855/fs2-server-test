using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network;

// Mirrors the existing Classic restricted restore projection.
// No mutation, transaction replay or evidence promotion.
// Original-client reconnect acceptance remains a separate gate.
public static class InventoryBootstrapProjection
{
    public static bool TryEncode(
        string clientBuildId,
        long characterId,
        CharacterInventorySnapshot inventory,
        long walletBalance,
        out byte[]? frame,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        frame = null;
        failure = string.Empty;

        if (!string.Equals(clientBuildId,
                OfficialMerchantPurchaseResultCodec.ClientBuildId,
                StringComparison.Ordinal))
            failure = "BuildMismatch";
        else if (characterId <= 0 ||
                 inventory.CharacterId != characterId ||
                 inventory.InventoryId == Guid.Empty ||
                 inventory.Capacity <= 0 ||
                 inventory.Version < 0 ||
                 inventory.MutationSequence < 0)
            failure = "InventorySnapshotMismatch";
        else if (walletBalance < 0 || walletBalance > uint.MaxValue)
            failure = "WalletBalanceOutOfRange";
        else if (inventory.Slots.Count > 1)
            failure = "InventoryLayoutEvidenceBlocked";

        if (failure.Length != 0)
            return false;

        // Empty inventory requires no restore frame.
        if (inventory.Slots.Count == 0)
            return true;

        var slot = inventory.Slots[0];
        if (slot.SlotIndex != 0 ||
            slot.ItemId != 253231541 ||
            slot.Quantity != 1)
        {
            failure = "InventorySlotEvidenceBlocked";
            return false;
        }

        return OfficialMerchantPurchaseResultCodec.TryEncode(
            clientBuildId,
            new OfficialMerchantTransactionSelection(
                3954, 6901, 1,
                OfficialMerchantTransactionOperation.Buy, 7),
            walletBalance,
            out frame,
            out failure);
    }
}
