using System.Data;
using System.Text.Json;
using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class InventoryGrantStackCommittedTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Committed_stacks_replay_and_competing_versions_retry_safely()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value : throw new InvalidOperationException($"Missing {name}");

        Assert.Equal("1", Required("GOD2_RUN_DB_INTEGRATION"));
        var character = long.Parse(Required("GOD2_STACK_FIXTURE_CHARACTER_ID"));
        Assert.True(character > 1);
        var user = Required("GOD2_STACK_FIXTURE_USER");
        Assert.StartsWith("stacktest_", user);
        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            user, Required("GOD2_STACK_FIXTURE_PASSWORD"));
        // Each operation must use a distinct physical connection.
        var connectionString = options.BuildConnectionString() + ";Pooling=false";

        await using var verification = new MySqlConnection(connectionString);
        await verification.OpenAsync();

        async Task<long> Scalar(string sql)
        {
            await using var command = verification.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@character", character);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.characters
            WHERE character_id=@character AND enabled=0
              AND name LIKE 'stackfixture_%'
              AND admin_note='InventoryStackCommittedFixture';
            """));

        var repository = new MariaDbCharacterInventorySnapshotRepository(options);
        var initial = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.NotNull(initial);
        Assert.Empty(initial.Slots);
        Assert.Equal(0L, initial.Version);
        Assert.Equal(0L, initial.MutationSequence);

        async Task<InventoryGrantResult> Grant(InventoryGrantRequest request)
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            // Content fixture is local to this physical connection.
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE TEMPORARY TABLE god2_game.items (
                        item_id BIGINT NOT NULL PRIMARY KEY,
                        name_zh_tw VARCHAR(100) NOT NULL,
                        maximum_stack INT NOT NULL,
                        enabled BOOLEAN NOT NULL
                    ) ENGINE=InnoDB;
                    INSERT INTO god2_game.items
                    VALUES (253231541,'Committed stack fixture',5,1);
                    """;
                await command.ExecuteNonQueryAsync();
            }
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted);
            try
            {
                var result = await new MariaDbInventoryGrantWriter(options)
                    .GrantInTransactionAsync(
                        connection, transaction, request, CancellationToken.None);
                if (result.Status == InventoryGrantStatus.Granted)
                    await transaction.CommitAsync();
                else
                    await transaction.RollbackAsync();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        InventoryGrantRequest Request(long version, int quantity) => new(
            Guid.NewGuid(), "stack-committed-" + Guid.NewGuid(),
            "IntegrationTest/CommittedStack", character, initial.InventoryId,
            version, version, 253231541, quantity);

        var seed = Request(0, 3);
        Assert.Equal(InventoryGrantStatus.Granted, (await Grant(seed)).Status);
        var originalIdentity = await Scalar("""
            SELECT inventory_id FROM god2_player.character_inventory
            WHERE character_id=@character AND slot_index=0;
            """);

        var merge = Request(1, 1);
        var gate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await gate.Task;
            return await Grant(merge);
        }).ToArray();
        gate.SetResult(true);
        var merged = await Task.WhenAll(tasks);
        Assert.Single(merged, x => x.Status == InventoryGrantStatus.Granted);
        Assert.Equal(7, merged.Count(x => x.Status == InventoryGrantStatus.Replayed));
        Assert.All(merged, x =>
        {
            Assert.Equal(merge.TransactionId, x.TransactionId);
            Assert.Equal(1L, x.VersionBefore);
            Assert.Equal(2L, x.VersionAfter);
        });
        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.character_inventory
            WHERE character_id=@character AND slot_index=0
              AND quantity=4 AND slot_version=2 AND inventory_version=2;
            """));

        // Different requests compete for the same expected version.
        var competing = new[] { Request(2, 2), Request(2, 2) };
        var start = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var competition = competing.Select(async request =>
        {
            await start.Task;
            return await Grant(request);
        }).ToArray();
        start.SetResult(true);
        var results = await Task.WhenAll(competition);
        Assert.Single(results, x => x.Status == InventoryGrantStatus.Granted);
        Assert.Single(results, x => x.Status == InventoryGrantStatus.VersionConflict);
        Assert.Equal(3, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE CharacterId=@character;
            """));
        Assert.Equal(4, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_audit_ledger
            WHERE CharacterId=@character;
            """));

        var current = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal(3L, current.Version);
        Assert.Equal(3L, current.MutationSequence);
        Assert.Equal(6, current.Slots.Sum(x => x.Quantity));

        // The rejected operation has no receipt; retry against current state.
        var loser = competing[Array.FindIndex(results,
            x => x.Status == InventoryGrantStatus.VersionConflict)];
        var retried = loser with
        {
            ExpectedVersion = current.Version,
            ExpectedMutationSequence = current.MutationSequence
        };
        Assert.Equal(InventoryGrantStatus.Granted, (await Grant(retried)).Status);

        var final = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.NotNull(final);
        Assert.Equal(4L, final.Version);
        Assert.Equal(4L, final.MutationSequence);
        Assert.Equal(new[] { 5, 3 },
            final.Slots.OrderBy(x => x.SlotIndex).Select(x => x.Quantity).ToArray());
        Assert.Equal(originalIdentity, await Scalar("""
            SELECT inventory_id FROM god2_player.character_inventory
            WHERE character_id=@character AND slot_index=0;
            """));
        var spilledIdentity = await Scalar("""
            SELECT inventory_id FROM god2_player.character_inventory
            WHERE character_id=@character AND slot_index=1;
            """);
        Assert.NotEqual(originalIdentity, spilledIdentity);
        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.character_inventory
            WHERE character_id=@character AND slot_index=0
              AND quantity=5 AND slot_version=3 AND inventory_version=3;
            """));
        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.character_inventory
            WHERE character_id=@character AND slot_index=1
              AND quantity=3 AND slot_version=2 AND inventory_version=4;
            """));
        Assert.Equal(2, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_item_identity_sequence s
            JOIN god2_player.character_inventory i
              ON i.inventory_id=s.PersistentInventoryItemId
            WHERE i.character_id=@character;
            """));

        // Older committed requests replay after later mutations on new connections.
        foreach (var request in new[] { seed, merge, retried })
        {
            var replay = await Grant(request);
            Assert.Equal(InventoryGrantStatus.Replayed, replay.Status);
            Assert.Equal(request.TransactionId, replay.TransactionId);
            Assert.Equal(request.ExpectedVersion, replay.VersionBefore);
            Assert.Equal(request.ExpectedVersion + 1, replay.VersionAfter);
        }
        Assert.Equal(InventoryGrantStatus.IdempotencyConflict,
            (await Grant(merge with { Quantity = 2 })).Status);
        var afterReplay = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(final), JsonSerializer.Serialize(afterReplay));
        Assert.Equal(4, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE CharacterId=@character AND OperationType='V2ItemGrant';
            """));
        Assert.Equal(5, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_audit_ledger
            WHERE CharacterId=@character AND OperationType='V2ItemGrant';
            """));
        Assert.Equal(5, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_audit_ledger
            WHERE CharacterId=@character
              AND InventoryVersionAfter=InventoryVersionBefore+1
              AND QuantityAfter>QuantityBefore;
            """));
        Assert.Equal(8, await Scalar("""
            SELECT SUM(QuantityAfter-QuantityBefore)
            FROM god2_player.inventory_audit_ledger
            WHERE CharacterId=@character;
            """));
    }
}
