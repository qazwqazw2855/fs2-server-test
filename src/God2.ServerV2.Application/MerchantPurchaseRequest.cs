namespace God2.ServerV2.Application;

// Constructed by trusted runtime after validating session/NPC interaction.
// Prices and currency are not supplied by the client.
public sealed record MerchantPurchaseRequest(
    Guid TransactionId,
    string IdempotencyKey,
    long CharacterId,
    long MerchantId,
    long ItemId,
    int Quantity,
    Guid InventoryId,
    long ExpectedInventoryVersion,
    long ExpectedMutationSequence,
    long ExpectedWalletVersion);

public enum MerchantPurchaseStatus
{
    Purchased,
    Replayed,
    IdempotencyConflict,
    CharacterMissing,
    ListingMissing,
    ListingDisabled,
    PricingBlocked,
    EvidenceBlocked,
    WalletMissing,
    WalletVersionConflict,
    InsufficientFunds,
    InventoryRejected
}

public sealed record MerchantPurchaseResult(
    MerchantPurchaseStatus Status,
    Guid TransactionId,
    long BalanceBefore,
    long BalanceAfter,
    long WalletVersionAfter,
    long InventoryVersionBefore,
    long InventoryVersionAfter,
    InventoryGrantStatus? InventoryFailure = null)
{
    public bool Succeeded =>
        Status is MerchantPurchaseStatus.Purchased or MerchantPurchaseStatus.Replayed;
}

// Must approve price/currency, merchant/item mapping and current interaction.
// An enabled row alone is insufficient. Gold mapping must be reviewed.
public interface IMerchantPurchaseEvidenceGate
{
    ValueTask<bool> IsApprovedAsync(
        MerchantPurchaseRequest request,
        MerchantPurchaseQuote quote,
        CancellationToken cancellationToken);
}

public sealed class BlockedMerchantPurchaseEvidenceGate
    : IMerchantPurchaseEvidenceGate
{
    public ValueTask<bool> IsApprovedAsync(
        MerchantPurchaseRequest request,
        MerchantPurchaseQuote quote,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}

public interface IMerchantPurchaseWriter
{
    ValueTask<MerchantPurchaseResult> PurchaseAsync(
        MerchantPurchaseRequest request,
        CancellationToken cancellationToken);
}
