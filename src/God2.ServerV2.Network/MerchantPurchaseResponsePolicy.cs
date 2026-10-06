using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network;

// Internal projection checks only, not evidence or execution authorization.
// Before writing, caller must validate this policy under the connection lease.
// Writer must independently validate current DB versions and evidence.
// After commit, projection failure requires reconciliation; never a new purchase.
public static class MerchantPurchaseResponsePolicy
{
    public static bool ValidateBeforeExecution(
        string build,
        OfficialMerchantTransactionSelection selection,
        MerchantPurchaseCommand command,
        CharacterInventorySnapshot inventory,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(inventory);

        if (!OfficialMerchantPurchaseResultCodec.TryEncode(
                build, selection, 0, out var probe, out failure))
            return false;
        Array.Clear(probe!);

        failure = string.Empty;
        if (command.TransactionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) ||
            command.ItemId != 253231541 || command.Quantity != 1)
            failure = "CommandProfileMismatch";
        else if (inventory.InventoryId == Guid.Empty ||
                 inventory.CharacterId <= 0 ||
                 inventory.InventoryId != command.InventoryId ||
                 inventory.Version != command.ExpectedInventoryVersion ||
                 inventory.MutationSequence != command.ExpectedMutationSequence)
            failure = "InventorySnapshotMismatch";
        else if (inventory.Capacity <= 0 || inventory.Slots.Count != 0)
            failure = "InventoryLayoutEvidenceBlocked";
        else if (command.ExpectedInventoryVersion < 0 ||
                 command.ExpectedInventoryVersion == long.MaxValue ||
                 command.ExpectedMutationSequence < 0 ||
                 command.ExpectedMutationSequence == long.MaxValue ||
                 command.ExpectedWalletVersion < 0 ||
                 command.ExpectedWalletVersion == long.MaxValue)
            failure = "VersionOutOfRange";

        return failure.Length == 0;
    }

    public static bool TryEncodeCommitted(
        string build,
        OfficialMerchantTransactionSelection selection,
        MerchantPurchaseCommand command,
        CharacterInventorySnapshot before,
        CharacterInventorySnapshot after,
        MerchantPurchaseResult result,
        out byte[]? encoded,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(result);
        encoded = null;

        if (!ValidateBeforeExecution(build, selection, command, before, out failure))
            return false;

        // A historical replay receipt may contain an obsolete wallet balance.
        // Receipt replay and client-state reconciliation are separate operations.
        if (result.Status != MerchantPurchaseStatus.Purchased)
            failure = "FreshCommittedPurchaseRequired";
        else if (result.TransactionId != command.TransactionId ||
                 result.InventoryFailure is not null ||
                 result.InventoryVersionBefore != command.ExpectedInventoryVersion ||
                 result.InventoryVersionAfter != command.ExpectedInventoryVersion + 1 ||
                 result.WalletVersionAfter != command.ExpectedWalletVersion + 1 ||
                 result.BalanceBefore < 0 ||
                 result.BalanceBefore > uint.MaxValue ||
                 result.BalanceAfter < 0 ||
                 result.BalanceAfter >= result.BalanceBefore)
            failure = "CommittedResultMismatch";
        else if (after.InventoryId != before.InventoryId ||
                 after.CharacterId != before.CharacterId ||
                 after.Capacity != before.Capacity ||
                 after.Version != result.InventoryVersionAfter ||
                 after.MutationSequence != command.ExpectedMutationSequence + 1 ||
                 after.Slots.Count != 1 ||
                 after.Slots[0].SlotIndex != 0 ||
                 after.Slots[0].ItemId != command.ItemId ||
                 after.Slots[0].Quantity != 1)
            failure = "CommittedInventoryMismatch";

        if (failure.Length != 0)
            return false;

        return OfficialMerchantPurchaseResultCodec.TryEncode(
            build, selection, result.BalanceAfter, out encoded, out failure);
    }
}
