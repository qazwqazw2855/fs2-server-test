using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class InventoryGrantConcurrencyTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Concurrent_committed_grants_are_durable_and_not_duplicated()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {name}");

        Assert.Equal("1", Required("GOD2_RUN_DB_INTEGRATION"));
        var characterId = long.Parse(Required("GOD2_GRANT_FIXTURE_CHARACTER_ID"));
        Assert.True(characterId > 1);

        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            Required("GOD2_DB_USER"), Required("GOD2_DB_PASSWORD"));

        // Require a dedicated disabled fixture, never an ordinary player.
        await using (var connection =
            new MySqlConnection(options.BuildConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT name, enabled, admin_note
                FROM god2_player.characters WHERE character_id=@id;
                """;
            command.Parameters.AddWithValue("@id", characterId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.StartsWith("grantfixture_", reader.GetString(0));
            Assert.False(reader.GetBoolean(1));
            Assert.Equal("InventoryGrantConcurrencyFixture", reader.GetString(2));
        }

        var repository = new MariaDbCharacterInventorySnapshotRepository(options);
        var initial = await repository.GetByCharacterAsync(
            characterId, CancellationToken.None);
        Assert.NotNull(initial);
        Assert.Empty(initial.Slots);
        Assert.Equal(0L, initial.Version);

        var request = new InventoryGrantRequest(
            Guid.NewGuid(), "concurrency-" + Guid.NewGuid(),
            "IntegrationTest/ConcurrentGrant",
            characterId, initial.InventoryId,
            initial.Version, initial.MutationSequence, 253231541, 2);

        var gate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await gate.Task;
            return await new MariaDbInventoryGrantWriter(options)
                .GrantAsync(request, CancellationToken.None);
        }).ToArray();

        gate.SetResult(true);
        var results = await Task.WhenAll(tasks);

        Assert.Single(results,
            result => result.Status == InventoryGrantStatus.Granted);
        Assert.Equal(7, results.Count(
            result => result.Status == InventoryGrantStatus.Replayed));
        Assert.All(results, result =>
        {
            Assert.Equal(request.TransactionId, result.TransactionId);
            Assert.Equal(0L, result.VersionBefore);
            Assert.Equal(1L, result.VersionAfter);
        });

        var first = await repository.GetByCharacterAsync(
            characterId, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(1L, first.Version);
        Assert.Equal(1L, first.MutationSequence);
        Assert.Equal(2, first.Slots.Count);
        Assert.All(first.Slots, slot => Assert.Equal(1, slot.Quantity));

        var competing = request with
        {
            TransactionId = Guid.NewGuid(),
            IdempotencyKey = "competing-" + Guid.NewGuid(),
            ExpectedVersion = 1,
            ExpectedMutationSequence = 1,
            Quantity = 1
        };
        var other = competing with
        {
            TransactionId = Guid.NewGuid(),
            IdempotencyKey = "competing-" + Guid.NewGuid()
        };

        var competingResults = await Task.WhenAll(
            new MariaDbInventoryGrantWriter(options)
                .GrantAsync(competing, CancellationToken.None).AsTask(),
            new MariaDbInventoryGrantWriter(options)
                .GrantAsync(other, CancellationToken.None).AsTask());

        Assert.Single(competingResults,
            result => result.Status == InventoryGrantStatus.Granted);
        Assert.Single(competingResults,
            result => result.Status == InventoryGrantStatus.VersionConflict);

        // Replay an older committed operation after a later mutation.
        var replay = await new MariaDbInventoryGrantWriter(options)
            .GrantAsync(request, CancellationToken.None);
        Assert.Equal(InventoryGrantStatus.Replayed, replay.Status);
        Assert.Equal(0L, replay.VersionBefore);
        Assert.Equal(1L, replay.VersionAfter);

        var conflict = await new MariaDbInventoryGrantWriter(options)
            .GrantAsync(request with { Quantity = 3 }, CancellationToken.None);
        Assert.Equal(
            InventoryGrantStatus.IdempotencyConflict, conflict.Status);

        var final = await repository.GetByCharacterAsync(
            characterId, CancellationToken.None);
        Assert.NotNull(final);
        Assert.Equal(2L, final.Version);
        Assert.Equal(2L, final.MutationSequence);
        Assert.Equal(3, final.Slots.Count);
        Assert.Equal(3, final.Slots.Sum(slot => slot.Quantity));

        await using var verification =
            new MySqlConnection(options.BuildConnectionString());
        await verification.OpenAsync();
        foreach (var entry in new[]
        {
            ("inventory_transaction_idempotency", 2L),
            ("inventory_audit_ledger", 3L)
        })
        {
            await using var command = verification.CreateCommand();
            command.CommandText =
                $"SELECT COUNT(*) FROM god2_player.{entry.Item1} " +
                "WHERE CharacterId=@id";
            command.Parameters.AddWithValue("@id", characterId);
            Assert.Equal(entry.Item2,
                Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
    }
}
