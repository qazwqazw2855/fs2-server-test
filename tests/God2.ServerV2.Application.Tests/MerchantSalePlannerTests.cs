using System.Text.Json;
using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class MerchantSalePlannerTests
{
    private static CharacterInventorySnapshot Inventory(
        int quantity = 1, string bind = "Unbound", string metadata = "{}") =>
        new(Guid.NewGuid(), 1, 8, 2, 2, "Clean",
            [new CharacterInventorySlot(0, 253231541, quantity, bind, metadata)]);

    private static readonly MerchantSaleQuote Quote =
        new(3954, 253231541, "Gold", 4, true, true);

    private static readonly CharacterWalletSnapshot Wallet =
        new(1, "Gold", 20, 2);

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void Sale_decrements_one_and_preserves_input(
        int quantity, int remaining)
    {
        var inventory = Inventory(quantity);
        var before = JsonSerializer.Serialize(inventory);
        var plan = MerchantSalePlanner.Plan(inventory, Wallet, Quote, 0, 1);

        Assert.True(plan.Succeeded);
        Assert.Equal(quantity, plan.QuantityBefore);
        Assert.Equal(remaining, plan.QuantityAfter);
        Assert.Equal(4, plan.Proceeds);
        Assert.Equal(24, plan.BalanceAfter);
        Assert.Equal(before, JsonSerializer.Serialize(inventory));
    }

    [Theory]
    [InlineData("Unknown", "{}")]
    [InlineData("Bound", "{}")]
    [InlineData("Unbound", "{\"upgrade\":1}")]
    public void Sale_rejects_unsupported_instance_state(
        string bind, string metadata)
    {
        var plan = MerchantSalePlanner.Plan(
            Inventory(1, bind, metadata), Wallet, Quote, 0, 1);
        Assert.Equal(MerchantSaleFailure.UnsupportedItemState, plan.Failure);
        Assert.Equal(0, plan.Proceeds);
        Assert.Equal(20, plan.BalanceAfter);
    }

    [Fact]
    public void Sale_requires_enabled_and_sellable_quote()
    {
        Assert.Equal(MerchantSaleFailure.Disabled,
            MerchantSalePlanner.Plan(
                Inventory(), Wallet, Quote with { Enabled = false }, 0, 1).Failure);
        Assert.Equal(MerchantSaleFailure.ItemNotSellable,
            MerchantSalePlanner.Plan(
                Inventory(), Wallet, Quote with { ItemSellable = false }, 0, 1).Failure);
    }

    [Fact]
    public void Sale_rejects_missing_slot_and_wrong_item()
    {
        Assert.Equal(MerchantSaleFailure.SlotMissing,
            MerchantSalePlanner.Plan(Inventory(), Wallet, Quote, 1, 1).Failure);
        Assert.Equal(MerchantSaleFailure.ItemMismatch,
            MerchantSalePlanner.Plan(
                Inventory(), Wallet, Quote with { ItemId = 99 }, 0, 1).Failure);
    }

    [Fact]
    public void Sale_rejects_unsupported_quantity_and_wallet()
    {
        Assert.Equal(MerchantSaleFailure.UnsupportedQuantity,
            MerchantSalePlanner.Plan(Inventory(), Wallet, Quote, 0, 2).Failure);
        Assert.Equal(MerchantSaleFailure.WalletMismatch,
            MerchantSalePlanner.Plan(
                Inventory(), Wallet with { CharacterId = 2 }, Quote, 0, 1).Failure);
        Assert.Equal(MerchantSaleFailure.WalletMismatch,
            MerchantSalePlanner.Plan(
                Inventory(), Wallet with { CurrencyType = "Unknown" }, Quote, 0, 1).Failure);
    }

    [Fact]
    public void Sale_rejects_balance_overflow()
    {
        Assert.Throws<OverflowException>(() =>
            MerchantSalePlanner.Plan(
                Inventory(), Wallet with { Balance = long.MaxValue }, Quote, 0, 1));
    }

    [Fact]
    public void Sale_rejects_invalid_price()
    {
        Assert.Throws<InvalidDataException>(() =>
            MerchantSalePlanner.Plan(
                Inventory(), Wallet, Quote with { UnitPrice = 0 }, 0, 1));
    }
}
