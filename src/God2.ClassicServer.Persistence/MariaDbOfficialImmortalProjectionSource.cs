using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbOfficialImmortalProjectionSource :
    MariaDbRuntimeRepository,
    ICharacterImmortalProjectionSource
{
    public MariaDbOfficialImmortalProjectionSource(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<OperationResult<IReadOnlyList<OfficialOwnedImmortalWireState>>> LoadAsync(
        long characterId,
        CancellationToken cancellationToken)
    {
        if (characterId <= 0)
        {
            return OperationResult<IReadOnlyList<OfficialOwnedImmortalWireState>>.Failure(
                "immortal_projection.character_invalid",
                "Owned immortal projection requires a positive character identity.",
                characterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                SELECT owned.`immortal_instance_id`, template.`resource_id`,
                       COALESCE(owned.`level`,template.`initial_level`,baseline.`level`) AS `level`,
                       owned.`current_hp`, owned.`max_hp`, owned.`current_mp`, owned.`max_mp`,
                       COALESCE(owned.`strength_base`,baseline.`strength`) + COALESCE(owned.`strength_bonus`,0) AS `strength`,
                       COALESCE(owned.`constitution_base`,baseline.`constitution`) + COALESCE(owned.`constitution_bonus`,0) AS `constitution`,
                       COALESCE(owned.`intelligence_base`,baseline.`intelligence`) + COALESCE(owned.`intelligence_bonus`,0) AS `intelligence`,
                       COALESCE(owned.`speed_base`,baseline.`speed`) + COALESCE(owned.`speed_bonus`,0) AS `speed`,
                       COALESCE(owned.`metal_base`,baseline.`metal`) + COALESCE(owned.`metal_bonus`,0) AS `metal`,
                       COALESCE(owned.`wood_base`,baseline.`wood`) + COALESCE(owned.`wood_bonus`,0) AS `wood`,
                       COALESCE(owned.`water_base`,baseline.`water`) + COALESCE(owned.`water_bonus`,0) AS `water`,
                       COALESCE(owned.`fire_base`,baseline.`fire`) + COALESCE(owned.`fire_bonus`,0) AS `fire`,
                       COALESCE(owned.`earth_base`,baseline.`earth`) + COALESCE(owned.`earth_bonus`,0) AS `earth`,
                       owned.`is_active`
                FROM `god2_player`.`character_immortals` owned
                JOIN `god2_game`.`immortal_templates` template
                  ON template.`immortal_template_id`=owned.`immortal_template_id` AND template.`enabled`=1
                LEFT JOIN `god2_game`.`immortal_base_stats` baseline
                  ON baseline.`immortal_template_id`=template.`immortal_template_id` AND baseline.`enabled`=1
                WHERE owned.`owner_character_id`=@characterId AND owned.`enabled`=1
                ORDER BY owned.`immortal_instance_id`;
                """;
            command.Parameters.AddWithValue("@characterId", characterId);

            var rows = new List<OfficialOwnedImmortalWireState>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                for (var ordinal = 0; ordinal <= 15; ordinal++)
                {
                    if (reader.IsDBNull(ordinal))
                    {
                        return OperationResult<IReadOnlyList<OfficialOwnedImmortalWireState>>.Failure(
                            "immortal_projection.incomplete_row",
                            "An enabled owned immortal is missing its official resource identity, level, vitals, or one of the nine proven attributes.",
                            reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }

                rows.Add(new OfficialOwnedImmortalWireState(
                    reader.GetInt64(0),
                    checked(Convert.ToByte(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture)),
                    checked(Convert.ToByte(reader.GetValue(2), System.Globalization.CultureInfo.InvariantCulture)),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    ReadUInt16(reader, 7),
                    ReadUInt16(reader, 8),
                    ReadUInt16(reader, 9),
                    ReadUInt16(reader, 10),
                    ReadUInt16(reader, 11),
                    ReadUInt16(reader, 12),
                    ReadUInt16(reader, 13),
                    ReadUInt16(reader, 14),
                    ReadUInt16(reader, 15),
                    reader.GetBoolean(16)));
            }

            IReadOnlyList<OfficialOwnedImmortalWireState> result = rows.AsReadOnly();
            var validation = OfficialImmortalReplicationWireCodec.Validate(result);
            return validation is null
                ? OperationResult<IReadOnlyList<OfficialOwnedImmortalWireState>>.Success(result)
                : OperationResult<IReadOnlyList<OfficialOwnedImmortalWireState>>.Failure(
                    "immortal_projection.invalid_rows",
                    validation,
                    characterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or OverflowException)
        {
            return OperationResult<IReadOnlyList<OfficialOwnedImmortalWireState>>.Failure(
                "immortal_projection.load_failed",
                exception.Message,
                nameof(MariaDbOfficialImmortalProjectionSource));
        }
    }

    private static ushort ReadUInt16(MySqlDataReader reader, int ordinal) =>
        checked(Convert.ToUInt16(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture));
}
