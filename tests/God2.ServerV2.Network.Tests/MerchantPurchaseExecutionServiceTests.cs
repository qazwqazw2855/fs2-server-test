using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network.Tests;

public sealed class MerchantPurchaseExecutionServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task ExecutionPreservesAuthorizationAndCommitBoundaries(int scenario)
    {
        var f = new Fixture();
        switch (scenario)
        {
            case 1: f.Repos.BadCommittedLayout = true; break;
            case 2: f.Repos.Status = MerchantPurchaseStatus.EvidenceBlocked; break;
            case 3: f.Repos.Status = MerchantPurchaseStatus.Replayed; break;
            case 4: f.Repos.Failure = new IOException("uncertain writer result"); break;
            case 5: f.Repos.Snapshot = f.Repos.Snapshot with
                { Slots = [new CharacterInventorySlot(0, 253231541, 1)] }; break;
            case 6: f.Repos.BeforeRead = () =>
                Assert.True(f.Presences.TryMove(101, 7, 100, 124, out _)); break;
        }

        using var lease = await f.Lane.EnterAsync(CancellationToken.None);
        if (scenario == 4)
        {
            var error = await Assert.ThrowsAsync<IOException>(async () =>
                await f.Execute(lease));
            Assert.Same(f.Repos.Failure, error);
            Assert.Equal(1, f.Repos.Writes);
            Assert.Equal(1, f.Repos.Reads);
            Assert.Equal(f.Command.TransactionId, f.Repos.LastRequest!.TransactionId);
            Assert.Equal(f.Command.IdempotencyKey, f.Repos.LastRequest.IdempotencyKey);
            return;
        }

        var result = await f.Execute(lease);
        Assert.Equal(0, f.Repos.Sales);
        Assert.Equal(scenario is 5 or 6 ? 0 : 1, f.Repos.Writes);
        Assert.Equal(scenario is 0 or 1 ? 2 : 1, f.Repos.Reads);

        if (scenario == 0)
        {
            Assert.Equal(MerchantInteractionStatus.Allowed, result.InteractionStatus);
            Assert.Equal(MerchantPurchaseStatus.Purchased, result.TransactionResult!.Status);
            Assert.Equal("", result.FailureCode);
            Assert.False(result.RequiresReconciliation);
            Assert.True(OfficialMerchantPurchaseResultCodec.TryEncode(
                OfficialMerchantPurchaseResultCodec.ClientBuildId,
                f.Selection, 25000, out var expected, out _));
            Assert.Equal(expected, result.EncodedResponse);
            Assert.Equal(7, f.Repos.LastRequest!.CharacterId);
            Assert.Equal(99, f.Repos.LastRequest.MerchantId);
        }
        else
        {
            Assert.Null(result.EncodedResponse);
            Assert.Equal(scenario is 1 or 3, result.RequiresReconciliation);
            Assert.Equal(scenario switch
            {
                1 => "CommittedInventoryMismatch",
                2 => "EvidenceBlocked",
                3 => "ReplayRequiresReconciliation",
                5 => "InventoryLayoutEvidenceBlocked",
                6 => "InteractionBlocked",
                _ => throw new InvalidOperationException()
            }, result.FailureCode);
            if (scenario == 6)
                Assert.Equal(MerchantInteractionStatus.OutOfRange, result.InteractionStatus);
        }
    }

    private sealed class Fixture
    {
        public ConnectionCommandLane Lane { get; } = new();
        public WorldPresenceRegistry Presences { get; } = new();
        public Repositories Repos { get; } = new();
        public MerchantPurchaseExecutionService Service { get; }
        public Guid InteractionId { get; }
        public MerchantInteractionBinding Binding { get; } = new(
            99, 8001, 9001, 100, 3954,
            OfficialMerchantPurchaseResultCodec.ClientBuildId,
            2, "synthetic-test-policy", true);
        public OfficialMerchantTransactionSelection Selection { get; } =
            new(3954, 6901, 1, OfficialMerchantTransactionOperation.Buy, 7);
        public MerchantPurchaseCommand Command { get; }

        public Fixture()
        {
            var now = DateTimeOffset.Parse("2026-10-06T00:00:00Z");
            var npc = new NpcSnapshotEntry(
                9001, 8001, "Fixture NPC", 100, 74, 124,
                OfficialMerchantPurchaseResultCodec.ClientBuildId,
                3954, null, null, null, null, null,
                null, null, "EvidenceBlocked", "synthetic");
            var npcs = new WorldNpcRegistry();
            npcs.PublishMap(100, [npc]);
            Assert.True(Presences.TryEnter(new WorldPresence(
                101, "fixture-account", 1,
                new CharacterListEntry(
                    7, 1, "Fixture", null, null, null, 1, null,
                    100, 74, 124, now, null), now)).Succeeded);
            var interactions = new NpcInteractionSessionRegistry();
            InteractionId = interactions.TryOpen(
                101, 7, 100, 3954, [npc], now).Session!.InteractionId;
            var resolver = new MerchantInteractionResolver(
                interactions, Presences, npcs);
            var commands = new MerchantCommandService(
                Presences, resolver, Repos, Repos, Lane);
            Service = new(Lane, Presences, resolver, commands, Repos);
            Command = new(Guid.NewGuid(), "execution-fixture",
                253231541, 1, Repos.Snapshot.InventoryId, 3, 4, 5);
        }

        public ValueTask<MerchantPurchaseExecutionResult> Execute(
            ConnectionCommandLane.Lease lease) =>
            Service.ExecuteInLeaseAsync(
                lease, 101, InteractionId, Binding, Selection,
                Command, CancellationToken.None);
    }

    private sealed class Repositories :
        ICharacterInventorySnapshotRepository,
        IMerchantPurchaseWriter, IMerchantSaleWriter
    {
        public CharacterInventorySnapshot Snapshot { get; set; } =
            new(Guid.NewGuid(), 7, 8, 3, 4, "Clean", []);
        public MerchantPurchaseStatus Status { get; set; } =
            MerchantPurchaseStatus.Purchased;
        public bool BadCommittedLayout { get; set; }
        public Exception? Failure { get; set; }
        public Action? BeforeRead { get; set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public int Sales { get; private set; }
        public MerchantPurchaseRequest? LastRequest { get; private set; }

        public ValueTask<CharacterInventorySnapshot?> GetByCharacterAsync(
            long characterId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(7, characterId);
            Reads++;
            BeforeRead?.Invoke();
            return ValueTask.FromResult<CharacterInventorySnapshot?>(Snapshot);
        }

        public ValueTask<MerchantPurchaseResult> PurchaseAsync(
            MerchantPurchaseRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes++;
            LastRequest = request;
            if (Failure is not null)
                throw Failure;
            if (Status == MerchantPurchaseStatus.Purchased)
                Snapshot = Snapshot with
                {
                    Version = 4,
                    MutationSequence = 5,
                    Slots = [new CharacterInventorySlot(
                        BadCommittedLayout ? 1 : 0, 253231541, 1)]
                };
            return ValueTask.FromResult(new MerchantPurchaseResult(
                Status, request.TransactionId, 50000, 25000, 6, 3, 4));
        }

        public ValueTask<MerchantSaleResult> SellAsync(
            MerchantSaleRequest request, CancellationToken cancellationToken)
        {
            Sales++;
            throw new InvalidOperationException("BUY must not dispatch SELL");
        }
    }
}
