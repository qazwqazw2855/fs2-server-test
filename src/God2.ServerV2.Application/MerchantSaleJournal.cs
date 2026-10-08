namespace God2.ServerV2.Application;

// Save the exact immutable request before writer dispatch.
// Finding no receipt does not authorize a new sale or identity rebuild.
public interface IMerchantSaleJournal
{
    ValueTask SaveAsync(
        MerchantSaleRequest request,
        CancellationToken cancellationToken);

    ValueTask<MerchantSaleRequest?> FindAsync(
        long characterId,
        Guid transactionId,
        CancellationToken cancellationToken);
}
