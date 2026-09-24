using God2.ServerV2.Persistence;
using God2.ServerV2.Protocol;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class EnabledPortalBuildIntegrationTests
{
    [Fact]
    public async Task EnabledFormalRoutes_UseCurrentClientBuildOnBothMaps()
    {
        if (Environment.GetEnvironmentVariable("GOD2_RUN_DB_INTEGRATION") != "1")
        {
            throw new InvalidOperationException(
                "DB integration test must be explicitly enabled.");
        }

        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {name}");

        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"),
            int.Parse(Required("GOD2_DB_PORT")),
            Required("GOD2_DB_USER"),
            Required("GOD2_DB_PASSWORD"));

        await using var connection =
            new MySqlConnection(options.BuildConnectionString());
        await connection.OpenAsync(CancellationToken.None);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.portal_id,
                   source_map.client_build_id,
                   destination_map.client_build_id,
                   p.destination_x,
                   p.destination_y,
                   destination_map.minimum_x,
                   destination_map.maximum_x,
                   destination_map.minimum_y,
                   destination_map.maximum_y
            FROM god2_game.portals AS p
            JOIN god2_game.maps AS source_map
              ON source_map.map_id = p.source_map_id
            JOIN god2_game.maps AS destination_map
              ON destination_map.map_id = p.destination_map_id
            WHERE p.enabled = 1
              AND source_map.enabled = 1
              AND destination_map.enabled = 1
            ORDER BY p.portal_id;
            """;

        var portalIds = new List<long>();
        await using var reader =
            await command.ExecuteReaderAsync(CancellationToken.None);

        while (await reader.ReadAsync(CancellationToken.None))
        {
            var portalId = reader.GetInt64(0);
            portalIds.Add(portalId);

            Assert.Equal(
                OfficialPortalWireCodec.ClientBuildId,
                reader.GetString(1));
            Assert.Equal(
                OfficialPortalWireCodec.ClientBuildId,
                reader.GetString(2));

            Assert.False(reader.IsDBNull(3) || reader.IsDBNull(4),
                $"Portal {portalId} has no destination position.");
            Assert.False(reader.IsDBNull(5) || reader.IsDBNull(6) ||
                         reader.IsDBNull(7) || reader.IsDBNull(8),
                $"Portal {portalId} has no destination bounds.");

            var x = reader.GetInt32(3);
            var y = reader.GetInt32(4);
            Assert.True(x >= reader.GetInt32(5) && x <= reader.GetInt32(6) &&
                        y >= reader.GetInt32(7) && y <= reader.GetInt32(8),
                $"Portal {portalId} destination ({x},{y}) is outside map bounds.");
        }

        Assert.Equal(
            new long[] { 1, 2, 3, 4, 170015007 },
            portalIds);
    }
}
