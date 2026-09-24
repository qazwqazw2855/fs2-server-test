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
                   destination_map.client_build_id
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
        }

        Assert.Equal(
            new long[] { 1, 2, 3, 4, 170015007 },
            portalIds);
    }
}
