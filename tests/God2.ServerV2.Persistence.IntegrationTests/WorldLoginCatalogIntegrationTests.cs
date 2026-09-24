using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class WorldLoginCatalogIntegrationTests
{
    [Fact]
    public async Task EveryEnabledCurrentBuildMap_IsReadableByWorldLoginRepository()
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

        const string clientBuildId = "god2-opt-6b127086e0c0";
        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"),
            int.Parse(Required("GOD2_DB_PORT")),
            Required("GOD2_DB_USER"),
            Required("GOD2_DB_PASSWORD"));

        var mapIds = new List<long>();

        await using (var connection =
            new MySqlConnection(options.BuildConnectionString()))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT map_id
                FROM god2_game.maps
                WHERE client_build_id = @clientBuildId
                  AND enabled = 1
                ORDER BY map_id;
                """;
            command.Parameters.AddWithValue("@clientBuildId", clientBuildId);

            await using var reader =
                await command.ExecuteReaderAsync(CancellationToken.None);

            while (await reader.ReadAsync(CancellationToken.None))
            {
                mapIds.Add(reader.GetInt64(0));
            }
        }

        Assert.Equal(144, mapIds.Count);
        Assert.Equal(mapIds.Count, mapIds.Distinct().Count());

        var repository =
            new MariaDbWorldLoginMapIdentityRepository(options);

        foreach (var mapId in mapIds)
        {
            var identity = await repository.GetByMapAsync(
                mapId, CancellationToken.None);

            Assert.NotNull(identity);
            Assert.Equal(mapId, identity.MapId);
            Assert.Equal(clientBuildId, identity.ClientBuildId);
            Assert.True(
                identity.MinimumX <= identity.MaximumX &&
                identity.MinimumY <= identity.MaximumY,
                $"Map {mapId} has invalid World Login bounds.");
            Assert.True(identity.Contains(
                identity.MinimumX, identity.MinimumY));
            Assert.True(identity.Contains(
                identity.MaximumX, identity.MaximumY));
        }

        Assert.Null(await repository.GetByMapAsync(
            1200070008, CancellationToken.None));
    }
}
