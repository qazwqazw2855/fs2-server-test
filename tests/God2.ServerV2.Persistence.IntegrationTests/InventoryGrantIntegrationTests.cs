using System.Globalization;
using System.Text.Json;
using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class InventoryGrantIntegrationTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("stale")]
    [InlineData("capacity")]
    [InlineData("inactive")]
    [Trait("Category", "Integration")]
    public async Task Grant_preserves_atomicity_and_rolls_back(string scenario)
    {
        Assert.Equal(
            "1", Environment.GetEnvironmentVariable("GOD2_RUN_DB_INTEGRATION"));

        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {name}");

        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"),
            int.Parse(Required("GOD2_DB_PORT")),
            Required("GOD2_DB_USER"),
            Required("GOD2_DB_PASSWORD"));

        await using var connection =
            new MySqlConnection(options.BuildConnectionString());
        await connection.OpenAsync();

        var original = await CaptureAsync(connection, null);
        var transactionId = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            try
            {
                async Task Execute(string sql)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = sql;
                    await command.ExecuteNonQueryAsync();
                }

                // Lock before changing fixture state; all changes roll back.
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        SELECT character_id FROM god2_player.characters
                        WHERE character_id=1 AND deleted_at_utc IS NULL
                        FOR UPDATE;
                        """;
                    Assert.NotNull(await command.ExecuteScalarAsync());
                }

                long itemId;
                int maximum;
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        SELECT i.item_id, COALESCE(i.maximum_stack,1)
                        FROM god2_game.items AS i
                        JOIN god2_game.item_registry AS r ON r.item_id=i.item_id
                        WHERE i.enabled=1 AND i.item_id=253231541
                        ORDER BY i.item_id LIMIT 1;
                        """;
                    await using var reader = await command.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync(),
                        "Verified fixture item is missing or disabled.");
                    itemId = reader.GetInt64(0);
                    maximum = reader.GetInt32(1);
                    Assert.Equal(1, maximum);
                }

                await Execute("""
                    DELETE FROM god2_player.character_inventory
                    WHERE character_id=1;
                    """);

                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        UPDATE god2_player.player_inventory_state
                        SET InventoryId=@id, Capacity=@capacity,
                            InventoryVersion=7, MutationSequence=11,
                            DirtyState='Clean'
                        WHERE CharacterId=1;
                        """;
                    command.Parameters.AddWithValue("@id", inventoryId.ToString());
                    command.Parameters.AddWithValue(
                        "@capacity", scenario == "capacity" ? 1 :
                            scenario == "inactive" ? 2 : 3);
                    Assert.Equal(1, await command.ExecuteNonQueryAsync());
                }

                // Reserve a fixture identity through the canonical allocator.
                long originalItemId;
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO god2_player.inventory_item_identity_sequence
                        (ReservedAtUtc) VALUES (UTC_TIMESTAMP(6));
                        """;
                    await command.ExecuteNonQueryAsync();
                    originalItemId = command.LastInsertedId;
                }

                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO god2_player.character_inventory
                            (inventory_id,character_id,slot_index,item_id,quantity,
                             inventory_version,slot_version,bound,bind_state,
                             item_instance_metadata,enabled,deleted_at_utc)
                        VALUES (@id,1,0,@item,@quantity,7,3,0,'Unbound','{}',
                                @enabled,@deleted);
                        """;
                    command.Parameters.AddWithValue("@id", originalItemId);
                    command.Parameters.AddWithValue("@item", itemId);
                    command.Parameters.AddWithValue("@quantity", 1);
                    command.Parameters.AddWithValue(
                        "@enabled", scenario == "inactive" ? 0 : 1);
                    command.Parameters.AddWithValue("@deleted",
                        scenario == "inactive"
                            ? (object)DateTime.UtcNow
                            : DBNull.Value);
                    await command.ExecuteNonQueryAsync();
                }

                var before = await CaptureAsync(connection, transaction);
                var request = new InventoryGrantRequest(
                    transactionId,
                    "integration-" + transactionId,
                    "IntegrationTest/InventoryGrant",
                    1, inventoryId,
                    scenario == "stale" ? 6 : 7,
                    11, itemId, 2);
                var writer = new MariaDbInventoryGrantWriter(options);

                var result = await writer.GrantInTransactionAsync(
                    connection, transaction, request, CancellationToken.None);

                if (scenario is "stale" or "capacity")
                {
                    Assert.Equal(
                        scenario == "stale"
                            ? InventoryGrantStatus.VersionConflict
                            : InventoryGrantStatus.InsufficientCapacity,
                        result.Status);
                    Assert.False(result.Succeeded);
                    Assert.Equal(before,
                        await CaptureAsync(connection, transaction));
                }
                else
                {
                    Assert.Equal(InventoryGrantStatus.Granted, result.Status);
                    Assert.Equal(7L, result.VersionBefore);
                    Assert.Equal(8L, result.VersionAfter);

                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = """
                            SELECT InventoryVersion, MutationSequence
                            FROM god2_player.player_inventory_state
                            WHERE CharacterId=1;
                            """;
                        await using var reader =
                            await command.ExecuteReaderAsync();
                        Assert.True(await reader.ReadAsync());
                        Assert.Equal(8L, reader.GetInt64(0));
                        Assert.Equal(12L, reader.GetInt64(1));
                    }


                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = """
                            SELECT inventory_id,slot_index,quantity,slot_version,
                                   inventory_version,bind_state,
                                   item_instance_metadata,enabled,deleted_at_utc,
                                   item_id
                            FROM god2_player.character_inventory
                            WHERE character_id=1 ORDER BY slot_index;
                            """;
                        await using var reader =
                            await command.ExecuteReaderAsync();

                        var identities = new HashSet<long>();
                        var count = 0;
                        while (await reader.ReadAsync())
                        {
                            var identity = reader.GetInt64(0);
                            Assert.True(identities.Add(identity));
                            Assert.Equal(count, reader.GetInt32(1));
                            Assert.Equal(1L, reader.GetInt64(2));
                            Assert.Equal("Unbound", reader.GetString(5));
                            Assert.Equal("{}", reader.GetString(6));
                            Assert.True(reader.GetBoolean(7));
                            Assert.True(reader.IsDBNull(8));
                            Assert.Equal(itemId, reader.GetInt64(9));

                            if (scenario == "success" && count == 0)
                            {
                                // Non-stackable existing item is untouched.
                                Assert.Equal(originalItemId, identity);
                                Assert.Equal(3L, reader.GetInt64(3));
                                Assert.Equal(7L, reader.GetInt64(4));
                            }
                            else
                            {
                                Assert.NotEqual(originalItemId, identity);
                                Assert.Equal(1L, reader.GetInt64(3));
                                Assert.Equal(8L, reader.GetInt64(4));
                            }
                            count++;
                        }
                        Assert.Equal(scenario == "inactive" ? 2 : 3, count);
                    }

                    foreach (var table in new[]
                    {
                        "inventory_transaction_idempotency",
                        "inventory_audit_ledger"
                    })
                    {
                        await using var command = connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText =
                            $"SELECT COUNT(*) FROM god2_player.{table} " +
                            "WHERE TransactionId=@transaction";
                        command.Parameters.AddWithValue(
                            "@transaction", transactionId.ToString());
                        var expected =
                            table == "inventory_audit_ledger" ? 2L : 1L;
                        Assert.Equal(expected,
                            Convert.ToInt64(await command.ExecuteScalarAsync()));
                    }

                    var after = await CaptureAsync(connection, transaction);

                    var replay = await writer.GrantInTransactionAsync(
                        connection, transaction, request, CancellationToken.None);
                    Assert.Equal(InventoryGrantStatus.Replayed, replay.Status);
                    Assert.Equal(result.TransactionId, replay.TransactionId);
                    Assert.Equal(result.VersionBefore, replay.VersionBefore);
                    Assert.Equal(result.VersionAfter, replay.VersionAfter);
                    Assert.Equal(after,
                        await CaptureAsync(connection, transaction));

                    var conflict = await writer.GrantInTransactionAsync(
                        connection, transaction,
                        request with { Quantity = 3 }, CancellationToken.None);
                    Assert.Equal(
                        InventoryGrantStatus.IdempotencyConflict, conflict.Status);
                    Assert.Equal(after,
                        await CaptureAsync(connection, transaction));

                    var stale = await writer.GrantInTransactionAsync(
                        connection, transaction,
                        request with
                        {
                            TransactionId = Guid.NewGuid(),
                            IdempotencyKey = "new-" + Guid.NewGuid()
                        },
                        CancellationToken.None);
                    Assert.Equal(
                        InventoryGrantStatus.VersionConflict, stale.Status);
                    Assert.Equal(after,
                        await CaptureAsync(connection, transaction));
                }
            }
            finally
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        Assert.Equal(original, await CaptureAsync(connection, null));

        await using var check = connection.CreateCommand();
        check.CommandText = """
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE TransactionId=@transaction;
            """;
        check.Parameters.AddWithValue("@transaction", transactionId.ToString());
        Assert.Equal(0L, Convert.ToInt64(await check.ExecuteScalarAsync()));
    }

    private static async Task<string> CaptureAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction)
    {
        var captured = new List<string>();
        foreach (var query in new[]
        {
            "SELECT * FROM god2_player.player_inventory_state WHERE CharacterId=1",
            "SELECT * FROM god2_player.character_inventory WHERE character_id=1 ORDER BY slot_index",
            "SELECT * FROM god2_player.inventory_transaction_idempotency WHERE CharacterId=1 ORDER BY IdempotencyKeyHash",
            "SELECT * FROM god2_player.inventory_audit_ledger WHERE CharacterId=1 ORDER BY AuditId",
            "SELECT * FROM god2_player.player_currency_balances WHERE CharacterId=1 ORDER BY CurrencyType"
        })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = query;
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string?[]>();
            while (await reader.ReadAsync())
            {
                var row = new string?[reader.FieldCount];
                for (var index = 0; index < row.Length; index++)
                {
                    row[index] = reader.IsDBNull(index)
                        ? null
                        : reader.GetValue(index) switch
                        {
                            DateTime date => date.ToString(
                                "O", CultureInfo.InvariantCulture),
                            byte[] bytes => Convert.ToHexString(bytes),
                            var value => Convert.ToString(
                                value, CultureInfo.InvariantCulture)
                        };
                }
                rows.Add(row);
            }
            captured.Add(JsonSerializer.Serialize(rows));
        }
        return JsonSerializer.Serialize(captured);
    }
}
