namespace God2.ServerV2.Application;

public sealed record MapMovementBounds(
    long MapId,
    int MinimumX,
    int MaximumX,
    int MinimumY,
    int MaximumY)
{
    public bool Contains(int x, int y) =>
        x >= MinimumX && x <= MaximumX &&
        y >= MinimumY && y <= MaximumY;
}

public interface IMapMovementBoundsRepository
{
    ValueTask<MapMovementBounds?> GetByMapAsync(
        long mapId,
        CancellationToken cancellationToken);
}
