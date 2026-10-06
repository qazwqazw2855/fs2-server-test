using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network;

public sealed record MerchantPurchasePreparationResult(
    MerchantInteractionStatus InteractionStatus,
    MerchantPurchaseCommand? Command,
    string FailureCode);

// Read-only preparation. Does not call a writer or approve response wire.
// Caller retains the existing connection lease throughout preparation.
// Execution must revalidate interaction and DB versions.
public sealed class MerchantPurchasePreparationService
{
    private readonly ConnectionCommandLane _lane;
    private readonly WorldPresenceRegistry _presences;
    private readonly MerchantInteractionResolver _resolver;
    private readonly IMerchantCatalogIdentityRepository _catalog;
    private readonly ICharacterInventorySnapshotRepository _inventory;
    private readonly ICharacterWalletSnapshotRepository _wallet;

    public MerchantPurchasePreparationService(
        ConnectionCommandLane lane,
        WorldPresenceRegistry presences,
        MerchantInteractionResolver resolver,
        IMerchantCatalogIdentityRepository catalog,
        ICharacterInventorySnapshotRepository inventory,
        ICharacterWalletSnapshotRepository wallet)
    {
        _lane = lane ?? throw new ArgumentNullException(nameof(lane));
        _presences = presences ?? throw new ArgumentNullException(nameof(presences));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
    }

    public async ValueTask<MerchantPurchasePreparationResult> PrepareInLeaseAsync(
        ConnectionCommandLane.Lease lease,
        long connectionId,
        Guid interactionId,
        MerchantInteractionBinding binding,
        OfficialMerchantTransactionSelection selection,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(selection);
        lease.ValidateOwner(_lane);
        cancellationToken.ThrowIfCancellationRequested();

        var status = _resolver.Check(
            connectionId, interactionId, binding.MerchantId, binding);
        if (status != MerchantInteractionStatus.Allowed)
            return new(status, null, "InteractionRejected");

        if (!_presences.TryGetByConnection(connectionId, out var presence) ||
            presence is null)
            return new(MerchantInteractionStatus.WorldPresenceMissing,
                null, "WorldPresenceMissing");

        if (selection.Operation != OfficialMerchantTransactionOperation.Buy)
            return new(status, null, "UnsupportedOperation");
        if (selection.Quantity != 1)
            return new(status, null, "UnsupportedQuantity");
        if (transactionId == Guid.Empty)
            return new(status, null, "InvalidServerIdentity");
        if (selection.ClientEntityHandle != binding.ClientEntityHandle)
            return new(status, null, "BindingMismatch");

        var characterId = presence.Character.CharacterId;
        var catalog = await _catalog.ResolvePurchaseAsync(
            binding.MerchantId,
            binding.ClientBuildId,
            selection.CatalogIndexOrClientInventorySlot,
            selection.ClientItemId,
            cancellationToken);

        if (catalog is null)
            return new(status, null, "CatalogMissing");

        var inventory = await _inventory.GetByCharacterAsync(
            characterId, cancellationToken);
        var wallet = await _wallet.GetGoldByCharacterAsync(
            characterId, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        lease.ValidateOwner(_lane);

        status = _resolver.Check(
            connectionId, interactionId, binding.MerchantId, binding);
        if (status != MerchantInteractionStatus.Allowed)
            return new(status, null, "InteractionRejected");

        if (!_presences.TryGetByConnection(connectionId, out var current) ||
            !ReferenceEquals(presence, current))
            return new(MerchantInteractionStatus.StateChanged,
                null, "StateChanged");

        MerchantPurchaseCommandFactory.TryCreate(
            characterId, binding, selection, catalog, inventory, wallet,
            transactionId, out var command, out var failure);

        return new(status, command, failure);
    }
}
