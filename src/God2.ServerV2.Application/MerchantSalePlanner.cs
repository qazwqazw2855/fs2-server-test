namespace God2.ServerV2.Application;

// Trusted server quote; approval must cover merchant buying policy,
// item eligibility, currency and price. Never build from client prices.
public sealed record MerchantSaleQuote(
    long MerchantId,
    long ItemId,
    string CurrencyType,
    long UnitPrice,
    bool Enabled,
    bool ItemSellable);

public enum MerchantSaleFailure
{
    None,
    Disabled,
    ItemNotSellable,
    UnsupportedQuantity,
    SlotMissing,
    ItemMismatch,
    UnsupportedItemState,
    WalletMismatch
}

public sealed record MerchantSalePlan(
    MerchantSaleFailure Failure,
    int SlotIndex,
    int QuantityBefore,
    int QuantityAfter,
    long Proceeds,
    long BalanceAfter)
{
    public bool Succeeded => Failure == MerchantSaleFailure.None;
}

public static class MerchantSalePlanner
{
    public static MerchantSalePlan Plan(
        CharacterInventorySnapshot inventory,
        CharacterWalletSnapshot wallet,
        MerchantSaleQuote quote,
        int slotIndex,
        int quantity)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(quote);

        if (inventory.CharacterId <= 0 ||
            inventory.Version < 0 || inventory.MutationSequence < 0 ||
            wallet.CharacterId <= 0 || wallet.Version < 0 ||
            wallet.Balance < 0 ||
            quote.MerchantId <= 0 || quote.ItemId <= 0 ||
            quote.UnitPrice <= 0 ||
            string.IsNullOrWhiteSpace(quote.CurrencyType) ||
            quote.CurrencyType.Length > 32)
            throw new InvalidDataException("Invalid sale planning state.");

        // Validate all occupied slots, including unrelated items.
        _ = ItemStackChangePlanner.Preview(inventory, quote.ItemId, 1);

        MerchantSalePlan Reject(MerchantSaleFailure failure) =>
            new(failure, slotIndex, 0, 0, 0, wallet.Balance);

        if (!quote.Enabled)
            return Reject(MerchantSaleFailure.Disabled);
        if (!quote.ItemSellable)
            return Reject(MerchantSaleFailure.ItemNotSellable);
        if (quantity != 1)
            return Reject(MerchantSaleFailure.UnsupportedQuantity);
        if (wallet.CharacterId != inventory.CharacterId ||
            wallet.CurrencyType != quote.CurrencyType)
            return Reject(MerchantSaleFailure.WalletMismatch);

        var slot = inventory.Slots.SingleOrDefault(
            value => value.SlotIndex == slotIndex);
        if (slot is null)
            return Reject(MerchantSaleFailure.SlotMissing);
        if (slot.ItemId != quote.ItemId)
            return Reject(MerchantSaleFailure.ItemMismatch);
        if (slot.BindState != "Unbound" ||
            slot.ItemInstanceMetadata != "{}")
            return Reject(MerchantSaleFailure.UnsupportedItemState);

        return new MerchantSalePlan(
            MerchantSaleFailure.None, slotIndex,
            slot.Quantity, slot.Quantity - 1,
            quote.UnitPrice, checked(wallet.Balance + quote.UnitPrice));
    }
}
