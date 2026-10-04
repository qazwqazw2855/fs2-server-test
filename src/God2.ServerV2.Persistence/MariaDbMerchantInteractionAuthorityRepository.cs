using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbMerchantInteractionAuthorityRepository(
    MariaDbAuthenticationOptions options) :
    IMerchantInteractionAuthorityRepository
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
            .BuildConnectionString();

    public async ValueTask<MerchantInteractionAuthority?> ResolveAsync(
        long spawnId,
        long mapId,
        string clientBuildId,
        uint clientEntityHandle,
        CancellationToken cancellationToken)
    {
        if (spawnId <= 0)
            throw new ArgumentOutOfRangeException(nameof(spawnId));
        if (mapId <= 0)
            throw new ArgumentOutOfRangeException(nameof(mapId));
        if (string.IsNullOrWhiteSpace(clientBuildId))
            throw new ArgumentException(
                "Client build id is required.", nameof(clientBuildId));
        if (clientEntityHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(clientEntityHandle));

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 15;
        command.CommandText = """
            SELECT
                merchant_row.merchant_id,
                npc_row.npc_id,
                spawn_row.spawn_id,
                spawn_row.map_id,
                spawn_row.observed_client_entity_handle,
                spawn_row.client_build_id,
                spawn_row.wire_evidence_reference
            FROM god2_game.merchants merchant_row
            JOIN god2_game.npcs npc_row
              ON npc_row.npc_id = merchant_row.npc_id
             AND npc_row.merchant_id = merchant_row.merchant_id
            JOIN god2_game.npc_spawns spawn_row
              ON spawn_row.npc_id = npc_row.npc_id
            JOIN god2_game.maps map_row
              ON map_row.map_id = spawn_row.map_id
            WHERE spawn_row.spawn_id = @spawn
              AND spawn_row.map_id = @map
              AND spawn_row.client_build_id = @build
              AND spawn_row.observed_client_entity_handle = @handle
              AND merchant_row.enabled = 1
              AND npc_row.enabled = 1
              AND spawn_row.enabled = 1
              AND map_row.enabled = 1
            LIMIT 2;
            """;

        command.Parameters.AddWithValue("@spawn", spawnId);
        command.Parameters.AddWithValue("@map", mapId);
        command.Parameters.AddWithValue("@build", clientBuildId);
        command.Parameters.AddWithValue("@handle", clientEntityHandle);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var evidenceReference = reader.IsDBNull(6)
            ? string.Empty
            : reader.GetString(6);

        var result = new MerchantInteractionAuthority(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            checked((uint)reader.GetUInt64(4)),
            reader.GetString(5),
            evidenceReference,
            MaximumDistance: null,
            Enabled: false);

        // Fail closed if formal identity is ambiguous.
        if (await reader.ReadAsync(cancellationToken))
            return null;

        return result;
    }
}
