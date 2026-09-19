using God2.ServerV2.Application;
using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class WorldNpcStateServiceTests
{
    [Fact]
    public async Task Ensure_loads_map_once()
    {
        var repository = new CountingRepository(
            [Npc(100, 1001)]);

        var registry = new WorldNpcRegistry();

        var service = new WorldNpcStateService(
            new NpcSnapshotService(repository),
            registry);

        var first = await service.EnsureLoadedAsync(
            100,
            CancellationToken.None);

        var second = await service.EnsureLoadedAsync(
            100,
            CancellationToken.None);

        Assert.Equal(
            WorldNpcLoadStatus.Loaded,
            first.Status);

        Assert.Equal(
            WorldNpcLoadStatus.AlreadyLoaded,
            second.Status);

        Assert.Equal(1, repository.CallCount);
        Assert.True(registry.IsMapLoaded(100));
        Assert.Single(registry.SnapshotMap(100));
    }

    [Fact]
    public async Task Empty_map_is_still_considered_loaded()
    {
        var repository = new CountingRepository([]);

        var registry = new WorldNpcRegistry();

        var service = new WorldNpcStateService(
            new NpcSnapshotService(repository),
            registry);

        await service.EnsureLoadedAsync(
            100,
            CancellationToken.None);

        await service.EnsureLoadedAsync(
            100,
            CancellationToken.None);

        Assert.Equal(1, repository.CallCount);
        Assert.True(registry.IsMapLoaded(100));
        Assert.Empty(registry.SnapshotMap(100));
    }

    [Fact]
    public async Task Refresh_explicitly_reloads_map()
    {
        var repository = new CountingRepository(
            [Npc(100, 1001)]);

        var registry = new WorldNpcRegistry();

        var service = new WorldNpcStateService(
            new NpcSnapshotService(repository),
            registry);

        await service.EnsureLoadedAsync(
            100,
            CancellationToken.None);

        repository.Entries =
            [Npc(100, 2002)];

        var refreshed = await service.RefreshAsync(
            100,
            CancellationToken.None);

        Assert.Equal(
            WorldNpcLoadStatus.Refreshed,
            refreshed.Status);

        Assert.Equal(2, repository.CallCount);

        var snapshot =
            registry.SnapshotMap(100);

        Assert.Single(snapshot);
        Assert.Equal(2002, snapshot[0].SpawnId);
    }

    private static NpcSnapshotEntry Npc(
        long mapId,
        long spawnId) =>
        new(
            SpawnId: spawnId,
            NpcId: spawnId,
            Name: $"NPC-{spawnId}",
            MapId: mapId,
            PositionX: 10,
            PositionY: 20,
            ClientBuildId: null,
            ClientEntityHandle: null,
            ResourceType: null,
            ResourceOrdinal: null,
            SelectorHighBits: null,
            DirectionCode: null,
            StateCode: null,
            SpawnMessageSha256: null,
            OpaqueTemplateSha256: null,
            WireEvidenceStatus: "EvidenceBlocked",
            WireEvidenceReference: null);

    private sealed class CountingRepository :
        INpcSnapshotRepository
    {
        public CountingRepository(
            IReadOnlyList<NpcSnapshotEntry> entries)
        {
            Entries = entries;
        }

        public IReadOnlyList<NpcSnapshotEntry> Entries
        {
            get;
            set;
        }

        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyList<NpcSnapshotEntry>>
            ListByMapAsync(
                long mapId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            return ValueTask.FromResult(Entries);
        }
    }
}
