namespace God2.ServerV2.Application;

// Server-owned persistent identity; never populated from a client item handle.
public sealed record CharacterInventoryItemIdentity(
    long CharacterId,
    long ItemInstanceId,
    int SlotIndex,
    long ItemId,
    int Quantity,
    long SlotVersion,
    string BindState,
    bool? Bound,
    string ItemInstanceMetadata);

public interface ICharacterInventoryItemIdentityRepository
{
    ValueTask<CharacterInventoryItemIdentity?> GetBySlotAsync(
        long characterId,
        int authoritySlotIndex,
        CancellationToken cancellationToken);
}
