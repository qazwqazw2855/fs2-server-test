using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class MerchantPurchasePlannerTests
{
    private static CharacterInventorySnapshot Inventory(
        int capacity = 2,
        params CharacterInventorySlot[] slots) =>
        new(Guid.NewGuid(), 1, capacity, 0, 0, "Clean", slots);

    private static readonly ItemStackRule Rule =
        new(253231541, "肉塊", null, true);

    // Synthetic test price; this is not a production price assertion.
    private static readonly MerchantPurchaseQuote Quote =
        new(170015954, 253231541, "Gold", 10, 1, true);

    [Theory]
    [InlineData(9, false, 9)]
    [InlineData(10, true, 0)]
    [InlineData(11, true, 1)]
    public void Purchase_checks_funds_and_preserves_input(
        long balance, bool succeeds, long expectedBalance)
    {
        var inventory = Inventory();
        var wallet = new CharacterWalletSnapshot(1, "Gold", balance, 0);
        var plan = MerchantPurchasePlanner.Plan(
            inventory, wallet, Quote, Rule, 1);

        Assert.Equal(succeeds, plan.Succeeded);
        Assert.Equal(expectedBalance, plan.BalanceAfter);
        Assert.Empty(inventory.Slots);
        Assert.Equal(balance, wallet.Balance);
        if (succeeds)
            Assert.Equal(1, Assert.Single(plan.ChangedSlots).Quantity);
        else
            Assert.Empty(plan.ChangedSlots);
    }

    [Fact]
    public void Full_inventory_rejects_without_deduction_plan()
    {
        var inventory = Inventory(
            1, new CharacterInventorySlot(0, 253231541, 1));
        var plan = MerchantPurchasePlanner.Plan(
            inventory, new(1, "Gold", 100, 0), Quote, Rule, 1);

        Assert.Equal(MerchantPurchaseFailure.InsufficientCapacity, plan.Failure);
        Assert.Equal(100, plan.BalanceAfter);
        Assert.Equal(0, plan.Cost);
        Assert.Empty(plan.ChangedSlots);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Unverified_quantity_is_rejected(int quantity)
    {
        var plan = MerchantPurchasePlanner.Plan(
            Inventory(), new(1, "Gold", 100, 0), Quote, Rule, quantity);
        Assert.Equal(MerchantPurchaseFailure.UnsupportedQuantity, plan.Failure);
    }

    [Fact]
    public void Different_currency_is_rejected()
    {
        var plan = MerchantPurchasePlanner.Plan(
            Inventory(), new(1, "Other", 100, 0), Quote, Rule, 1);
        Assert.Equal(MerchantPurchaseFailure.WalletMismatch, plan.Failure);
    }

    [Fact]
    public void Unknown_price_cannot_be_used_as_free_purchase()
    {
        Assert.Throws<InvalidDataException>(() =>
            MerchantPurchasePlanner.Plan(
                Inventory(), new(1, "Gold", 100, 0),
                Quote with { UnitPrice = 0 }, Rule, 1));
    }
}
