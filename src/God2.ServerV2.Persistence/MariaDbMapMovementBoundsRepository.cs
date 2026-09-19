using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbMapMovementBoundsRepository(
    MariaDbAuthenticationOptions options)
    : IMapMovementBoundsRepository
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
            .BuildConnectionString();

    public async ValueTask<MapMovementBounds?> GetByMapAsync(
        long mapId,
        CancellationToken cancellationToken)
    {
        if (mapId <= 0)
            throw new ArgumentOutOfRangeException(nameof(mapId));

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT map_id, minimum_x, maximum_x,
                   minimum_y, maximum_y
            FROM god2_game.maps
            WHERE map_id = @mapId AND enabled = 1
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@mapId", mapId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        if (reader.IsDBNull(1) || reader.IsDBNull(2) ||
            reader.IsDBNull(3) || reader.IsDBNull(4))
            throw new InvalidDataException(
                $"Map movement bounds are missing: map={mapId}");

        var bounds = new MapMovementBounds(
            reader.GetInt64(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4));

        if (bounds.MinimumX > bounds.MaximumX ||
            bounds.MinimumY > bounds.MaximumY)
            throw new InvalidDataException(
                $"Map movement bounds are reversed: map={mapId}");

        return bounds;
    }
}
