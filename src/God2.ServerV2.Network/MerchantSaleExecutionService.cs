using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network;

public sealed record MerchantSaleExecutionResult(
    MerchantInteractionStatus InteractionStatus,
    MerchantSaleResult? TransactionResult,
    byte[]? EncodedResponse,
    string FailureCode)
{
    public bool RequiresReconciliation =>
        TransactionResult?.Succeeded == true && EncodedResponse is null;
}

// Caller holds the lease through dispatch and ordered response delivery.
// Writer retains its independent evidence gate and DB version checks.
// Exceptions propagate; preserve the original command, never retry automatically.
public sealed class MerchantSaleExecutionService
{
    private readonly ConnectionCommandLane _lane;
    private readonly WorldPresenceRegistry _presences;
    private readonly MerchantInteractionResolver _resolver;
    private readonly MerchantCommandService _commands;
    private readonly ICharacterEconomySnapshotRepository _economy;
    private readonly ICharacterInventoryItemIdentityRepository _items;

    public MerchantSaleExecutionService(
        ConnectionCommandLane lane,
        WorldPresenceRegistry presences,
        MerchantInteractionResolver resolver,
        MerchantCommandService commands,
        ICharacterEconomySnapshotRepository economy,
        ICharacterInventoryItemIdentityRepository items)
    {
        _lane = lane ?? throw new ArgumentNullException(nameof(lane));
        _presences = presences ?? throw new ArgumentNullException(nameof(presences));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public async ValueTask<MerchantSaleExecutionResult> ExecuteInLeaseAsync(
        ConnectionCommandLane.Lease lease,
        long connectionId,
        Guid interactionId,
        MerchantInteractionBinding binding,
        OfficialMerchantTransactionSelection selection,
        MerchantSaleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(command);
        lease.ValidateOwner(_lane);
        cancellationToken.ThrowIfCancellationRequested();

        var status = _resolver.Check(
            connectionId, interactionId, binding.MerchantId, binding);
        if (status != MerchantInteractionStatus.Allowed)
            return new(status, null, null, "InteractionBlocked");

        if (!_presences.TryGetByConnection(connectionId, out var captured) ||
            captured is null)
            return new(MerchantInteractionStatus.WorldPresenceMissing,
                null, null, "WorldPresenceMissing");

        var characterId = captured.Character.CharacterId;
        var before = await _economy.GetByCharacterAsync(
            characterId, cancellationToken);
        var identity = await _items.GetBySlotAsync(
            characterId, 0, cancellationToken);

        lease.ValidateOwner(_lane);
        cancellationToken.ThrowIfCancellationRequested();
        status = _resolver.Check(
            connectionId, interactionId, binding.MerchantId, binding);
        if (status != MerchantInteractionStatus.Allowed)
            return new(status, null, null, "InteractionBlocked");
        if (!_presences.TryGetByConnection(connectionId, out var current) ||
            !ReferenceEquals(captured, current))
            return new(MerchantInteractionStatus.StateChanged,
                null, null, "StateChanged");

        if (!MerchantSaleResponsePolicy.ValidateBeforeExecution(
            binding, characterId, selection, command, before, identity,
            out var failure))
            return new(status, null, null, failure);

        var execution = await _commands.SellInLeaseAsync(
            lease, connectionId, interactionId, binding, command,
            cancellationToken);
        var transaction = execution.TransactionResult;
        if (transaction is null)
            return new(execution.InteractionStatus, null, null,
                "InteractionBlocked");
        if (!transaction.Succeeded)
            return new(execution.InteractionStatus, transaction, null,
                transaction.Status.ToString());
        if (transaction.Status == MerchantSaleStatus.Replayed)
            return new(execution.InteractionStatus, transaction, null,
                "ReplayRequiresReconciliation");

        var after = await _economy.GetByCharacterAsync(
            characterId, cancellationToken);
        lease.ValidateOwner(_lane);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_presences.TryGetByConnection(connectionId, out current) ||
            !ReferenceEquals(captured, current))
            return new(MerchantInteractionStatus.StateChanged,
                transaction, null, "CommittedStateChanged");

        MerchantSaleResponsePolicy.TryEncodeCommitted(
            binding, characterId, selection, command, before, identity,
            after, transaction, out var response, out failure);
        return new(execution.InteractionStatus, transaction, response, failure);
    }
}
