using God2.ServerV2.Application;

namespace God2.ServerV2.Persistence;

// Journal durability precedes sale dispatch.
// Exceptions propagate without automatic retry.
// Saving a request does not approve evidence or establish sale success.
public sealed class JournaledMerchantSaleWriter(
    IMerchantSaleJournal journal,
    IMerchantSaleWriter writer) : IMerchantSaleWriter
{
    private readonly IMerchantSaleJournal _journal =
        journal ?? throw new ArgumentNullException(nameof(journal));
    private readonly IMerchantSaleWriter _writer =
        writer ?? throw new ArgumentNullException(nameof(writer));

    public async ValueTask<MerchantSaleResult> SellAsync(
        MerchantSaleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _journal.SaveAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await _writer.SellAsync(request, cancellationToken);
    }
}
