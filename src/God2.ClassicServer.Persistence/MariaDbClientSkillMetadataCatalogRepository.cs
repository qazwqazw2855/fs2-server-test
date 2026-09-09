using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbClientSkillMetadataCatalogRepository : MariaDbRuntimeRepository
{
    public MariaDbClientSkillMetadataCatalogRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<ClientSkillMetadataCatalog> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var records = new List<ClientSkillMetadata>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                SELECT `official_client_item_id`, `official_display_id`, `name_zh_tw`,
                       `skill_mode_zh_tw`, `skill_category_zh_tw`, `skill_tier`, `mp_cost`,
                       `attack_range`, `target_scope_zh_tw`, `target_shape`,
                       `official_effect_text_zh_tw`, `live_verified`
                FROM `god2_game`.`skill_client_metadata`
                ORDER BY `official_client_item_id`;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var liveVerified = reader.GetBoolean("live_verified");
                var runtimeEvidenceStatus = liveVerified ? "OfficialClientLiveVerified" : "FormalRuntimeCatalog";
                records.Add(new ClientSkillMetadata(
                    reader.GetInt32("official_client_item_id"),
                    NullableInt(reader, "official_display_id"),
                    reader.GetString("name_zh_tw"),
                    NullableString(reader, "skill_mode_zh_tw"),
                    NullableString(reader, "skill_category_zh_tw"),
                    NullableInt(reader, "skill_tier"),
                    NullableInt(reader, "mp_cost"),
                    NullableInt(reader, "attack_range"),
                    NullableString(reader, "target_scope_zh_tw"),
                    ParseTargetShape(reader.GetString("target_shape")),
                    runtimeEvidenceStatus,
                    NullableString(reader, "official_effect_text_zh_tw"),
                    runtimeEvidenceStatus,
                    liveVerified));
            }
        }

        var mappings = new List<(long SkillId, int OfficialClientItemId)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                SELECT `skill_id`, `official_client_item_id`
                FROM `god2_game`.`skill_client_metadata_mappings`
                ORDER BY `skill_id`, `official_client_item_id`;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                mappings.Add((reader.GetInt64("skill_id"), reader.GetInt32("official_client_item_id")));
            }
        }

        return new ClientSkillMetadataCatalog(records, mappings);
    }

    private static int? NullableInt(MySqlConnector.MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt32(name);

    private static string? NullableString(MySqlConnector.MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(name);

    private static ClientSkillTargetShape ParseTargetShape(string value) =>
        value switch
        {
            "未確認" => ClientSkillTargetShape.Unknown,
            "單一目標" => ClientSkillTargetShape.SingleTarget,
            _ when Enum.TryParse<ClientSkillTargetShape>(value, ignoreCase: false, out var parsed) &&
                   Enum.IsDefined(parsed) => parsed,
            _ => throw new InvalidDataException($"Unknown client skill target shape '{value}'.")
        };
}