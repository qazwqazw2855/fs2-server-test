namespace God2.ServerV2.Application;

// Trusted server-side command. Do not accept grants directly from client packets.
public sealed record InventoryGrantRequest(
    Guid TransactionId,
    string IdempotencyKey,
    string SourceReference,
    long CharacterId,
    Guid InventoryId,
    long ExpectedVersion,
    long ExpectedMutationSequence,
    long ItemId,
    int Quantity);

public enum InventoryGrantStatus
{
    Granted,
    Replayed,
    IdempotencyConflict,
    CharacterMissing,
    InventoryMissing,
    VersionConflict,
    ItemMissing,
    ItemDisabled,
    InsufficientCapacity
}

public sealed record InventoryGrantResult(
    InventoryGrantStatus Status,
    Guid TransactionId,
    long VersionBefore,
    long VersionAfter)
{
    public bool Succeeded =>
        Status is InventoryGrantStatus.Granted or InventoryGrantStatus.Replayed;
}

public interface IInventoryGrantWriter
{
    ValueTask<InventoryGrantResult> GrantAsync(
        InventoryGrantRequest request,
        CancellationToken cancellationToken);
}
