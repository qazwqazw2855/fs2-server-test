using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace God2.GameplayContentRecovery;

public static class OfficialPortalLinkMigrationWriter
{
    public static async Task<OfficialPortalLinkMigrationResult> WriteAsync(
        string repositoryRoot,
        string clientRoot,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var gameDataPath = Path.Combine(Path.GetFullPath(clientRoot), "Data2", "Patch", "Comm", "gamedata.csvZ");
        var gameData = await God2PackedFile.ReadCsvZAsync(gameDataPath, cancellationToken);
        var sections = GameDataSections.Parse(gameData.Rows);
        var mapIds = OfficialClientExtractor.ParseOfficialMapDefinitions(sections["Map_City_Coordniate"])
            .GroupBy(definition => definition.ResourceAuthorityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => OfficialMapMigrationWriter.ResolveCanonicalMapId(group.First().ClientAreaId, group.First().ClientMapId),
                StringComparer.OrdinalIgnoreCase);

        var sourcePath = Path.Combine(Path.GetFullPath(repositoryRoot), "db", "imports", "official", "portals", "portals.official.json");
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        using var document = JsonDocument.Parse(sourceBytes);
        var links = new List<OfficialPortalResourceLink>();
        foreach (var record in document.RootElement.GetProperty("records").EnumerateArray())
        {
            var id = record.GetProperty("id").GetString() ?? string.Empty;
            if (!id.StartsWith("client:can-link/", StringComparison.Ordinal))
            {
                continue;
            }

            var sourceResourceKey = record.GetProperty("sourceMapId").GetString() ?? throw new InvalidDataException($"Portal {id} is missing sourceMapId.");
            var destinationResourceKey = record.GetProperty("destinationMapIdCandidate").GetString() ?? throw new InvalidDataException($"Portal {id} is missing destinationMapIdCandidate.");
            if (!mapIds.TryGetValue(sourceResourceKey, out var sourceMapId))
            {
                throw new InvalidDataException($"Portal {id} source map identity is unresolved: {sourceResourceKey}.");
            }

            var can = record.GetProperty("canEvidence");
            links.Add(new OfficialPortalResourceLink(
                id,
                sourceResourceKey,
                destinationResourceKey,
                sourceMapId,
                mapIds.TryGetValue(destinationResourceKey, out var destinationMapId) ? destinationMapId : null,
                can.GetProperty("sourcePath").GetString() ?? string.Empty,
                can.GetProperty("recordIndex").GetInt32(),
                can.GetProperty("recordType").GetInt32()));
        }

        if (links.Count != 65 || links.Select(link => link.PortalLinkId).Distinct(StringComparer.Ordinal).Count() != 65)
        {
            throw new InvalidDataException($"Expected 65 unique official CAN portal links, got {links.Count}.");
        }

        var sql = BuildSql(links, sourceHash);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await File.WriteAllTextAsync(outputPath, sql, new UTF8Encoding(false), cancellationToken);
        return new OfficialPortalLinkMigrationResult(
            links.Count,
            links.Count(link => link.DestinationMapId is not null),
            links.Count(link => link.DestinationMapId is null),
            sourceHash,
            Path.GetFullPath(outputPath));
    }

    private static string BuildSql(IReadOnlyList<OfficialPortalResourceLink> links, string sourceHash)
    {
        var sql = new StringBuilder();
        sql.AppendLine("-- Generated from db/imports/official/portals/portals.official.json and official map identities.");
        sql.AppendLine("-- These are resource graph links, not verified gameplay trigger coordinates; all remain disabled.");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `god2_game`.`portal_resource_links` (");
        sql.AppendLine("    `portal_link_id` varchar(191) NOT NULL,");
        sql.AppendLine("    `portal_id` bigint NULL,");
        sql.AppendLine("    `client_build_id` varchar(64) NOT NULL,");
        sql.AppendLine("    `source_resource_key` varchar(512) NOT NULL,");
        sql.AppendLine("    `destination_resource_key` varchar(512) NOT NULL,");
        sql.AppendLine("    `source_map_id` bigint NOT NULL,");
        sql.AppendLine("    `destination_map_id` bigint NULL,");
        sql.AppendLine("    `enabled` tinyint(1) NOT NULL DEFAULT 0,");
        sql.AppendLine("    PRIMARY KEY (`portal_link_id`),");
        sql.AppendLine("    UNIQUE KEY `ux_portal_resource_links_portal` (`portal_id`),");
        sql.AppendLine("    KEY `ix_portal_resource_links_source` (`source_resource_key`),");
        sql.AppendLine("    KEY `ix_portal_resource_links_destination` (`destination_resource_key`),");
        sql.AppendLine("    CONSTRAINT `fk_portal_resource_links_portal` FOREIGN KEY (`portal_id`) REFERENCES `god2_game`.`portals` (`portal_id`),");
        sql.AppendLine("    CONSTRAINT `fk_portal_resource_links_source_resource` FOREIGN KEY (`source_resource_key`) REFERENCES `god2_game`.`client_map_resources` (`resource_key`),");
        sql.AppendLine("    CONSTRAINT `fk_portal_resource_links_destination_resource` FOREIGN KEY (`destination_resource_key`) REFERENCES `god2_game`.`client_map_resources` (`resource_key`),");
        sql.AppendLine("    CONSTRAINT `fk_portal_resource_links_source_map` FOREIGN KEY (`source_map_id`) REFERENCES `god2_game`.`maps` (`map_id`),");
        sql.AppendLine("    CONSTRAINT `fk_portal_resource_links_destination_map` FOREIGN KEY (`destination_map_id`) REFERENCES `god2_game`.`maps` (`map_id`),");
        sql.AppendLine("    CONSTRAINT `ck_portal_resource_links_enabled` CHECK (`enabled` IN (0,1))");
        sql.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        sql.AppendLine();
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `god2_research`.`portal_resource_link_evidence` (");
        sql.AppendLine("    `portal_link_id` varchar(191) NOT NULL,");
        sql.AppendLine("    `can_relative_path` varchar(512) NOT NULL,");
        sql.AppendLine("    `can_record_index` int NOT NULL,");
        sql.AppendLine("    `can_record_type` tinyint unsigned NOT NULL,");
        sql.AppendLine("    `source_sha256` char(64) NOT NULL,");
        sql.AppendLine("    `identity_evidence_status` varchar(30) NOT NULL,");
        sql.AppendLine("    `source_coordinate_evidence_status` varchar(30) NOT NULL,");
        sql.AppendLine("    `destination_coordinate_evidence_status` varchar(30) NOT NULL,");
        sql.AppendLine("    `trigger_evidence_status` varchar(30) NOT NULL,");
        sql.AppendLine("    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),");
        sql.AppendLine("    PRIMARY KEY (`portal_link_id`)");
        sql.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `god2_game`.`portal_resource_links`");
        sql.AppendLine("    (`portal_link_id`,`client_build_id`,`source_resource_key`,`destination_resource_key`,`source_map_id`,`destination_map_id`,");
        sql.AppendLine("     `enabled`)");
        sql.AppendLine("VALUES");
        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            sql.Append("    (").Append(Q(link.PortalLinkId)).Append(",'god2-opt-6b127086e0c0',")
                .Append(Q(link.SourceResourceKey)).Append(',').Append(Q(link.DestinationResourceKey)).Append(',')
                .Append(link.SourceMapId).Append(',').Append(link.DestinationMapId?.ToString() ?? "NULL").Append(',')
                .Append('0').Append(')')
                .AppendLine(index + 1 == links.Count ? string.Empty : ",");
        }
        sql.AppendLine("ON DUPLICATE KEY UPDATE");
        sql.AppendLine("    `source_resource_key`=VALUES(`source_resource_key`),`destination_resource_key`=VALUES(`destination_resource_key`),");
        sql.AppendLine("    `source_map_id`=VALUES(`source_map_id`),`destination_map_id`=VALUES(`destination_map_id`),");
        sql.AppendLine("    `enabled`=0;");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `god2_research`.`portal_resource_link_evidence`");
        sql.AppendLine("    (`portal_link_id`,`can_relative_path`,`can_record_index`,`can_record_type`,`source_sha256`,");
        sql.AppendLine("     `identity_evidence_status`,`source_coordinate_evidence_status`,`destination_coordinate_evidence_status`,`trigger_evidence_status`)");
        sql.AppendLine("VALUES");
        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            sql.Append("    (").Append(Q(link.PortalLinkId)).Append(',')
                .Append(Q(link.CanRelativePath.Replace('\\', '/'))).Append(',').Append(link.CanRecordIndex).Append(',').Append(link.CanRecordType).Append(',')
                .Append(Q(sourceHash)).Append(',').Append(link.DestinationMapId is null ? "'Candidate'" : "'Derived'")
                .Append(",'Unknown','Unknown','Unknown')")
                .AppendLine(index + 1 == links.Count ? string.Empty : ",");
        }
        sql.AppendLine("ON DUPLICATE KEY UPDATE");
        sql.AppendLine("    `can_relative_path`=VALUES(`can_relative_path`),`can_record_index`=VALUES(`can_record_index`),");
        sql.AppendLine("    `can_record_type`=VALUES(`can_record_type`),`source_sha256`=VALUES(`source_sha256`),");
        sql.AppendLine("    `identity_evidence_status`=VALUES(`identity_evidence_status`),");
        sql.AppendLine("    `source_coordinate_evidence_status`=VALUES(`source_coordinate_evidence_status`),");
        sql.AppendLine("    `destination_coordinate_evidence_status`=VALUES(`destination_coordinate_evidence_status`),");
        sql.AppendLine("    `trigger_evidence_status`=VALUES(`trigger_evidence_status`),");
        sql.AppendLine("    `moved_at_utc`=UTC_TIMESTAMP(6);");
        sql.AppendLine();
        sql.AppendLine("UPDATE `god2_game`.`portal_resource_links` link_row");
        sql.AppendLine("JOIN `god2_game`.`portals` portal_row ON portal_row.`name_zh_tw`=link_row.`portal_link_id`");
        sql.AppendLine("SET link_row.`portal_id`=portal_row.`portal_id`;");
        sql.AppendLine();
        sql.AppendLine("UPDATE `god2_game`.`portals` portal_row");
        sql.AppendLine("JOIN `god2_game`.`portal_resource_links` link_row ON link_row.`portal_id`=portal_row.`portal_id`");
        sql.AppendLine("SET portal_row.`source_map_id`=link_row.`source_map_id`,portal_row.`destination_map_id`=link_row.`destination_map_id`,");
        sql.AppendLine("    portal_row.`source_x`=NULL,portal_row.`source_y`=NULL,portal_row.`source_radius`=NULL,");
        sql.AppendLine("    portal_row.`destination_x`=NULL,portal_row.`destination_y`=NULL,portal_row.`enabled`=0;");
        sql.AppendLine();
        sql.AppendLine($"-- Source SHA-256: {sourceHash}");
        return sql.ToString();
    }

    private static string Q(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private sealed record OfficialPortalResourceLink(
        string PortalLinkId,
        string SourceResourceKey,
        string DestinationResourceKey,
        int SourceMapId,
        int? DestinationMapId,
        string CanRelativePath,
        int CanRecordIndex,
        int CanRecordType);
}

public sealed record OfficialPortalLinkMigrationResult(
    int LinkCount,
    int BothMapIdentitiesResolvedCount,
    int DestinationMapIdentityPendingCount,
    string SourceHash,
    string OutputPath);
