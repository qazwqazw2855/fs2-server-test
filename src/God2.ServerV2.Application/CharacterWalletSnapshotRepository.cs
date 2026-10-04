namespace God2.ServerV2.Application;

public interface ICharacterWalletSnapshotRepository
{
    ValueTask<CharacterWalletSnapshot?> GetGoldByCharacterAsync(
        long characterId,
        CancellationToken cancellationToken);
}
