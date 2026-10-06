using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network;

// Snapshot projection only: not interaction authorization or transaction approval.
// Execution must recheck interaction in the existing connection lane and let
// the writer validate current DB versions, listing, price and evidence.
// Retry the same command unchanged; never rebuild it after an uncertain result.
public static class MerchantPurchaseCommandFactory
{
    public static bool TryCreate(
        long characterId,
        MerchantInteractionBinding binding,
        OfficialMerchantTransactionSelection selection,
        MerchantCatalogIdentity? catalog,
        CharacterInventorySnapshot? inventory,
        CharacterWalletSnapshot? wallet,
        Guid transactionId,
        out MerchantPurchaseCommand? command,
        out string failureCode)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(selection);
        command = null;
        failureCode = string.Empty;

        if (characterId <= 0 || transactionId == Guid.Empty)
            failureCode = "InvalidServerIdentity";
        else if (selection.Operation != OfficialMerchantTransactionOperation.Buy)
            failureCode = "UnsupportedOperation";
        else if (selection.Quantity != 1)
            failureCode = "UnsupportedQuantity";
        else if (binding.MerchantId <= 0 ||
                 binding.ClientEntityHandle == 0 ||
                 selection.ClientEntityHandle != binding.ClientEntityHandle)
            failureCode = "BindingMismatch";
        else if (catalog is null)
            failureCode = "CatalogMissing";
        else if (catalog.MerchantInventoryId <= 0 ||
                 catalog.MerchantId != binding.MerchantId ||
                 catalog.ItemId <= 0 || catalog.ItemId > int.MaxValue ||
                 catalog.ClientCatalogIndex != selection.CatalogIndexOrClientInventorySlot ||
                 catalog.ClientItemId != selection.ClientItemId)
            failureCode = "CatalogMismatch";
        else if (inventory is null)
            failureCode = "InventoryMissing";
        else if (inventory.CharacterId != characterId ||
                 inventory.InventoryId == Guid.Empty ||
                 inventory.Capacity <= 0 ||
                 inventory.Version < 0 ||
                 inventory.MutationSequence < 0)
            failureCode = "InventoryMismatch";
        else if (wallet is null)
            failureCode = "WalletMissing";
        else if (wallet.CharacterId != characterId ||
                 !string.Equals(wallet.CurrencyType, "Gold", StringComparison.Ordinal) ||
                 wallet.Balance < 0 || wallet.Version < 0)
            failureCode = "WalletMismatch";

        if (failureCode.Length != 0)
            return false;

        command = new MerchantPurchaseCommand(
            transactionId,
            $"MerchantBuy:{transactionId:N}",
            catalog!.ItemId,
            selection.Quantity,
            inventory!.InventoryId,
            inventory.Version,
            inventory.MutationSequence,
            wallet!.Version);
        return true;
    }
}
