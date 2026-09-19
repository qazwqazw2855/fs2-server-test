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
        if (inventory.Capacity < inventory.Slots.Count)
            throw new InvalidDataException("Inventory exceeds capacity.");

        var additional = 0;
        foreach (var slot in inventory.Slots.Where(s => s.ItemId == itemId))
        {
            if (slot.Quantity <= 0)
                throw new InvalidDataException("Inventory quantity must be positive.");

            additional = checked(
                additional + (slot.Quantity - 1) / newMaximumStack);
        }

        var free = inventory.Capacity - inventory.Slots.Count;
        return new ItemStackChangePreview(
            additional, free, additional <= free);
    }
}
