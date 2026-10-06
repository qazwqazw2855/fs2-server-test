using God2.ServerV2.Application;

namespace God2.ServerV2.Persistence;

// Journal durability precedes purchase dispatch.
// Exceptions propagate without automatic retry.
// Saving a request does not approve evidence or establish purchase success.
public sealed class JournaledMerchantPurchaseWriter(
    IMerchantPurchaseJournal journal,
    IMerchantPurchaseWriter writer) : IMerchantPurchaseWriter
{
    private readonly IMerchantPurchaseJournal _journal =
        journal ?? throw new ArgumentNullException(nameof(journal));
    private readonly IMerchantPurchaseWriter _writer =
        writer ?? throw new ArgumentNullException(nameof(writer));

    public async ValueTask<MerchantPurchaseResult> PurchaseAsync(
        MerchantPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _journal.SaveAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await _writer.PurchaseAsync(request, cancellationToken);
    }
}
