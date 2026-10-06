namespace God2.ServerV2.Application;

// Save the exact immutable request before writer dispatch.
// Finding no receipt does not authorize a new purchase or identity rebuild.
public interface IMerchantPurchaseJournal
{
    ValueTask SaveAsync(
        MerchantPurchaseRequest request,
        CancellationToken cancellationToken);

    ValueTask<MerchantPurchaseRequest?> FindAsync(
        long characterId,
        Guid transactionId,
        CancellationToken cancellationToken);
}
