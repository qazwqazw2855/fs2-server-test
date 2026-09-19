using God2.ServerV2.Application;
using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class WorldNpcSpatialQueryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resolves_npc_on_players_current_map()
    {
        var presences = new WorldPresenceRegistry();
        var npcs = new WorldNpcRegistry();

        presences.TryEnter(
            Presence(
                connectionId: 101,
                characterId: 11,
                mapId: 170015007,
                x: 65,
                y: 64));

        npcs.PublishMap(
            170015007,
            [
                Npc(
                    spawnId: 170153793,
                    mapId: 170015007,
                    handle: 3793,
                    x: 65,
                    y: 61)
            ]);

        var query =
            new WorldNpcSpatialQuery(presences, npcs);

        Assert.True(
            query.TryResolve(
                101,
                3793,
                out var result));

        Assert.Equal(170153793, result.Npc.SpawnId);
        Assert.Equal(0, result.DeltaX);
        Assert.Equal(3, result.DeltaY);
        Assert.Equal(3, result.ChebyshevDistance);
    }

    [Fact]
    public void Movement_changes_spatial_result()
    {
        var presences = new WorldPresenceRegistry();
        var npcs = new WorldNpcRegistry();

        presences.TryEnter(
            Presence(
                connectionId: 101,
                characterId: 11,
                mapId: 170015007,
                x: 16,
                y: 15));

        npcs.PublishMap(
            170015007,
            [
                Npc(
                    spawnId: 170153793,
                    mapId: 170015007,
                    handle: 3793,
                    x: 65,
                    y: 61)
            ]);

        var query =
            new WorldNpcSpatialQuery(presences, npcs);

        Assert.True(
            query.TryResolve(
                101,
                3793,
                out var before));

        Assert.True(
            presences.TryMove(
                101,
                11,
                65,
                64,
                out _));

        Assert.True(
            query.TryResolve(
                101,
                3793,
                out var after));

        Assert.Equal(49, before.DeltaX);
        Assert.Equal(46, before.DeltaY);
        Assert.Equal(49, before.ChebyshevDistance);

        Assert.Equal(0, after.DeltaX);
        Assert.Equal(3, after.DeltaY);
        Assert.Equal(3, after.ChebyshevDistance);
    }

    [Fact]
    public void Npc_on_other_map_cannot_be_resolved()
    {
        var presences = new WorldPresenceRegistry();
        var npcs = new WorldNpcRegistry();

        presences.TryEnter(
            Presence(
                connectionId: 101,
                characterId: 11,
                mapId: 100,
                x: 65,
                y: 64));

        npcs.PublishMap(
            200,
            [
                Npc(
                    spawnId: 2001,
                    mapId: 200,
                    handle: 3793,
                    x: 65,
                    y: 61)
            ]);

        var query =
            new WorldNpcSpatialQuery(presences, npcs);

        Assert.False(
            query.TryResolve(
                101,
                3793,
                out _));
    }

    [Fact]
    public void Unknown_connection_cannot_resolve_npc()
    {
        var presences = new WorldPresenceRegistry();
        var npcs = new WorldNpcRegistry();

        npcs.PublishMap(
            170015007,
            [
                Npc(
                    spawnId: 170153793,
                    mapId: 170015007,
                    handle: 3793,
                    x: 65,
                    y: 61)
            ]);

        var query =
            new WorldNpcSpatialQuery(presences, npcs);

        Assert.False(
            query.TryResolve(
                999,
                3793,
                out _));
    }

    private static WorldPresence Presence(
        long connectionId,
        long characterId,
        long mapId,
        int x,
        int y) =>
        new(
            connectionId,
            $"account-{characterId}",
            characterId,
            new CharacterListEntry(
                CharacterId: characterId,
                AccountId: characterId,
                Name: $"character-{characterId}",
                ClassCode: "Swordsman",
                GenderCode: "Male",
                LifeSkillCode: "LifeSkill1",
                Level: 1,
                AppearanceCode: "Default",
                MapId: mapId,
                PositionX: x,
                PositionY: y,
                CreatedAtUtc: Now,
                LastPlayedAtUtc: null),
            Now);

    private static NpcSnapshotEntry Npc(
        long spawnId,
        long mapId,
        uint handle,
        int x,
        int y) =>
        new(
            SpawnId: spawnId,
            NpcId: spawnId,
            Name: $"npc-{handle}",
            MapId: mapId,
            PositionX: x,
            PositionY: y,
            ClientBuildId: null,
            ClientEntityHandle: handle,
            ResourceType: null,
            ResourceOrdinal: null,
            SelectorHighBits: null,
            DirectionCode: null,
            StateCode: null,
            SpawnMessageSha256: null,
            OpaqueTemplateSha256: null,
            WireEvidenceStatus: "EvidenceBlocked",
            WireEvidenceReference: null);
}
