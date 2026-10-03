using System.Text.Json;
using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class MerchantSaleIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Buy_sell_rolls_back_replays_and_rejects_reused_slot_identity()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value : throw new InvalidOperationException($"Missing {name}");

        Assert.Equal("1", Required("GOD2_RUN_DB_INTEGRATION"));
        var user = Required("GOD2_SALE_FIXTURE_USER");
        Assert.StartsWith("shoptest_", user);
        var character = long.Parse(Required("GOD2_SALE_FIXTURE_CHARACTER_ID"));
        var merchant = long.Parse(Required("GOD2_SALE_FIXTURE_MERCHANT_ID"));
        Assert.True(character > 1);
        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            user, Required("GOD2_SALE_FIXTURE_PASSWORD"));

        await using var connection = new MySqlConnection(options.BuildConnectionString());
        await connection.OpenAsync();

        async Task<long> Scalar(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@character", character);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.characters
            WHERE character_id=@character AND enabled=0
              AND admin_note='MerchantPurchaseFixture'
              AND name LIKE 'shopfixture_%';
            """));
        var repository = new MariaDbCharacterInventorySnapshotRepository(options);
        var initial = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.NotNull(initial);
        Assert.Empty(initial.Slots);

        var purchase = new MerchantPurchaseRequest(
            Guid.NewGuid(), "sale-fixture-buy", character, merchant, 253231541, 1,
            initial.InventoryId, 0, 0, 0);
        var buyWriter = new MariaDbMerchantPurchaseWriter(
            options, new BuyGate(character, merchant));
        Assert.Equal(MerchantPurchaseStatus.Purchased,
            (await buyWriter.PurchaseAsync(purchase, CancellationToken.None)).Status);

        var before = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.NotNull(before);
        Assert.Equal(1, before.Version);
        var slot = Assert.Single(before.Slots);
        var instance = await Scalar("""
            SELECT inventory_id FROM god2_player.character_inventory
            WHERE character_id=@character AND enabled=1 AND deleted_at_utc IS NULL;
            """);
        var slotVersion = await Scalar("""
            SELECT slot_version FROM god2_player.character_inventory
            WHERE character_id=@character AND enabled=1 AND deleted_at_utc IS NULL;
            """);

        var sale = new MerchantSaleRequest(
            Guid.NewGuid(), "sale-one", character, merchant, 253231541,
            instance, slot.SlotIndex, 1, before.InventoryId,
            before.Version, before.MutationSequence, slotVersion, 1);
        var gate = new SaleGate(character, merchant);
        var writer = new MariaDbMerchantSaleWriter(options, gate);

        Assert.Equal(MerchantSaleStatus.EvidenceBlocked,
            (await new MariaDbMerchantSaleWriter(
                options, new BlockedMerchantSaleEvidenceGate())
                .SellAsync(sale, CancellationToken.None)).Status);

        Assert.Equal(MerchantSaleStatus.SlotConflict,
            (await writer.SellAsync(sale with
            {
                TransactionId = Guid.NewGuid(),
                IdempotencyKey = "wrong-instance",
                ItemInstanceId = checked(instance + 1)
            }, CancellationToken.None)).Status);

        var faultUser = Required("GOD2_SALE_FAULT_USER");
        Assert.StartsWith("shopfault_", faultUser);
        var faultOptions = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            faultUser, Required("GOD2_SALE_FIXTURE_PASSWORD"));
        var error = await Assert.ThrowsAsync<MySqlException>(async () =>
        {
            await new MariaDbMerchantSaleWriter(faultOptions, gate)
                .SellAsync(sale, CancellationToken.None);
        });
        Assert.Equal(1142, error.Number);
        Assert.Contains("player_currency_balances", error.Message);
        var restored = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(restored));
        Assert.Equal(slotVersion, await Scalar("""
            SELECT slot_version FROM god2_player.character_inventory
            WHERE character_id=@character AND enabled=1 AND deleted_at_utc IS NULL;
            """));
        Assert.Equal(60, await Scalar("""
            SELECT Balance FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));
        Assert.Equal(1, await Scalar("""
            SELECT Version FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));
        Assert.Equal(0, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE CharacterId=@character AND OperationType='V2MerchantSell';
            """));
        Assert.Equal(0, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_audit_ledger
            WHERE CharacterId=@character AND OperationType='V2MerchantSell';
            """));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            await new MariaDbMerchantSaleWriter(options, gate)
                .SellAsync(sale, CancellationToken.None)));
        Assert.Single(results, result => result.Status == MerchantSaleStatus.Sold);
        Assert.Equal(7, results.Count(result => result.Status == MerchantSaleStatus.Replayed));
        Assert.All(results, result =>
        {
            Assert.Equal(60, result.BalanceBefore);
            Assert.Equal(64, result.BalanceAfter);
            Assert.Equal(2, result.WalletVersionAfter);
            Assert.Equal(2, result.InventoryVersionAfter);
        });
        var sold = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.NotNull(sold);
        Assert.Empty(sold.Slots);
        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.character_inventory
            WHERE character_id=@character AND quantity=0
              AND enabled=0 AND deleted_at_utc IS NOT NULL;
            """));

        Assert.Equal(MerchantSaleStatus.IdempotencyConflict,
            (await writer.SellAsync(sale with { TransactionId = Guid.NewGuid() },
                CancellationToken.None)).Status);
        Assert.Equal(MerchantSaleStatus.Replayed,
            (await new MariaDbMerchantSaleWriter(options, gate)
                .SellAsync(sale, CancellationToken.None)).Status);

        Assert.Equal(MerchantPurchaseStatus.Purchased,
            (await buyWriter.PurchaseAsync(purchase with
            {
                TransactionId = Guid.NewGuid(),
                IdempotencyKey = "sale-fixture-rebuy",
                ExpectedInventoryVersion = 2,
                ExpectedMutationSequence = 2,
                ExpectedWalletVersion = 2
            }, CancellationToken.None)).Status);
        var newInstance = await Scalar("""
            SELECT inventory_id FROM god2_player.character_inventory
            WHERE character_id=@character AND enabled=1 AND deleted_at_utc IS NULL;
            """);
        Assert.NotEqual(instance, newInstance);
        var currentSale = sale with
        {
            TransactionId = Guid.NewGuid(),
            IdempotencyKey = "stale-instance",
            ExpectedInventoryVersion = 3,
            ExpectedMutationSequence = 3,
            ExpectedWalletVersion = 3,
            ExpectedSlotVersion = 1
        };
        Assert.Equal(MerchantSaleStatus.SlotConflict,
            (await writer.SellAsync(currentSale, CancellationToken.None)).Status);
        Assert.Equal(MerchantSaleStatus.Sold,
            (await writer.SellAsync(currentSale with
            {
                TransactionId = Guid.NewGuid(),
                IdempotencyKey = "sell-new-instance",
                ItemInstanceId = newInstance
            }, CancellationToken.None)).Status);
        Assert.Equal(28, await Scalar("""
            SELECT Balance FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));
        Assert.Equal(2, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE CharacterId=@character AND OperationType='V2MerchantSell';
            """));
        Assert.Equal(2, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_audit_ledger
            WHERE CharacterId=@character AND OperationType='V2MerchantSell';
            """));
    }

    private sealed class BuyGate(long character, long merchant)
        : IMerchantPurchaseEvidenceGate
    {
        public ValueTask<bool> IsApprovedAsync(
            MerchantPurchaseRequest request, MerchantPurchaseQuote quote,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(request.CharacterId == character &&
                quote == new MerchantPurchaseQuote(merchant, 253231541, "Gold", 40, 1, true));
    }

    private sealed class SaleGate(long character, long merchant)
        : IMerchantSaleEvidenceGate
    {
        public ValueTask<bool> IsApprovedAsync(
            MerchantSaleRequest request, MerchantSaleQuote quote,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(request.CharacterId == character &&
                quote == new MerchantSaleQuote(merchant, 253231541, "Gold", 4, true, true));
    }
}
