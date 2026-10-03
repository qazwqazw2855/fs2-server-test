using System.Text.Json;
using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class InventoryGrantStackTests
{
    [Theory]
    [InlineData("merge", 1)]
    [InlineData("spill", 4)]
    [InlineData("capacity", 8)]
    [Trait("Category", "Integration")]
    public async Task Stack_writes_preserve_identity_versions_and_audit(
        string scenario, int quantity)
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value : throw new InvalidOperationException($"Missing {name}");

        var user = Required("GOD2_GRANT_FAULT_USER");
        Assert.StartsWith("grantfault_", user);
        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            user, Required("GOD2_GRANT_FAULT_PASSWORD"));
        var repository = new MariaDbCharacterInventorySnapshotRepository(options);
        var original = await repository.GetByCharacterAsync(1, CancellationToken.None);
        Assert.NotNull(original);

        await using var connection = new MySqlConnection(options.BuildConnectionString());
        await connection.OpenAsync();
        async Task Execute(string sql, MySqlTransaction? transaction = null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        async Task<long> Scalar(string sql, MySqlTransaction? transaction = null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        // Connection-local content fixture; no formal item row is changed.
        await Execute("""
            CREATE TEMPORARY TABLE god2_game.items (
                item_id BIGINT NOT NULL PRIMARY KEY,
                name_zh_tw VARCHAR(100) NOT NULL,
                maximum_stack INT NOT NULL,
                enabled BOOLEAN NOT NULL
            ) ENGINE=InnoDB;
            INSERT INTO god2_game.items
            VALUES (253231541,'Stack fixture',5,1);
            """);
        await Execute("""
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
            CorrelationId VARCHAR(64) NOT NULL
            ) ENGINE=InnoDB;
            """);

        var reservedBefore = await Scalar(
            "SELECT COUNT(*) FROM god2_player.inventory_item_identity_sequence");
        var receiptsBefore = await Scalar(
            "SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency");

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            try
            {
                Assert.Equal(1, await Scalar("""
                    SELECT character_id FROM god2_player.characters
                    WHERE character_id=1 FOR UPDATE;
                    """, transaction));
                await Execute("""
                    DELETE FROM god2_player.character_inventory WHERE character_id=1;
                    UPDATE god2_player.player_inventory_state
                    SET Capacity=2,InventoryVersion=7,MutationSequence=11
                    WHERE CharacterId=1;
                    INSERT INTO god2_player.inventory_item_identity_sequence
                        (ReservedAtUtc) VALUES (UTC_TIMESTAMP(6));
                    """, transaction);
                var identity = await Scalar("SELECT LAST_INSERT_ID();", transaction);
                await Execute($"""
                    INSERT INTO god2_player.character_inventory
                      (inventory_id,character_id,slot_index,item_id,quantity,
                       inventory_version,slot_version,bound,bind_state,
                       item_instance_metadata,enabled)
                    VALUES ({identity},1,0,253231541,3,7,3,0,'Unbound',JSON_OBJECT(),1);
                    """, transaction);

                var reservedFixture = await Scalar(
                    "SELECT COUNT(*) FROM god2_player.inventory_item_identity_sequence",
                    transaction);
                var request = new InventoryGrantRequest(
                    Guid.NewGuid(), "stack-" + Guid.NewGuid(),
                    "IntegrationTest/Stack", 1, original.InventoryId,
                    7, 11, 253231541, quantity);
                var writer = new MariaDbInventoryGrantWriter(options);
                var result = await writer.GrantInTransactionAsync(
                    connection, transaction, request, CancellationToken.None);

                var rejected = scenario == "capacity";
                Assert.Equal(rejected
                    ? InventoryGrantStatus.InsufficientCapacity
                    : InventoryGrantStatus.Granted, result.Status);
                Assert.Equal(identity, await Scalar("""
                    SELECT inventory_id FROM god2_player.character_inventory
                    WHERE character_id=1 AND slot_index=0;
                    """, transaction));
                Assert.Equal(rejected ? 3 : scenario == "merge" ? 4 : 5,
                    await Scalar("""
                        SELECT quantity FROM god2_player.character_inventory
                        WHERE character_id=1 AND slot_index=0;
                        """, transaction));
                Assert.Equal(rejected ? 3 : 4, await Scalar("""
                    SELECT slot_version FROM god2_player.character_inventory
                    WHERE character_id=1 AND slot_index=0;
                    """, transaction));
                Assert.Equal(rejected ? 7 : 8, await Scalar("""
                    SELECT inventory_version FROM god2_player.character_inventory
                    WHERE character_id=1 AND slot_index=0;
                    """, transaction));
                Assert.Equal(rejected ? 7 : 8, await Scalar("""
                    SELECT InventoryVersion FROM god2_player.player_inventory_state
                    WHERE CharacterId=1;
                    """, transaction));
                Assert.Equal(rejected ? 11 : 12, await Scalar("""
                    SELECT MutationSequence FROM god2_player.player_inventory_state
                    WHERE CharacterId=1;
                    """, transaction));

                var changed = rejected ? 0 : scenario == "merge" ? 1 : 2;
                Assert.Equal(scenario == "spill" ? 2 : 1, await Scalar("""
                    SELECT COUNT(*) FROM god2_player.character_inventory
                    WHERE character_id=1;
                    """, transaction));
                Assert.Equal(changed, await Scalar(
                    "SELECT COUNT(*) FROM god2_player.inventory_audit_ledger",
                    transaction));
                Assert.Equal(reservedFixture + (scenario == "spill" ? 1 : 0),
                    await Scalar(
                        "SELECT COUNT(*) FROM god2_player.inventory_item_identity_sequence",
                        transaction));
                Assert.Equal(receiptsBefore + (rejected ? 0 : 1),
                    await Scalar(
                        "SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency",
                        transaction));

                if (!rejected)
                {
                    Assert.Equal(1, await Scalar($"""
                        SELECT COUNT(*) FROM god2_player.inventory_audit_ledger
                        WHERE InventoryItemId={identity}
                          AND QuantityBefore=3
                          AND QuantityAfter={(scenario == "merge" ? 4 : 5)}
                          AND InventoryVersionBefore=7 AND InventoryVersionAfter=8;
                        """, transaction));
                    if (scenario == "spill")
                    {
                        var second = await Scalar("""
                            SELECT inventory_id FROM god2_player.character_inventory
                            WHERE character_id=1 AND slot_index=1;
                            """, transaction);
                        Assert.NotEqual(identity, second);
                        Assert.Equal(1, await Scalar("""
                            SELECT COUNT(*) FROM god2_player.character_inventory
                            WHERE character_id=1 AND slot_index=1
                              AND quantity=2 AND slot_version=1 AND inventory_version=8;
                            """, transaction));
                        Assert.Equal(1, await Scalar($"""
                            SELECT COUNT(*) FROM god2_player.inventory_audit_ledger
                            WHERE InventoryItemId={second}
                              AND QuantityBefore=0 AND QuantityAfter=2
                              AND InventoryVersionBefore=7 AND InventoryVersionAfter=8;
                            """, transaction));
                    }
                    Assert.Equal(InventoryGrantStatus.Replayed,
                        (await writer.GrantInTransactionAsync(
                            connection, transaction, request, CancellationToken.None)).Status);
                    Assert.Equal(changed, await Scalar(
                        "SELECT COUNT(*) FROM god2_player.inventory_audit_ledger",
                        transaction));
                }
            }
            finally
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        var after = await repository.GetByCharacterAsync(1, CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(after));
        Assert.Equal(reservedBefore, await Scalar(
            "SELECT COUNT(*) FROM god2_player.inventory_item_identity_sequence"));
        Assert.Equal(receiptsBefore, await Scalar(
            "SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency"));
        Assert.Equal(0, await Scalar(
            "SELECT COUNT(*) FROM god2_player.inventory_audit_ledger"));
    }
}
