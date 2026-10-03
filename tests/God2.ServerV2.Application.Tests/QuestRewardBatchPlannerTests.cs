using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class QuestRewardBatchPlannerTests
{
    private static CharacterInventorySnapshot Inventory(int capacity) =>
        new(Guid.NewGuid(), 1, capacity, 7, 11, "Clean",
            Array.Empty<CharacterInventorySlot>());

    private static Dictionary<long, ItemStackRule> Rules(int maximum = 1) =>
        new() { [253231541] = new(253231541, "肉塊", maximum, true) };

    [Fact]
    public void Later_reward_capacity_failure_discards_entire_plan()
    {
        var inventory = Inventory(1);
        var result = QuestRewardBatchPlanner.Plan(
            inventory,
            [
                new(1, "Item", 253231541, 1),
                new(2, "Item", 253231541, 1)
            ],
            Rules());

        Assert.Equal(
            QuestRewardBatchFailure.InsufficientCapacity, result.Failure);
        Assert.Empty(result.ChangedSlots);
        Assert.Empty(inventory.Slots);
        Assert.Equal(7, inventory.Version);
        Assert.Equal(11, inventory.MutationSequence);
    }

    [Fact]
    public void Rewards_share_projected_stack_without_mutating_input()
    {
        var inventory = Inventory(1);
        var result = QuestRewardBatchPlanner.Plan(
            inventory,
            [
                new(1, "Item", 253231541, 3),
                new(2, "Item", 253231541, 4)
            ],
            Rules(20));

        Assert.True(result.Succeeded);
        var slot = Assert.Single(result.ChangedSlots);
        Assert.Equal(0, slot.SlotIndex);
        Assert.Equal(7, slot.Quantity);
        Assert.Empty(inventory.Slots);
    }

    [Theory]
    [InlineData("Experience", null, null, null)]
    [InlineData("Item", 10L, null, null)]
    [InlineData("Item", null, 10L, null)]
    [InlineData("Item", null, null, "choice")]
    public void Unsupported_reward_rejects_whole_manifest(
        string type, long? experience, long? currency, string? selection)
    {
        var result = QuestRewardBatchPlanner.Plan(
            Inventory(4),
            [
                new(1, "Item", 253231541, 1),
                new(2, type, 253231541, 1,
                    experience, currency, selection)
            ],
            Rules());

        Assert.Equal(
            QuestRewardBatchFailure.UnsupportedReward, result.Failure);
        Assert.Empty(result.ChangedSlots);
    }

    [Fact]
    public void Duplicate_reward_identity_is_rejected()
    {
        var result = QuestRewardBatchPlanner.Plan(
            Inventory(4),
            [
                new(1, "Item", 253231541, 1),
                new(1, "Item", 253231541, 1)
            ],
            Rules());

        Assert.Equal(
            QuestRewardBatchFailure.InvalidReward, result.Failure);
        Assert.Empty(result.ChangedSlots);
    }

    [Fact]
    public void Missing_item_rule_is_rejected()
    {
        var result = QuestRewardBatchPlanner.Plan(
            Inventory(4),
            [new(1, "Item", 253231541, 1)],
            new Dictionary<long, ItemStackRule>());

        Assert.Equal(QuestRewardBatchFailure.ItemMissing, result.Failure);
        Assert.Empty(result.ChangedSlots);
    }

    [Fact]
    public void Empty_manifest_is_not_assumed_to_be_verified_no_reward()
    {
        var result = QuestRewardBatchPlanner.Plan(
            Inventory(4), Array.Empty<QuestRewardEntry>(), Rules());

        Assert.Equal(QuestRewardBatchFailure.EmptyRewards, result.Failure);
    }
}
