using God2.ServerV2.Application;
using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class MerchantInteractionResolverTests
{
    [Fact]
    public void Movement_uses_current_position_and_rejects_out_of_range()
    {
        var fixture = new Fixture();
        Assert.Equal(MerchantInteractionStatus.Allowed, fixture.Check());

        Assert.True(fixture.Presences.TryMove(101, 1, 77, 124, out _));
        Assert.Equal(MerchantInteractionStatus.OutOfRange, fixture.Check());

        Assert.True(fixture.Presences.TryMove(101, 1, 76, 124, out _));
        Assert.Equal(MerchantInteractionStatus.Allowed, fixture.Check());
    }

    [Fact]
    public void Map_change_rejects_previous_map_interaction()
    {
        var fixture = new Fixture();
        Assert.True(fixture.Presences.TryChangeMap(
            101, 1, 200, 74, 124, out _, out _, out _, out _));

        // Even the same handle on the destination map is not the old target.
        fixture.Npcs.PublishMap(200, [fixture.Npc with { MapId = 200 }]);
        Assert.Equal(MerchantInteractionStatus.MapMismatch, fixture.Check());
    }

    [Fact]
    public void Leaving_world_rejects_interaction_even_before_registry_cleanup()
    {
        var fixture = new Fixture();
        Assert.True(fixture.Presences.TryLeave(101, out _));
        Assert.Equal(MerchantInteractionStatus.WorldPresenceMissing,
            fixture.Check());

        Assert.True(fixture.Interactions.Remove(101, out _));
        Assert.Equal(MerchantInteractionStatus.InteractionConflict,
            fixture.Check());
    }

    [Fact]
    public void Closing_and_reopening_does_not_revalidate_old_identity()
    {
        var fixture = new Fixture();
        Assert.Equal(NpcInteractionCloseStatus.Closed,
            fixture.Interactions.TryClose(101, 5042, out _));
        Assert.Equal(MerchantInteractionStatus.InteractionConflict,
            fixture.Check());

        var reopened = fixture.Interactions.TryOpen(
            101, 1, 100, 5042, [fixture.Npc], Fixture.Now).Session!;
        Assert.NotEqual(fixture.InteractionId, reopened.InteractionId);
        Assert.Equal(MerchantInteractionStatus.InteractionConflict,
            fixture.Check());
        Assert.Equal(MerchantInteractionStatus.Allowed,
            fixture.Resolver.Check(
                101, reopened.InteractionId, 99, fixture.Binding));
    }

    [Fact]
    public void Npc_removal_or_handle_reuse_rejects_previous_target()
    {
        var fixture = new Fixture();
        fixture.Npcs.PublishMap(100, []);
        Assert.Equal(MerchantInteractionStatus.NpcMissing, fixture.Check());

        fixture.Npcs.PublishMap(100,
            [fixture.Npc with { SpawnId = 9002, NpcId = 8002 }]);
        Assert.Equal(MerchantInteractionStatus.NpcMismatch, fixture.Check());
    }

    [Fact]
    public void Connection_cannot_use_another_connections_interaction()
    {
        var fixture = new Fixture();
        Assert.Equal(MerchantInteractionStatus.InteractionConflict,
            fixture.Resolver.Check(
                202, fixture.InteractionId, 99, fixture.Binding));
    }

    [Fact]
    public void Replaced_world_character_cannot_use_previous_interaction()
    {
        var fixture = new Fixture();
        Assert.True(fixture.Presences.TryLeave(101, out _));
        Assert.True(fixture.Presences.TryEnter(
            Fixture.Presence(2)).Succeeded);

        Assert.Equal(MerchantInteractionStatus.CharacterMismatch,
            fixture.Check());
    }

    private sealed class Fixture
    {
        public static readonly DateTimeOffset Now =
            new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        public NpcInteractionSessionRegistry Interactions { get; } = new();
        public WorldPresenceRegistry Presences { get; } = new();
        public WorldNpcRegistry Npcs { get; } = new();
        public MerchantInteractionResolver Resolver { get; }
        public Guid InteractionId { get; }

        // Artificial server fixture; no official mapping is promoted.
        public NpcSnapshotEntry Npc { get; } = new(
            9001, 8001, "Fixture NPC", 100, 74, 124,
            "fixture-build", 5042, null, null, null, null, null,
            null, null, "EvidenceBlocked", "fixture");

        public MerchantInteractionBinding Binding { get; } = new(
            99, 8001, 9001, 100, 5042,
            "fixture-build", 2, "fixture-policy", true);

        public Fixture()
        {
            Assert.True(Presences.TryEnter(Presence(1)).Succeeded);
            Npcs.PublishMap(100, [Npc]);
            var opened = Interactions.TryOpen(
                101, 1, 100, 5042, [Npc], Now);
            Assert.True(opened.Succeeded);
            InteractionId = opened.Session!.InteractionId;
            Resolver = new(Interactions, Presences, Npcs);
        }

        public MerchantInteractionStatus Check() =>
            Resolver.Check(101, InteractionId, 99, Binding);

        public static WorldPresence Presence(long characterId) => new(
            101, "fixture-account", 1,
            new CharacterListEntry(
                characterId, 1, "Fixture", null, null, null, 1, null,
                100, 74, 124, Now, null),
            Now);
    }
}
