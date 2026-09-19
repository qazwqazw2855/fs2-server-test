using God2.ServerV2.Application;

namespace God2.ServerV2.Network;

public enum WorldNpcLoadStatus
{
    AlreadyLoaded,
    Loaded,
    Refreshed
}

public sealed record WorldNpcLoadResult(
    long MapId,
    WorldNpcLoadStatus Status,
    IReadOnlyList<NpcSnapshotEntry> Snapshot);

public sealed class WorldNpcStateService
{
    private readonly NpcSnapshotService _snapshotService;
    private readonly WorldNpcRegistry _worldNpcs;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public WorldNpcStateService(
        NpcSnapshotService snapshotService,
        WorldNpcRegistry worldNpcs)
    {
        _snapshotService = snapshotService ??
            throw new ArgumentNullException(
                nameof(snapshotService));

        _worldNpcs = worldNpcs ??
            throw new ArgumentNullException(
                nameof(worldNpcs));
    }

    public async ValueTask<WorldNpcLoadResult> EnsureLoadedAsync(
        long mapId,
        CancellationToken cancellationToken)
    {
        if (mapId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mapId));
        }

        if (_worldNpcs.IsMapLoaded(mapId))
        {
            return new WorldNpcLoadResult(
                mapId,
                WorldNpcLoadStatus.AlreadyLoaded,
                _worldNpcs.SnapshotMap(mapId));
        }

        await _loadGate.WaitAsync(cancellationToken);

        try
        {
            if (_worldNpcs.IsMapLoaded(mapId))
            {
                return new WorldNpcLoadResult(
                    mapId,
                    WorldNpcLoadStatus.AlreadyLoaded,
                    _worldNpcs.SnapshotMap(mapId));
            }

            var snapshot =
                await _snapshotService.GetAsync(
                    mapId,
                    cancellationToken);

            _worldNpcs.PublishMap(
                mapId,
                snapshot);

            return new WorldNpcLoadResult(
                mapId,
                WorldNpcLoadStatus.Loaded,
                _worldNpcs.SnapshotMap(mapId));
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public async ValueTask<WorldNpcLoadResult> RefreshAsync(
        long mapId,
        CancellationToken cancellationToken)
    {
        if (mapId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mapId));
        }

        await _loadGate.WaitAsync(cancellationToken);

        try
        {
            var snapshot =
                await _snapshotService.GetAsync(
                    mapId,
                    cancellationToken);

            _worldNpcs.PublishMap(
                mapId,
                snapshot);

            return new WorldNpcLoadResult(
                mapId,
                WorldNpcLoadStatus.Refreshed,
                _worldNpcs.SnapshotMap(mapId));
        }
        finally
        {
            _loadGate.Release();
        }
    }
}
