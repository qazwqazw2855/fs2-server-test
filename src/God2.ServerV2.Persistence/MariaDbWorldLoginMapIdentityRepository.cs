using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbWorldLoginMapIdentityRepository(
    MariaDbAuthenticationOptions options) : IWorldLoginMapIdentityRepository
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
        .BuildConnectionString();

    public async ValueTask<WorldLoginMapIdentity?> GetByMapAsync(
        long mapId, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT map_id, client_build_id, client_map_id, client_area_id,
                   minimum_x, maximum_x, minimum_y, maximum_y
            FROM god2_game.maps
            WHERE map_id = @mapId AND enabled = 1
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@mapId", mapId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        for (var column = 1; column < reader.FieldCount; column++)
            if (reader.IsDBNull(column))
                return null;
        return new WorldLoginMapIdentity(
            reader.GetInt64(0), reader.GetString(1),
            checked((ushort)reader.GetInt32(2)),
            checked((byte)reader.GetInt32(3)),
            reader.GetInt32(4), reader.GetInt32(5),
            reader.GetInt32(6), reader.GetInt32(7));
    }
}
