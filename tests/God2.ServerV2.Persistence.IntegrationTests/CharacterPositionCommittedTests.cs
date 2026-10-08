using God2.ServerV2.Application;
using God2.ServerV2.Persistence;

namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class CharacterPositionCommittedTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CommittedPositionIsReadByNewConnectionAndStaleWriteIsRejected()
    {
        if (Environment.GetEnvironmentVariable("GOD2_RUN_DB_INTEGRATION") != "1")
            return;

        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {name}");

        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"),
            int.Parse(Required("GOD2_DB_PORT")),
            Required("GOD2_POSITION_FIXTURE_USER"),
            Required("GOD2_POSITION_FIXTURE_PASSWORD"));
        var accountId = long.Parse(Required("GOD2_POSITION_FIXTURE_ACCOUNT_ID"));
        var characterId = long.Parse(Required("GOD2_POSITION_FIXTURE_CHARACTER_ID"));
        Assert.True(accountId > 1);
        Assert.True(characterId > 1);

        var before = Assert.Single(
            await new MariaDbCharacterListRepository(options)
                .ListByAccountAsync(accountId, CancellationToken.None));
        Assert.Equal(characterId, before.CharacterId);
        Assert.Equal(17, before.PositionX);
        Assert.Equal(15, before.PositionY);
        Assert.Equal(0, before.RuntimeVersion);
        Assert.Equal(32, before.ConcurrencyToken.Length);

        var request = new CharacterPositionWriteRequest(
            characterId, 16, 14,
            before.RuntimeVersion, before.ConcurrencyToken);
        var result = await new MariaDbCharacterPositionWriter(options)
            .TryUpdateAsync(request, CancellationToken.None);

        Assert.True(result.Updated);
        Assert.Equal(1, result.RuntimeVersion);
        Assert.Equal(32, result.ConcurrencyToken.Length);
        Assert.NotEqual(before.ConcurrencyToken, result.ConcurrencyToken);

        // The writer-owned connection has closed; this repository opens a new one.
        var committed = Assert.Single(
            await new MariaDbCharacterListRepository(options)
                .ListByAccountAsync(accountId, CancellationToken.None));
        Assert.Equal(16, committed.PositionX);
        Assert.Equal(14, committed.PositionY);
        Assert.Equal(before.MapId, committed.MapId);
        Assert.Equal(result.RuntimeVersion, committed.RuntimeVersion);
        Assert.Equal(result.ConcurrencyToken, committed.ConcurrencyToken);

        var stale = await new MariaDbCharacterPositionWriter(options)
            .TryUpdateAsync(request with { PositionX = 99, PositionY = 99 },
                CancellationToken.None);
        Assert.False(stale.Updated);

        var after = Assert.Single(
            await new MariaDbCharacterListRepository(options)
                .ListByAccountAsync(accountId, CancellationToken.None));
        Assert.Equal(committed.PositionX, after.PositionX);
        Assert.Equal(committed.PositionY, after.PositionY);
        Assert.Equal(committed.MapId, after.MapId);
        Assert.Equal(committed.RuntimeVersion, after.RuntimeVersion);
        Assert.Equal(committed.ConcurrencyToken, after.ConcurrencyToken);
    }
}
