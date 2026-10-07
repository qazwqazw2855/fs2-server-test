using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network;

// Internal projection checks. No evidence promotion or automatic retry.
// A successful transaction with no response requires reconciliation.
public static class MerchantSaleResponsePolicy
{
    public static bool ValidateBeforeExecution(
        MerchantInteractionBinding binding,
        long characterId,
        OfficialMerchantTransactionSelection selection,
        MerchantSaleCommand command,
        CharacterEconomySnapshot before,
        CharacterInventoryItemIdentity? identity,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);

        if (!MerchantSaleCommandFactory.TryCreate(
            binding.ClientBuildId, characterId, binding, selection,
            before.Inventory, before.Wallet, identity, command.TransactionId,
            out var expected, out failure))
            return false;

        if (expected != command)
        {
            failure = "CommandSnapshotMismatch";
            return false;
        }
        return true;
    }

    public static bool TryEncodeCommitted(
        MerchantInteractionBinding binding,
        long characterId,
        OfficialMerchantTransactionSelection selection,
        MerchantSaleCommand command,
        CharacterEconomySnapshot before,
        CharacterInventoryItemIdentity? identity,
        CharacterEconomySnapshot after,
        MerchantSaleResult result,
        out byte[]? response,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(result);
        response = null;

        if (!ValidateBeforeExecution(
            binding, characterId, selection, command, before, identity,
            out failure))
            return false;

        if (result.Status != MerchantSaleStatus.Sold)
            failure = "ResultRequiresReconciliation";
        else if (result.TransactionId != command.TransactionId ||
                 result.BalanceBefore != before.Wallet!.Balance ||
                 result.BalanceAfter <= result.BalanceBefore ||
                 result.BalanceAfter > uint.MaxValue ||
                 result.WalletVersionAfter != command.ExpectedWalletVersion + 1 ||
                 result.InventoryVersionBefore != command.ExpectedInventoryVersion ||
                 result.InventoryVersionAfter != command.ExpectedInventoryVersion + 1)
            failure = "CommittedResultMismatch";
        else if (after.Inventory is null || after.Wallet is null)
            failure = "CommittedSnapshotMissing";
        else if (after.Inventory.CharacterId != characterId ||
                 after.Inventory.InventoryId != command.InventoryId ||
                 after.Inventory.Capacity != before.Inventory!.Capacity ||
                 after.Inventory.Version != result.InventoryVersionAfter ||
                 after.Inventory.MutationSequence != command.ExpectedMutationSequence + 1 ||
                 after.Inventory.Slots.Count != 0 ||
                 after.Wallet.CharacterId != characterId ||
                 after.Wallet.CurrencyType != "Gold" ||
                 after.Wallet.Balance != result.BalanceAfter ||
                 after.Wallet.Version != result.WalletVersionAfter)
            failure = "CommittedSnapshotMismatch";

        if (failure.Length != 0)
            return false;

        return OfficialMerchantSaleResultCodec.TryEncode(
            binding.ClientBuildId, selection, result.BalanceAfter,
            out response, out failure);
    }
}
