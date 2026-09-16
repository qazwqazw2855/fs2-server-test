using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class NpcSnapshotServiceTests
{
    [Fact]
    public async Task Returns_map_snapshot_in_spawn_order()
    {
        var service = new NpcSnapshotService(
            new StubRepository([
                Entry(2, 5096),
                Entry(1, 5042)
            ]));

        var entries = await service.GetAsync(
            1675308248,
            CancellationToken.None);

        Assert.Equal([1L, 2L], entries.Select(x => x.SpawnId));
    }

    [Fact]
    public async Task Rejects_spawn_from_another_map()
    {
        var service = new NpcSnapshotService(
            new StubRepository([
                Entry(1, 5042) with { MapId = 999 }
            ]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.GetAsync(
                1675308248,
                CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_duplicate_client_handles()
    {
        var service = new NpcSnapshotService(
            new StubRepository([
                Entry(1, 5042),
                Entry(2, 5042)
            ]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.GetAsync(
                1675308248,
                CancellationToken.None));
    }

    private static NpcSnapshotEntry Entry(
        long spawnId,
        uint handle) =>
        new(
            spawnId,
            spawnId + 1000,
            $"NPC-{spawnId}",
            1675308248,
            74,
            124,
            "god2-opt-6b127086e0c0",
            handle,
            0,
            45,
            3,
            4,
            1,
            new string('A', 64),
            new string('B', 64),
            "Derived",
            "test");

    private sealed class StubRepository(
        IReadOnlyList<NpcSnapshotEntry> entries) :
        INpcSnapshotRepository
    {
        public ValueTask<IReadOnlyList<NpcSnapshotEntry>>
            ListByMapAsync(
                long mapId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(entries);
        }
    }
}
