using System.Text;

namespace God2.GameplayContentRecovery;

public static class OfficialMapMigrationWriter
{
    public static async Task<OfficialMapMigrationResult> WriteAsync(
        string clientRoot,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(clientRoot);
        var gameDataPath = Path.Combine(root, "Data2", "Patch", "Comm", "gamedata.csvZ");
        var gameData = await God2PackedFile.ReadCsvZAsync(gameDataPath, cancellationToken);
        var sections = GameDataSections.Parse(gameData.Rows);
        var definitions = OfficialClientExtractor.ParseOfficialMapDefinitions(sections["Map_City_Coordniate"]);
        var inventory = await ClientMapResourceInventory.BuildAsync(root, cancellationToken);
        var resources = inventory.Resources.ToDictionary(resource => resource.AuthorityKey, StringComparer.OrdinalIgnoreCase);
        var localization = new ZhTwLocalization(God2Glossary.Create());
        var rows = definitions.Select(definition =>
        {
            resources.TryGetValue(definition.ResourceAuthorityKey, out var resource);
            var enabled = resource?.GridWidth is > 0 && resource.GridHeight is > 0;
            return new PublishedOfficialMap(
                ResolveCanonicalMapId(definition.ClientAreaId, definition.ClientMapId),
                definition,
                localization.Convert(definition.DisplayName, gameData.SourceHash).ConvertedText,
                resource,
                enabled);
        }).ToArray();

        var sql = BuildSql(rows, gameData.SourceHash);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await File.WriteAllTextAsync(outputPath, sql, new UTF8Encoding(false), cancellationToken);
        return new OfficialMapMigrationResult(
            definitions.Count,
            rows.Count(row => row.Resource is not null),
            rows.Count(row => row.Enabled),
            rows.Count(row => !row.Enabled),
            gameData.SourceHash,
            Path.GetFullPath(outputPath));
    }

    internal static int ResolveCanonicalMapId(int clientAreaId, int clientMapId) => (clientAreaId, clientMapId) switch
    {
        (2, 0) => 130139698,
        (2, 43) => 192354557,
        (4, 3) => 1675308248,
        (15, 0) => 170015000,
        (15, 7) => 170015007,
        _ => checked(1_200_000_000 + clientAreaId * 10_000 + clientMapId)
    };

    private static string BuildSql(IReadOnlyList<PublishedOfficialMap> rows, string sourceHash)
    {
        var sql = new StringBuilder();
        sql.AppendLine("-- Generated from official Data2/Patch/Comm/gamedata.csvZ Map_City_Coordniate (144 rows).");
        sql.AppendLine("-- Map identity/world-map UI coordinate is official decoded data; coordinate bounds come from client navigation resources.");
        sql.AppendLine("ALTER TABLE `god2_game`.`maps`");
        sql.AppendLine("    ADD COLUMN IF NOT EXISTS `world_map_x` int NULL COMMENT 'Official world-map UI X; not a gameplay spawn',");
        sql.AppendLine("    ADD COLUMN IF NOT EXISTS `world_map_y` int NULL COMMENT 'Official world-map UI Y; not a gameplay spawn',");
        sql.AppendLine("    ADD UNIQUE KEY IF NOT EXISTS `ux_maps_client_identity` (`client_build_id`,`client_area_id`,`client_map_id`);");
        sql.AppendLine();
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `god2_game`.`client_map_resource_identities` (");
        sql.AppendLine("    `map_identity_id` bigint NOT NULL,");
        sql.AppendLine("    `client_build_id` varchar(64) NOT NULL,");
        sql.AppendLine("    `client_area_id` int NOT NULL,");
        sql.AppendLine("    `client_map_id` int NOT NULL,");
        sql.AppendLine("    `map_id` bigint NOT NULL,");
        sql.AppendLine("    `resource_key` varchar(512) NOT NULL,");
        sql.AppendLine("    `world_map_x` int NOT NULL,");
        sql.AppendLine("    `world_map_y` int NOT NULL,");
        sql.AppendLine("    `enabled` tinyint(1) NOT NULL DEFAULT 0,");
        sql.AppendLine("    PRIMARY KEY (`map_identity_id`),");
        sql.AppendLine("    UNIQUE KEY `ux_client_map_resource_identity` (`client_build_id`,`client_area_id`,`client_map_id`),");
        sql.AppendLine("    KEY `ix_client_map_resource_identity_resource` (`resource_key`),");
        sql.AppendLine("    CONSTRAINT `fk_client_map_resource_identity_map` FOREIGN KEY (`map_id`) REFERENCES `god2_game`.`maps` (`map_id`),");
        sql.AppendLine("    CONSTRAINT `fk_client_map_resource_identity_resource` FOREIGN KEY (`resource_key`) REFERENCES `god2_game`.`client_map_resources` (`resource_key`),");
        sql.AppendLine("    CONSTRAINT `ck_client_map_resource_identity_enabled` CHECK (`enabled` IN (0,1))");
        sql.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        sql.AppendLine();
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `god2_research`.`client_map_resource_identity_evidence` (");
        sql.AppendLine("    `map_identity_id` bigint NOT NULL,");
        sql.AppendLine("    `source_row` int NOT NULL,");
        sql.AppendLine("    `source_sha256` char(64) NOT NULL,");
        sql.AppendLine("    `identity_evidence_status` varchar(30) NOT NULL,");
        sql.AppendLine("    `coordinate_evidence_status` varchar(30) NOT NULL,");
        sql.AppendLine("    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),");
        sql.AppendLine("    PRIMARY KEY (`map_identity_id`)");
        sql.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `god2_game`.`maps`");
        sql.AppendLine("    (`map_id`,`code`,`name_zh_tw`,`resource_identity`,`width`,`height`,");
        sql.AppendLine("     `minimum_x`,`maximum_x`,`minimum_y`,`maximum_y`,`world_map_x`,`world_map_y`,");
        sql.AppendLine("     `allow_teleport`,`allow_escape`,`allow_resurrection`,`allow_mount`,`allow_pet`,");
        sql.AppendLine("     `client_build_id`,`client_map_id`,`client_area_id`,`coordinate_scale_x`,`coordinate_scale_y`,");
        sql.AppendLine("     `coordinate_offset_x`,`coordinate_offset_y`,`identity_evidence_status`,`coordinate_evidence_status`,`enabled`,`admin_note`)");
        sql.AppendLine("VALUES");
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var resource = row.Resource;
            var width = resource?.GridWidth;
            var height = resource?.GridHeight;
            var identity = resource?.ResourceName ?? row.Definition.ResourcePath;
            var code = $"official_a{row.Definition.ClientAreaId}_m{row.Definition.ClientMapId}";
            var note = row.Enabled
                ? $"Official gamedata row {row.Definition.SourceRow}; navigation dimensions from {resource!.NavigationFormat}."
                : $"EvidenceBlocked: official gamedata row {row.Definition.SourceRow}; navigation dimensions unavailable.";
            sql.Append("    (")
                .Append(row.MapId).Append(',').Append(Q(code)).Append(',').Append(Q(row.NameZhTw)).Append(',')
                .Append(Q(identity)).Append(',').Append(N(width)).Append(',').Append(N(height)).Append(',')
                .Append(width is > 0 ? "0" : "NULL").Append(',').Append(width is > 0 ? checked(width.Value * 21 - 1) : "NULL").Append(',')
                .Append(height is > 0 ? "0" : "NULL").Append(',').Append(height is > 0 ? checked(height.Value * 21 - 1) : "NULL").Append(',')
                .Append(row.Definition.WorldMapX).Append(',').Append(row.Definition.WorldMapY).Append(',')
                .Append("1,1,1,1,1,'god2-opt-6b127086e0c0',")
                .Append(row.Definition.ClientMapId).Append(',').Append(row.Definition.ClientAreaId).Append(",1,1,0,0,'Verified',")
                .Append(row.Enabled ? "'Derived',1," : "'EvidenceBlocked',0,")
                .Append(Q(note)).Append(')')
                .AppendLine(index + 1 == rows.Count ? string.Empty : ",");
        }
        sql.AppendLine("ON DUPLICATE KEY UPDATE");
        sql.AppendLine("    `code`=VALUES(`code`),`name_zh_tw`=VALUES(`name_zh_tw`),");
        sql.AppendLine("    `resource_identity`=VALUES(`resource_identity`),`width`=VALUES(`width`),`height`=VALUES(`height`),");
        sql.AppendLine("    `minimum_x`=VALUES(`minimum_x`),`maximum_x`=VALUES(`maximum_x`),`minimum_y`=VALUES(`minimum_y`),`maximum_y`=VALUES(`maximum_y`),");
        sql.AppendLine("    `world_map_x`=VALUES(`world_map_x`),`world_map_y`=VALUES(`world_map_y`),");
        sql.AppendLine("    `client_build_id`=VALUES(`client_build_id`),`client_map_id`=VALUES(`client_map_id`),`client_area_id`=VALUES(`client_area_id`),");
        sql.AppendLine("    `coordinate_scale_x`=VALUES(`coordinate_scale_x`),`coordinate_scale_y`=VALUES(`coordinate_scale_y`),");
        sql.AppendLine("    `coordinate_offset_x`=VALUES(`coordinate_offset_x`),`coordinate_offset_y`=VALUES(`coordinate_offset_y`),");
        sql.AppendLine("    `identity_evidence_status`=VALUES(`identity_evidence_status`),`coordinate_evidence_status`=VALUES(`coordinate_evidence_status`),");
        sql.AppendLine("    `enabled`=VALUES(`enabled`),`admin_note`=VALUES(`admin_note`);");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `god2_game`.`client_map_resource_identities`");
        sql.AppendLine("    (`map_identity_id`,`client_build_id`,`client_area_id`,`client_map_id`,`map_id`,`resource_key`,");
        sql.AppendLine("     `world_map_x`,`world_map_y`,`enabled`)");
        sql.AppendLine("VALUES");
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            sql.Append("    (").Append(row.MapId).Append(",'god2-opt-6b127086e0c0',")
                .Append(row.Definition.ClientAreaId).Append(',').Append(row.Definition.ClientMapId).Append(',').Append(row.MapId).Append(',')
                .Append(Q(row.Definition.ResourceAuthorityKey)).Append(',').Append(row.Definition.WorldMapX).Append(',').Append(row.Definition.WorldMapY).Append(',')
                .Append(row.Enabled ? "1)" : "0)")
                .AppendLine(index + 1 == rows.Count ? string.Empty : ",");
        }
        sql.AppendLine("ON DUPLICATE KEY UPDATE");
        sql.AppendLine("    `map_id`=VALUES(`map_id`),`resource_key`=VALUES(`resource_key`),");
        sql.AppendLine("    `world_map_x`=VALUES(`world_map_x`),`world_map_y`=VALUES(`world_map_y`),");
        sql.AppendLine("    `enabled`=VALUES(`enabled`);");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `god2_research`.`client_map_resource_identity_evidence`");
        sql.AppendLine("    (`map_identity_id`,`source_row`,`source_sha256`,`identity_evidence_status`,`coordinate_evidence_status`)");
        sql.AppendLine("VALUES");
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            sql.Append("    (").Append(row.MapId).Append(',')
                .Append(row.Definition.SourceRow).Append(',').Append(Q(sourceHash)).Append(",'Verified',")
                .Append(row.Enabled ? "'Derived')" : "'EvidenceBlocked')")
                .AppendLine(index + 1 == rows.Count ? string.Empty : ",");
        }
        sql.AppendLine("ON DUPLICATE KEY UPDATE");
        sql.AppendLine("    `source_row`=VALUES(`source_row`),`source_sha256`=VALUES(`source_sha256`),");
        sql.AppendLine("    `identity_evidence_status`=VALUES(`identity_evidence_status`),");
        sql.AppendLine("    `coordinate_evidence_status`=VALUES(`coordinate_evidence_status`),");
        sql.AppendLine("    `moved_at_utc`=UTC_TIMESTAMP(6);");
        sql.AppendLine();
        sql.AppendLine("UPDATE `god2_game`.`client_map_resources` resource");
        sql.AppendLine("JOIN (");
        sql.AppendLine("    SELECT `resource_key`,MIN(`map_id`) AS `canonical_map_id`,MIN(`client_map_id`) AS `client_map_id`,");
        sql.AppendLine("           MIN(`client_area_id`) AS `client_area_id`,MAX(`enabled`) AS `enabled`");
        sql.AppendLine("    FROM `god2_game`.`client_map_resource_identities` GROUP BY `resource_key`");
        sql.AppendLine(") identity_group ON identity_group.`resource_key`=resource.`resource_key`");
        sql.AppendLine("SET resource.`canonical_map_id`=identity_group.`canonical_map_id`,resource.`client_map_id`=identity_group.`client_map_id`,resource.`client_area_id`=identity_group.`client_area_id`,");
        sql.AppendLine("    resource.`enabled`=identity_group.`enabled`");
        sql.AppendLine("WHERE resource.`client_build_id`='god2-opt-6b127086e0c0';");
        sql.AppendLine();
        sql.AppendLine("UPDATE `god2_research`.`client_map_resource_evidence` evidence_row");
        sql.AppendLine("JOIN (");
        sql.AppendLine("    SELECT `resource_key`,MAX(`enabled`) AS `enabled`");
        sql.AppendLine("    FROM `god2_game`.`client_map_resource_identities` GROUP BY `resource_key`");
        sql.AppendLine(") identity_group ON identity_group.`resource_key`=evidence_row.`resource_key`");
        sql.AppendLine("SET evidence_row.`map_identity_evidence_status`='Verified',");
        sql.AppendLine("    evidence_row.`admin_note`='Official gamedata identity joined to client navigation resource; Migration 114.',");
        sql.AppendLine("    evidence_row.`moved_at_utc`=UTC_TIMESTAMP(6);");
        sql.AppendLine();
        sql.AppendLine($"-- Source SHA-256: {sourceHash}");
        return sql.ToString();
    }

    private static string Q(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    private static string N(int? value) => value?.ToString() ?? "NULL";

    private sealed record PublishedOfficialMap(
        int MapId,
        OfficialClientExtractor.OfficialMapDefinition Definition,
        string NameZhTw,
        ClientMapResourceRecord? Resource,
        bool Enabled);
}

public sealed record OfficialMapMigrationResult(
    int DefinitionCount,
    int ResourceMatchedCount,
    int EnabledCount,
    int EvidenceBlockedCount,
    string SourceHash,
    string OutputPath);
