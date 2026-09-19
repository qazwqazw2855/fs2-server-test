using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbPortalRouteRepository :
    IPortalRouteRepository
{
    private readonly string _connectionString;

    public MariaDbPortalRouteRepository(
        MariaDbAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connectionString = options.BuildConnectionString();
    }

    public async ValueTask<IReadOnlyList<PortalRouteEntry>> ListBySourceMapAsync(
        long sourceMapId,
        CancellationToken cancellationToken)
    {
        if (sourceMapId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceMapId));
        }

        await using var connection =
            new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                portal.portal_id,
                portal.name_zh_tw,
                portal.source_map_id,
                portal.source_x,
                portal.source_y,
                portal.source_radius,
                source_map.client_build_id AS source_client_build_id,
                source_map.client_map_id AS source_client_map_id,
                source_map.client_area_id AS source_client_area_id,
                portal.destination_map_id,
                portal.destination_x,
                portal.destination_y,
                destination_map.client_build_id AS destination_client_build_id,
                destination_map.client_map_id AS destination_client_map_id,
                destination_map.client_area_id AS destination_client_area_id
            FROM god2_game.portals AS portal
            INNER JOIN god2_game.maps AS source_map
                ON source_map.map_id = portal.source_map_id
            INNER JOIN god2_game.maps AS destination_map
                ON destination_map.map_id = portal.destination_map_id
            WHERE portal.source_map_id = @sourceMapId
              AND portal.enabled = 1
              AND source_map.enabled = 1
              AND destination_map.enabled = 1
              AND portal.source_x IS NOT NULL
              AND portal.source_y IS NOT NULL
              AND portal.source_radius IS NOT NULL
              AND portal.destination_x IS NOT NULL
              AND portal.destination_y IS NOT NULL
              AND source_map.client_build_id IS NOT NULL
              AND source_map.client_map_id IS NOT NULL
              AND source_map.client_area_id IS NOT NULL
              AND destination_map.client_build_id IS NOT NULL
              AND destination_map.client_map_id IS NOT NULL
              AND destination_map.client_area_id IS NOT NULL
            ORDER BY portal.portal_id;
            """;
        command.Parameters.AddWithValue("@sourceMapId", sourceMapId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var entries = new List<PortalRouteEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new PortalRouteEntry(
                reader.GetInt64("portal_id"),
                reader.GetString("name_zh_tw"),
                reader.GetInt64("source_map_id"),
                reader.GetInt32("source_x"),
                reader.GetInt32("source_y"),
                reader.GetInt32("source_radius"),
                reader.GetString("source_client_build_id"),
                checked((ushort)reader.GetInt32("source_client_map_id")),
                checked((byte)reader.GetInt32("source_client_area_id")),
                reader.GetInt64("destination_map_id"),
                reader.GetInt32("destination_x"),
                reader.GetInt32("destination_y"),
                reader.GetString("destination_client_build_id"),
                checked((ushort)reader.GetInt32("destination_client_map_id")),
                checked((byte)reader.GetInt32("destination_client_area_id"))));
        }

        return entries;
    }
}
