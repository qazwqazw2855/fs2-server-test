using God2.ServerV2.Application;
using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class WorldPresenceRegistryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_world_connection_enters()
    {
        var registry = new WorldPresenceRegistry();

        var result = registry.TryEnter(
            Presence(101, "account-a", 1, 11));

        Assert.True(result.Succeeded);
        Assert.Equal(WorldPresenceEnterStatus.Entered, result.Status);
        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGetByCharacter(11, out var presence));
        Assert.Equal(101, presence!.ConnectionId);
    }

    [Fact]
    public void Connection_cannot_control_two_characters()
    {
        var registry = new WorldPresenceRegistry();

        registry.TryEnter(
            Presence(101, "account-a", 1, 11));

        var result = registry.TryEnter(
            Presence(101, "account-b", 2, 22));

        Assert.Equal(
            WorldPresenceEnterStatus.ConnectionAlreadyPresent,
            result.Status);
        Assert.Equal(101, result.ExistingConnectionId);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Account_cannot_enter_world_twice()
    {
        var registry = new WorldPresenceRegistry();

        registry.TryEnter(
            Presence(101, "Account-A", 1, 11));

        var result = registry.TryEnter(
            Presence(202, " account-a ", 1, 12));

        Assert.Equal(
            WorldPresenceEnterStatus.AccountAlreadyPresent,
            result.Status);
        Assert.Equal(101, result.ExistingConnectionId);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Character_cannot_be_owned_by_two_connections()
    {
        var registry = new WorldPresenceRegistry();

        registry.TryEnter(
            Presence(101, "account-a", 1, 11));

        var result = registry.TryEnter(
            Presence(202, "account-b", 2, 11, characterAccountId: 2));

        Assert.Equal(
            WorldPresenceEnterStatus.CharacterAlreadyPresent,
            result.Status);
        Assert.Equal(101, result.ExistingConnectionId);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Character_must_belong_to_authenticated_account()
    {
        var registry = new WorldPresenceRegistry();

        Assert.Throws<ArgumentException>(() =>
            registry.TryEnter(
                Presence(
                    101,
                    "account-a",
                    1,
                    11,
                    characterAccountId: 99)));
    }

    [Fact]
    public void Leaving_releases_account_and_character()
    {
        var registry = new WorldPresenceRegistry();

        registry.TryEnter(
            Presence(101, "account-a", 1, 11));

        Assert.True(registry.TryLeave(101, out var departed));
        Assert.Equal(11, departed!.Character.CharacterId);
        Assert.Equal(0, registry.Count);
        Assert.False(registry.TryGetByCharacter(11, out _));

        var replacement = registry.TryEnter(
            Presence(202, "account-a", 1, 11));

        Assert.True(replacement.Succeeded);
        Assert.Equal(202, replacement.ExistingConnectionId);
    }

    [Fact]
    public void Wrong_connection_cannot_remove_presence()
    {
        var registry = new WorldPresenceRegistry();

        registry.TryEnter(
            Presence(101, "account-a", 1, 11));

        Assert.False(registry.TryLeave(202, out _));
        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGetByConnection(101, out _));
    }

    [Fact]
    public void Enter_returns_existing_peers_on_same_map()
    {
        var registry = new WorldPresenceRegistry();

        registry.TryEnter(
            Presence(101, "account-a", 1, 11, mapId: 100));

        var result = registry.TryEnter(
            Presence(202, "account-b", 2, 22, mapId: 100),
            out var visiblePeers);

        Assert.True(result.Succeeded);
        var peer = Assert.Single(visiblePeers);
        Assert.Equal(101, peer.ConnectionId);
        Assert.Equal(11, peer.Character.CharacterId);
    }

    [Fact]
    public void Enter_excludes_players_on_other_maps()
    {
        var registry = new WorldPresenceRegistry();

        registry.TryEnter(
            Presence(101, "account-a", 1, 11, mapId: 100));

        var result = registry.TryEnter(
            Presence(202, "account-b", 2, 22, mapId: 200),
            out var visiblePeers);

        Assert.True(result.Succeeded);
        Assert.Empty(visiblePeers);
    }

    [Fact]
    public void Leave_returns_remaining_peers_on_same_map()
    {
        var registry = new WorldPresenceRegistry();

        registry.TryEnter(
            Presence(101, "account-a", 1, 11, mapId: 100));
        registry.TryEnter(
            Presence(202, "account-b", 2, 22, mapId: 100));
        registry.TryEnter(
            Presence(303, "account-c", 3, 33, mapId: 200));

        Assert.True(registry.TryLeave(
            101,
            out var departed,
            out var visiblePeers));

        Assert.Equal(11, departed!.Character.CharacterId);
        var peer = Assert.Single(visiblePeers);
        Assert.Equal(202, peer.ConnectionId);
        Assert.Equal(22, peer.Character.CharacterId);
    }

    [Fact]
    public void Character_without_map_has_no_visible_peers()
    {
        var registry = new WorldPresenceRegistry();

        registry.TryEnter(
            Presence(101, "account-a", 1, 11, mapId: null));

        var result = registry.TryEnter(
            Presence(202, "account-b", 2, 22, mapId: null),
            out var visiblePeers);

        Assert.True(result.Succeeded);
        Assert.Empty(visiblePeers);
    }

    [Fact]
    public void Snapshot_is_stable_and_connection_ordered()
    {
        var registry = new WorldPresenceRegistry();

        registry.TryEnter(
            Presence(202, "account-b", 2, 22));
        registry.TryEnter(
            Presence(101, "account-a", 1, 11));

        var snapshot = registry.Snapshot();

        Assert.Equal(
            new long[] { 101, 202 },
            snapshot.Select(presence => presence.ConnectionId));
    }

    private static WorldPresence Presence(
        long connectionId,
        string accountName,
        long accountId,
        long characterId,
        long? characterAccountId = null,
        long? mapId = 1)
    {
        var ownerAccountId =
            characterAccountId ?? accountId;

        return new WorldPresence(
            connectionId,
            accountName,
            accountId,
            new CharacterListEntry(
                CharacterId: characterId,
                AccountId: ownerAccountId,
                Name: $"character-{characterId}",
                ClassCode: "Warrior",
                GenderCode: "Male",
                LifeSkillCode: "WeaponForging",
                Level: 1,
                AppearanceCode: "Default",
                MapId: mapId,
                PositionX: 10,
                PositionY: 20,
                CreatedAtUtc: Now,
                LastPlayedAtUtc: null),
            Now);
    }
}
