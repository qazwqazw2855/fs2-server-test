using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network;

// Restricted snapshot projection, not interaction or evidence approval.
// Client slot 4 maps only to the existing observed authority slot 0 profile.
// Writer must revalidate ownership, versions, price and evidence under DB locks.
public static class MerchantSaleCommandFactory
{
    public static bool TryCreate(
        string clientBuildId,
        long characterId,
        MerchantInteractionBinding binding,
        OfficialMerchantTransactionSelection selection,
        CharacterInventorySnapshot? inventory,
        CharacterWalletSnapshot? wallet,
        CharacterInventoryItemIdentity? identity,
        Guid transactionId,
        out MerchantSaleCommand? command,
        out string failureCode)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(selection);
        command = null;

        if (!OfficialMerchantSaleResultCodec.TryEncode(
            clientBuildId, selection, 0, out var probe, out failureCode))
            return false;
        Array.Clear(probe!);

        if (!string.Equals(binding.ClientBuildId, clientBuildId,
                StringComparison.Ordinal))
            failureCode = "BindingBuildMismatch";
        else if (characterId <= 0 || transactionId == Guid.Empty)
            failureCode = "InvalidServerIdentity";
        else if (binding.MerchantId <= 0 ||
                 binding.ClientEntityHandle != selection.ClientEntityHandle)
            failureCode = "BindingMismatch";
        else if (inventory is null)
            failureCode = "InventoryMissing";
        else if (inventory.CharacterId != characterId ||
                 inventory.InventoryId == Guid.Empty ||
                 inventory.Capacity <= 0 ||
                 inventory.Version < 0 || inventory.Version == long.MaxValue ||
                 inventory.MutationSequence < 0 ||
                 inventory.MutationSequence == long.MaxValue)
            failureCode = "InventoryMismatch";
        else if (inventory.Slots.Count != 1 ||
                 inventory.Slots[0].SlotIndex != 0 ||
                 inventory.Slots[0].ItemId != 253231541 ||
                 inventory.Slots[0].Quantity != 1)
            failureCode = "InventoryLayoutEvidenceBlocked";
        else if (identity is null)
            failureCode = "ItemIdentityMissing";
        else if (identity.CharacterId != characterId ||
                 identity.ItemInstanceId <= 0 ||
                 identity.SlotIndex != 0 ||
                 identity.ItemId != inventory.Slots[0].ItemId ||
                 identity.Quantity != inventory.Slots[0].Quantity ||
                 identity.SlotVersion < 0 ||
                 identity.SlotVersion == long.MaxValue ||
                 identity.BindState != inventory.Slots[0].BindState ||
                 identity.ItemInstanceMetadata !=
                     inventory.Slots[0].ItemInstanceMetadata)
            failureCode = "ItemIdentityMismatch";
        else if (identity.Bound != false ||
                 identity.BindState != "Unbound" ||
                 identity.ItemInstanceMetadata != "{}")
            failureCode = "ItemStateBlocked";
        else if (wallet is null)
            failureCode = "WalletMissing";
        else if (wallet.CharacterId != characterId ||
                 wallet.CurrencyType != "Gold" ||
                 wallet.Balance < 0 || wallet.Balance > uint.MaxValue ||
                 wallet.Version < 0 || wallet.Version == long.MaxValue)
            failureCode = "WalletMismatch";

        if (failureCode.Length != 0)
            return false;

        command = new MerchantSaleCommand(
            transactionId, $"MerchantSell:{transactionId:N}",
            identity!.ItemId, identity.ItemInstanceId, identity.SlotIndex, 1,
            inventory!.InventoryId, inventory.Version,
            inventory.MutationSequence, identity.SlotVersion, wallet!.Version);
        return true;
    }
}
