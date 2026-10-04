using God2.ServerV2.Persistence;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class MerchantCatalogIdentityIntegrationTests
{
    private const string ClientBuildId = "god2-opt-6b127086e0c0";
    private const long MerchantId = 170015954;
    private const long MerchantInventoryId = 170156901;
    private const int ClientCatalogIndex = 7;
    private const long ClientItemId = 6901;
    private const long CanonicalItemId = 253231541;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Exact_current_purchase_identity_resolves_and_mismatches_fail_closed()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {name}");

        Assert.Equal("1", Required("GOD2_RUN_DB_INTEGRATION"));

        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"),
            int.Parse(Required("GOD2_DB_PORT")),
            Required("GOD2_DB_USER"),
            Required("GOD2_DB_PASSWORD"));

        var repository =
            new MariaDbMerchantCatalogIdentityRepository(options);

        var exact = await repository.ResolvePurchaseAsync(
            MerchantId,
            ClientBuildId,
            ClientCatalogIndex,
            ClientItemId,
            CancellationToken.None);

        Assert.NotNull(exact);
        Assert.Equal(MerchantInventoryId, exact.MerchantInventoryId);
        Assert.Equal(MerchantId, exact.MerchantId);
        Assert.Equal(CanonicalItemId, exact.ItemId);
        Assert.Equal(ClientCatalogIndex, exact.ClientCatalogIndex);
        Assert.Equal(ClientItemId, exact.ClientItemId);

        Assert.Null(await repository.ResolvePurchaseAsync(
            MerchantId + 1,
            ClientBuildId,
            ClientCatalogIndex,
            ClientItemId,
            CancellationToken.None));

        Assert.Null(await repository.ResolvePurchaseAsync(
            MerchantId,
            "wrong-build",
            ClientCatalogIndex,
            ClientItemId,
            CancellationToken.None));

        // display_order=1 is deliberately not the wire catalog index.
        Assert.Null(await repository.ResolvePurchaseAsync(
            MerchantId,
            ClientBuildId,
            1,
            ClientItemId,
            CancellationToken.None));

        Assert.Null(await repository.ResolvePurchaseAsync(
            MerchantId,
            ClientBuildId,
            ClientCatalogIndex,
            ClientItemId + 1,
            CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_authority_inputs_are_rejected_before_database_access()
    {
        var repository = new MariaDbMerchantCatalogIdentityRepository(
            new MariaDbAuthenticationOptions(
                "127.0.0.1",
                1,
                "unused",
                "unused"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await repository.ResolvePurchaseAsync(
                0, ClientBuildId, ClientCatalogIndex, ClientItemId,
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await repository.ResolvePurchaseAsync(
                MerchantId, " ", ClientCatalogIndex, ClientItemId,
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await repository.ResolvePurchaseAsync(
                MerchantId, ClientBuildId, -1, ClientItemId,
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await repository.ResolvePurchaseAsync(
                MerchantId, ClientBuildId, ClientCatalogIndex, 0,
                CancellationToken.None));
    }
}
