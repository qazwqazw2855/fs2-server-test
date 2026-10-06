using System.Data;
using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbCharacterEconomySnapshotRepository(
    MariaDbAuthenticationOptions options) : ICharacterEconomySnapshotRepository
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
            .BuildConnectionString();

    public async ValueTask<CharacterEconomySnapshot> GetByCharacterAsync(
        long characterId,
        CancellationToken cancellationToken)
    {
        return await ReadAsync(characterId, null, cancellationToken);
    }

    // Internal scheduling seam for deterministic concurrent-commit tests.
    internal async ValueTask<CharacterEconomySnapshot> ReadAsync(
        long characterId,
        Func<CancellationToken, ValueTask>? afterInventoryRead,
        CancellationToken cancellationToken)
    {
        if (characterId <= 0)
            throw new ArgumentOutOfRangeException(nameof(characterId));

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);

        // Ordinary SELECTs share the InnoDB consistent read snapshot.
        // Do not replace either read with a locking/current read.
        var inventory =
            await MariaDbCharacterInventorySnapshotRepository.ReadInTransactionAsync(
                connection, transaction, characterId, cancellationToken);
        if (afterInventoryRead is not null)
            await afterInventoryRead(cancellationToken);

        var wallet =
            await MariaDbCharacterWalletSnapshotRepository.ReadInTransactionAsync(
                connection, transaction, characterId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new CharacterEconomySnapshot(inventory, wallet);
    }
}
