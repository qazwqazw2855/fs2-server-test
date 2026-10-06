using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network.Tests;

public sealed class MerchantPurchasePreparationServiceTests
{
    [Fact]
    public async Task ApprovedFixtureProducesServerOwnedCommand()
    {
        var f = new Fixture();
        using var lease = await f.Lane.EnterAsync(CancellationToken.None);
        var result = await f.Prepare(lease);
        Assert.Equal(MerchantInteractionStatus.Allowed, result.InteractionStatus);
        Assert.Equal("", result.FailureCode);
        Assert.NotNull(result.Command);
        Assert.Equal(253231541, result.Command!.ItemId);
        Assert.Equal(3, result.Command.ExpectedInventoryVersion);
        Assert.Equal(4, result.Command.ExpectedMutationSequence);
        Assert.Equal(5, result.Command.ExpectedWalletVersion);
        Assert.Equal((99L, "fixture-build", 7, 6901L), f.Repos.Lookup);
        Assert.Equal(7, f.Repos.InventoryCharacter);
        Assert.Equal(7, f.Repos.WalletCharacter);
    }

    [Fact]
    public async Task BlockedBindingDoesNotReadRepositories()
    {
        var f = new Fixture();
        using var lease = await f.Lane.EnterAsync(CancellationToken.None);
        var result = await f.Prepare(lease,
            f.Binding with { Enabled = false, MaximumDistance = null });
        Assert.Equal(MerchantInteractionStatus.BindingBlocked, result.InteractionStatus);
        Assert.Null(result.Command);
        Assert.Equal(0, f.Repos.Calls);
    }

    [Theory]
    [InlineData(false, "CatalogMissing")]
    [InlineData(true, "WalletMissing")]
    public async Task MissingAuthorityDoesNotProduceCommand(
        bool walletMissing, string expected)
    {
        var f = new Fixture();
        if (walletMissing) f.Repos.Wallet = null;
        else f.Repos.Catalog = null;
        using var lease = await f.Lane.EnterAsync(CancellationToken.None);
        var result = await f.Prepare(lease);
        Assert.Null(result.Command);
        Assert.Equal(expected, result.FailureCode);
        if (!walletMissing)
        {
            Assert.Equal(0, f.Repos.InventoryCharacter);
            Assert.Equal(0, f.Repos.WalletCharacter);
        }
    }

    [Theory]
    [InlineData(false, MerchantInteractionStatus.InteractionConflict)]
    [InlineData(true, MerchantInteractionStatus.NpcMissing)]
    public async Task StateChangedDuringDatabaseAwaitRejectsCommand(
        bool mapChange, MerchantInteractionStatus expected)
    {
        var f = new Fixture();
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        f.Repos.BeforeCatalog = async () =>
        {
            entered.TrySetResult(true);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };

        using var lease = await f.Lane.EnterAsync(CancellationToken.None);
        var pending = f.Prepare(lease).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Deliberately bypass the lane to exercise defensive revalidation.
        try
        {
            if (mapChange)
                Assert.True(f.Presences.TryChangeMap(
                    101, 7, 200, 74, 124, out _, out _, out _, out _));
            else
                Assert.Equal(NpcInteractionCloseStatus.Closed,
                    f.Interactions.TryClose(101, 5042, out _));
        }
        finally
        {
            release.TrySetResult(true);
        }

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expected, result.InteractionStatus);
        Assert.Null(result.Command);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOrDisposedLeaseDoesNotReadRepositories(bool disposed)
    {
        var f = new Fixture();
        var owner = disposed ? f.Lane : new ConnectionCommandLane();
        using var lease = await owner.EnterAsync(CancellationToken.None);
        if (disposed) lease.Dispose();
        async Task Prepare() => await f.Prepare(lease);
        if (disposed)
            await Assert.ThrowsAsync<ObjectDisposedException>(Prepare);
        else
            await Assert.ThrowsAsync<InvalidOperationException>(Prepare);
        Assert.Equal(0, f.Repos.Calls);
    }

    [Fact]
    public async Task CancelledPreparationDoesNotReadRepositories()
    {
        var f = new Fixture();
        using var lease = await f.Lane.EnterAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await f.Service.PrepareInLeaseAsync(
                lease, 101, f.InteractionId, f.Binding, f.Selection,
                Guid.NewGuid(), cancellation.Token));
        Assert.Equal(0, f.Repos.Calls);
    }

    private sealed class Fixture
    {
        public ConnectionCommandLane Lane { get; } = new();
        public WorldPresenceRegistry Presences { get; } = new();
        public NpcInteractionSessionRegistry Interactions { get; } = new();
        public Repositories Repos { get; } = new();
        public MerchantPurchasePreparationService Service { get; }
        public Guid InteractionId { get; }
        public MerchantInteractionBinding Binding { get; } = new(
            99, 8001, 9001, 100, 5042,
            "fixture-build", 2, "fixture-policy", true);
        public OfficialMerchantTransactionSelection Selection { get; } = new(
            5042, 6901, 1, OfficialMerchantTransactionOperation.Buy, 7);

        public Fixture()
        {
            var now = DateTimeOffset.UtcNow;
            var npc = new NpcSnapshotEntry(
                9001, 8001, "Fixture NPC", 100, 74, 124,
                "fixture-build", 5042, null, null, null, null, null,
                null, null, "EvidenceBlocked", "fixture");
            var npcs = new WorldNpcRegistry();
            npcs.PublishMap(100, [npc]);
            Assert.True(Presences.TryEnter(new WorldPresence(
                101, "fixture-account", 1,
                new CharacterListEntry(
                    7, 1, "Fixture", null, null, null, 1, null,
                    100, 74, 124, now, null), now)).Succeeded);
            InteractionId = Interactions.TryOpen(
                101, 7, 100, 5042, [npc], now).Session!.InteractionId;
            Service = new(Lane, Presences,
                new MerchantInteractionResolver(Interactions, Presences, npcs),
                Repos, Repos, Repos);
        }

        public ValueTask<MerchantPurchasePreparationResult> Prepare(
            ConnectionCommandLane.Lease lease,
            MerchantInteractionBinding? binding = null) =>
            Service.PrepareInLeaseAsync(
                lease, 101, InteractionId, binding ?? Binding, Selection,
                Guid.NewGuid(), CancellationToken.None);
    }

    private sealed class Repositories :
        IMerchantCatalogIdentityRepository,
        ICharacterInventorySnapshotRepository,
        ICharacterWalletSnapshotRepository
    {
        public int Calls { get; private set; }
        public (long, string, int, long) Lookup { get; private set; }
        public long InventoryCharacter { get; private set; }
        public long WalletCharacter { get; private set; }
        public Func<Task>? BeforeCatalog { get; set; }
        public MerchantCatalogIdentity? Catalog { get; set; } =
            new(500, 99, 253231541, 7, 6901);
        public CharacterInventorySnapshot Inventory { get; } =
            new(Guid.NewGuid(), 7, 8, 3, 4, "Clean", []);
        public CharacterWalletSnapshot? Wallet { get; set; } =
            new(7, "Gold", 100, 5);

        public async ValueTask<MerchantCatalogIdentity?> ResolvePurchaseAsync(
            long merchantId, string build, int index, long item,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Lookup = (merchantId, build, index, item);
            if (BeforeCatalog is not null) await BeforeCatalog();
            return Catalog;
        }

        public ValueTask<CharacterInventorySnapshot?> GetByCharacterAsync(
            long characterId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            InventoryCharacter = characterId;
            return ValueTask.FromResult<CharacterInventorySnapshot?>(Inventory);
        }

        public ValueTask<CharacterWalletSnapshot?> GetGoldByCharacterAsync(
            long characterId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            WalletCharacter = characterId;
            return ValueTask.FromResult(Wallet);
        }
    }
}
