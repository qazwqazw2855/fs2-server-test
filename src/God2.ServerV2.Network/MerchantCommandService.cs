using God2.ServerV2.Application;

namespace God2.ServerV2.Network;

// Trusted internal commands. Protocol decoding and item identity projection
// must be verified separately before constructing these commands.
public sealed record MerchantPurchaseCommand(
    Guid TransactionId,
    string IdempotencyKey,
    long ItemId,
    int Quantity,
    Guid InventoryId,
    long ExpectedInventoryVersion,
    long ExpectedMutationSequence,
    long ExpectedWalletVersion);

public sealed record MerchantSaleCommand(
    Guid TransactionId,
    string IdempotencyKey,
    long ItemId,
    long ItemInstanceId,
    int SlotIndex,
    int Quantity,
    Guid InventoryId,
    long ExpectedInventoryVersion,
    long ExpectedMutationSequence,
    long ExpectedSlotVersion,
    long ExpectedWalletVersion);

public sealed record MerchantPurchaseCommandResult(
    MerchantInteractionStatus InteractionStatus,
    MerchantPurchaseResult? TransactionResult);

public sealed record MerchantSaleCommandResult(
    MerchantInteractionStatus InteractionStatus,
    MerchantSaleResult? TransactionResult);

public sealed class MerchantCommandService
{
    private readonly WorldPresenceRegistry _presences;
    private readonly MerchantInteractionResolver _resolver;
    private readonly IMerchantPurchaseWriter _purchases;
    private readonly IMerchantSaleWriter _sales;

    public MerchantCommandService(
        WorldPresenceRegistry presences,
        MerchantInteractionResolver resolver,
        IMerchantPurchaseWriter purchases,
        IMerchantSaleWriter sales)
    {
        _presences = presences ??
            throw new ArgumentNullException(nameof(presences));
        _resolver = resolver ??
            throw new ArgumentNullException(nameof(resolver));
        _purchases = purchases ??
            throw new ArgumentNullException(nameof(purchases));
        _sales = sales ??
            throw new ArgumentNullException(nameof(sales));
    }

    // Call and await only within the connection's serialized command loop.
    // This service does not serialize registry mutations from other callers.
    public async ValueTask<MerchantPurchaseCommandResult> PurchaseAsync(
        long connectionId,
        Guid interactionId,
        MerchantInteractionBinding binding,
        MerchantPurchaseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var status = Resolve(
            connectionId, interactionId, binding, out var presence);
        if (status != MerchantInteractionStatus.Allowed)
            return new(status, null);

        var request = new MerchantPurchaseRequest(
            command.TransactionId,
            command.IdempotencyKey,
            presence!.Character.CharacterId,
            binding.MerchantId,
            command.ItemId,
            command.Quantity,
            command.InventoryId,
            command.ExpectedInventoryVersion,
            command.ExpectedMutationSequence,
            command.ExpectedWalletVersion);

        var result = await _purchases.PurchaseAsync(
            request, cancellationToken);
        return new(MerchantInteractionStatus.Allowed, result);
    }

    public async ValueTask<MerchantSaleCommandResult> SellAsync(
        long connectionId,
        Guid interactionId,
        MerchantInteractionBinding binding,
        MerchantSaleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var status = Resolve(
            connectionId, interactionId, binding, out var presence);
        if (status != MerchantInteractionStatus.Allowed)
            return new(status, null);

        var request = new MerchantSaleRequest(
            command.TransactionId,
            command.IdempotencyKey,
            presence!.Character.CharacterId,
            binding.MerchantId,
            command.ItemId,
            command.ItemInstanceId,
            command.SlotIndex,
            command.Quantity,
            command.InventoryId,
            command.ExpectedInventoryVersion,
            command.ExpectedMutationSequence,
            command.ExpectedSlotVersion,
            command.ExpectedWalletVersion);

        var result = await _sales.SellAsync(request, cancellationToken);
        return new(MerchantInteractionStatus.Allowed, result);
    }

    private MerchantInteractionStatus Resolve(
        long connectionId,
        Guid interactionId,
        MerchantInteractionBinding binding,
        out WorldPresence? presence)
    {
        if (connectionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        presence = null;
        if (!_presences.TryGetByConnection(connectionId, out var captured) ||
            captured is null)
            return MerchantInteractionStatus.WorldPresenceMissing;

        var status = _resolver.Check(
            connectionId, interactionId, binding.MerchantId, binding);
        if (status != MerchantInteractionStatus.Allowed)
            return status;

        if (!_presences.TryGetByConnection(connectionId, out var current) ||
            !ReferenceEquals(captured, current))
            return MerchantInteractionStatus.StateChanged;

        presence = captured;
        return MerchantInteractionStatus.Allowed;
    }
}
