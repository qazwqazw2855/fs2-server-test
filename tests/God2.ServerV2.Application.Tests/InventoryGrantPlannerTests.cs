using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class InventoryGrantPlannerTests
{
    private static readonly ItemStackRule Rule =
        new(253231541, "肉塊", 20, true);

    private static CharacterInventorySnapshot Inventory(
        int capacity,
        params CharacterInventorySlot[] slots) =>
        new(Guid.NewGuid(), 1, capacity, 7, 11, "Clean", slots);

    private static CharacterInventorySlot Plain(int slot, int quantity) =>
        new(slot, Rule.ItemId, quantity, "Unbound", "{}");

    [Fact]
    public void Grant_fills_existing_stacks_then_lowest_free_slots()
    {
        var inventory = Inventory(5, Plain(3, 18), Plain(1, 19));

        var plan = InventoryGrantPlanner.Plan(
            inventory, Rule, 25, "Unbound", "{}");

        Assert.True(plan.Succeeded);
        Assert.Equal(
            new[] { Plain(0, 20), Plain(1, 20), Plain(2, 2), Plain(3, 20) },
            plan.ChangedSlots.ToArray());
        Assert.Equal(7L, plan.ExpectedVersion);
        Assert.Equal(11L, plan.ExpectedMutationSequence);
        Assert.Equal(inventory.InventoryId, plan.InventoryId);
        Assert.Equal(inventory.CharacterId, plan.CharacterId);
        Assert.Equal(new[] { Plain(3, 18), Plain(1, 19) },
            inventory.Slots.ToArray());
    }

    [Fact]
    public void Insufficient_capacity_returns_no_partial_changes()
    {
        var inventory = Inventory(2, Plain(0, 19), Plain(1, 20));

        var plan = InventoryGrantPlanner.Plan(
            inventory, Rule, 2, "Unbound", "{}");

        Assert.Equal(
            InventoryGrantFailure.InsufficientCapacity, plan.Failure);
        Assert.Empty(plan.ChangedSlots);
        Assert.Equal(19, inventory.Slots[0].Quantity);
    }

    [Theory]
    [InlineData("Unknown", "{}")]
    [InlineData("Bound", "{}")]
    [InlineData("Unbound", """{"upgrade":1}""")]
    public void Grant_does_not_merge_into_unknown_or_special_items(
        string bindState,
        string metadata)
    {
        var inventory = Inventory(
            2, new CharacterInventorySlot(
                0, Rule.ItemId, 3, bindState, metadata));

        var plan = InventoryGrantPlanner.Plan(
            inventory, Rule, 4, "Unbound", "{}");

        Assert.True(plan.Succeeded);
        Assert.Equal(Plain(1, 4), Assert.Single(plan.ChangedSlots));
        Assert.Equal(3, inventory.Slots[0].Quantity);
    }

    [Theory]
    [InlineData("Unknown", "{}")]
    [InlineData("Bound", "{}")]
    [InlineData("Unbound", """{"upgrade":1}""")]
    public void Grant_rejects_unsupported_incoming_item_state(
        string bindState,
        string metadata)
    {
        var plan = InventoryGrantPlanner.Plan(
            Inventory(2), Rule, 1, bindState, metadata);

        Assert.Equal(
            InventoryGrantFailure.UnsupportedItemState, plan.Failure);
        Assert.Empty(plan.ChangedSlots);
    }

    [Fact]
    public void Missing_stack_limit_uses_one_item_per_slot()
    {
        var rule = Rule with { ConfiguredMaximumStack = null };

        var plan = InventoryGrantPlanner.Plan(
            Inventory(2), rule, 2, "Unbound", "{}");

        Assert.True(plan.Succeeded);
        Assert.Equal(
            new[] { Plain(0, 1), Plain(1, 1) },
            plan.ChangedSlots.ToArray());
    }

    [Fact]
    public void Disabled_item_is_rejected()
    {
        var plan = InventoryGrantPlanner.Plan(
            Inventory(2), Rule with { Enabled = false },
            1, "Unbound", "{}");

        Assert.Equal(InventoryGrantFailure.ItemDisabled, plan.Failure);
        Assert.Empty(plan.ChangedSlots);
    }

    [Fact]
    public void Large_quantity_is_rejected_without_allocating_partial_slots()
    {
        var plan = InventoryGrantPlanner.Plan(
            Inventory(2), Rule, int.MaxValue, "Unbound", "{}");

        Assert.Equal(
            InventoryGrantFailure.InsufficientCapacity, plan.Failure);
        Assert.Empty(plan.ChangedSlots);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 1)]
    [InlineData(0, 2)]
    public void Invalid_existing_slots_are_rejected(
        int firstSlot,
        int secondSlot)
    {
        var inventory = Inventory(
            2, Plain(firstSlot, 1), Plain(secondSlot, 1));

        Assert.Throws<InvalidDataException>(() =>
            InventoryGrantPlanner.Plan(
                inventory, Rule, 1, "Unbound", "{}"));
    }

    [Fact]
    public void Invalid_unrelated_item_is_rejected()
    {
        var inventory = Inventory(
            2, new CharacterInventorySlot(0, 987654321, 0));

        Assert.Throws<InvalidDataException>(() =>
            InventoryGrantPlanner.Plan(
                inventory, Rule, 1, "Unbound", "{}"));
    }

    [Fact]
    public void Existing_stack_above_limit_requires_reconciliation()
    {
        Assert.Throws<InvalidDataException>(() =>
            InventoryGrantPlanner.Plan(
                Inventory(2, Plain(0, 21)),
                Rule, 1, "Unbound", "{}"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_grant_is_rejected(int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryGrantPlanner.Plan(
                Inventory(2), Rule, quantity, "Unbound", "{}"));
    }

    [Fact]
    public void Invalid_rule_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            InventoryGrantPlanner.Plan(
                Inventory(2),
                Rule with { ConfiguredMaximumStack = 0 },
                1, "Unbound", "{}"));
    }
}
