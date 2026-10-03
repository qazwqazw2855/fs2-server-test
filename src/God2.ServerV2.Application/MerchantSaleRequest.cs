namespace God2.ServerV2.Application;

// Trusted runtime operation after session/NPC interaction validation.
// ItemInstanceId is the authoritative DB identity, not a client-visible ID.
public sealed record MerchantSaleRequest(
    Guid TransactionId,
    string IdempotencyKey,
    long CharacterId,
    long MerchantId,
    long ItemId,
    long ItemInstanceId,
    int SlotIndex,
    int Quantity,
    Guid InventoryId,
    long ExpectedInventoryVersion,
    long ExpectedMutationSequence,
    long ExpectedSlotVersion,
    long ExpectedWalletVersion);

public enum MerchantSaleStatus
{
    Sold,
    Replayed,
    IdempotencyConflict,
    CharacterMissing,
    ListingMissing,
    ListingDisabled,
    PricingBlocked,
    EvidenceBlocked,
    InventoryMissing,
    InventoryVersionConflict,
    SlotMissing,
    SlotConflict,
    ItemStateBlocked,
    WalletMissing,
    WalletVersionConflict
}

public sealed record MerchantSaleResult(
    MerchantSaleStatus Status,
    Guid TransactionId,
    long BalanceBefore,
    long BalanceAfter,
    long WalletVersionAfter,
    long InventoryVersionBefore,
    long InventoryVersionAfter)
{
    public bool Succeeded =>
        Status is MerchantSaleStatus.Sold or MerchantSaleStatus.Replayed;
}

public interface IMerchantSaleEvidenceGate
{
    ValueTask<bool> IsApprovedAsync(
        MerchantSaleRequest request,
        MerchantSaleQuote quote,
        CancellationToken cancellationToken);
}

public sealed class BlockedMerchantSaleEvidenceGate : IMerchantSaleEvidenceGate
{
    public ValueTask<bool> IsApprovedAsync(
        MerchantSaleRequest request,
        MerchantSaleQuote quote,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);
}

public interface IMerchantSaleWriter
{
    ValueTask<MerchantSaleResult> SellAsync(
        MerchantSaleRequest request,
        CancellationToken cancellationToken);
}
