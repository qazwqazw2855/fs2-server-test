using God2.ServerV2.Application;
using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class WorldNpcRegistryTests
{
    [Fact]
    public void Published_npcs_are_map_scoped()
    {
        var registry = new WorldNpcRegistry();

        registry.PublishMap(
            100,
            [
                Npc(1001, 10, 100, 3793),
                Npc(1002, 11, 100, 3954)
            ]);

        registry.PublishMap(
            200,
            [
                Npc(2001, 20, 200, 5004)
            ]);

        Assert.Equal(2, registry.LoadedMapCount);
        Assert.Equal(3, registry.NpcCount);

        Assert.Equal(
            new long[] { 1001, 1002 },
            registry.SnapshotMap(100)
                .Select(entry => entry.SpawnId));

        Assert.Equal(
            new long[] { 2001 },
            registry.SnapshotMap(200)
                .Select(entry => entry.SpawnId));
    }

    [Fact]
    public void Publishing_map_replaces_snapshot_atomically()
    {
        var registry = new WorldNpcRegistry();

        registry.PublishMap(
            100,
            [Npc(1001, 10, 100, 3793)]);

        registry.PublishMap(
            100,
            [
                Npc(1002, 11, 100, 3954),
                Npc(1003, 12, 100, 5004)
            ]);

        Assert.Equal(1, registry.LoadedMapCount);
        Assert.Equal(2, registry.NpcCount);
        Assert.False(registry.TryGetSpawn(1001, out _));
        Assert.True(registry.TryGetSpawn(1002, out _));
        Assert.True(registry.TryGetSpawn(1003, out _));
    }

    [Fact]
    public void Registry_does_not_depend_on_player_connections()
    {
        var registry = new WorldNpcRegistry();

        registry.PublishMap(
            100,
            [Npc(1001, 10, 100, 3793)]);

        // There is intentionally no connection ID, account or character
        // involved in NPC ownership.
        var snapshotBefore = registry.SnapshotMap(100);
        var snapshotAfter = registry.SnapshotMap(100);

        Assert.Single(snapshotBefore);
        Assert.Single(snapshotAfter);
        Assert.Equal(
            snapshotBefore[0].SpawnId,
            snapshotAfter[0].SpawnId);
    }

    [Fact]
    public void Can_resolve_npc_by_map_and_client_handle()
    {
        var registry = new WorldNpcRegistry();

        registry.PublishMap(
            100,
            [
                Npc(1001, 10, 100, 3793),
                Npc(1002, 11, 100, 3954)
            ]);

        Assert.True(
            registry.TryGetByClientEntityHandle(
                100,
                3793,
                out var npc));

        Assert.Equal(1001, npc!.SpawnId);

        Assert.False(
            registry.TryGetByClientEntityHandle(
                200,
                3793,
                out _));
    }

    [Fact]
    public void Evidence_blocked_npc_still_exists_in_world_registry()
    {
        var registry = new WorldNpcRegistry();

        var blocked = Npc(
            1002,
            11,
            100,
            3954,
            wireEvidenceStatus: "EvidenceBlocked");

        registry.PublishMap(100, [blocked]);

        var npc = Assert.Single(registry.SnapshotMap(100));

        Assert.Equal(3954u, npc.ClientEntityHandle);
        Assert.Equal("EvidenceBlocked", npc.WireEvidenceStatus);
    }

    [Fact]
    public void Publishing_foreign_map_entry_is_rejected()
    {
        var registry = new WorldNpcRegistry();

        Assert.Throws<ArgumentException>(() =>
            registry.PublishMap(
                100,
                [Npc(2001, 20, 200, 5004)]));

        Assert.Equal(0, registry.LoadedMapCount);
    }

    [Fact]
    public void Duplicate_spawn_ids_are_rejected()
    {
        var registry = new WorldNpcRegistry();

        Assert.Throws<ArgumentException>(() =>
            registry.PublishMap(
                100,
                [
                    Npc(1001, 10, 100, 3793),
                    Npc(1001, 11, 100, 3954)
                ]));

        Assert.Equal(0, registry.LoadedMapCount);
    }

    private static NpcSnapshotEntry Npc(
        long spawnId,
        long npcId,
        long mapId,
        uint handle,
        string wireEvidenceStatus = "Verified") =>
        new(
            SpawnId: spawnId,
            NpcId: npcId,
            Name: $"npc-{npcId}",
            MapId: mapId,
            PositionX: 65,
            PositionY: 61,
            ClientBuildId: "god2-opt-6b127086e0c0",
            ClientEntityHandle: handle,
            ResourceType: 0,
            ResourceOrdinal: 87,
            SelectorHighBits: 3,
            DirectionCode: 5,
            StateCode: 1,
            SpawnMessageSha256: "test",
            OpaqueTemplateSha256: "test",
            WireEvidenceStatus: wireEvidenceStatus,
            WireEvidenceReference: "test");
}
