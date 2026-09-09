using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace God2.GameplayContentRecovery;

public sealed record OfficialItemUsageFlagRecord(
    string SourceSection,
    int ClientItemId,
    string ResourceKey,
    string DisplayNameOriginal,
    string DisplayNameZhTw,
    bool? NormalUse,
    bool? BattleUse,
    bool? Equippable,
    bool? UseOnOther,
    bool? HotkeyAllowed,
    bool? Tradable,
    bool? Droppable,
    bool? Storable,
    bool? Stackable,
    bool? CombineUp,
    bool? CombineDown,
    bool PresentInFormalBaseline,
    int SourceRecordIndex);

public sealed record OfficialItemUsageFlagCatalog(
    string Category,
    string SourcePath,
    string SourceSha256,
    int RecordCount,
    int FormalBaselineCount,
    int ExactCurrentAdditionCount,
    string NormalizedRuntimeGateSha256,
    string NormalizedFullStaticFlagSha256,
    object Counts,
    string EvidenceStatus,
    string ActivationEvidenceStatus,
    bool RuntimeEligible,
    IReadOnlyList<OfficialItemUsageFlagRecord> Records);

public sealed record OfficialItemUsageFlagCatalogWriteResult(
    int RecordCount,
    int FormalBaselineCount,
    int ExactCurrentAdditionCount,
    string SourceSha256,
    string NormalizedRuntimeGateSha256,
    string NormalizedFullStaticFlagSha256,
    string CatalogOutputPath,
    string MigrationOutputPath);

public static class OfficialItemUsageFlagCatalogWriter
{
    private const string RelativeSourcePath = "Data2/Patch/Comm/gamedata.csvZ";
    private static readonly string[] ItemSections =
    [
        "WPN", "EQU", "GOD", "MAP", "MED01", "MED02", "TLI", "PET", "MAT01", "MIS", "SPI", "SKB", "PFD",
        "GWP", "GEH", "GEB", "GEQ", "PEQ", "STR", "KIT01", "KIT02", "NST", "EGG", "SPP", "CBK", "SCD",
        "EQC", "CBF01", "CAD", "BEB", "VPT", "CBF02", "ELE", "MIS02", "MED03", "TWP", "TEQ", "AMU", "BAR",
        "GEQ02", "MEQ", "COM", "NCP", "NMP", "MIS03", "UPS", "SSW", "SES"
    ];

    public static async Task<OfficialItemUsageFlagCatalogWriteResult> WriteAsync(
        string repositoryRoot,
        string clientRoot,
        string catalogOutputPath,
        string migrationOutputPath,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(clientRoot, RelativeSourcePath.Replace('/', Path.DirectorySeparatorChar));
        var document = await God2PackedFile.ReadCsvZAsync(sourcePath, cancellationToken);
        var sections = GameDataSections.Parse(document.Rows);
        var localization = new ZhTwLocalization(God2Glossary.Create());
        var records = new List<OfficialItemUsageFlagRecord>();
        foreach (var sectionName in ItemSections)
        {
            if (!sections.TryGetValue(sectionName, out var section))
            {
                throw new InvalidDataException($"Exact-client item section is missing: {sectionName}.");
            }
            foreach (var row in section.Rows)
            {
                if (row.Fields.Count == 0 || !int.TryParse(row.Fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var clientItemId))
                {
                    throw new InvalidDataException($"Invalid exact-client {sectionName} item row {row.RecordIndex}.");
                }
                records.Add(new OfficialItemUsageFlagRecord(
                    sectionName, clientItemId, At(row.Fields, 2), At(row.Fields, 4),
                    localization.Convert(At(row.Fields, 4), document.SourceHash).ConvertedText,
                    Flag(At(row.Fields, 14), sectionName, row.RecordIndex, 14),
                    Flag(At(row.Fields, 15), sectionName, row.RecordIndex, 15),
                    Flag(At(row.Fields, 16), sectionName, row.RecordIndex, 16),
                    Flag(At(row.Fields, 17), sectionName, row.RecordIndex, 17),
                    Flag(At(row.Fields, 18), sectionName, row.RecordIndex, 18),
                    Flag(At(row.Fields, 19), sectionName, row.RecordIndex, 19),
                    Flag(At(row.Fields, 20), sectionName, row.RecordIndex, 20),
                    Flag(At(row.Fields, 21), sectionName, row.RecordIndex, 21),
                    Flag(At(row.Fields, 22), sectionName, row.RecordIndex, 22),
                    Flag(At(row.Fields, 23), sectionName, row.RecordIndex, 23),
                    Flag(At(row.Fields, 24), sectionName, row.RecordIndex, 24),
                    false,
                    row.RecordIndex));
            }
        }

        records = records.OrderBy(row => row.SourceSection, StringComparer.Ordinal).ThenBy(row => row.ClientItemId).ToList();
        var baselineKeys = ReadFormalBaselineKeys(repositoryRoot);
        records = records.Select(row => row with
        {
            PresentInFormalBaseline = baselineKeys.Contains((row.SourceSection, row.ClientItemId))
        }).ToList();
        var baselineRecords = records.Where(row => row.PresentInFormalBaseline).ToArray();
        var additionalRecords = records.Where(row => !row.PresentInFormalBaseline).ToArray();
        var distinctClientItemIds = records.Select(row => row.ClientItemId).Distinct().Count();
        if (records.Count != 17418 || distinctClientItemIds != records.Count || baselineRecords.Length != 17407 || additionalRecords.Length != 11)
        {
            throw new InvalidDataException($"Exact-client item usage split is unexpected; rows={records.Count}, distinct={distinctClientItemIds}, baseline={baselineRecords.Length}, additions={additionalRecords.Length}.");
        }

        var normalizedHash = HashNormalized(baselineRecords);
        var fullStaticHash = HashFullStaticFlags(baselineRecords);
        var catalog = new OfficialItemUsageFlagCatalog(
            "exact-client-item-usage-flags",
            RelativeSourcePath,
            document.SourceHash,
            records.Count,
            baselineRecords.Length,
            additionalRecords.Length,
            normalizedHash,
            fullStaticHash,
            new
            {
                normalUse = records.Count(row => row.NormalUse == true),
                battleUse = records.Count(row => row.BattleUse == true),
                equippable = records.Count(row => row.Equippable == true),
                useOnOther = records.Count(row => row.UseOnOther == true),
                hotkeyAllowed = records.Count(row => row.HotkeyAllowed == true),
                tradable = records.Count(row => row.Tradable == true),
                droppable = records.Count(row => row.Droppable == true),
                storable = records.Count(row => row.Storable == true),
                stackable = records.Count(row => row.Stackable == true),
                combineUp = records.Count(row => row.CombineUp == true),
                combineDown = records.Count(row => row.CombineDown == true)
            },
            "VerifiedExactCurrentClientStaticFlags",
            "EvidenceBlockedMissingOfficialItemUseTransition",
            false,
            records);

        var fullCatalogPath = Path.GetFullPath(catalogOutputPath);
        var fullMigrationPath = Path.GetFullPath(migrationOutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullCatalogPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(fullMigrationPath)!);
        await File.WriteAllTextAsync(fullCatalogPath, RecoveryJson.Serialize(catalog) + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(fullMigrationPath, BuildMigration(document.SourceHash, normalizedHash, additionalRecords), new UTF8Encoding(false), cancellationToken);
        return new(records.Count, baselineRecords.Length, additionalRecords.Length, document.SourceHash, normalizedHash, fullStaticHash, fullCatalogPath, fullMigrationPath);
    }

    private static bool? Flag(string value, string section, int row, int field) => value switch
    {
        "" => null,
        "0" => false,
        "1" => true,
        _ => throw new InvalidDataException($"Exact-client {section} row {row} field {field} is not a binary flag: '{value}'.")
    };

    private static string HashNormalized(IEnumerable<OfficialItemUsageFlagRecord> records)
    {
        var text = string.Join('\n', records.Select(row => string.Join('|',
            row.SourceSection,
            row.ClientItemId.ToString(CultureInfo.InvariantCulture),
            Bit(row.NormalUse), Bit(row.BattleUse), Bit(row.Equippable), Bit(row.UseOnOther), Bit(row.HotkeyAllowed))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string HashFullStaticFlags(IEnumerable<OfficialItemUsageFlagRecord> records)
    {
        var text = string.Join('\n', records.Select(row => string.Join('|',
            row.SourceSection,
            row.ClientItemId.ToString(CultureInfo.InvariantCulture),
            Bit(row.NormalUse), Bit(row.BattleUse), Bit(row.Equippable), Bit(row.UseOnOther), Bit(row.HotkeyAllowed),
            Bit(row.Tradable), Bit(row.Droppable), Bit(row.Storable), Bit(row.Stackable), Bit(row.CombineUp), Bit(row.CombineDown))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string BuildMigration(string sourceSha256, string normalizedHash, IReadOnlyList<OfficialItemUsageFlagRecord> additions)
    {
        var additionValues = string.Join(",\n", additions.Select(row =>
            $"    ({row.ClientItemId},{Q(row.DisplayNameZhTw)},{SqlBit(row.NormalUse)},{SqlBit(row.BattleUse)},{SqlBit(row.Equippable)},{SqlBit(row.UseOnOther)},{SqlBit(row.HotkeyAllowed)},{SqlBit(row.Tradable)},{SqlBit(row.Droppable)},{SqlBit(row.Storable)},{SqlBit(row.Stackable)},{SqlBit(row.CombineUp)},{SqlBit(row.CombineDown)},'EvidenceBlockedMissingCanonicalItemBinding',0)"));
        return $$"""
        -- Exact-current-client item usage flags are static catalog evidence only.
        -- This migration separates known client flags from server-authoritative activation.
        CREATE TABLE IF NOT EXISTS `god2_game`.`client_item_usage_flag_additions` (
            `client_item_id` int NOT NULL,
            `display_name_zh_tw` varchar(300) NOT NULL,
            `normal_use` tinyint(1) NULL, `battle_use` tinyint(1) NULL, `equippable` tinyint(1) NULL,
            `use_on_other` tinyint(1) NULL, `hotkey_allowed` tinyint(1) NULL, `tradable` tinyint(1) NULL,
            `droppable` tinyint(1) NULL, `storable` tinyint(1) NULL, `stackable` tinyint(1) NULL,
            `combine_up` tinyint(1) NULL, `combine_down` tinyint(1) NULL,
            `binding_status` varchar(64) NOT NULL,
            `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0,
            `created_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
            PRIMARY KEY (`client_item_id`),
            CONSTRAINT `ck_client_item_usage_addition_runtime` CHECK (`runtime_eligible`=0),
            CONSTRAINT `ck_client_item_usage_addition_binding` CHECK (`binding_status`='EvidenceBlockedMissingCanonicalItemBinding')
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        INSERT INTO `god2_game`.`client_item_usage_flag_additions`
            (`client_item_id`,`display_name_zh_tw`,`normal_use`,`battle_use`,`equippable`,`use_on_other`,`hotkey_allowed`,
             `tradable`,`droppable`,`storable`,`stackable`,`combine_up`,`combine_down`,`binding_status`,`runtime_eligible`)
        VALUES
        {{additionValues}}
        ON DUPLICATE KEY UPDATE
            `display_name_zh_tw`=VALUES(`display_name_zh_tw`),
            `normal_use`=VALUES(`normal_use`),`battle_use`=VALUES(`battle_use`),`equippable`=VALUES(`equippable`),
            `use_on_other`=VALUES(`use_on_other`),`hotkey_allowed`=VALUES(`hotkey_allowed`),`tradable`=VALUES(`tradable`),
            `droppable`=VALUES(`droppable`),`storable`=VALUES(`storable`),`stackable`=VALUES(`stackable`),
            `combine_up`=VALUES(`combine_up`),`combine_down`=VALUES(`combine_down`),
            `binding_status`='EvidenceBlockedMissingCanonicalItemBinding',`runtime_eligible`=0;

        ALTER TABLE `god2_game`.`item_usage_rules`
            ADD COLUMN IF NOT EXISTS `exact_client_source_sha256` char(64) NULL AFTER `source_reference_zh_tw`,
            ADD COLUMN IF NOT EXISTS `activation_evidence_status` varchar(64) NOT NULL DEFAULT 'EvidenceBlockedMissingOfficialItemUseTransition' AFTER `exact_client_source_sha256`,
            ADD COLUMN IF NOT EXISTS `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0 AFTER `activation_evidence_status`;

        DROP PROCEDURE IF EXISTS `god2_game`.`assert_exact_item_usage_flag_catalog`;
        DELIMITER $$
        CREATE PROCEDURE `god2_game`.`assert_exact_item_usage_flag_catalog`()
        BEGIN
            DECLARE actual_count bigint DEFAULT 0;
            DECLARE actual_hash char(64) DEFAULT NULL;
            SET SESSION group_concat_max_len=16777216;
            SELECT COUNT(*), LOWER(SHA2(GROUP_CONCAT(CONCAT_WS('|',
                       COALESCE(evidence_row.`source_item_type`,''),registry_row.`client_item_id`,
                       COALESCE(CAST(rule_row.`normal_use` AS CHAR),'N'),COALESCE(CAST(rule_row.`battle_use` AS CHAR),'N'),
                       COALESCE(CAST(rule_row.`equippable` AS CHAR),'N'),COALESCE(CAST(rule_row.`use_on_other` AS CHAR),'N'),
                       COALESCE(CAST(rule_row.`hotkey_allowed` AS CHAR),'N'))
                       ORDER BY COALESCE(evidence_row.`source_item_type`,''),registry_row.`client_item_id` SEPARATOR '\n'),256))
              INTO actual_count,actual_hash
              FROM `god2_game`.`item_usage_rules` rule_row
              JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=rule_row.`item_id`
              LEFT JOIN `god2_research`.`item_catalog_evidence` evidence_row
                ON evidence_row.`catalog_table`='item_registry' AND evidence_row.`item_id`=registry_row.`item_id`;
            IF actual_count<>17407 OR actual_hash<>'{{normalizedHash}}' THEN
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Exact-client item usage flag catalog does not match the formal rows';
            END IF;
        END$$
        DELIMITER ;
        CALL `god2_game`.`assert_exact_item_usage_flag_catalog`();
        DROP PROCEDURE `god2_game`.`assert_exact_item_usage_flag_catalog`;

        UPDATE `god2_game`.`item_usage_rules`
        SET `field_evidence_status`='Verified',
            `source_reference_zh_tw`='Exact current client Data2/Patch/Comm/gamedata.csvZ fields 14..18; static flags only',
            `exact_client_source_sha256`='{{sourceSha256}}',
            `activation_evidence_status`='EvidenceBlockedMissingOfficialItemUseTransition',
            `runtime_eligible`=0,
            `enabled`=0;

        ALTER TABLE `god2_game`.`item_usage_rules`
            ADD CONSTRAINT `ck_item_usage_rule_exact_source` CHECK (`exact_client_source_sha256`='{{sourceSha256}}'),
            ADD CONSTRAINT `ck_item_usage_rule_runtime_gate` CHECK (`runtime_eligible`=0 AND `enabled`=0 AND `activation_evidence_status`='EvidenceBlockedMissingOfficialItemUseTransition');

        ALTER TABLE `god2_game`.`item_effects`
            ADD COLUMN IF NOT EXISTS `activation_evidence_status` varchar(64) NOT NULL DEFAULT 'EvidenceBlockedMissingOfficialItemUseTransition' AFTER `evidence_status`,
            ADD COLUMN IF NOT EXISTS `runtime_eligible` tinyint(1) NOT NULL DEFAULT 0 AFTER `activation_evidence_status`;

        UPDATE `god2_game`.`item_effects`
        SET `activation_evidence_status`='EvidenceBlockedMissingOfficialItemUseTransition',
            `runtime_eligible`=0,
            `enabled`=0;

        ALTER TABLE `god2_game`.`item_effects`
            ADD CONSTRAINT `ck_item_effect_runtime_gate` CHECK (`runtime_eligible`=0 AND `enabled`=0 AND `activation_evidence_status`='EvidenceBlockedMissingOfficialItemUseTransition');
        """;
    }

    private static string Bit(bool? value) => value switch { true => "1", false => "0", null => "N" };
    private static string At(IReadOnlyList<string> fields, int index) => index < fields.Count ? fields[index] : string.Empty;
    private static string SqlBit(bool? value) => value switch { true => "1", false => "0", null => "NULL" };
    private static string Q(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static HashSet<(string Section, int ClientItemId)> ReadFormalBaselineKeys(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "db", "imports", "official", "items", "items.official.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.GetProperty("records").EnumerateArray()
            .Where(record => !record.TryGetProperty("presentInFormalBaseline", out var present) || present.GetBoolean())
            .Select(record => (
                record.GetProperty("itemType").GetString() ?? throw new InvalidDataException("Baseline item type is missing."),
                record.GetProperty("clientItemId").GetInt32()))
            .ToHashSet();
    }
}
