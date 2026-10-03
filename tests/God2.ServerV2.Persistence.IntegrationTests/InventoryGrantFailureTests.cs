using System.Text.Json;
using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class InventoryGrantFailureTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Audit_failure_after_slot_write_rolls_back_all_changes()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {name}");

        var user = Required("GOD2_GRANT_FAULT_USER");
        Assert.StartsWith("grantfault_", user);
        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"),
            int.Parse(Required("GOD2_DB_PORT")),
            user, Required("GOD2_GRANT_FAULT_PASSWORD"));

        var repository = new MariaDbCharacterInventorySnapshotRepository(options);
        var original = await repository.GetByCharacterAsync(
            1, CancellationToken.None);
        Assert.NotNull(original);

        await using var connection =
            new MySqlConnection(options.BuildConnectionString());
        await connection.OpenAsync();

        async Task<long> Scalar(
            string sql, MySqlTransaction? transaction = null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }


        // Explicit temporary fixture avoids LIKE/RENAME permissions.
        // It shadows the real ledger only on this connection.
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TEMPORARY TABLE god2_player.inventory_audit_ledger (
                    AuditId CHAR(36) NOT NULL PRIMARY KEY,
                    TransactionId CHAR(36) NOT NULL,
                    IdempotencySafeId VARCHAR(32) NOT NULL,
                    CharacterId BIGINT NOT NULL,
                    SessionId VARCHAR(64) NOT NULL,
                    OperationType VARCHAR(32) NOT NULL,
                    Source VARCHAR(128) NOT NULL,
                    ItemTemplateId INT NULL,
                    InventoryItemId BIGINT NULL,
                    QuantityBefore INT NOT NULL,
                    QuantityAfter INT NOT NULL,
                    CurrencyType VARCHAR(32) NOT NULL,
                    CurrencyBefore BIGINT NOT NULL,
                    CurrencyAfter BIGINT NOT NULL,
                    InventoryVersionBefore BIGINT NOT NULL,
                    InventoryVersionAfter BIGINT NOT NULL,
                    Result VARCHAR(32) NOT NULL,
                    FailureCode VARCHAR(128) NOT NULL,
                    CreatedAtUtc DATETIME(6) NOT NULL,
                    CompletedAtUtc DATETIME(6) NOT NULL,
                    CorrelationId VARCHAR(64) NOT NULL,
                    CONSTRAINT fixture_reject_audit CHECK (QuantityAfter < 0)
                ) ENGINE=InnoDB;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var reservedBefore = await Scalar(
            "SELECT COUNT(*) FROM god2_player.inventory_item_identity_sequence");
        var replayBefore = await Scalar(
            "SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency");

        var transactionId = Guid.NewGuid();
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

                await Scalar("""
                    SELECT character_id FROM god2_player.characters
                    WHERE character_id=1 FOR UPDATE;
                    """, transaction);

                await Execute("""
                    DELETE FROM god2_player.character_inventory
                    WHERE character_id=1;
                    UPDATE god2_player.player_inventory_state
                    SET Capacity=3,InventoryVersion=7,MutationSequence=11
                    WHERE CharacterId=1;
                    """);

                var request = new InventoryGrantRequest(
                    transactionId, "failure-" + transactionId,
                    "IntegrationTest/AuditFailure",
                    1, original.InventoryId, 7, 11, 253231541, 2);

                var exception = await Assert.ThrowsAsync<MySqlException>(
                    async () =>
                    {
                        await new MariaDbInventoryGrantWriter(options)
                            .GrantInTransactionAsync(
                                connection, transaction, request,
                                CancellationToken.None);
                    });

                // MariaDB CHECK constraint violation.
                Assert.Equal(4025, exception.Number);

                // The first slot write happened BEFORE the audit failure.
                Assert.Equal(1L, await Scalar("""
                    SELECT COUNT(*) FROM god2_player.character_inventory
                    WHERE character_id=1 AND item_id=253231541
                      AND quantity=1 AND inventory_version=8;
                    """, transaction));

                Assert.Equal(reservedBefore + 1, await Scalar(
                    "SELECT COUNT(*) FROM god2_player.inventory_item_identity_sequence",
                    transaction));

                // Later state/replay writes were never reached.
                Assert.Equal(7L, await Scalar("""
                    SELECT InventoryVersion
                    FROM god2_player.player_inventory_state WHERE CharacterId=1;
                    """, transaction));
                Assert.Equal(replayBefore, await Scalar(
                    "SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency",
                    transaction));
            }
            finally
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        var after = await repository.GetByCharacterAsync(
            1, CancellationToken.None);
        Assert.Equal(
            JsonSerializer.Serialize(original),
            JsonSerializer.Serialize(after));
        Assert.Equal(reservedBefore, await Scalar(
            "SELECT COUNT(*) FROM god2_player.inventory_item_identity_sequence"));
        Assert.Equal(replayBefore, await Scalar(
            "SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency"));
        Assert.Equal(0L, await Scalar(
            "SELECT COUNT(*) FROM god2_player.inventory_audit_ledger"));
    }
}
