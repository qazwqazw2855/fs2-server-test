using System.Globalization;
using System.Text;
using System.Text.Json;

namespace God2.GameplayContentRecovery;

public sealed record OfficialNpcSourceRowMigrationResult(
    int SourceRowCount,
    int UniqueHandleCount,
    int DuplicateHandleRowCount,
    string SourceSha256,
    string OutputPath);

public static class OfficialNpcSourceRowMigrationWriter
{
    public static async Task<OfficialNpcSourceRowMigrationResult> WriteAsync(
        string clientRoot,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetFullPath(clientRoot), "Data2", "Patch", "Comm", "gamedata.csvZ");
        var document = await God2PackedFile.ReadCsvZAsync(path, cancellationToken);
        var section = GameDataSections.Parse(document.Rows)["NPCAppearData"];
        var localization = new ZhTwLocalization(God2Glossary.Create());
        var rows = section.Rows.Select((row, index) =>
        {
            if (!int.TryParse(row.Fields.ElementAtOrDefault(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out var handle) || handle <= 0 ||
                !int.TryParse(row.Fields.ElementAtOrDefault(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var selector) ||
                selector <= 0 || selector % 1000 is < 1 or > 255)
            {
                throw new InvalidDataException($"Invalid official NPCAppearData row at source line {row.RecordIndex}.");
            }

            var original = row.Fields.ElementAtOrDefault(1)?.Trim() ?? string.Empty;
            return new SourceRow(
                1_340_000_000L + index,
                row.RecordIndex,
                index,
                handle,
                original,
                localization.Convert(original, document.SourceHash).ConvertedText.Trim(),
                selector,
                selector / 1000,
                selector % 1000 - 1,
                JsonSerializer.Serialize(row.Fields));
        }).ToArray();

        var sql = BuildSql(rows, document.SourceHash);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await File.WriteAllTextAsync(outputPath, sql, new UTF8Encoding(false), cancellationToken);
        return new OfficialNpcSourceRowMigrationResult(
            rows.Length,
            rows.Select(row => row.ClientEntityHandle).Distinct().Count(),
            rows.Length - rows.Select(row => row.ClientEntityHandle).Distinct().Count(),
            document.SourceHash,
            Path.GetFullPath(outputPath));
    }

    private static string BuildSql(IReadOnlyList<SourceRow> rows, string sourceSha256)
    {
        var sql = new StringBuilder();
        sql.AppendLine("-- Migration 117: preserve every official NPCAppearData source row, including duplicate handles.");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `god2_game`.`npc_appearance_source_rows` (");
        sql.AppendLine("    `appearance_source_row_id` bigint NOT NULL,`client_build_id` varchar(64) NOT NULL,`client_entity_handle` int unsigned NOT NULL,");
        sql.AppendLine("    `source_line` int NOT NULL,`section_row_index` int NOT NULL,`name_zh_tw` varchar(150) NOT NULL,");
        sql.AppendLine("    `official_selector` int unsigned NOT NULL,`official_resource_type` tinyint unsigned NOT NULL,`official_resource_ordinal` tinyint unsigned NOT NULL,");
        sql.AppendLine("    `source_sha256` char(64) NOT NULL,`raw_fields_json` json NOT NULL,PRIMARY KEY (`appearance_source_row_id`),");
        sql.AppendLine("    UNIQUE KEY `ux_npc_appearance_source_row` (`client_build_id`,`section_row_index`),KEY `ix_npc_appearance_source_handle` (`client_build_id`,`client_entity_handle`),");
        sql.AppendLine("    CONSTRAINT `fk_npc_appearance_source_identity` FOREIGN KEY (`client_build_id`,`client_entity_handle`) REFERENCES `god2_game`.`npc_appearance_identities` (`client_build_id`,`client_entity_handle`)");
        sql.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Lossless official NPCAppearData source rows';");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `god2_game`.`npc_appearance_source_rows` (`appearance_source_row_id`,`client_build_id`,`client_entity_handle`,`source_line`,`section_row_index`,`name_zh_tw`,`official_selector`,`official_resource_type`,`official_resource_ordinal`,`source_sha256`,`raw_fields_json`) VALUES");
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            sql.Append("    (").Append(row.Id).Append(",'god2-opt-6b127086e0c0',").Append(row.ClientEntityHandle).Append(',')
                .Append(row.SourceLine).Append(',').Append(row.SectionRowIndex).Append(',').Append(Q(row.NameZhTw)).Append(',')
                .Append(row.Selector).Append(',').Append(row.ResourceType).Append(',').Append(row.ResourceOrdinal).Append(',').Append(Q(sourceSha256)).Append(',')
                .Append(Q(row.RawFieldsJson)).Append(')').AppendLine(index + 1 == rows.Count ? string.Empty : ",");
        }
        sql.AppendLine("ON DUPLICATE KEY UPDATE `client_entity_handle`=VALUES(`client_entity_handle`),`source_line`=VALUES(`source_line`),`name_zh_tw`=VALUES(`name_zh_tw`),`official_selector`=VALUES(`official_selector`),`official_resource_type`=VALUES(`official_resource_type`),`official_resource_ordinal`=VALUES(`official_resource_ordinal`),`source_sha256`=VALUES(`source_sha256`),`raw_fields_json`=VALUES(`raw_fields_json`);");
        sql.AppendLine();
        sql.AppendLine($"-- Official gamedata SHA-256: {sourceSha256}");
        return sql.ToString();
    }

    private static string Q(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private sealed record SourceRow(
        long Id,
        int SourceLine,
        int SectionRowIndex,
        int ClientEntityHandle,
        string NameOriginal,
        string NameZhTw,
        int Selector,
        int ResourceType,
        int ResourceOrdinal,
        string RawFieldsJson);
}
