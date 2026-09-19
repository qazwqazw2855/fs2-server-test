namespace God2.ServerV2.Application;

public sealed record ItemStackChangeImpact(
    long CharacterId,
    ItemStackChangePreview Preview);

public interface IItemStackChangeImpactRepository
{
    ValueTask<IReadOnlyList<ItemStackChangeImpact>> ListByItemAsync(
        long itemId,
        int newMaximumStack,
        CancellationToken cancellationToken);
}
