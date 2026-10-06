using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network;

public sealed record MerchantPurchaseExecutionResult(
    MerchantInteractionStatus InteractionStatus,
    MerchantPurchaseResult? TransactionResult,
    byte[]? EncodedResponse,
    string FailureCode)
{
    public bool RequiresReconciliation =>
        TransactionResult?.Succeeded == true && EncodedResponse is null;
}

// Internal orchestration, not merchant evidence approval.
// The injected command service's writer retains its independent evidence gate.
// Caller owns the lease through execution and ordered response delivery.
// Exceptions propagate: preserve the original command for reconciliation.
// Never rebuild its identity or automatically buy again after uncertainty.
public sealed class MerchantPurchaseExecutionService
{
    private readonly ConnectionCommandLane _lane;
    private readonly WorldPresenceRegistry _presences;
    private readonly MerchantInteractionResolver _resolver;
    private readonly MerchantCommandService _commands;
    private readonly ICharacterInventorySnapshotRepository _inventory;

    public MerchantPurchaseExecutionService(
        ConnectionCommandLane lane,
        WorldPresenceRegistry presences,
        MerchantInteractionResolver resolver,
        MerchantCommandService commands,
        ICharacterInventorySnapshotRepository inventory)
    {
        _lane = lane ?? throw new ArgumentNullException(nameof(lane));
        _presences = presences ?? throw new ArgumentNullException(nameof(presences));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    public async ValueTask<MerchantPurchaseExecutionResult> ExecuteInLeaseAsync(
        ConnectionCommandLane.Lease lease,
        long connectionId,
        Guid interactionId,
        MerchantInteractionBinding binding,
        OfficialMerchantTransactionSelection selection,
        MerchantPurchaseCommand command,
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
        var before = await _inventory.GetByCharacterAsync(
            characterId, cancellationToken);

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
        if (before is null)
            return new(status, null, null, "InventoryMissing");
        if (before.CharacterId != characterId)
            return new(status, null, null, "InventoryCharacterMismatch");
        if (selection.ClientEntityHandle != binding.ClientEntityHandle)
            return new(status, null, null, "BindingMismatch");
        if (!MerchantPurchaseResponsePolicy.ValidateBeforeExecution(
                binding.ClientBuildId, selection, command, before, out var failure))
            return new(status, null, null, failure);

        var execution = await _commands.PurchaseInLeaseAsync(
            lease, connectionId, interactionId, binding, command, cancellationToken);
        var transaction = execution.TransactionResult;
        if (transaction is null)
            return new(execution.InteractionStatus, null, null, "InteractionBlocked");
        if (!transaction.Succeeded)
            return new(execution.InteractionStatus, transaction, null,
                transaction.Status.ToString());

        // A replay receipt may be older than the current client state.
        if (transaction.Status == MerchantPurchaseStatus.Replayed)
            return new(execution.InteractionStatus, transaction, null,
                "ReplayRequiresReconciliation");

        var after = await _inventory.GetByCharacterAsync(
            characterId, cancellationToken);
        lease.ValidateOwner(_lane);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_presences.TryGetByConnection(connectionId, out current) ||
            !ReferenceEquals(captured, current))
            return new(MerchantInteractionStatus.StateChanged,
                transaction, null, "CommittedStateChanged");
        if (after is null)
            return new(execution.InteractionStatus, transaction, null,
                "CommittedInventoryMissing");

        MerchantPurchaseResponsePolicy.TryEncodeCommitted(
            binding.ClientBuildId, selection, command, before, after,
            transaction, out var response, out failure);
        return new(execution.InteractionStatus, transaction, response, failure);
    }
}
