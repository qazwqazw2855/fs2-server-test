using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class CharacterListServiceTests
{
    [Fact]
    public async Task Returns_empty_list_for_account_without_characters()
    {
        var service = new CharacterListService(
            new StubRepository([]));

        var result = await service.GetAsync(
            42,
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Orders_characters_by_creation_time_then_id()
    {
        var later = Character(3, 42, "later", 2);
        var earlierHighId = Character(2, 42, "second", 1);
        var earlierLowId = Character(1, 42, "first", 1);
        var service = new CharacterListService(
            new StubRepository(
                [later, earlierHighId, earlierLowId]));

        var result = await service.GetAsync(
            42,
            CancellationToken.None);

        Assert.Equal(
            new long[] { 1, 2, 3 },
            result.Select(character => character.CharacterId));
    }

    [Fact]
    public async Task Rejects_rows_owned_by_another_account()
    {
        var service = new CharacterListService(
            new StubRepository(
                [Character(1, 99, "intruder", 1)]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.GetAsync(
                42,
                CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_duplicate_character_ids()
    {
        var service = new CharacterListService(
            new StubRepository(
                [
                    Character(1, 42, "first", 1),
                    Character(1, 42, "duplicate", 2)
                ]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.GetAsync(
                42,
                CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Rejects_invalid_account_id(long accountId)
    {
        var service = new CharacterListService(
            new StubRepository([]));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await service.GetAsync(
                accountId,
                CancellationToken.None));
    }

    [Fact]
    public async Task Forwards_cancellation_token_to_repository()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var repository = new StubRepository([]);
        var service = new CharacterListService(repository);

        await service.GetAsync(42, cancellation.Token);

        Assert.Equal(
            cancellation.Token,
            repository.ObservedCancellationToken);
    }

    private static CharacterListEntry Character(
        long characterId,
        long accountId,
        string name,
        int createdDay) =>
        new(
            characterId,
            accountId,
            name,
            "Warrior",
            "Male",
            "WeaponForging",
            1,
            "Default",
            1,
            10,
            20,
            new DateTimeOffset(
                2026,
                9,
                createdDay,
                0,
                0,
                0,
                TimeSpan.Zero),
            null);

    private sealed class StubRepository(
        IReadOnlyList<CharacterListEntry> characters) :
        ICharacterListRepository
    {
        public CancellationToken ObservedCancellationToken { get; private set; }

        public ValueTask<IReadOnlyList<CharacterListEntry>> ListByAccountAsync(
            long accountId,
            CancellationToken cancellationToken)
        {
            ObservedCancellationToken = cancellationToken;
            return ValueTask.FromResult(characters);
        }
    }
}
