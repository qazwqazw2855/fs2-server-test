using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class MerchantSaleJournalProcessRecoveryTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SeparateProcessRecoversCommittedSaleWithoutDispatch()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value : throw new InvalidOperationException($"Missing {name}");

        Assert.Equal("1", Required("GOD2_RUN_DB_INTEGRATION"));
        Assert.Equal("127.0.0.1", Required("GOD2_DB_HOST"));
        Assert.Equal("3308", Required("GOD2_DB_PORT"));
        var user = Required("GOD2_MERCHANT_COMMAND_FIXTURE_USER");
        Assert.StartsWith("shoptest_", user);
        var character = long.Parse(
            Required("GOD2_MERCHANT_COMMAND_FIXTURE_CHARACTER_ID"));
        var merchant = long.Parse(
            Required("GOD2_MERCHANT_COMMAND_FIXTURE_MERCHANT_ID"));
        Assert.True(character > 1);
        var options = new MariaDbAuthenticationOptions(
            "127.0.0.1", 3308, user,
            Required("GOD2_MERCHANT_COMMAND_FIXTURE_PASSWORD"));

        async Task<long> Scalar(string sql)
        {
            await using var connection =
                new MySqlConnection(options.BuildConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@character", character);
            command.Parameters.AddWithValue("@merchant", merchant);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.characters c
            JOIN god2_game.merchants m ON m.merchant_id=@merchant
            WHERE c.character_id=@character AND c.enabled=0
              AND c.admin_note='MerchantPurchaseFixture'
              AND c.name LIKE 'shopfixture_%' AND m.admin_note=c.name;
            """));

        Guid transaction;
        await using (var connection =
            new MySqlConnection(options.BuildConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TransactionId FROM god2_player.v2_merchant_sale_journal
                WHERE CharacterId=@character LIMIT 2;
                """;
            command.Parameters.AddWithValue("@character", character);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            transaction = reader.GetGuid(0);
            Assert.False(await reader.ReadAsync());
        }

        var request = await new MariaDbMerchantSaleJournal(options)
            .FindAsync(character, transaction, CancellationToken.None);
        Assert.NotNull(request);
        Assert.Equal(character, request!.CharacterId);
        Assert.Equal(merchant, request.MerchantId);
        Assert.Equal(253231541L, request.ItemId);
        Assert.True(request.ItemInstanceId > 0);
        Assert.Equal(0, request.SlotIndex);
        Assert.Equal(1, request.Quantity);
        Assert.Equal(1L, request.ExpectedInventoryVersion);
        Assert.Equal(1L, request.ExpectedMutationSequence);
        Assert.True(request.ExpectedSlotVersion >= 0);
        Assert.Equal(1L, request.ExpectedWalletVersion);

        var recovery = new MariaDbMerchantSaleRecoveryReader(options);
        var receipt = await recovery.ReadAsync(request, CancellationToken.None);
        Assert.Equal(MerchantSaleRecoveryStatus.Committed, receipt.Status);
        Assert.Equal(transaction, receipt.TransactionId);
        Assert.Equal(60L, receipt.BalanceBefore);
        Assert.Equal(64L, receipt.BalanceAfter);
        Assert.Equal(1L, receipt.InventoryVersionBefore);
        Assert.Equal(2L, receipt.InventoryVersionAfter);

        Assert.Equal(MerchantSaleRecoveryStatus.IdentityConflict,
            (await recovery.ReadAsync(
                request with { ExpectedWalletVersion = 2 },
                CancellationToken.None)).Status);
        Assert.Equal(MerchantSaleRecoveryStatus.Unknown,
            (await recovery.ReadAsync(
                request with {
                    TransactionId = Guid.NewGuid(),
                    IdempotencyKey = request.IdempotencyKey + "-missing"
                }, CancellationToken.None)).Status);

        var economy = new MariaDbCharacterEconomySnapshotRepository(options);
        var before = await economy.GetByCharacterAsync(
            character, CancellationToken.None);
        Assert.NotNull(before.Inventory);
        Assert.NotNull(before.Wallet);
        Assert.Equal(request.InventoryId, before.Inventory!.InventoryId);
        Assert.Empty(before.Inventory.Slots);
        Assert.Equal(2L, before.Inventory.Version);
        Assert.Equal(2L, before.Inventory.MutationSequence);
        Assert.Equal(64L, before.Wallet!.Balance);
        Assert.Equal(2L, before.Wallet.Version);

        // Repeated reconciliation performs reads only.
        Assert.Equal(receipt,
            await recovery.ReadAsync(request, CancellationToken.None));
        Assert.Equal(request, await new MariaDbMerchantSaleJournal(options)
            .FindAsync(character, transaction, CancellationToken.None));

        var after = await economy.GetByCharacterAsync(
            character, CancellationToken.None);
        Assert.NotNull(after.Inventory);
        Assert.NotNull(after.Wallet);
        Assert.Equal(before.Inventory.InventoryId, after.Inventory!.InventoryId);
        Assert.Empty(after.Inventory.Slots);
        Assert.Equal(before.Inventory.Version, after.Inventory.Version);
        Assert.Equal(before.Inventory.MutationSequence,
            after.Inventory.MutationSequence);
        Assert.Equal(before.Wallet.Balance, after.Wallet!.Balance);
        Assert.Equal(before.Wallet.Version, after.Wallet.Version);

        foreach (var operation in new[] { "V2MerchantBuy", "V2MerchantSell" })
            Assert.Equal(1, await Scalar(
                "SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency " +
                "WHERE CharacterId=@character AND OperationType='" + operation + "';"));
        Assert.Equal(1, await Scalar(
            "SELECT COUNT(*) FROM god2_player.v2_merchant_sale_journal " +
            "WHERE CharacterId=@character;"));

        Console.WriteLine(
            "PASS: separate process recovered SELL request and receipt; balance 64; no dispatch.");
    }
}
