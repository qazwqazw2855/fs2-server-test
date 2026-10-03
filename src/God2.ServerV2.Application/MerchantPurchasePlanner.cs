namespace God2.ServerV2.Application;

// Loaded from trusted server pricing and reviewed merchant/item mapping.
// Currency and price must never be taken from a client packet.
public sealed record MerchantPurchaseQuote(
    long MerchantId,
    long ItemId,
    string CurrencyType,
    long UnitPrice,
    int PackCount,
    bool Enabled);

public sealed record CharacterWalletSnapshot(
    long CharacterId,
    string CurrencyType,
    long Balance,
    long Version);

public enum MerchantPurchaseFailure
{
    None,
    Disabled,
    UnsupportedQuantity,
    WalletMismatch,
    InsufficientFunds,
    ItemDisabled,
    UnsupportedItemState,
    InsufficientCapacity
}

public sealed record MerchantPurchasePlan(
    MerchantPurchaseFailure Failure,
    long Cost,
    long BalanceAfter,
    IReadOnlyList<CharacterInventorySlot> ChangedSlots)
{
    public bool Succeeded => Failure == MerchantPurchaseFailure.None;
}

public static class MerchantPurchasePlanner
{
    public static MerchantPurchasePlan Plan(
        CharacterInventorySnapshot inventory,
        CharacterWalletSnapshot wallet,
        MerchantPurchaseQuote quote,
        ItemStackRule rule,
        int quantity)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(rule);

        if (quote.MerchantId <= 0 || quote.ItemId <= 0 ||
            quote.ItemId != rule.ItemId ||
            string.IsNullOrWhiteSpace(quote.CurrencyType) ||
            quote.CurrencyType.Length > 32 ||
            quote.UnitPrice <= 0 ||
            wallet.CharacterId <= 0 || wallet.Version < 0 ||
            wallet.Balance < 0)
            throw new InvalidDataException("Invalid purchase state or quote.");

        MerchantPurchasePlan Reject(MerchantPurchaseFailure failure) =>
            new(failure, 0, wallet.Balance,
                Array.Empty<CharacterInventorySlot>());

        if (!quote.Enabled)
            return Reject(MerchantPurchaseFailure.Disabled);

        // Current observed merchant slice is quantity one, one item per pack.
        if (quantity != 1 || quote.PackCount != 1)
            return Reject(MerchantPurchaseFailure.UnsupportedQuantity);

        if (wallet.CharacterId != inventory.CharacterId ||
            wallet.CurrencyType != quote.CurrencyType)
            return Reject(MerchantPurchaseFailure.WalletMismatch);

        if (wallet.Balance < quote.UnitPrice)
            return Reject(MerchantPurchaseFailure.InsufficientFunds);

        var grant = InventoryGrantPlanner.Plan(
            inventory, rule, 1, "Unbound", "{}");
        if (!grant.Succeeded)
        {
            return Reject(grant.Failure switch
            {
                InventoryGrantFailure.ItemDisabled =>
                    MerchantPurchaseFailure.ItemDisabled,
                InventoryGrantFailure.UnsupportedItemState =>
                    MerchantPurchaseFailure.UnsupportedItemState,
                InventoryGrantFailure.InsufficientCapacity =>
                    MerchantPurchaseFailure.InsufficientCapacity,
                _ => throw new InvalidDataException("Unexpected grant failure.")
            });
        }

        return new MerchantPurchasePlan(
            MerchantPurchaseFailure.None,
            quote.UnitPrice,
            wallet.Balance - quote.UnitPrice,
            grant.ChangedSlots);
    }
}
