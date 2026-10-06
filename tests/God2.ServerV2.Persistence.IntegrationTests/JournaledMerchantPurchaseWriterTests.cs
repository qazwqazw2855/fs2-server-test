using God2.ServerV2.Application;

namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class JournaledMerchantPurchaseWriterTests
{
    private static MerchantPurchaseRequest Request() => new(
        Guid.NewGuid(), "journal-fixture", 7, 99, 253231541, 1,
        Guid.NewGuid(), 3, 4, 5);

    [Fact]
    public async Task JournalFailurePreventsPurchaseDispatch()
    {
        var failure = new IOException("journal unavailable");
        var journal = new RecordingJournal { Failure = failure };
        var writer = new RecordingWriter(journal);
        var service = new JournaledMerchantPurchaseWriter(journal, writer);

        var error = await Assert.ThrowsAsync<IOException>(async () =>
            await service.PurchaseAsync(Request(), CancellationToken.None));

        Assert.Same(failure, error);
        Assert.Equal(1, journal.Calls);
        Assert.Null(journal.Saved);
        Assert.Equal(0, writer.Calls);
    }

    [Fact]
    public async Task UncertainWriterResultPreservesRequestWithoutAutomaticRetry()
    {
        var request = Request();
        var failure = new IOException("purchase result lost");
        var journal = new RecordingJournal();
        var writer = new RecordingWriter(journal) { Failure = failure };
        var service = new JournaledMerchantPurchaseWriter(journal, writer);

        var error = await Assert.ThrowsAsync<IOException>(async () =>
            await service.PurchaseAsync(request, CancellationToken.None));

        Assert.Same(failure, error);
        Assert.Equal(1, journal.Calls);
        Assert.Equal(1, writer.Calls);
        Assert.Same(request, journal.Saved);
        Assert.Same(request, writer.Received);
    }

    private sealed class RecordingJournal : IMerchantPurchaseJournal
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public MerchantPurchaseRequest? Saved { get; private set; }

        public ValueTask SaveAsync(
            MerchantPurchaseRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Failure is not null)
                throw Failure;
            Saved = request;
            return ValueTask.CompletedTask;
        }

        public ValueTask<MerchantPurchaseRequest?> FindAsync(
            long characterId, Guid transactionId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                Saved?.CharacterId == characterId &&
                Saved.TransactionId == transactionId ? Saved : null);
    }

    private sealed class RecordingWriter(RecordingJournal journal)
        : IMerchantPurchaseWriter
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public MerchantPurchaseRequest? Received { get; private set; }

        public ValueTask<MerchantPurchaseResult> PurchaseAsync(
            MerchantPurchaseRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Same(request, journal.Saved);
            Calls++;
            Received = request;
            throw Failure ?? new InvalidOperationException("Unexpected dispatch");
        }
    }
}
