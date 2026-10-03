namespace God2.ServerV2.Application;

public sealed record QuestRewardEntry(
    long RewardId,
    string RewardType,
    long? ItemId,
    int? Quantity,
    long? Experience = null,
    long? Currency = null,
    string? SelectionGroup = null);

public enum QuestRewardBatchFailure
{
    None,
    EmptyRewards,
    UnsupportedReward,
    InvalidReward,
    ItemMissing,
    ItemDisabled,
    InsufficientCapacity
}

public sealed record QuestRewardBatchPlan(
    QuestRewardBatchFailure Failure,
    IReadOnlyList<CharacterInventorySlot> ChangedSlots)
{
    public bool Succeeded => Failure == QuestRewardBatchFailure.None;
}

// Planning only. Neither a completion proof nor permission to grant rewards.
public static class QuestRewardBatchPlanner
{
    public static QuestRewardBatchPlan Plan(
        CharacterInventorySnapshot inventory,
        IReadOnlyList<QuestRewardEntry> rewards,
        IReadOnlyDictionary<long, ItemStackRule> rules)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(rewards);
        ArgumentNullException.ThrowIfNull(rules);

        static QuestRewardBatchPlan Reject(QuestRewardBatchFailure failure) =>
            new(failure, Array.Empty<CharacterInventorySlot>());

        if (rewards.Count == 0)
            return Reject(QuestRewardBatchFailure.EmptyRewards);

        var seen = new HashSet<long>();

        // Validate the complete manifest before planning any inventory changes.
        foreach (var reward in rewards)
        {
            ArgumentNullException.ThrowIfNull(reward);
            if (reward.RewardId <= 0 || !seen.Add(reward.RewardId))
                return Reject(QuestRewardBatchFailure.InvalidReward);

            if (reward.RewardType != "Item" ||
                reward.Experience is not null ||
                reward.Currency is not null ||
                !string.IsNullOrEmpty(reward.SelectionGroup))
                return Reject(QuestRewardBatchFailure.UnsupportedReward);

            if (reward.ItemId is not > 0 ||
                reward.ItemId > int.MaxValue ||
                reward.Quantity is not > 0)
                return Reject(QuestRewardBatchFailure.InvalidReward);

            if (!rules.TryGetValue(reward.ItemId.Value, out var rule))
                return Reject(QuestRewardBatchFailure.ItemMissing);

            ArgumentNullException.ThrowIfNull(rule);
            if (rule.ItemId != reward.ItemId.Value)
                throw new InvalidDataException("Item rule identity mismatch.");

            if (!rule.Enabled)
                return Reject(QuestRewardBatchFailure.ItemDisabled);
        }

        var projected = inventory;
        var changes = new Dictionary<int, CharacterInventorySlot>();

        foreach (var reward in rewards.OrderBy(value => value.RewardId))
        {
            var itemId = reward.ItemId!.Value;
            var grant = InventoryGrantPlanner.Plan(
                projected, rules[itemId], reward.Quantity!.Value,
                "Unbound", "{}");

            if (!grant.Succeeded)
            {
                if (grant.Failure == InventoryGrantFailure.InsufficientCapacity)
                    return Reject(QuestRewardBatchFailure.InsufficientCapacity);

                throw new InvalidDataException(
                    $"Unexpected inventory planning failure: {grant.Failure}");
            }

            var slots = projected.Slots.ToDictionary(value => value.SlotIndex);
            foreach (var slot in grant.ChangedSlots)
            {
                slots[slot.SlotIndex] = slot;
                changes[slot.SlotIndex] = slot;
            }

            projected = projected with
            {
                Slots = Array.AsReadOnly(
                    slots.Values.OrderBy(value => value.SlotIndex).ToArray())
            };
        }

        return new QuestRewardBatchPlan(
            QuestRewardBatchFailure.None,
            Array.AsReadOnly(
                changes.Values.OrderBy(value => value.SlotIndex).ToArray()));
    }
}
