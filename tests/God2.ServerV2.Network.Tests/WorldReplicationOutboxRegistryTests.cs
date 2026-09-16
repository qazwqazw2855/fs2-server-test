using God2.ServerV2.Application;
using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class WorldReplicationOutboxRegistryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Registered_connection_accepts_ordered_events()
    {
        var registry =
            new WorldReplicationOutboxRegistry();

        Assert.True(registry.TryRegister(101));

        Assert.True(registry.TryEnqueue(
            101,
            WorldReplicationEventKind.PlayerEntered,
            Presence(202, 2),
            Now,
            out var entered));

        Assert.True(registry.TryEnqueue(
            101,
            WorldReplicationEventKind.PlayerLeft,
            Presence(202, 2),
            Now.AddSeconds(1),
            out var left));

        var events = registry.Snapshot(101);

        Assert.Equal(2, events.Count);
        Assert.Equal(entered!.Sequence, events[0].Sequence);
        Assert.Equal(left!.Sequence, events[1].Sequence);
        Assert.True(events[0].Sequence < events[1].Sequence);
    }

    [Fact]
    public void Unknown_recipient_is_rejected()
    {
        var registry =
            new WorldReplicationOutboxRegistry();

        Assert.False(registry.TryEnqueue(
            999,
            WorldReplicationEventKind.PlayerEntered,
            Presence(202, 2),
            Now,
            out var queuedEvent));

        Assert.Null(queuedEvent);
    }

    [Fact]
    public void Duplicate_registration_is_rejected()
    {
        var registry =
            new WorldReplicationOutboxRegistry();

        Assert.True(registry.TryRegister(101));
        Assert.False(registry.TryRegister(101));
        Assert.Equal(1, registry.ConnectionCount);
    }

    [Fact]
    public void Outbox_is_bounded_and_drops_oldest_event()
    {
        var registry =
            new WorldReplicationOutboxRegistry(
                maximumEventsPerConnection: 2);

        registry.TryRegister(101);

        registry.TryEnqueue(
            101,
            WorldReplicationEventKind.PlayerEntered,
            Presence(201, 1),
            Now,
            out _);

        registry.TryEnqueue(
            101,
            WorldReplicationEventKind.PlayerEntered,
            Presence(202, 2),
            Now.AddSeconds(1),
            out _);

        registry.TryEnqueue(
            101,
            WorldReplicationEventKind.PlayerEntered,
            Presence(203, 3),
            Now.AddSeconds(2),
            out _);

        var events = registry.Snapshot(101);

        Assert.Equal(2, events.Count);
        Assert.Equal(2, events[0].Subject.Character.CharacterId);
        Assert.Equal(3, events[1].Subject.Character.CharacterId);
    }

    [Fact]
    public void Removing_connection_discards_only_its_events()
    {
        var registry =
            new WorldReplicationOutboxRegistry();

        registry.TryRegister(101);
        registry.TryRegister(202);

        registry.TryEnqueue(
            101,
            WorldReplicationEventKind.PlayerEntered,
            Presence(202, 2),
            Now,
            out _);

        registry.TryEnqueue(
            202,
            WorldReplicationEventKind.PlayerEntered,
            Presence(101, 1),
            Now,
            out _);

        Assert.True(registry.TryRemove(
            101,
            out var discarded));

        Assert.Single(discarded);
        Assert.Empty(registry.Snapshot(101));
        Assert.Single(registry.Snapshot(202));
        Assert.Equal(1, registry.ConnectionCount);
    }

    [Fact]
    public void Movement_event_preserves_verified_fields()
    {
        var registry =
            new WorldReplicationOutboxRegistry();

        registry.TryRegister(101);

        var movement =
            new WorldReplicationMovement(
                X: 16,
                Y: 15,
                Sequence: 2,
                State: 0xFF);

        Assert.True(registry.TryEnqueueMovement(
            101,
            Presence(202, 2),
            movement,
            Now,
            out var queued));

        Assert.Equal(
            WorldReplicationEventKind.PlayerMoved,
            queued!.Kind);
        Assert.Equal(movement, queued.Movement);
        Assert.Equal(2, queued.Subject.Character.CharacterId);
    }

    [Fact]
    public void Generic_enqueue_rejects_movement_without_payload()
    {
        var registry =
            new WorldReplicationOutboxRegistry();

        registry.TryRegister(101);

        Assert.Throws<ArgumentException>(() =>
            registry.TryEnqueue(
                101,
                WorldReplicationEventKind.PlayerMoved,
                Presence(202, 2),
                Now,
                out _));

        Assert.Empty(registry.Snapshot(101));
    }

    [Fact]
    public void Unknown_recipient_rejects_movement_event()
    {
        var registry =
            new WorldReplicationOutboxRegistry();

        Assert.False(registry.TryEnqueueMovement(
            999,
            Presence(202, 2),
            new WorldReplicationMovement(16, 15, 2, 0xFF),
            Now,
            out var queued));

        Assert.Null(queued);
    }

    private static WorldPresence Presence(
        long connectionId,
        long characterId) =>
        new(
            connectionId,
            $"account-{characterId}",
            characterId,
            new CharacterListEntry(
                CharacterId: characterId,
                AccountId: characterId,
                Name: $"character-{characterId}",
                ClassCode: "Swordsman",
                GenderCode: "Female",
                LifeSkillCode: "LifeSkill1",
                Level: 1,
                AppearanceCode: "Appearance1",
                MapId: 100,
                PositionX: 0,
                PositionY: 0,
                CreatedAtUtc: Now,
                LastPlayedAtUtc: null),
            Now);
}
