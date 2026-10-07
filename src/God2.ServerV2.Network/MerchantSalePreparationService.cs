using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network;

public sealed record MerchantSalePreparationResult(
    MerchantInteractionStatus InteractionStatus,
    MerchantSaleCommand? Command,
    string FailureCode);

// Read-only preparation under the caller's connection lease.
// Separate item identity reads are checked against the economy snapshot;
// the transaction writer must still revalidate all versions under DB locks.
public sealed class MerchantSalePreparationService
{
    private readonly ConnectionCommandLane _lane;
    private readonly WorldPresenceRegistry _presences;
    private readonly MerchantInteractionResolver _resolver;
    private readonly ICharacterEconomySnapshotRepository _economy;
    private readonly ICharacterInventoryItemIdentityRepository _items;

    public MerchantSalePreparationService(
        ConnectionCommandLane lane,
        WorldPresenceRegistry presences,
        MerchantInteractionResolver resolver,
        ICharacterEconomySnapshotRepository economy,
        ICharacterInventoryItemIdentityRepository items)
    {
        _lane = lane ?? throw new ArgumentNullException(nameof(lane));
        _presences = presences ?? throw new ArgumentNullException(nameof(presences));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public async ValueTask<MerchantSalePreparationResult> PrepareInLeaseAsync(
        ConnectionCommandLane.Lease lease,
        string clientBuildId,
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

        if (!_presences.TryGetByConnection(connectionId, out var captured) ||
            captured is null)
            return new(MerchantInteractionStatus.WorldPresenceMissing,
                null, "WorldPresenceMissing");

        var status = _resolver.Check(
            connectionId, interactionId, binding.MerchantId, binding);
        if (status != MerchantInteractionStatus.Allowed)
            return new(status, null, status.ToString());

        // Reject unsupported wire selections before repository reads.
        if (!OfficialMerchantSaleResultCodec.TryEncode(
            clientBuildId, selection, 0, out var probe, out var failure))
            return new(status, null, failure);
        Array.Clear(probe!);

        var characterId = captured.Character.CharacterId;
        var economy = await _economy.GetByCharacterAsync(
            characterId, cancellationToken);
        var identity = await _items.GetBySlotAsync(
            characterId, 0, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        status = _resolver.Check(
            connectionId, interactionId, binding.MerchantId, binding);
        if (status != MerchantInteractionStatus.Allowed)
            return new(status, null, status.ToString());

        if (!_presences.TryGetByConnection(connectionId, out var current) ||
            !ReferenceEquals(captured, current))
            return new(MerchantInteractionStatus.StateChanged,
                null, "StateChanged");

        MerchantSaleCommandFactory.TryCreate(
            clientBuildId, characterId, binding, selection,
            economy.Inventory, economy.Wallet, identity, transactionId,
            out var command, out failure);
        return new(status, command, failure);
    }
}
