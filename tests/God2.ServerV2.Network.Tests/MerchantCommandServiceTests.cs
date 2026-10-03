using God2.ServerV2.Application;
using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class MerchantCommandServiceTests
{
    [Fact]
    public async Task Purchase_uses_server_identity_and_preserves_writer_rejection()
    {
        var f = new Fixture();
        var command = f.Purchase;
        var result = await f.Service.PurchaseAsync(
            101, f.InteractionId, f.Binding, command, CancellationToken.None);

        Assert.Equal(MerchantInteractionStatus.Allowed, result.InteractionStatus);
        Assert.Same(f.Writer.PurchaseResult, result.TransactionResult);
        Assert.False(result.TransactionResult!.Succeeded);
        Assert.Equal(new MerchantPurchaseRequest(
            command.TransactionId, command.IdempotencyKey, 7, 99,
            command.ItemId, command.Quantity, command.InventoryId,
            command.ExpectedInventoryVersion, command.ExpectedMutationSequence,
            command.ExpectedWalletVersion), f.Writer.LastPurchase);
        Assert.Equal(1, f.Writer.PurchaseCalls);
        Assert.Equal(0, f.Writer.SaleCalls);
    }

    [Fact]
    public async Task Sale_preserves_authoritative_item_identity_and_versions()
    {
        var f = new Fixture();
        var command = f.Sale;
        var result = await f.Service.SellAsync(
            101, f.InteractionId, f.Binding, command, CancellationToken.None);

        Assert.Equal(MerchantInteractionStatus.Allowed, result.InteractionStatus);
        Assert.Same(f.Writer.SaleResult, result.TransactionResult);
        Assert.False(result.TransactionResult!.Succeeded);
        Assert.Equal(new MerchantSaleRequest(
            command.TransactionId, command.IdempotencyKey, 7, 99,
            command.ItemId, command.ItemInstanceId, command.SlotIndex,
            command.Quantity, command.InventoryId,
            command.ExpectedInventoryVersion, command.ExpectedMutationSequence,
            command.ExpectedSlotVersion, command.ExpectedWalletVersion),
            f.Writer.LastSale);
        Assert.Equal(1, f.Writer.SaleCalls);
        Assert.Equal(0, f.Writer.PurchaseCalls);
    }

    [Theory]
    [InlineData("closed", MerchantInteractionStatus.InteractionConflict)]
    [InlineData("left", MerchantInteractionStatus.WorldPresenceMissing)]
    [InlineData("far", MerchantInteractionStatus.OutOfRange)]
    [InlineData("binding", MerchantInteractionStatus.BindingBlocked)]
    public async Task Rejected_interaction_never_calls_either_writer(
        string scenario, MerchantInteractionStatus expected)
    {
        var f = new Fixture();
        var binding = f.Binding;
        switch (scenario)
        {
            case "closed":
                Assert.True(f.Interactions.Remove(101, out _));
                break;
            case "left":
                Assert.True(f.Presences.TryLeave(101, out _));
                break;
            case "far":
                Assert.True(f.Presences.TryMove(101, 7, 100, 124, out _));
                break;
            case "binding":
                binding = binding with { Enabled = false };
                break;
        }

        var buy = await f.Service.PurchaseAsync(
            101, f.InteractionId, binding, f.Purchase, CancellationToken.None);
        var sell = await f.Service.SellAsync(
            101, f.InteractionId, binding, f.Sale, CancellationToken.None);

        Assert.Equal(expected, buy.InteractionStatus);
        Assert.Equal(expected, sell.InteractionStatus);
        Assert.Null(buy.TransactionResult);
        Assert.Null(sell.TransactionResult);
        Assert.Equal(0, f.Writer.PurchaseCalls);
        Assert.Equal(0, f.Writer.SaleCalls);
    }

    [Fact]
    public async Task Cancellation_before_dispatch_never_calls_writer()
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            f.Service.PurchaseAsync(
                101, f.InteractionId, f.Binding, f.Purchase,
                cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            f.Service.SellAsync(
                101, f.InteractionId, f.Binding, f.Sale,
                cancellation.Token).AsTask());

        Assert.Equal(0, f.Writer.PurchaseCalls);
        Assert.Equal(0, f.Writer.SaleCalls);
    }

    [Fact]
    public async Task Writer_exception_is_not_reported_as_success()
    {
        var f = new Fixture();
        f.Writer.Failure = new InvalidOperationException("fixture failure");

        var buyError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.PurchaseAsync(
                101, f.InteractionId, f.Binding, f.Purchase,
                CancellationToken.None).AsTask());
        var sellError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.SellAsync(
                101, f.InteractionId, f.Binding, f.Sale,
                CancellationToken.None).AsTask());

        Assert.Same(f.Writer.Failure, buyError);
        Assert.Same(f.Writer.Failure, sellError);
    }

    [Fact]
    public async Task Shared_lane_checks_state_after_preceding_update()
    {
        var f = new Fixture();
        await f.Lane.RunAsync<bool>(token => ValueTask.FromResult(
            f.Presences.TryMove(101, 7, 100, 124, out _)),
            CancellationToken.None);

        var result = await f.Service.PurchaseAsync(
            101, f.InteractionId, f.Binding, f.Purchase,
            CancellationToken.None);

        Assert.Equal(MerchantInteractionStatus.OutOfRange,
            result.InteractionStatus);
        Assert.Null(result.TransactionResult);
        Assert.Equal(0, f.Writer.PurchaseCalls);
    }

    [Fact]
    public async Task Closed_shared_lane_rejects_both_commands()
    {
        var f = new Fixture();
        await f.Lane.CloseAsync(() =>
        {
            f.Interactions.Remove(101, out _);
            f.Presences.TryLeave(101, out _);
            return ValueTask.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.PurchaseAsync(
                101, f.InteractionId, f.Binding, f.Purchase,
                CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.SellAsync(
                101, f.InteractionId, f.Binding, f.Sale,
                CancellationToken.None).AsTask());

        Assert.Equal(0, f.Writer.PurchaseCalls);
        Assert.Equal(0, f.Writer.SaleCalls);
    }

    [Fact]
    public async Task Closing_waits_for_dispatched_writer_before_world_cleanup()
    {
        var f = new Fixture();
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        f.Writer.BeforePurchase = async () =>
        {
            entered.SetResult(true);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };

        var purchase = f.Service.PurchaseAsync(
            101, f.InteractionId, f.Binding, f.Purchase,
            CancellationToken.None).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cleanupCalls = 0;
        var close = f.Lane.CloseAsync(() =>
        {
            cleanupCalls++;
            f.Interactions.Remove(101, out _);
            f.Presences.TryLeave(101, out _);
            return ValueTask.CompletedTask;
        });

        Assert.False(close.IsCompleted);
        Assert.Equal(0, cleanupCalls);
        Assert.True(f.Interactions.IsCurrent(101, f.InteractionId));
        Assert.True(f.Presences.TryGetByConnection(101, out _));

        release.SetResult(true);
        var result = await purchase.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(f.Writer.PurchaseResult, result.TransactionResult);
        await close.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, cleanupCalls);
        Assert.False(f.Interactions.TryGetByConnection(101, out _));
        Assert.False(f.Presences.TryGetByConnection(101, out _));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.SellAsync(
                101, f.InteractionId, f.Binding, f.Sale,
                CancellationToken.None).AsTask());
        Assert.Equal(0, f.Writer.SaleCalls);
    }

    [Fact]
    public async Task Held_frame_lease_dispatches_both_commands_without_reentry()
    {
        var f = new Fixture();
        using var lease = await f.Lane.EnterAsync(CancellationToken.None);

        var buy = await f.Service.PurchaseInLeaseAsync(
            lease, 101, f.InteractionId, f.Binding, f.Purchase,
            CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var sell = await f.Service.SellInLeaseAsync(
            lease, 101, f.InteractionId, f.Binding, f.Sale,
            CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(f.Writer.PurchaseResult, buy.TransactionResult);
        Assert.Same(f.Writer.SaleResult, sell.TransactionResult);
        Assert.Equal(1, f.Writer.PurchaseCalls);
        Assert.Equal(1, f.Writer.SaleCalls);

        var cleanupCalls = 0;
        var close = f.Lane.CloseAsync(() =>
        {
            cleanupCalls++;
            return ValueTask.CompletedTask;
        });
        Assert.False(close.IsCompleted);
        Assert.Equal(0, cleanupCalls);
        lease.Dispose();
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, cleanupCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreign_or_disposed_lease_never_dispatches(bool disposed)
    {
        var f = new Fixture();
        var owner = disposed ? f.Lane : new ConnectionCommandLane();
        using var lease = await owner.EnterAsync(CancellationToken.None);
        if (disposed)
            lease.Dispose();

        async Task Buy() => await f.Service.PurchaseInLeaseAsync(
            lease, 101, f.InteractionId, f.Binding, f.Purchase,
            CancellationToken.None);
        async Task Sell() => await f.Service.SellInLeaseAsync(
            lease, 101, f.InteractionId, f.Binding, f.Sale,
            CancellationToken.None);

        if (disposed)
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(Buy);
            await Assert.ThrowsAsync<ObjectDisposedException>(Sell);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(Buy);
            await Assert.ThrowsAsync<InvalidOperationException>(Sell);
        }
        Assert.Equal(0, f.Writer.PurchaseCalls);
        Assert.Equal(0, f.Writer.SaleCalls);
    }

    [Fact]
    public async Task Held_lease_still_checks_interaction_and_cancellation()
    {
        var f = new Fixture();
        using var lease = await f.Lane.EnterAsync(CancellationToken.None);
        Assert.True(f.Interactions.Remove(101, out _));

        var buy = await f.Service.PurchaseInLeaseAsync(
            lease, 101, f.InteractionId, f.Binding, f.Purchase,
            CancellationToken.None);
        var sell = await f.Service.SellInLeaseAsync(
            lease, 101, f.InteractionId, f.Binding, f.Sale,
            CancellationToken.None);
        Assert.Equal(MerchantInteractionStatus.InteractionConflict,
            buy.InteractionStatus);
        Assert.Equal(MerchantInteractionStatus.InteractionConflict,
            sell.InteractionStatus);
        Assert.Null(buy.TransactionResult);
        Assert.Null(sell.TransactionResult);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await f.Service.PurchaseInLeaseAsync(
                lease, 101, f.InteractionId, f.Binding, f.Purchase,
                cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await f.Service.SellInLeaseAsync(
                lease, 101, f.InteractionId, f.Binding, f.Sale,
                cancellation.Token));

        Assert.Equal(0, f.Writer.PurchaseCalls);
        Assert.Equal(0, f.Writer.SaleCalls);
    }

    private sealed class Fixture
    {
        public NpcInteractionSessionRegistry Interactions { get; } = new();
        public WorldPresenceRegistry Presences { get; } = new();
        public RecordingWriter Writer { get; } = new();
        public ConnectionCommandLane Lane { get; } = new();
        public MerchantCommandService Service { get; }
        public Guid InteractionId { get; }
        public MerchantInteractionBinding Binding { get; } = new(
            99, 8001, 9001, 100, 5042,
            "fixture-build", 2, "fixture-policy", true);
        public MerchantPurchaseCommand Purchase { get; } = new(
            Guid.NewGuid(), "purchase-fixture", 253231541, 1,
            Guid.NewGuid(), 3, 4, 5);
        public MerchantSaleCommand Sale { get; } = new(
            Guid.NewGuid(), "sale-fixture", 253231541, 123456, 2, 1,
            Guid.NewGuid(), 6, 7, 8, 9);

        public Fixture()
        {
            var now = new DateTimeOffset(
                2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
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
            Service = new(
                Presences,
                new MerchantInteractionResolver(Interactions, Presences, npcs),
                Writer, Writer, Lane);
        }
    }

    private sealed class RecordingWriter :
        IMerchantPurchaseWriter, IMerchantSaleWriter
    {
        public int PurchaseCalls { get; private set; }
        public int SaleCalls { get; private set; }
        public MerchantPurchaseRequest? LastPurchase { get; private set; }
        public MerchantSaleRequest? LastSale { get; private set; }
        public Exception? Failure { get; set; }
        public Func<ValueTask>? BeforePurchase { get; set; }
        public MerchantPurchaseResult PurchaseResult { get; } = new(
            MerchantPurchaseStatus.EvidenceBlocked,
            Guid.NewGuid(), 100, 100, 5, 3, 3);
        public MerchantSaleResult SaleResult { get; } = new(
            MerchantSaleStatus.EvidenceBlocked,
            Guid.NewGuid(), 100, 100, 9, 6, 6);

        public async ValueTask<MerchantPurchaseResult> PurchaseAsync(
            MerchantPurchaseRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PurchaseCalls++;
            LastPurchase = request;
            if (BeforePurchase is not null)
                await BeforePurchase();
            if (Failure is not null)
                throw Failure;
            return PurchaseResult;
        }

        public ValueTask<MerchantSaleResult> SellAsync(
            MerchantSaleRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaleCalls++;
            LastSale = request;
            return Failure is null
                ? ValueTask.FromResult(SaleResult)
                : ValueTask.FromException<MerchantSaleResult>(Failure);
        }
    }
}
