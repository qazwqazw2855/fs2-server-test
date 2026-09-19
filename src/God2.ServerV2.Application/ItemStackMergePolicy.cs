namespace God2.ServerV2.Application;

public static class ItemStackMergePolicy
{
    public static bool CanMerge(
        ItemStackRule rule,
        CharacterInventorySlot source,
        CharacterInventorySlot target)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        return rule.IsStackable &&
            source.SlotIndex != target.SlotIndex &&
            source.ItemId == rule.ItemId &&
            target.ItemId == rule.ItemId &&
            source.Quantity > 0 &&
            target.Quantity > 0 &&
            (long)source.Quantity + target.Quantity <=
                rule.EffectiveMaximumStack &&
            source.BindState == "Unbound" &&
            target.BindState == "Unbound" &&
            source.ItemInstanceMetadata == "{}" &&
            target.ItemInstanceMetadata == "{}";
    }
}
