using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbCharacterWalletSnapshotRepository(
    MariaDbAuthenticationOptions options) : ICharacterWalletSnapshotRepository
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
            .BuildConnectionString();

    public async ValueTask<CharacterWalletSnapshot?> GetGoldByCharacterAsync(
        long characterId,
        CancellationToken cancellationToken)
    {
        if (characterId <= 0)
            throw new ArgumentOutOfRangeException(nameof(characterId));

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await ReadInTransactionAsync(
            connection, null, characterId, cancellationToken);
    }

    internal static async ValueTask<CharacterWalletSnapshot?> ReadInTransactionAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        long characterId,
        CancellationToken cancellationToken)
    {
        if (characterId <= 0)
            throw new ArgumentOutOfRangeException(nameof(characterId));

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 15;
        command.CommandText = """
            SELECT CharacterId, CurrencyType, Balance, Version
            FROM god2_player.player_currency_balances
            WHERE CharacterId = @character
              AND CurrencyType = 'Gold'
            LIMIT 2;
            """;

        command.Parameters.AddWithValue("@character", characterId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var result = new CharacterWalletSnapshot(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt64(3));

        // Fail closed if authority is ambiguous.
        if (await reader.ReadAsync(cancellationToken))
            return null;

        return result;
    }
}
