using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbNpcSnapshotRepository :
    INpcSnapshotRepository
{
    private readonly string _connectionString;

    public MariaDbNpcSnapshotRepository(
        MariaDbAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connectionString = options.BuildConnectionString();
    }

    public async ValueTask<IReadOnlyList<NpcSnapshotEntry>> ListByMapAsync(
        long mapId,
        CancellationToken cancellationToken)
    {
        if (mapId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId));
        }

        await using var connection =
            new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                spawn.spawn_id,
                spawn.npc_id,
                npc.name_zh_tw,
                spawn.map_id,
                spawn.position_x,
                spawn.position_y,
                spawn.client_build_id,
                spawn.observed_client_entity_handle,
                spawn.official_resource_type,
                spawn.official_resource_ordinal,
                spawn.official_selector_high_bits,
                spawn.official_direction_code,
                spawn.official_state_code,
                spawn.spawn_message_sha256,
                spawn.opaque_template_sha256,
                spawn.wire_evidence_status,
                spawn.wire_evidence_reference
            FROM god2_game.npc_spawns AS spawn
            INNER JOIN god2_game.npcs AS npc
                ON npc.npc_id = spawn.npc_id
            INNER JOIN god2_game.maps AS map
                ON map.map_id = spawn.map_id
            WHERE spawn.map_id = @mapId
              AND spawn.enabled = 1
              AND npc.enabled = 1
              AND map.enabled = 1
            ORDER BY spawn.spawn_id;
            """;
        command.Parameters.AddWithValue("@mapId", mapId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var entries = new List<NpcSnapshotEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new NpcSnapshotEntry(
                reader.GetInt64("spawn_id"),
                reader.GetInt64("npc_id"),
                reader.GetString("name_zh_tw"),
                reader.GetInt64("map_id"),
                GetNullableInt32(reader, "position_x"),
                GetNullableInt32(reader, "position_y"),
                GetNullableString(reader, "client_build_id"),
                GetNullableUInt32(
                    reader,
                    "observed_client_entity_handle"),
                GetNullableByte(reader, "official_resource_type"),
                GetNullableByte(reader, "official_resource_ordinal"),
                GetNullableByte(
                    reader,
                    "official_selector_high_bits"),
                GetNullableByte(
                    reader,
                    "official_direction_code"),
                GetNullableByte(reader, "official_state_code"),
                GetNullableString(
                    reader,
                    "spawn_message_sha256"),
                GetNullableString(
                    reader,
                    "opaque_template_sha256"),
                reader.GetString("wire_evidence_status"),
                GetNullableString(
                    reader,
                    "wire_evidence_reference")));
        }

        return entries;
    }

    private static string? GetNullableString(
        MySqlDataReader reader,
        string name) =>
        reader.IsDBNull(reader.GetOrdinal(name))
            ? null
            : reader.GetString(name);

    private static int? GetNullableInt32(
        MySqlDataReader reader,
        string name) =>
        reader.IsDBNull(reader.GetOrdinal(name))
            ? null
            : reader.GetInt32(name);

    private static uint? GetNullableUInt32(
        MySqlDataReader reader,
        string name) =>
        reader.IsDBNull(reader.GetOrdinal(name))
            ? null
            : checked((uint)reader.GetUInt64(name));

    private static byte? GetNullableByte(
        MySqlDataReader reader,
        string name) =>
        reader.IsDBNull(reader.GetOrdinal(name))
            ? null
            : reader.GetByte(name);
}
