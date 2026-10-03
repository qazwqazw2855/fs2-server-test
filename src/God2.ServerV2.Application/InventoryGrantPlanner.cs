namespace God2.ServerV2.Application;

public enum InventoryGrantFailure
{
    None,
    ItemDisabled,
    UnsupportedItemState,
    InsufficientCapacity
}

public sealed record InventoryGrantPlan(
    Guid InventoryId,
    long CharacterId,
    long ExpectedVersion,
    long ExpectedMutationSequence,
    InventoryGrantFailure Failure,
    IReadOnlyList<CharacterInventorySlot> ChangedSlots)
{
    public bool Succeeded => Failure == InventoryGrantFailure.None;
}

public static class InventoryGrantPlanner
{
    public static InventoryGrantPlan Plan(
        CharacterInventorySnapshot inventory,
        ItemStackRule rule,
        int quantity,
        string bindState,
        string itemInstanceMetadata)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(inventory.Slots);
        ArgumentNullException.ThrowIfNull(bindState);
        ArgumentNullException.ThrowIfNull(itemInstanceMetadata);

        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (rule.ItemId <= 0 || rule.EffectiveMaximumStack <= 0)
            throw new InvalidDataException("Invalid item stack rule.");
        if (inventory.InventoryId == Guid.Empty ||
            inventory.CharacterId <= 0 ||
            inventory.Version < 0 ||
            inventory.MutationSequence < 0 ||
            inventory.Capacity <= 0 ||
            inventory.Slots.Count > inventory.Capacity)
            throw new InvalidDataException("Invalid inventory state.");

        var occupied = new HashSet<int>();
        foreach (var slot in inventory.Slots)
        {
            if (slot is null ||
                slot.SlotIndex < 0 ||
                slot.SlotIndex >= inventory.Capacity ||
                slot.ItemId <= 0 ||
                slot.Quantity <= 0 ||
                !occupied.Add(slot.SlotIndex))
                throw new InvalidDataException("Invalid inventory slot.");

            if (slot.ItemId == rule.ItemId &&
                slot.Quantity > rule.EffectiveMaximumStack)
                throw new InvalidDataException(
                    "Existing item quantity exceeds its stack limit.");
        }

        InventoryGrantPlan Result(
            InventoryGrantFailure failure,
            IReadOnlyList<CharacterInventorySlot> changedSlots) =>
            new(
                inventory.InventoryId,
                inventory.CharacterId,
                inventory.Version,
                inventory.MutationSequence,
                failure,
                changedSlots);

        if (!rule.Enabled)
            return Result(
                InventoryGrantFailure.ItemDisabled,
                Array.Empty<CharacterInventorySlot>());

        if (bindState != "Unbound" || itemInstanceMetadata != "{}")
            return Result(
                InventoryGrantFailure.UnsupportedItemState,
                Array.Empty<CharacterInventorySlot>());

        // Work on an independent plan; the input snapshot is never mutated.
        var changes = new List<CharacterInventorySlot>();
        long remaining = quantity;

        foreach (var target in inventory.Slots.OrderBy(slot => slot.SlotIndex))
        {
            if (remaining == 0)
                break;
            if (target.ItemId != rule.ItemId)
                continue;

            var available = rule.EffectiveMaximumStack - target.Quantity;
            if (available <= 0)
                continue;

            var added = (int)Math.Min(remaining, available);
            // -1 represents incoming items, not an occupied inventory slot.
            var source = new CharacterInventorySlot(
                -1, rule.ItemId, added, bindState, itemInstanceMetadata);

            if (!ItemStackMergePolicy.CanMerge(rule, source, target))
                continue;

            changes.Add(target with
            {
                Quantity = checked(target.Quantity + added)
            });
            remaining -= added;
        }

        var freeSlots = inventory.Capacity - occupied.Count;
        var requiredSlots =
            (remaining + rule.EffectiveMaximumStack - 1L) /
            rule.EffectiveMaximumStack;

        if (requiredSlots > freeSlots)
            return Result(
                InventoryGrantFailure.InsufficientCapacity,
                Array.Empty<CharacterInventorySlot>());

        for (var index = 0; remaining > 0 && index < inventory.Capacity; index++)
        {
            if (occupied.Contains(index))
                continue;

            var added = (int)Math.Min(
                remaining, rule.EffectiveMaximumStack);
            changes.Add(new CharacterInventorySlot(
                index, rule.ItemId, added, bindState, itemInstanceMetadata));
            remaining -= added;
        }

        return Result(
            InventoryGrantFailure.None,
            Array.AsReadOnly(
                changes.OrderBy(slot => slot.SlotIndex).ToArray()));
    }
}
