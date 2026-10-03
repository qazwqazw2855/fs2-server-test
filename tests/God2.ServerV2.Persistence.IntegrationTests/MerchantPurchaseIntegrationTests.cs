using System.Text.Json;
using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class MerchantPurchaseIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Purchase_is_atomic_replayable_and_version_checked()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value : throw new InvalidOperationException($"Missing {name}");

        Assert.Equal("1", Required("GOD2_RUN_DB_INTEGRATION"));
        var user = Required("GOD2_SHOP_FIXTURE_USER");
        Assert.StartsWith("shoptest_", user);
        var character = long.Parse(Required("GOD2_SHOP_FIXTURE_CHARACTER_ID"));
        var merchant = long.Parse(Required("GOD2_SHOP_FIXTURE_MERCHANT_ID"));
        Assert.True(character > 1);
        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            user, Required("GOD2_SHOP_FIXTURE_PASSWORD"));

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
        var request = new MerchantPurchaseRequest(
            Guid.NewGuid(), "purchase-one", character, merchant, 253231541, 1,
            initial.InventoryId, 0, 0, 0);
        var gate = new FixtureGate(character, merchant);

        Assert.Equal(MerchantPurchaseStatus.EvidenceBlocked,
            (await new MariaDbMerchantPurchaseWriter(
                options, new BlockedMerchantPurchaseEvidenceGate())
                .PurchaseAsync(request, CancellationToken.None)).Status);
        Assert.Equal(100, await Scalar("""
            SELECT Balance FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));


        // Grant writes succeed, then wallet UPDATE is denied.
        // PurchaseAsync owns the transaction and must roll everything back.
        Assert.Equal(0, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE CharacterId=@character;
            """));
        Assert.Equal(0, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_audit_ledger
            WHERE CharacterId=@character;
            """));

        var faultUser = Required("GOD2_SHOP_FAULT_USER");
        Assert.StartsWith("shopfault_", faultUser);
        var faultOptions = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            faultUser, Required("GOD2_SHOP_FIXTURE_PASSWORD"));

        var error = await Assert.ThrowsAsync<MySqlException>(async () =>
        {
            await new MariaDbMerchantPurchaseWriter(faultOptions, gate)
                .PurchaseAsync(request, CancellationToken.None);
        });
        Assert.Equal(1142, error.Number);
        Assert.Contains("player_currency_balances", error.Message);

        var rolledBack = await repository.GetByCharacterAsync(
            character, CancellationToken.None);
        Assert.NotNull(rolledBack);
        Assert.Equal(
            JsonSerializer.Serialize(initial),
            JsonSerializer.Serialize(rolledBack));

        Assert.Equal(100, await Scalar("""
            SELECT Balance FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));
        Assert.Equal(0, await Scalar("""
            SELECT Version FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));
        Assert.Equal(0, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE CharacterId=@character;
            """));
        Assert.Equal(0, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_audit_ledger
            WHERE CharacterId=@character;
            """));

        // Retry the exact failed request using the normal fixture account.

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            await new MariaDbMerchantPurchaseWriter(options, gate)
                .PurchaseAsync(request, CancellationToken.None)));
        Assert.Single(results, x => x.Status == MerchantPurchaseStatus.Purchased);
        Assert.Equal(7, results.Count(x => x.Status == MerchantPurchaseStatus.Replayed));
        Assert.All(results, x =>
        {
            Assert.Equal(100, x.BalanceBefore);
            Assert.Equal(60, x.BalanceAfter);
            Assert.Equal(1, x.WalletVersionAfter);
        });

        var first = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(1, first.Version);
        Assert.Equal(1, Assert.Single(first.Slots).Quantity);

        var writer = new MariaDbMerchantPurchaseWriter(options, gate);
        Assert.Equal(MerchantPurchaseStatus.IdempotencyConflict,
            (await writer.PurchaseAsync(
                request with { TransactionId = Guid.NewGuid() },
                CancellationToken.None)).Status);

        var competing = Enumerable.Range(0, 2).Select(index =>
            request with
            {
                TransactionId = Guid.NewGuid(),
                IdempotencyKey = "competing-" + index,
                ExpectedInventoryVersion = 1,
                ExpectedMutationSequence = 1,
                ExpectedWalletVersion = 1
            }).ToArray();
        var competition = await Task.WhenAll(competing.Select(async value =>
            await new MariaDbMerchantPurchaseWriter(options, gate)
                .PurchaseAsync(value, CancellationToken.None)));
        Assert.Single(competition, x => x.Status == MerchantPurchaseStatus.Purchased);
        Assert.Single(competition, x => x.Status == MerchantPurchaseStatus.WalletVersionConflict);

        var final = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.NotNull(final);
        Assert.Equal(2, final.Version);
        Assert.Equal(2, final.Slots.Sum(x => x.Quantity));
        Assert.Equal(20, await Scalar("""
            SELECT Balance FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));

        Assert.Equal(MerchantPurchaseStatus.InsufficientFunds,
            (await writer.PurchaseAsync(request with
            {
                TransactionId = Guid.NewGuid(),
                IdempotencyKey = "insufficient",
                ExpectedInventoryVersion = 2,
                ExpectedMutationSequence = 2,
                ExpectedWalletVersion = 2
            }, CancellationToken.None)).Status);

        Assert.Equal(MerchantPurchaseStatus.Replayed,
            (await new MariaDbMerchantPurchaseWriter(options, gate)
                .PurchaseAsync(request, CancellationToken.None)).Status);
        Assert.Equal(2, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE CharacterId=@character AND OperationType='V2MerchantBuy';
            """));
        Assert.Equal(2, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_audit_ledger
            WHERE CharacterId=@character AND OperationType='V2MerchantBuy';
            """));
        Assert.Equal(20, await Scalar("""
            SELECT Balance FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));
    }

    private sealed class FixtureGate(long character, long merchant)
        : IMerchantPurchaseEvidenceGate
    {
        public ValueTask<bool> IsApprovedAsync(
            MerchantPurchaseRequest request, MerchantPurchaseQuote quote,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                request.CharacterId == character &&
                quote == new MerchantPurchaseQuote(
                    merchant, 253231541, "Gold", 40, 1, true));
    }
}
