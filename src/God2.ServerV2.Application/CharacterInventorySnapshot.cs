namespace God2.ServerV2.Application;

public sealed record CharacterInventorySlot(
    int SlotIndex,
    long ItemId,
    int Quantity,
    string BindState = "Unknown",
    string ItemInstanceMetadata = "{}");

public sealed record CharacterInventorySnapshot(
    Guid InventoryId,
    long CharacterId,
    int Capacity,
    long Version,
    long MutationSequence,
    string DirtyState,
    IReadOnlyList<CharacterInventorySlot> Slots);

public interface ICharacterInventorySnapshotRepository
{
    ValueTask<CharacterInventorySnapshot?> GetByCharacterAsync(
        long characterId,
        CancellationToken cancellationToken);
}
