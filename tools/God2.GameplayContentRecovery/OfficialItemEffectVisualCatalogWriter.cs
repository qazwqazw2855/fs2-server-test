using System.Globalization;
using System.Text;

namespace God2.GameplayContentRecovery;

public sealed record OfficialItemEffectVisualRecord(
    int EffectId,
    string SourceTable,
    string? RomResourcePath,
    string? RtgResourcePath,
    int? SoundIndex,
    string VisualDescriptionOriginal,
    string VisualDescription,
    string? AuthoredMechanicalDescriptionOriginal,
    string? AuthoredMechanicalDescription,
    string SourcePath,
    string SourceSha256,
    int SourceRecordIndex,
    string EvidenceStatus,
    string MechanicalSemanticsStatus,
    bool RuntimeEligible);

public sealed record OfficialItemEffectVisualCatalog(
    string Category,
    string Format,
    int RecordCount,
    int ItemEft3Count,
    int GodItemEftCount,
    string VerificationStatus,
    bool CanDirectImportToGameplay,
    object CrossVersionCorroboration,
    IReadOnlyList<OfficialItemEffectVisualRecord> Records);

public sealed record OfficialItemEffectVisualCatalogWriteResult(
    int RecordCount,
    int ItemEft3Count,
    int GodItemEftCount,
    string CatalogOutputPath,
    string MigrationOutputPath,
    string ItemEft3Sha256,
    string GodItemEftSha256);

public static class OfficialItemEffectVisualCatalogWriter
{
    private const string ItemEft3RelativePath = "Data2/Patch/ItemEft3.csvZ";
    private const string GodItemEftRelativePath = "Data2/Patch/GodItemEft.csvZ";

    public static async Task<OfficialItemEffectVisualCatalogWriteResult> WriteAsync(
        string clientRoot,
        string catalogOutputPath,
        string migrationOutputPath,
        CancellationToken cancellationToken)
    {
        var itemEft3 = await ReadAsync(clientRoot, ItemEft3RelativePath, cancellationToken);
        var godItemEft = await ReadAsync(clientRoot, GodItemEftRelativePath, cancellationToken);
        var localization = new ZhTwLocalization(God2Glossary.Create());
        var itemRows = Parse(itemEft3, "ItemEft3", ItemEft3RelativePath, 8400, 8471, localization);
        var godRows = Parse(godItemEft, "GodItemEft", GodItemEftRelativePath, 8500, 8529, localization);
        var records = itemRows.Concat(godRows).OrderBy(row => row.EffectId).ToArray();
        if (records.Length != 102 || records.Select(row => row.EffectId).Distinct().Count() != records.Length)
        {
            throw new InvalidDataException("The exact-client item-effect visual catalog must contain 102 unique identities.");
        }

        var catalog = new OfficialItemEffectVisualCatalog(
            "item-effect-client-visuals",
            "Decoded exact-client 05 16 LZSS + CP936 tables",
            records.Length,
            itemRows.Count,
            godRows.Count,
            "VerifiedExactClientStaticVisualCatalog; MechanicalSemanticsEvidenceBlocked",
            false,
            new
            {
                evidenceClass = "CrossVersionCorroborationOnly",
                packageSha256 = "c6a002de544732919b34194d991a34e1cda7c684394233948601e85d8a156562",
                matchingIdentityCount = 102,
                matchingSoundIndexCount = 102,
                exactCurrentAdditionalRomBindings = 30,
                exactCurrentAdditionalRtgBindings = 30,
                localizedVisualLabelDifferences = 1,
                boundary = "V4 supplied the extraction lead from another build. These records were independently decoded from the exact current client. The exact current source adds 30 ROM/RTG bindings and retains its own original text; visual labels do not prove server item effects."
            },
            records);

        var fullCatalogPath = Path.GetFullPath(catalogOutputPath);
        var fullMigrationPath = Path.GetFullPath(migrationOutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullCatalogPath)
            ?? throw new InvalidDataException("Item-effect catalog output directory is invalid."));
        Directory.CreateDirectory(Path.GetDirectoryName(fullMigrationPath)
            ?? throw new InvalidDataException("Item-effect migration output directory is invalid."));
        await File.WriteAllTextAsync(fullCatalogPath, RecoveryJson.Serialize(catalog) + Environment.NewLine,
            new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(fullMigrationPath, BuildMigration(records), new UTF8Encoding(false), cancellationToken);

        return new OfficialItemEffectVisualCatalogWriteResult(
            records.Length, itemRows.Count, godRows.Count, fullCatalogPath, fullMigrationPath,
            itemEft3.SourceHash, godItemEft.SourceHash);
    }

    public static IReadOnlyList<OfficialItemEffectVisualRecord> Parse(
        CsvZDocument document,
        string sourceTable,
        string relativeSourcePath,
        int firstId,
        int lastId,
        ZhTwLocalization localization)
    {
        var records = new List<OfficialItemEffectVisualRecord>();
        foreach (var row in document.Rows)
        {
            if (row.Fields.Count == 0 ||
                !int.TryParse(row.Fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var effectId) ||
                effectId < firstId || effectId > lastId)
            {
                continue;
            }
            if (row.Fields.Count is < 5 or > 6 || string.IsNullOrWhiteSpace(row.Fields[4]))
            {
                throw new InvalidDataException($"Invalid {sourceTable} row at source record {row.RecordIndex}.");
            }

            var soundIndex = NullIfEmpty(row.Fields[3]) is { } soundText
                ? int.Parse(soundText, NumberStyles.Integer, CultureInfo.InvariantCulture)
                : (int?)null;
            var visual = localization.Convert(row.Fields[4], document.SourceHash);
            var mechanical = row.Fields.Count > 5 && !string.IsNullOrWhiteSpace(row.Fields[5])
                ? localization.Convert(row.Fields[5], document.SourceHash).ConvertedText
                : null;
            records.Add(new OfficialItemEffectVisualRecord(
                effectId,
                sourceTable,
                NormalizeResource(row.Fields[1]),
                NormalizeResource(row.Fields[2]),
                soundIndex,
                row.Fields[4],
                visual.ConvertedText,
                row.Fields.Count > 5 ? NullIfEmpty(row.Fields[5]) : null,
                mechanical,
                relativeSourcePath,
                document.SourceHash,
                row.RecordIndex,
                "VerifiedExactClientStaticVisualCatalog",
                "DisplayTextOnlyServerAuthorityBlocked",
                false));
        }

        var expectedCount = checked(lastId - firstId + 1);
        if (records.Count != expectedCount ||
            !records.Select(record => record.EffectId).SequenceEqual(Enumerable.Range(firstId, expectedCount)))
        {
            throw new InvalidDataException($"{sourceTable} must contain the contiguous exact-client range {firstId}..{lastId}.");
        }
        return records;
    }

    private static async Task<CsvZDocument> ReadAsync(string clientRoot, string relativePath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(clientRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Exact-client table was not found: {relativePath}.", path);
        }
        return await God2PackedFile.ReadCsvZAsync(path, cancellationToken);
    }

    private static string BuildMigration(IReadOnlyList<OfficialItemEffectVisualRecord> records)
    {
        var sql = new StringBuilder();
        sql.AppendLine("-- Exact-current-client visual/resource catalog only. It does not authorize item-use mechanics.");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `god2_game`.`client_item_effect_visuals` (");
        sql.AppendLine("    `effect_id` int NOT NULL,");
        sql.AppendLine("    `sound_index` int NULL,");
        sql.AppendLine("    `visual_description_zh_tw` varchar(500) NOT NULL,");
        sql.AppendLine("    `authored_mechanical_description_zh_tw` varchar(500) NULL,");
        sql.AppendLine("    `mechanical_semantics_status` varchar(64) NOT NULL,");
        sql.AppendLine("    `catalog_enabled` tinyint(1) NOT NULL DEFAULT 1,");
        sql.AppendLine("    `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0,");
        sql.AppendLine("    `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),");
        sql.AppendLine("    `updated_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),");
        sql.AppendLine("    PRIMARY KEY (`effect_id`),");
        sql.AppendLine("    CONSTRAINT `ck_client_item_effect_visual_catalog_enabled` CHECK (`catalog_enabled` IN (0,1)),");
        sql.AppendLine("    CONSTRAINT `ck_client_item_effect_visual_runtime_blocked` CHECK (`runtime_eligible`=0),");
        sql.AppendLine("    CONSTRAINT `ck_client_item_effect_visual_mechanics` CHECK (`mechanical_semantics_status`='DisplayTextOnlyServerAuthorityBlocked')");
        sql.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        sql.AppendLine();
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `god2_research`.`client_item_effect_visual_evidence` (");
        sql.AppendLine("    `effect_id` int NOT NULL,");
        sql.AppendLine("    `source_table` varchar(24) NULL,");
        sql.AppendLine("    `rom_resource_path` varchar(260) NULL,");
        sql.AppendLine("    `rtg_resource_path` varchar(260) NULL,");
        sql.AppendLine("    `source_path` varchar(260) NOT NULL,");
        sql.AppendLine("    `source_row` int NOT NULL,");
        sql.AppendLine("    `source_sha256` char(64) NOT NULL,");
        sql.AppendLine("    `evidence_status` varchar(64) NOT NULL,");
        sql.AppendLine("    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),");
        sql.AppendLine("    PRIMARY KEY (`effect_id`)");
        sql.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        sql.AppendLine();
        sql.AppendLine("ALTER TABLE `god2_research`.`client_item_effect_visual_evidence`");
        sql.AppendLine("    ADD COLUMN IF NOT EXISTS `source_table` varchar(24) NULL AFTER `effect_id`,");
        sql.AppendLine("    ADD COLUMN IF NOT EXISTS `rom_resource_path` varchar(260) NULL AFTER `source_table`,");
        sql.AppendLine("    ADD COLUMN IF NOT EXISTS `rtg_resource_path` varchar(260) NULL AFTER `rom_resource_path`;");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `god2_game`.`client_item_effect_visuals`");
        sql.AppendLine("    (`effect_id`,`sound_index`,`visual_description_zh_tw`,");
        sql.AppendLine("     `authored_mechanical_description_zh_tw`,");
        sql.AppendLine("     `mechanical_semantics_status`,`catalog_enabled`,`runtime_eligible`)");
        sql.AppendLine("VALUES");
        for (var index = 0; index < records.Count; index++)
        {
            var row = records[index];
            sql.Append("    (").Append(row.EffectId).Append(',')
                .Append(row.SoundIndex?.ToString(CultureInfo.InvariantCulture) ?? "NULL").Append(',')
                .Append(Q(row.VisualDescription)).Append(',')
                .Append(Qn(row.AuthoredMechanicalDescription)).Append(',')
                .Append(Q(row.MechanicalSemanticsStatus)).Append(",1,0)")
                .AppendLine(index + 1 == records.Count ? string.Empty : ",");
        }
        sql.AppendLine("ON DUPLICATE KEY UPDATE");
        sql.AppendLine("    `sound_index`=VALUES(`sound_index`),`visual_description_zh_tw`=VALUES(`visual_description_zh_tw`),");
        sql.AppendLine("    `authored_mechanical_description_zh_tw`=VALUES(`authored_mechanical_description_zh_tw`),");
        sql.AppendLine("    `mechanical_semantics_status`=VALUES(`mechanical_semantics_status`),");
        sql.AppendLine("    `catalog_enabled`=1,`runtime_eligible`=0;");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `god2_research`.`client_item_effect_visual_evidence`");
        sql.AppendLine("    (`effect_id`,`source_table`,`rom_resource_path`,`rtg_resource_path`,`source_path`,`source_row`,`source_sha256`,`evidence_status`)");
        sql.AppendLine("VALUES");
        for (var index = 0; index < records.Count; index++)
        {
            var row = records[index];
            sql.Append("    (").Append(row.EffectId).Append(',')
                .Append(Q(row.SourceTable)).Append(',')
                .Append(Qn(row.RomResourcePath)).Append(',').Append(Qn(row.RtgResourcePath)).Append(',')
                .Append(Q(row.SourcePath)).Append(',')
                .Append(row.SourceRecordIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Q(row.SourceSha256)).Append(',')
                .Append(Q(row.EvidenceStatus)).Append(')')
                .AppendLine(index + 1 == records.Count ? string.Empty : ",");
        }
        sql.AppendLine("ON DUPLICATE KEY UPDATE");
        sql.AppendLine("    `source_table`=VALUES(`source_table`),");
        sql.AppendLine("    `rom_resource_path`=VALUES(`rom_resource_path`),");
        sql.AppendLine("    `rtg_resource_path`=VALUES(`rtg_resource_path`),");
        sql.AppendLine("    `source_path`=VALUES(`source_path`),");
        sql.AppendLine("    `source_row`=VALUES(`source_row`),");
        sql.AppendLine("    `source_sha256`=VALUES(`source_sha256`),");
        sql.AppendLine("    `evidence_status`=VALUES(`evidence_status`),");
        sql.AppendLine("    `moved_at_utc`=UTC_TIMESTAMP(6);");
        sql.AppendLine();
        sql.AppendLine("CREATE OR REPLACE VIEW `god2_game`.`vw_client_item_effect_visuals_readable` AS");
        sql.AppendLine("SELECT `effect_id` AS `特效編號`,`sound_index` AS `音效索引`,`visual_description_zh_tw` AS `視覺繁中`,");
        sql.AppendLine("       `authored_mechanical_description_zh_tw` AS `機械提示繁中`,");
        sql.AppendLine("       `mechanical_semantics_status` AS `服務端語意`,`runtime_eligible` AS `可執行`");
        sql.AppendLine("FROM `god2_game`.`client_item_effect_visuals` WHERE `catalog_enabled`=1;");
        return sql.ToString();
    }

    private static string? NormalizeResource(string value) => NullIfEmpty(value)?.Replace('\\', '/');
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Q(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    private static string Qn(string? value) => value is null ? "NULL" : Q(value);
}
