namespace God2.ServerV2.Application;

public sealed record ItemStackRule(
    long ItemId,
    string Name,
    int? ConfiguredMaximumStack,
    bool Enabled)
{
    public int EffectiveMaximumStack => ConfiguredMaximumStack ?? 1;
    public bool IsStackable => Enabled && EffectiveMaximumStack > 1;
}

public interface IItemStackRuleRepository
{
    ValueTask<ItemStackRule?> GetByItemIdAsync(
        long itemId,
        CancellationToken cancellationToken);
}
