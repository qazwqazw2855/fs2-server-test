using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class MerchantPurchaseJournalProcessRecoveryTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SeparateProcessRecoversCommittedRequestWithoutDispatch()
    {
        var repos = await MerchantPurchaseJournalIntegrationTests.Repositories
            .CreateAsync(requireInitialState: false);

        Guid transactionId;
        await using (var connection =
            new MySqlConnection(repos.Options.BuildConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TransactionId
                FROM god2_player.v2_merchant_purchase_journal
                WHERE CharacterId=@character LIMIT 2;
                """;
            command.Parameters.AddWithValue("@character", repos.CharacterId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            transactionId = reader.GetGuid(0);
            Assert.False(await reader.ReadAsync());
        }

        var request = await new MariaDbMerchantPurchaseJournal(repos.Options)
            .FindAsync(repos.CharacterId, transactionId, CancellationToken.None);
        Assert.NotNull(request);
        Assert.Equal(repos.CharacterId, request!.CharacterId);
        Assert.Equal(repos.MerchantId, request.MerchantId);
        Assert.Equal(253231541L, request.ItemId);
        Assert.Equal(1, request.Quantity);
        Assert.Equal(0, request.ExpectedInventoryVersion);
        Assert.Equal(0, request.ExpectedMutationSequence);
        Assert.Equal(0, request.ExpectedWalletVersion);

        var receipt = await new MariaDbMerchantPurchaseRecoveryReader(repos.Options)
            .ReadAsync(request, CancellationToken.None);
        Assert.Equal(MerchantPurchaseRecoveryStatus.Committed, receipt.Status);
        Assert.Equal(transactionId, receipt.TransactionId);
        Assert.Equal(100L, receipt.BalanceBefore);
        Assert.Equal(60L, receipt.BalanceAfter);
        Assert.Equal(0L, receipt.InventoryVersionBefore);
        Assert.Equal(1L, receipt.InventoryVersionAfter);

        var economy = await new MariaDbCharacterEconomySnapshotRepository(repos.Options)
            .GetByCharacterAsync(repos.CharacterId, CancellationToken.None);
        Assert.NotNull(economy.Inventory);
        Assert.NotNull(economy.Wallet);
        Assert.Equal(request.InventoryId, economy.Inventory!.InventoryId);
        var slot = Assert.Single(economy.Inventory.Slots);
        Assert.Equal(253231541L, slot.ItemId);
        Assert.Equal(1, slot.Quantity);
        Assert.Equal(0, slot.SlotIndex);
        Assert.Equal(1, economy.Inventory.Version);
        Assert.Equal(1, economy.Inventory.MutationSequence);
        Assert.Equal(60, economy.Wallet!.Balance);
        Assert.Equal(1, economy.Wallet.Version);
        Assert.Equal(0, repos.PurchaseCalls);
        Assert.Equal(0, repos.SaleCalls);
        Assert.Equal(1, await repos.ScalarAsync(
            "SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency " +
            "WHERE CharacterId=@character AND OperationType='V2MerchantBuy';"));
        Assert.Equal(1, await repos.ScalarAsync(
            "SELECT COUNT(*) FROM god2_player.v2_merchant_purchase_journal " +
            "WHERE CharacterId=@character;"));
        Console.WriteLine(
            "PASS: separate process recovered persisted request and receipt without dispatch.");
    }
}
