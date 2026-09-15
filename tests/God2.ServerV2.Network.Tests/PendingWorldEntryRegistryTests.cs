using God2.ServerV2.Application;
using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class PendingWorldEntryRegistryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reserved_entry_can_be_claimed_exactly_once()
    {
        var registry = new PendingWorldEntryRegistry();

        Assert.True(registry.TryReserve(
            "127.0.0.1",
            1,
            1,
            Character(),
            Now));

        Assert.True(registry.TryClaim(
            "127.0.0.1",
            Now.AddSeconds(1),
            out var claimed));

        Assert.NotNull(claimed);
        Assert.Equal(1, claimed.AccountId);
        Assert.Equal("test001", claimed.Character.Name);

        Assert.False(registry.TryClaim(
            "127.0.0.1",
            Now.AddSeconds(2),
            out _));
    }

    [Fact]
    public void Expired_entry_cannot_be_claimed()
    {
        var registry =
            new PendingWorldEntryRegistry(TimeSpan.FromMinutes(2));

        Assert.True(registry.TryReserve(
            "127.0.0.1",
            1,
            1,
            Character(),
            Now));

        Assert.False(registry.TryClaim(
            "127.0.0.1",
            Now.AddMinutes(2),
            out _));
    }

    [Fact]
    public void Duplicate_remote_address_is_rejected()
    {
        var registry = new PendingWorldEntryRegistry();

        Assert.True(registry.TryReserve(
            "127.0.0.1",
            1,
            1,
            Character(),
            Now));

        Assert.False(registry.TryReserve(
            "127.0.0.1",
            1,
            1,
            Character(),
            Now.AddSeconds(1)));
    }

    [Fact]
    public void Character_must_belong_to_authenticated_account()
    {
        var registry = new PendingWorldEntryRegistry();

        Assert.Throws<ArgumentException>(() =>
            registry.TryReserve(
                "127.0.0.1",
                2,
                1,
                Character(),
                Now));
    }

    private static CharacterListEntry Character() =>
        new(
            CharacterId: 1,
            AccountId: 1,
            Name: "test001",
            ClassCode: "Swordsman",
            GenderCode: "Female",
            LifeSkillCode: "LifeSkill1",
            Level: 1,
            AppearanceCode: "Appearance1",
            MapId: 1675308248,
            PositionX: 0,
            PositionY: 0,
            CreatedAtUtc: Now,
            LastPlayedAtUtc: null);
}
