namespace God2.ServerV2.Application;

// Inventory and wallet represent one database read snapshot.
// Missing components remain explicit; callers must reject incomplete authority.
public sealed record CharacterEconomySnapshot(
    CharacterInventorySnapshot? Inventory,
    CharacterWalletSnapshot? Wallet);

public interface ICharacterEconomySnapshotRepository
{
    ValueTask<CharacterEconomySnapshot> GetByCharacterAsync(
        long characterId,
        CancellationToken cancellationToken);
}
