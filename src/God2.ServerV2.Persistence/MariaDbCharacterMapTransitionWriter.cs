using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbCharacterMapTransitionWriter :
    ICharacterMapTransitionWriter
{
    private readonly string _connectionString;

    public MariaDbCharacterMapTransitionWriter(
        MariaDbAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connectionString = options.BuildConnectionString();
    }

    public async ValueTask<CharacterMapTransitionWriteResult> TryUpdateAsync(
        CharacterMapTransitionWriteRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        return await ExecuteAsync(
            request,
            connection,
            transaction: null,
            cancellationToken);
    }

    public async ValueTask<CharacterMapTransitionWriteResult> TryUpdateAsync(
        CharacterMapTransitionWriteRequest request,
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        Validate(request);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        if (!ReferenceEquals(transaction.Connection, connection))
        {
            throw new ArgumentException(
                "Transaction does not belong to the supplied connection.",
                nameof(transaction));
        }

        return await ExecuteAsync(
            request,
            connection,
            transaction,
            cancellationToken);
    }

    private static async ValueTask<CharacterMapTransitionWriteResult>
        ExecuteAsync(
            CharacterMapTransitionWriteRequest request,
            MySqlConnection connection,
            MySqlTransaction? transaction,
            CancellationToken cancellationToken)
    {
        var nextVersion =
            checked(request.ExpectedRuntimeVersion + 1);

        var nextToken =
            Guid.NewGuid().ToString("N");

        await using var command =
            connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText = """
            UPDATE god2_player.characters
            SET map_id = @mapId,
                position_x = @positionX,
                position_y = @positionY,
                runtime_version = @nextVersion,
                concurrency_token = @nextToken,
                updated_at_utc = UTC_TIMESTAMP(6)
            WHERE character_id = @characterId
              AND runtime_version = @expectedVersion
              AND concurrency_token = @expectedToken
              AND enabled = 1
              AND deleted_at_utc IS NULL;
            """;

        command.Parameters.AddWithValue(
            "@mapId",
            request.MapId);

        command.Parameters.AddWithValue(
            "@positionX",
            request.PositionX);

        command.Parameters.AddWithValue(
            "@positionY",
            request.PositionY);

        command.Parameters.AddWithValue(
            "@nextVersion",
            nextVersion);

        command.Parameters.AddWithValue(
            "@nextToken",
            nextToken);

        command.Parameters.AddWithValue(
            "@characterId",
            request.CharacterId);

        command.Parameters.AddWithValue(
            "@expectedVersion",
            request.ExpectedRuntimeVersion);

        command.Parameters.AddWithValue(
            "@expectedToken",
            request.ExpectedConcurrencyToken);

        var affected =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        Console.WriteLine(
            $"[CharacterMapTransitionWriter] " +
            $"character={request.CharacterId}; " +
            $"map={request.MapId}; " +
            $"expectedVersion={request.ExpectedRuntimeVersion}; " +
            $"expectedToken={request.ExpectedConcurrencyToken}; " +
            $"nextVersion={nextVersion}; " +
            $"affected={affected}");

        return affected == 1
            ? CharacterMapTransitionWriteResult.Success(
                nextVersion,
                nextToken)
            : CharacterMapTransitionWriteResult.Conflict;
    }

    private static void Validate(
        CharacterMapTransitionWriteRequest request)
    {
        if (request.CharacterId <= 0 ||
            request.MapId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request));
        }

        if (request.PositionX is < 0 or > 0x7FFF ||
            request.PositionY is < 0 or > 0x7FFF)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request));
        }

        if (request.ExpectedRuntimeVersion < 0 ||
            request.ExpectedConcurrencyToken.Length != 32)
        {
            throw new ArgumentException(
                "Expected concurrency state is invalid.",
                nameof(request));
        }
    }
}
