using God2.ServerV2.Application;

namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class JournaledMerchantSaleWriterTests
{
    private static MerchantSaleRequest Request() => new(
        Guid.NewGuid(), "journal-fixture", 7, 99, 253231541,
        123, 0, 1, Guid.NewGuid(), 3, 4, 2, 5);

    [Fact]
    public async Task JournalFailurePreventsSaleDispatch()
    {
        var failure = new IOException("journal unavailable");
        var journal = new RecordingJournal { Failure = failure };
        var writer = new RecordingWriter(journal);
        var service = new JournaledMerchantSaleWriter(journal, writer);

        var error = await Assert.ThrowsAsync<IOException>(async () =>
            await service.SellAsync(Request(), CancellationToken.None));

        Assert.Same(failure, error);
        Assert.Equal(1, journal.Calls);
        Assert.Null(journal.Saved);
        Assert.Equal(0, writer.Calls);
    }

    [Fact]
    public async Task UncertainWriterResultPreservesRequestWithoutAutomaticRetry()
    {
        var request = Request();
        var failure = new IOException("sale result lost");
        var journal = new RecordingJournal();
        var writer = new RecordingWriter(journal) { Failure = failure };
        var service = new JournaledMerchantSaleWriter(journal, writer);

        var error = await Assert.ThrowsAsync<IOException>(async () =>
            await service.SellAsync(request, CancellationToken.None));

        Assert.Same(failure, error);
        Assert.Equal(1, journal.Calls);
        Assert.Equal(1, writer.Calls);
        Assert.Same(request, journal.Saved);
        Assert.Same(request, writer.Received);
    }

    [Fact]
    public async Task CancellationAfterJournalSavePreventsSaleDispatch()
    {
        using var cancellation = new CancellationTokenSource();
        var request = Request();
        var journal = new RecordingJournal {
            AfterSave = () => cancellation.Cancel()
        };
        var writer = new RecordingWriter(journal);
        var service = new JournaledMerchantSaleWriter(journal, writer);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.SellAsync(request, cancellation.Token));

        Assert.Equal(1, journal.Calls);
        Assert.Same(request, journal.Saved);
        Assert.Equal(0, writer.Calls);
        Assert.Null(writer.Received);
    }

    private sealed class RecordingJournal : IMerchantSaleJournal
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public MerchantSaleRequest? Saved { get; private set; }
        public Action? AfterSave { get; init; }

        public ValueTask SaveAsync(
            MerchantSaleRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Failure is not null)
                throw Failure;
            Saved = request;
            AfterSave?.Invoke();
            return ValueTask.CompletedTask;
        }

        public ValueTask<MerchantSaleRequest?> FindAsync(
            long characterId, Guid transactionId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                Saved?.CharacterId == characterId &&
                Saved.TransactionId == transactionId ? Saved : null);
    }

    private sealed class RecordingWriter(RecordingJournal journal)
        : IMerchantSaleWriter
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public MerchantSaleRequest? Received { get; private set; }

        public ValueTask<MerchantSaleResult> SellAsync(
            MerchantSaleRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Same(request, journal.Saved);
            Calls++;
            Received = request;
            throw Failure ?? new InvalidOperationException("Unexpected dispatch");
        }
    }
}
