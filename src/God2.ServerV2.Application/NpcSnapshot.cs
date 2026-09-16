namespace God2.ServerV2.Application;

public sealed record NpcSnapshotEntry(
    long SpawnId,
    long NpcId,
    string Name,
    long MapId,
    int? PositionX,
    int? PositionY,
    string? ClientBuildId,
    uint? ClientEntityHandle,
    byte? ResourceType,
    byte? ResourceOrdinal,
    byte? SelectorHighBits,
    byte? DirectionCode,
    byte? StateCode);

public interface INpcSnapshotRepository
{
    ValueTask<IReadOnlyList<NpcSnapshotEntry>> ListByMapAsync(
        long mapId,
        CancellationToken cancellationToken);
}

public sealed class EmptyNpcSnapshotRepository :
    INpcSnapshotRepository
{
    public ValueTask<IReadOnlyList<NpcSnapshotEntry>> ListByMapAsync(
        long mapId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<NpcSnapshotEntry>>([]);
    }
}

public sealed class NpcSnapshotService
{
    private readonly INpcSnapshotRepository _repository;

    public NpcSnapshotService(INpcSnapshotRepository repository)
    {
        _repository = repository ??
            throw new ArgumentNullException(nameof(repository));
    }

    public async ValueTask<IReadOnlyList<NpcSnapshotEntry>> GetAsync(
        long mapId,
        CancellationToken cancellationToken)
    {
        if (mapId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId));
        }

        var entries = await _repository.ListByMapAsync(
            mapId,
            cancellationToken);

        if (entries.Any(entry => entry.MapId != mapId))
        {
            throw new InvalidOperationException(
                "The NPC repository returned a spawn from another map.");
        }

        if (entries
            .GroupBy(entry => entry.SpawnId)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                "The NPC repository returned duplicate spawn IDs.");
        }

        if (entries
            .Where(entry => entry.ClientEntityHandle.HasValue)
            .GroupBy(entry => entry.ClientEntityHandle!.Value)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                "The NPC repository returned duplicate Client entity handles.");
        }

        return entries
            .OrderBy(entry => entry.SpawnId)
            .ToArray();
    }
}
