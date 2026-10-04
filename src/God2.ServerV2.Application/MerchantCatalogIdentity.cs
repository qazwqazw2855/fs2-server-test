namespace God2.ServerV2.Application;

// Formal server-authority projection of a client-visible merchant catalog entry.
// Client catalog index is a wire identity and is not merchant_inventory.display_order.
public sealed record MerchantCatalogIdentity(
    long MerchantInventoryId,
    long MerchantId,
    long ItemId,
    int ClientCatalogIndex,
    long ClientItemId);

public interface IMerchantCatalogIdentityRepository
{
    ValueTask<MerchantCatalogIdentity?> ResolvePurchaseAsync(
        long merchantId,
        string clientBuildId,
        int clientCatalogIndex,
        long clientItemId,
        CancellationToken cancellationToken);
}
