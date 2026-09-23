namespace God2.ServerV2.Application;

public sealed record ItemStackChangePreview(
    int AdditionalSlotsNeeded,
    int FreeSlots,
    bool Fits);

public static class ItemStackChangePlanner
{
    public static ItemStackChangePreview Preview(
        CharacterInventorySnapshot inventory,
        long itemId,
        int newMaximumStack)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (itemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(itemId));
        if (newMaximumStack <= 0)
            throw new ArgumentOutOfRangeException(nameof(newMaximumStack));
        if (inventory.Capacity <= 0 ||
            inventory.Capacity < inventory.Slots.Count)
            throw new InvalidDataException("Invalid inventory capacity.");

        var seenSlots = new HashSet<int>();
        var additional = 0;
        foreach (var slot in inventory.Slots)
        {
            if (slot.SlotIndex < 0 ||
                slot.SlotIndex >= inventory.Capacity ||
                slot.ItemId <= 0 ||
                slot.Quantity <= 0 ||
                !seenSlots.Add(slot.SlotIndex))
                throw new InvalidDataException("Invalid inventory slot.");

            if (slot.ItemId == itemId)
                additional = checked(
                    additional + (slot.Quantity - 1) / newMaximumStack);
        }

        var free = inventory.Capacity - inventory.Slots.Count;
        return new ItemStackChangePreview(
            additional, free, additional <= free);
    }
}
