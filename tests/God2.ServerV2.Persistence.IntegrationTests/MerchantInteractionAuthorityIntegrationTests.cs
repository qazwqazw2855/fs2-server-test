using God2.ServerV2.Persistence;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class MerchantInteractionAuthorityIntegrationTests
{
    private const string ClientBuildId = "god2-opt-6b127086e0c0";
    private const long MerchantId = 170015954;
    private const long NpcId = 170015081;
    private const long SpawnId = 170153954;
    private const long MapId = 170015007;
    private const uint ClientEntityHandle = 3954;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Exact_current_identity_resolves_and_mismatches_fail_closed()
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
            new MariaDbMerchantInteractionAuthorityRepository(options);

        var exact = await repository.ResolveAsync(
            SpawnId,
            MapId,
            ClientBuildId,
            ClientEntityHandle,
            CancellationToken.None);

        Assert.NotNull(exact);
        Assert.Equal(MerchantId, exact.MerchantId);
        Assert.Equal(NpcId, exact.NpcId);
        Assert.Equal(SpawnId, exact.SpawnId);
        Assert.Equal(MapId, exact.MapId);
        Assert.Equal(ClientEntityHandle, exact.ClientEntityHandle);
        Assert.Equal(ClientBuildId, exact.ClientBuildId);

        // Identity evidence does not approve merchant interaction policy.
        Assert.False(exact.Enabled);
        Assert.Null(exact.MaximumDistance);
        Assert.True(string.IsNullOrWhiteSpace(exact.EvidenceReference));

        Assert.Null(await repository.ResolveAsync(
            SpawnId + 1,
            MapId,
            ClientBuildId,
            ClientEntityHandle,
            CancellationToken.None));

        Assert.Null(await repository.ResolveAsync(
            SpawnId,
            MapId + 1,
            ClientBuildId,
            ClientEntityHandle,
            CancellationToken.None));

        Assert.Null(await repository.ResolveAsync(
            SpawnId,
            MapId,
            "wrong-build",
            ClientEntityHandle,
            CancellationToken.None));

        Assert.Null(await repository.ResolveAsync(
            SpawnId,
            MapId,
            ClientBuildId,
            ClientEntityHandle + 1,
            CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_authority_inputs_are_rejected_before_database_access()
    {
        var repository = new MariaDbMerchantInteractionAuthorityRepository(
            new MariaDbAuthenticationOptions(
                "127.0.0.1",
                1,
                "unused",
                "unused"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await repository.ResolveAsync(
                0,
                MapId,
                ClientBuildId,
                ClientEntityHandle,
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await repository.ResolveAsync(
                SpawnId,
                0,
                ClientBuildId,
                ClientEntityHandle,
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await repository.ResolveAsync(
                SpawnId,
                MapId,
                " ",
                ClientEntityHandle,
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await repository.ResolveAsync(
                SpawnId,
                MapId,
                ClientBuildId,
                0,
                CancellationToken.None));
    }
}
