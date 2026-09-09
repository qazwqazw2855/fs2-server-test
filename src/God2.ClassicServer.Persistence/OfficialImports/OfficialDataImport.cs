using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using MySqlConnector;

namespace God2.ClassicServer.Persistence.OfficialImports;

public interface IOfficialDataImporter
{
    string Category { get; }

    OperationResult<OfficialImportCategory> Load(string importRoot);
}

public abstract class OfficialJsonImporter : IOfficialDataImporter
{
    protected OfficialJsonImporter(string category)
    {
        Category = category;
    }

    public string Category { get; }

    public OperationResult<OfficialImportCategory> Load(string importRoot)
    {
        var path = Path.Combine(importRoot, Category, $"{Category}.official.json");
        if (!File.Exists(path))
        {
            return OperationResult<OfficialImportCategory>.Failure(
                "official_import.missing",
                $"Official import file is missing for category {Category}.",
                path);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var sourcePath = ReadString(root, "source", "unknown");
            var format = ReadString(root, "format", "JSON");
            var verificationStatus = ReadString(root, "verificationStatus", "Unverified");
            var recoveryStatus = ToRecoveryStatus(verificationStatus);
            var canDirectImport = ReadBoolean(root, "canDirectImportToGameplay", fallback: false);
            var sourceSizeBytes = ReadInt64(root, "sourceSizeBytes", fallback: 0);

            if (!root.TryGetProperty("records", out var recordsElement) || recordsElement.ValueKind != JsonValueKind.Array)
            {
                return OperationResult<OfficialImportCategory>.Failure(
                    "official_import.records_missing",
                    $"Official import file does not contain a records array for category {Category}.",
                    path);
            }

            var records = new List<OfficialImportRecord>();
            var index = 0;
            foreach (var recordElement in recordsElement.EnumerateArray())
            {
                index++;
                var payloadJson = recordElement.GetRawText();
                records.Add(new OfficialImportRecord(
                    Category,
                    NormalizeRecordKey(ExtractRecordKey(recordElement, index)),
                    ExtractRecordKey(recordElement, index),
                    sourcePath,
                    format,
                    verificationStatus,
                    recoveryStatus,
                    canDirectImport,
                    payloadJson,
                    Sha256(payloadJson),
                    ExtractSourceHash(recordElement)));
            }

            return OperationResult<OfficialImportCategory>.Success(new OfficialImportCategory(
                Category,
                sourcePath,
                format,
                sourceSizeBytes,
                records.Count,
                verificationStatus,
                recoveryStatus,
                canDirectImport,
                records));
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return OperationResult<OfficialImportCategory>.Failure("official_import.invalid_json", ex.Message, path);
        }
    }

    protected virtual string ExtractRecordKey(JsonElement record, int index)
    {
        foreach (var propertyName in new[] { "id", "clientItemId", "clientMonsterId", "clientNpcId", "clientQuestId", "clientSkillId", "clientMapId" })
        {
            if (record.TryGetProperty(propertyName, out var value))
            {
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return $"{Category}:{index:000000}";
    }

    private static string ReadString(JsonElement root, string propertyName, string fallback) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool ReadBoolean(JsonElement root, string propertyName, bool fallback) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static long ReadInt64(JsonElement root, string propertyName, long fallback) =>
        root.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var result) ? result : fallback;

    private static string ToRecoveryStatus(string verificationStatus)
    {
        if (verificationStatus.Contains("Needs Recovery", StringComparison.OrdinalIgnoreCase) ||
            verificationStatus.Contains("NeedsRecovery", StringComparison.OrdinalIgnoreCase))
        {
            return "NeedsRecovery";
        }

        if (verificationStatus.Contains("EvidenceOnly", StringComparison.OrdinalIgnoreCase) ||
            verificationStatus.Contains("Unverified", StringComparison.OrdinalIgnoreCase))
        {
            return "EvidenceOnly";
        }

        if (verificationStatus.Contains("Verified", StringComparison.OrdinalIgnoreCase))
        {
            return "Verified";
        }

        if (verificationStatus.Contains("ConfirmedMissing", StringComparison.OrdinalIgnoreCase) ||
            verificationStatus.Contains("CrossReferenceCompleteMissing", StringComparison.OrdinalIgnoreCase))
        {
            return "Missing";
        }

        return "Recovered";
    }

    private static string ExtractSourceHash(JsonElement record)
    {
        foreach (var propertyName in new[] { "sourceHash", "sourceSha256", "sha256" })
        {
            if (TryFindString(record, propertyName, out var value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool TryFindString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    value = property.Value.GetString() ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(value);
                }

                if (TryFindString(property.Value, propertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindString(item, propertyName, out value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string NormalizeRecordKey(string recordKey)
    {
        if (recordKey.Length <= 255)
        {
            return recordKey;
        }

        return $"sha256:{Sha256(recordKey)}";
    }

    private static string Sha256(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class MapImporter : OfficialJsonImporter { public MapImporter() : base("maps") { } }
public sealed class PortalImporter : OfficialJsonImporter { public PortalImporter() : base("portals") { } }
public sealed class NPCImporter : OfficialJsonImporter { public NPCImporter() : base("npcs") { } }
public sealed class MonsterImporter : OfficialJsonImporter { public MonsterImporter() : base("monsters") { } }
public sealed class SpawnImporter : OfficialJsonImporter { public SpawnImporter() : base("spawns") { } }
public sealed class ItemImporter : OfficialJsonImporter { public ItemImporter() : base("items") { } }
public sealed class EquipmentImporter : OfficialJsonImporter { public EquipmentImporter() : base("equipment") { } }
public sealed class SkillImporter : OfficialJsonImporter { public SkillImporter() : base("skills") { } }
public sealed class QuestImporter : OfficialJsonImporter { public QuestImporter() : base("quests") { } }
public sealed class MerchantImporter : OfficialJsonImporter { public MerchantImporter() : base("merchants") { } }
public sealed class ImmortalImporter : OfficialJsonImporter { public ImmortalImporter() : base("immortals") { } }
public sealed class BattlePetImporter : OfficialJsonImporter { public BattlePetImporter() : base("battle_pets") { } }
public sealed class DropTableImporter : OfficialJsonImporter { public DropTableImporter() : base("drop_tables") { } }
public sealed class RewardImporter : OfficialJsonImporter { public RewardImporter() : base("rewards") { } }
public sealed class DialogImporter : OfficialJsonImporter { public DialogImporter() : base("dialogs") { } }
public sealed class LocalizationImporter : OfficialJsonImporter { public LocalizationImporter() : base("localization") { } }
public sealed class AnimationImporter : OfficialJsonImporter { public AnimationImporter() : base("animations") { } }
public sealed class ModelImporter : OfficialJsonImporter { public ModelImporter() : base("models") { } }
public sealed class TextureImporter : OfficialJsonImporter { public TextureImporter() : base("textures") { } }
public sealed class IconImporter : OfficialJsonImporter { public IconImporter() : base("icons") { } }
public sealed class SoundImporter : OfficialJsonImporter { public SoundImporter() : base("sounds") { } }
public sealed class MusicImporter : OfficialJsonImporter { public MusicImporter() : base("music") { } }
public sealed class EffectImporter : OfficialJsonImporter { public EffectImporter() : base("effects") { } }
public sealed class ScriptImporter : OfficialJsonImporter { public ScriptImporter() : base("scripts") { } }
public sealed class LuaImporter : OfficialJsonImporter { public LuaImporter() : base("lua") { } }
public sealed class ContainerImporter : OfficialJsonImporter { public ContainerImporter() : base("containers") { } }
public sealed class ResourceTableImporter : OfficialJsonImporter { public ResourceTableImporter() : base("resource_tables") { } }
public sealed class StringTableImporter : OfficialJsonImporter { public StringTableImporter() : base("string_tables") { } }

public static class OfficialImporters
{
    public static IReadOnlyList<IOfficialDataImporter> All { get; } =
    [
        new MapImporter(),
        new PortalImporter(),
        new NPCImporter(),
        new MonsterImporter(),
        new SpawnImporter(),
        new ItemImporter(),
        new EquipmentImporter(),
        new SkillImporter(),
        new QuestImporter(),
        new MerchantImporter(),
        new ImmortalImporter(),
        new BattlePetImporter(),
        new DropTableImporter(),
        new RewardImporter(),
        new DialogImporter(),
        new LocalizationImporter(),
        new AnimationImporter(),
        new ModelImporter(),
        new TextureImporter(),
        new IconImporter(),
        new SoundImporter(),
        new MusicImporter(),
        new EffectImporter(),
        new ScriptImporter(),
        new LuaImporter(),
        new ContainerImporter(),
        new ResourceTableImporter(),
        new StringTableImporter()
    ];
}

public sealed class OfficialDataImportService
{
    private readonly DatabaseOptions _options;
    private readonly IReadOnlyList<IOfficialDataImporter> _importers;

    public OfficialDataImportService(DatabaseOptions options, IReadOnlyList<IOfficialDataImporter> importers)
    {
        _options = options;
        _importers = importers;
    }

    public async Task<OperationResult<OfficialImportReport>> ImportAsync(string importRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(importRoot))
        {
            return OperationResult<OfficialImportReport>.Failure("official_import.root_missing", "Official import root is missing.", importRoot);
        }

        var categories = new List<OfficialImportCategory>();
        foreach (var importer in _importers)
        {
            var result = importer.Load(importRoot);
            if (!result.Succeeded || result.Value is null)
            {
                return OperationResult<OfficialImportReport>.Failure(result.Error.Code, result.Error.Message, result.Error.Source);
            }

            categories.Add(result.Value);
        }

        var issues = BuildReferenceIssues(categories);
        var importBatch = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var password = MariaDbDatabaseBootstrapper.ResolvePassword(_options);
        if (string.IsNullOrWhiteSpace(password))
        {
            return OperationResult<OfficialImportReport>.Failure("mariadb.password_missing", "MariaDB password is not set.", "database.json:password");
        }

        try
        {
            await using var connection = new MySqlConnection(MariaDbDatabaseBootstrapper.BuildConnectionString(_options, password, _options.DatabaseName));
            await connection.OpenAsync(cancellationToken);
            await EnsureOfficialImportSchemaAsync(connection, cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            foreach (var category in categories)
            {
                await DeleteReferenceIssuesForCategoryAsync(connection, transaction, category.Category, cancellationToken);
                await UpsertCategoryAsync(connection, transaction, category, importBatch, cancellationToken);
                foreach (var record in category.Records)
                {
                    await UpsertRecordAsync(connection, transaction, record, importBatch, cancellationToken);
                }
            }

            foreach (var issue in issues)
            {
                await InsertReferenceIssueAsync(connection, transaction, issue, cancellationToken);
            }

            var formalImportCount = await OfficialRuntimeTableImporter.ImportAsync(connection, transaction, categories, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return OperationResult<OfficialImportReport>.Success(new OfficialImportReport(
                categories.Select(category => new OfficialImportCategoryReport(category.Category, category.RecordCount, category.VerificationStatus, category.CanDirectImportToGameplay)).ToArray(),
                categories.Sum(category => category.RecordCount),
                formalImportCount,
                issues.Count,
                importBatch));
        }
        catch (Exception ex) when (ex is MySqlException or InvalidOperationException)
        {
            return OperationResult<OfficialImportReport>.Failure("official_import.failed", ex.ToString(), importRoot);
        }
    }

    private static async Task EnsureOfficialImportSchemaAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS `god2_research`.`official_import_categories` (
                `Category` varchar(64) NOT NULL,
                `SourcePath` varchar(512) NOT NULL,
                `Format` varchar(64) NOT NULL,
                `SourceSizeBytes` bigint NOT NULL,
                `RecordCount` int NOT NULL,
                `VerificationStatus` varchar(128) NOT NULL,
                `RecoveryStatus` varchar(32) NOT NULL DEFAULT 'Recovered',
                `CanDirectImportToGameplay` tinyint(1) NOT NULL,
                `ImportBatch` varchar(32) NOT NULL DEFAULT 'legacy',
                `ImportedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`Category`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

            CREATE TABLE IF NOT EXISTS `god2_research`.`official_import_records` (
                `Category` varchar(64) NOT NULL,
                `RecordKey` varchar(255) NOT NULL,
                `SourcePath` varchar(512) NOT NULL,
                `Format` varchar(64) NOT NULL,
                `VerificationStatus` varchar(128) NOT NULL,
                `RecoveryStatus` varchar(32) NOT NULL DEFAULT 'Recovered',
                `CanDirectImportToGameplay` tinyint(1) NOT NULL,
                `ImportBatch` varchar(32) NOT NULL DEFAULT 'legacy',
                `OriginalId` varchar(512) NOT NULL DEFAULT '',
                `SourceHash` char(64) NOT NULL DEFAULT '',
                `ImportedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`Category`, `RecordKey`),
                CONSTRAINT `FK_OfficialImportRecords_Categories`
                    FOREIGN KEY (`Category`) REFERENCES `god2_research`.`official_import_categories` (`Category`)
                    ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

            CREATE TABLE IF NOT EXISTS `god2_research`.`official_import_reference_issues` (
                `Category` varchar(64) NOT NULL,
                `RecordKey` varchar(255) NOT NULL,
                `IssueCode` varchar(96) NOT NULL,
                `IssueMessage` varchar(512) NOT NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`Category`, `RecordKey`, `IssueCode`),
                CONSTRAINT `FK_OfficialImportReferenceIssues_Records`
                    FOREIGN KEY (`Category`, `RecordKey`) REFERENCES `god2_research`.`official_import_records` (`Category`, `RecordKey`)
                    ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

            ALTER TABLE `god2_research`.`official_import_categories`
                ADD COLUMN IF NOT EXISTS `RecoveryStatus` varchar(32) NOT NULL DEFAULT 'Recovered' AFTER `VerificationStatus`,
                ADD COLUMN IF NOT EXISTS `ImportBatch` varchar(32) NOT NULL DEFAULT 'legacy' AFTER `CanDirectImportToGameplay`;

            ALTER TABLE `god2_research`.`official_import_records`
                ADD COLUMN IF NOT EXISTS `RecoveryStatus` varchar(32) NOT NULL DEFAULT 'Recovered' AFTER `VerificationStatus`,
                ADD COLUMN IF NOT EXISTS `ImportBatch` varchar(32) NOT NULL DEFAULT 'legacy' AFTER `CanDirectImportToGameplay`,
                ADD COLUMN IF NOT EXISTS `OriginalId` varchar(512) NOT NULL DEFAULT '' AFTER `ImportBatch`,
                ADD COLUMN IF NOT EXISTS `SourceHash` char(64) NOT NULL DEFAULT '' AFTER `OriginalId`;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var researchCommand = connection.CreateCommand();
        researchCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS `god2_research`.`official_import_record_payload_evidence` (
                `category` varchar(64) NOT NULL,
                `record_key` varchar(255) NOT NULL,
                `payload_json` longtext NOT NULL,
                `payload_sha256` char(64) NOT NULL,
                `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                PRIMARY KEY (`category`,`record_key`),
                CONSTRAINT `ck_official_import_payload_sha256`
                    CHECK (CHAR_LENGTH(`payload_sha256`) = 64)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """;
        await researchCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<OfficialImportReferenceIssue> BuildReferenceIssues(IReadOnlyList<OfficialImportCategory> categories)
    {
        var categoryMap = categories.ToDictionary(category => category.Category, StringComparer.Ordinal);
        var mapIds = Keys(categoryMap, "maps");
        var monsterIds = Keys(categoryMap, "monsters");
        var issues = new List<OfficialImportReferenceIssue>();

        AddMissingMapIssues(categoryMap, "portals", "sourceMapId", mapIds, issues);
        AddMissingMapIssues(categoryMap, "portals", "sourceMapIdCandidate", mapIds, issues);
        AddMissingMapIssues(categoryMap, "portals", "destinationMapId", mapIds, issues);
        AddMissingMapIssues(categoryMap, "portals", "destinationMapIdCandidate", mapIds, issues, allowUnknownPrefix: true);
        AddMissingMapIssues(categoryMap, "spawns", "mapId", mapIds, issues);

        if (categoryMap.TryGetValue("drop_tables", out var dropTables))
        {
            foreach (var record in dropTables.Records)
            {
                using var document = JsonDocument.Parse(record.PayloadJson);
                if (TryReadString(document.RootElement, "monsterTemplateId", out var monsterTemplateId) && !monsterIds.Contains(monsterTemplateId))
                {
                    issues.Add(new OfficialImportReferenceIssue(record.Category, record.RecordKey, "missing_monster_template", $"monsterTemplateId is not present in recovered monsters: {monsterTemplateId}"));
                }
            }
        }

        return issues;
    }

    private static HashSet<string> Keys(IReadOnlyDictionary<string, OfficialImportCategory> categories, string category) =>
        categories.TryGetValue(category, out var payload)
            ? payload.Records.Select(record => record.RecordKey).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    private static void AddMissingMapIssues(
        IReadOnlyDictionary<string, OfficialImportCategory> categories,
        string category,
        string propertyName,
        HashSet<string> mapIds,
        ICollection<OfficialImportReferenceIssue> issues,
        bool allowUnknownPrefix = false)
    {
        if (!categories.TryGetValue(category, out var payload))
        {
            return;
        }

        foreach (var record in payload.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            if (!TryReadString(document.RootElement, propertyName, out var mapId))
            {
                continue;
            }

            if (allowUnknownPrefix && mapId.StartsWith("unknown:", StringComparison.Ordinal))
            {
                issues.Add(new OfficialImportReferenceIssue(record.Category, record.RecordKey, $"unresolved_{propertyName}", $"{propertyName} remains unresolved: {mapId}"));
                continue;
            }

            if (!mapIds.Contains(mapId))
            {
                issues.Add(new OfficialImportReferenceIssue(record.Category, record.RecordKey, $"missing_{propertyName}", $"{propertyName} is not present in recovered maps: {mapId}"));
            }
        }
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static async Task DeleteReferenceIssuesForCategoryAsync(MySqlConnection connection, MySqlTransaction transaction, string category, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM `god2_research`.`official_import_reference_issues` WHERE `Category` = @category;";
        command.Parameters.AddWithValue("@category", category);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertCategoryAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportCategory category, string importBatch, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO `god2_research`.`official_import_categories`
                (`Category`, `SourcePath`, `Format`, `SourceSizeBytes`, `RecordCount`, `VerificationStatus`, `RecoveryStatus`, `CanDirectImportToGameplay`, `ImportBatch`, `ImportedAtUtc`)
            VALUES
                (@category, @sourcePath, @format, @sourceSizeBytes, @recordCount, @verificationStatus, @recoveryStatus, @canDirectImportToGameplay, @importBatch, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                `SourcePath` = VALUES(`SourcePath`),
                `Format` = VALUES(`Format`),
                `SourceSizeBytes` = VALUES(`SourceSizeBytes`),
                `RecordCount` = VALUES(`RecordCount`),
                `VerificationStatus` = VALUES(`VerificationStatus`),
                `RecoveryStatus` = VALUES(`RecoveryStatus`),
                `CanDirectImportToGameplay` = VALUES(`CanDirectImportToGameplay`),
                `ImportBatch` = VALUES(`ImportBatch`),
                `ImportedAtUtc` = VALUES(`ImportedAtUtc`);
            """;
        command.Parameters.AddWithValue("@category", category.Category);
        command.Parameters.AddWithValue("@sourcePath", category.SourcePath);
        command.Parameters.AddWithValue("@format", category.Format);
        command.Parameters.AddWithValue("@sourceSizeBytes", category.SourceSizeBytes);
        command.Parameters.AddWithValue("@recordCount", category.RecordCount);
        command.Parameters.AddWithValue("@verificationStatus", category.VerificationStatus);
        command.Parameters.AddWithValue("@recoveryStatus", category.RecoveryStatus);
        command.Parameters.AddWithValue("@canDirectImportToGameplay", category.CanDirectImportToGameplay);
        command.Parameters.AddWithValue("@importBatch", importBatch);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertRecordAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportRecord record, string importBatch, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO `god2_research`.`official_import_records`
                (`Category`, `RecordKey`, `SourcePath`, `Format`, `VerificationStatus`, `RecoveryStatus`, `CanDirectImportToGameplay`, `ImportBatch`, `OriginalId`, `SourceHash`, `ImportedAtUtc`)
            VALUES
                (@category, @recordKey, @sourcePath, @format, @verificationStatus, @recoveryStatus, @canDirectImportToGameplay, @importBatch, @originalId, @sourceHash, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                `SourcePath` = VALUES(`SourcePath`),
                `Format` = VALUES(`Format`),
                `VerificationStatus` = VALUES(`VerificationStatus`),
                `RecoveryStatus` = VALUES(`RecoveryStatus`),
                `CanDirectImportToGameplay` = VALUES(`CanDirectImportToGameplay`),
                `ImportBatch` = VALUES(`ImportBatch`),
                `OriginalId` = VALUES(`OriginalId`),
                `SourceHash` = VALUES(`SourceHash`),
                `ImportedAtUtc` = VALUES(`ImportedAtUtc`);
            """;
        command.Parameters.AddWithValue("@category", record.Category);
        command.Parameters.AddWithValue("@recordKey", record.RecordKey);
        command.Parameters.AddWithValue("@sourcePath", record.SourcePath);
        command.Parameters.AddWithValue("@format", record.Format);
        command.Parameters.AddWithValue("@verificationStatus", record.VerificationStatus);
        command.Parameters.AddWithValue("@recoveryStatus", record.RecoveryStatus);
        command.Parameters.AddWithValue("@canDirectImportToGameplay", record.CanDirectImportToGameplay);
        command.Parameters.AddWithValue("@importBatch", importBatch);
        command.Parameters.AddWithValue("@originalId", record.OriginalId);
        command.Parameters.AddWithValue("@sourceHash", record.SourceHash);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await UpsertOfficialImportRecordPayloadAsync(connection, transaction, record, cancellationToken);
    }

    private static async Task UpsertOfficialImportRecordPayloadAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportRecord record, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO `god2_research`.`official_import_record_payload_evidence`
                (`category`,`record_key`,`payload_json`,`payload_sha256`)
            VALUES
                (@category,@recordKey,@payloadJson,@payloadSha256)
            ON DUPLICATE KEY UPDATE
                `payload_json`=VALUES(`payload_json`),
                `payload_sha256`=VALUES(`payload_sha256`);
            """;
        command.Parameters.AddWithValue("@category", record.Category);
        command.Parameters.AddWithValue("@recordKey", record.RecordKey);
        command.Parameters.AddWithValue("@payloadJson", record.PayloadJson);
        command.Parameters.AddWithValue("@payloadSha256", record.PayloadSha256);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertReferenceIssueAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportReferenceIssue issue, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO `god2_research`.`official_import_reference_issues`
                (`Category`, `RecordKey`, `IssueCode`, `IssueMessage`, `CreatedAtUtc`)
            VALUES
                (@category, @recordKey, @issueCode, @issueMessage, UTC_TIMESTAMP(6));
            """;
        command.Parameters.AddWithValue("@category", issue.Category);
        command.Parameters.AddWithValue("@recordKey", issue.RecordKey);
        command.Parameters.AddWithValue("@issueCode", issue.IssueCode);
        command.Parameters.AddWithValue("@issueMessage", issue.IssueMessage);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

internal static class OfficialRuntimeTableImporter
{
    private static readonly IReadOnlySet<string> RuntimeCategories = new HashSet<string>(StringComparer.Ordinal)
    {
        "maps",
        "portals",
        "items",
        "skills",
        "npcs",
        "monsters",
        "quests",
        "merchants",
        "dialogs",
        "drop_tables",
        "localization"
    };

    public static async Task<int> ImportAsync(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<OfficialImportCategory> categories, CancellationToken cancellationToken)
    {
        await EnsureFormalRuntimeSchemaAsync(connection, transaction, cancellationToken);

        var maps = CreateIdMap(categories, "maps");
        var npcs = CreateIdMap(categories, "npcs");
        var monsters = CreateIdMap(categories, "monsters");
        var total = 0;

        total += await ImportMapsAsync(connection, transaction, Category(categories, "maps"), maps, cancellationToken);
        total += await ImportItemsAsync(connection, transaction, Category(categories, "items"), cancellationToken);
        total += await ImportSkillsAsync(connection, transaction, Category(categories, "skills"), cancellationToken);
        total += await ImportNpcsAsync(connection, transaction, Category(categories, "npcs"), npcs, maps, cancellationToken);
        total += await ImportMonstersAsync(connection, transaction, Category(categories, "monsters"), monsters, cancellationToken);
        total += await ImportQuestsAsync(connection, transaction, Category(categories, "quests"), npcs, cancellationToken);
        total += await ImportMerchantsAsync(connection, transaction, Category(categories, "merchants"), npcs, cancellationToken);
        total += await ImportPortalsAsync(connection, transaction, Category(categories, "portals"), maps, cancellationToken);
        total += await ImportDropTablesAsync(connection, transaction, Category(categories, "drop_tables"), monsters, cancellationToken);
        total += await ImportDialogsAsync(connection, transaction, Category(categories, "dialogs"), cancellationToken);
        total += await ImportLocalizationAsync(connection, transaction, Category(categories, "localization"), cancellationToken);

        return total;
    }

    private static async Task EnsureFormalRuntimeSchemaAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            "ALTER TABLE `maps` MODIFY COLUMN `Width` int NULL;",
            "ALTER TABLE `maps` MODIFY COLUMN `Height` int NULL;",
            "ALTER TABLE `portals` MODIFY COLUMN `SourceMapId` int NULL;",
            "ALTER TABLE `portals` MODIFY COLUMN `SourceX` int NULL;",
            "ALTER TABLE `portals` MODIFY COLUMN `SourceY` int NULL;",
            "ALTER TABLE `portals` MODIFY COLUMN `TargetMapId` int NULL;",
            "ALTER TABLE `portals` MODIFY COLUMN `TargetX` int NULL;",
            "ALTER TABLE `portals` MODIFY COLUMN `TargetY` int NULL;",
            "ALTER TABLE `items` MODIFY COLUMN `ItemType` varchar(32) NULL;",
            "ALTER TABLE `items` MODIFY COLUMN `MaxStack` int NULL;",
            "ALTER TABLE `items` MODIFY COLUMN `SellPrice` bigint NULL;",
            "ALTER TABLE `skills` MODIFY COLUMN `MaxLevel` int NULL;",
            "ALTER TABLE `skills` MODIFY COLUMN `RequiredLevel` int NULL;",
            "ALTER TABLE `npcs` MODIFY COLUMN `MapId` int NULL;",
            "ALTER TABLE `npcs` MODIFY COLUMN `PositionX` int NULL;",
            "ALTER TABLE `npcs` MODIFY COLUMN `PositionY` int NULL;",
            "ALTER TABLE `monsters` MODIFY COLUMN `Level` int NULL;",
            "ALTER TABLE `monsters` MODIFY COLUMN `MaxHp` bigint NULL;",
            "ALTER TABLE `monsters` MODIFY COLUMN `Attack` int NULL;",
            "ALTER TABLE `monsters` MODIFY COLUMN `Defense` int NULL;",
            "ALTER TABLE `quests` MODIFY COLUMN `RequiredLevel` int NULL;",
            "ALTER TABLE `merchants` MODIFY COLUMN `NpcId` int NULL;",
            "ALTER TABLE `drop_tables` MODIFY COLUMN `MonsterId` int NULL;",
            "ALTER TABLE `dialogs` MODIFY COLUMN `TextKey` varchar(128) NULL;",
            "ALTER TABLE `localization_entries` MODIFY COLUMN `Language` varchar(32) NOT NULL;",
            "ALTER TABLE `localization_entries` MODIFY COLUMN `TextValue` longtext NOT NULL;"
        };

        foreach (var commandText in commands)
        {
            await ExecuteAsync(connection, transaction, commandText, cancellationToken);
        }

        foreach (var table in new[] { "maps", "portals", "items", "skills", "npcs", "monsters", "quests", "merchants", "drop_tables", "dialogs" })
        {
            await AddColumnAsync(connection, transaction, table, "`RecoveryStatus` varchar(32) NOT NULL DEFAULT 'Recovered'", cancellationToken);
        }
    }

    private static async Task AddColumnAsync(MySqlConnection connection, MySqlTransaction transaction, string table, string definition, CancellationToken cancellationToken) =>
        await ExecuteAsync(connection, transaction, $"ALTER TABLE `{table}` ADD COLUMN IF NOT EXISTS {definition};", cancellationToken);

    private static async Task ExecuteAsync(MySqlConnection connection, MySqlTransaction transaction, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ImportMapsAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportCategory? category, IReadOnlyDictionary<string, int> ids, CancellationToken cancellationToken)
    {
        if (category is null)
        {
            return 0;
        }

        foreach (var record in category.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            var width = TryGetInt(root, ["mbd", "gridWidthCandidate"]);
            var height = TryGetInt(root, ["mbd", "gridHeightCandidate"]);
            await ExecuteRuntimeUpsertAsync(connection, transaction, """
                INSERT INTO `maps` (`Id`, `Code`, `Name`, `Width`, `Height`, `CreatedAtUtc`, `UpdatedAtUtc`, `RecoveryStatus`)
                VALUES (@id, @code, @name, @width, @height, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @recoveryStatus)
                ON DUPLICATE KEY UPDATE
                    `Name` = VALUES(`Name`), `Width` = VALUES(`Width`), `Height` = VALUES(`Height`), `UpdatedAtUtc` = UTC_TIMESTAMP(6),
                    `RecoveryStatus` = VALUES(`RecoveryStatus`);
                """, record, ids[record.RecordKey], Code(record), FirstString(root, "displayNameCandidate", "resourceName", "id"), extra =>
                {
                    extra("@width", width);
                    extra("@height", height);
                }, cancellationToken);
            await UpsertLegacyOfficialRuntimePayloadAsync(connection, transaction, "maps", ids[record.RecordKey].ToString(System.Globalization.CultureInfo.InvariantCulture), record, cancellationToken);
        }

        return category.RecordCount;
    }

    private static async Task<int> ImportItemsAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportCategory? category, CancellationToken cancellationToken)
    {
        if (category is null)
        {
            return 0;
        }

        var ids = CreateIdMap(category);
        foreach (var record in category.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            await ExecuteRuntimeUpsertAsync(connection, transaction, """
                INSERT INTO `items` (`Id`, `Code`, `Name`, `ItemType`, `MaxStack`, `SellPrice`, `RecoveryStatus`)
                VALUES (@id, @code, @name, @itemType, @maxStack, @sellPrice, @recoveryStatus)
                ON DUPLICATE KEY UPDATE
                    `Name` = VALUES(`Name`), `ItemType` = VALUES(`ItemType`), `MaxStack` = VALUES(`MaxStack`), `SellPrice` = VALUES(`SellPrice`),
                    `RecoveryStatus` = VALUES(`RecoveryStatus`);
                """, record, ids[record.RecordKey], Code(record), FirstString(root, "name", "id"), extra =>
                {
                    extra("@itemType", ReadString(root, "itemType"));
                    extra("@maxStack", TryGetInt(root, "stackLimit"));
                    extra("@sellPrice", TryGetLong(root, ["priceCandidates", "systemSell"]));
                }, cancellationToken);
            await UpsertLegacyOfficialRuntimePayloadAsync(connection, transaction, "items", ids[record.RecordKey].ToString(System.Globalization.CultureInfo.InvariantCulture), record, cancellationToken);
        }

        return category.RecordCount;
    }

    private static async Task<int> ImportSkillsAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportCategory? category, CancellationToken cancellationToken)
    {
        if (category is null)
        {
            return 0;
        }

        var ids = CreateIdMap(category);
        foreach (var record in category.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            await ExecuteRuntimeUpsertAsync(connection, transaction, """
                INSERT INTO `skills` (`Id`, `Code`, `Name`, `MaxLevel`, `RequiredLevel`, `RecoveryStatus`)
                VALUES (@id, @code, @name, @maxLevel, @requiredLevel, @recoveryStatus)
                ON DUPLICATE KEY UPDATE
                    `Name` = VALUES(`Name`), `MaxLevel` = VALUES(`MaxLevel`), `RequiredLevel` = VALUES(`RequiredLevel`),
                    `RecoveryStatus` = VALUES(`RecoveryStatus`);
                """, record, ids[record.RecordKey], Code(record), FirstString(root, "name", "id"), extra =>
                {
                    extra("@maxLevel", TryGetInt(root, "maxLevel"));
                    extra("@requiredLevel", TryGetInt(root, "level"));
                }, cancellationToken);
            await ExecuteRuntimeUpsertAsync(connection, transaction, """
                INSERT INTO `god2_research`.`legacy_skill_catalog_evidence`
                    (`skill_id`,`payload_json`,`payload_sha256`)
                VALUES (@id,@payloadJson,@payloadSha256)
                ON DUPLICATE KEY UPDATE
                    `payload_json`=VALUES(`payload_json`),
                    `payload_sha256`=VALUES(`payload_sha256`);
                """, record, ids[record.RecordKey], Code(record), FirstString(root, "name", "id"), _ => { }, cancellationToken);
        }

        return category.RecordCount;
    }

    private static async Task<int> ImportNpcsAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportCategory? category, IReadOnlyDictionary<string, int> ids, IReadOnlyDictionary<string, int> maps, CancellationToken cancellationToken)
    {
        if (category is null)
        {
            return 0;
        }

        foreach (var record in category.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            var mapId = FirstMapReference(root, maps);
            await ExecuteRuntimeUpsertAsync(connection, transaction, """
                INSERT INTO `npcs` (`Id`, `Code`, `Name`, `MapId`, `PositionX`, `PositionY`, `RecoveryStatus`)
                VALUES (@id, @code, @name, @mapId, @x, @y, @recoveryStatus)
                ON DUPLICATE KEY UPDATE
                    `Name` = VALUES(`Name`), `MapId` = VALUES(`MapId`), `PositionX` = VALUES(`PositionX`), `PositionY` = VALUES(`PositionY`),
                    `RecoveryStatus` = VALUES(`RecoveryStatus`);
                """, record, ids[record.RecordKey], Code(record), FirstString(root, "name", "id"), extra =>
                {
                    extra("@mapId", mapId);
                    extra("@x", TryGetInt(root, ["templatePosition", "x"]));
                    extra("@y", TryGetInt(root, ["templatePosition", "y"]));
                }, cancellationToken);
            await UpsertLegacyOfficialRuntimePayloadAsync(connection, transaction, "npcs", ids[record.RecordKey].ToString(System.Globalization.CultureInfo.InvariantCulture), record, cancellationToken);
        }

        return category.RecordCount;
    }

    private static async Task<int> ImportMonstersAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportCategory? category, IReadOnlyDictionary<string, int> ids, CancellationToken cancellationToken)
    {
        if (category is null)
        {
            return 0;
        }

        foreach (var record in category.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            await ExecuteRuntimeUpsertAsync(connection, transaction, """
                INSERT INTO `monsters` (`Id`, `Code`, `Name`, `Level`, `MaxHp`, `Attack`, `Defense`, `RecoveryStatus`)
                VALUES (@id, @code, @name, @level, @maxHp, @attack, @defense, @recoveryStatus)
                ON DUPLICATE KEY UPDATE
                    `Name` = VALUES(`Name`), `Level` = VALUES(`Level`), `MaxHp` = VALUES(`MaxHp`), `Attack` = VALUES(`Attack`), `Defense` = VALUES(`Defense`),
                    `RecoveryStatus` = VALUES(`RecoveryStatus`);
                """, record, ids[record.RecordKey], Code(record), FirstString(root, "name", "id"), extra =>
                {
                    extra("@level", TryGetInt(root, "levelCandidate"));
                    extra("@maxHp", null);
                    extra("@attack", null);
                    extra("@defense", null);
                }, cancellationToken);
            await UpsertLegacyOfficialRuntimePayloadAsync(connection, transaction, "monsters", ids[record.RecordKey].ToString(System.Globalization.CultureInfo.InvariantCulture), record, cancellationToken);
        }

        return category.RecordCount;
    }

    private static async Task<int> ImportQuestsAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportCategory? category, IReadOnlyDictionary<string, int> npcs, CancellationToken cancellationToken)
    {
        if (category is null)
        {
            return 0;
        }

        var ids = CreateQuestIdMap(category);
        foreach (var record in category.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            var questId = ids[record.RecordKey];
            await ExecuteRuntimeUpsertAsync(connection, transaction, """
                INSERT INTO `quests` (`Id`, `Code`, `Name`, `RequiredLevel`, `StartNpcId`, `EndNpcId`, `RecoveryStatus`)
                VALUES (@id, @code, @name, @requiredLevel, @startNpcId, @endNpcId, @recoveryStatus)
                ON DUPLICATE KEY UPDATE
                    `Name` = VALUES(`Name`), `RequiredLevel` = VALUES(`RequiredLevel`), `StartNpcId` = VALUES(`StartNpcId`), `EndNpcId` = VALUES(`EndNpcId`),
                    `RecoveryStatus` = VALUES(`RecoveryStatus`);
                """, record, questId, $"quest_{questId}", FirstString(root, "name", "id"), extra =>
                {
                    extra("@requiredLevel", null);
                    extra("@startNpcId", ReadReferenceId(root, "startNpcReference", npcs));
                    extra("@endNpcId", ReadReferenceId(root, "endNpcReference", npcs));
                }, cancellationToken);
            await UpsertLegacyOfficialRuntimePayloadAsync(connection, transaction, "quests", questId.ToString(System.Globalization.CultureInfo.InvariantCulture), record, cancellationToken);
        }

        return category.RecordCount;
    }

    private static async Task<int> ImportMerchantsAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportCategory? category, IReadOnlyDictionary<string, int> npcs, CancellationToken cancellationToken)
    {
        if (category is null)
        {
            return 0;
        }

        var ids = CreateIdMap(category);
        foreach (var record in category.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            int? npcId = TryReadString(root, "npcTemplateId", out var npcKey) && npcs.TryGetValue(npcKey, out var id) ? id : null;
            await ExecuteRuntimeUpsertAsync(connection, transaction, """
                INSERT INTO `merchants` (`Id`, `NpcId`, `Name`, `RecoveryStatus`)
                VALUES (@id, @npcId, @name, @recoveryStatus)
                ON DUPLICATE KEY UPDATE
                    `NpcId` = VALUES(`NpcId`), `Name` = VALUES(`Name`), `RecoveryStatus` = VALUES(`RecoveryStatus`),
                    `RecoveryStatus` = VALUES(`RecoveryStatus`);
                """, record, ids[record.RecordKey], Code(record), FirstString(root, "name", "id"), extra => extra("@npcId", npcId), cancellationToken);
            await UpsertLegacyOfficialRuntimePayloadAsync(connection, transaction, "merchants", ids[record.RecordKey].ToString(System.Globalization.CultureInfo.InvariantCulture), record, cancellationToken);
        }

        return category.RecordCount;
    }

    private static async Task<int> ImportPortalsAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportCategory? category, IReadOnlyDictionary<string, int> maps, CancellationToken cancellationToken)
    {
        if (category is null)
        {
            return 0;
        }

        var ids = CreateIdMap(category);
        foreach (var record in category.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            await ExecuteRuntimeUpsertAsync(connection, transaction, """
                INSERT INTO `portals` (`Id`, `SourceMapId`, `SourceX`, `SourceY`, `TargetMapId`, `TargetX`, `TargetY`, `Name`, `RecoveryStatus`)
                VALUES (@id, @sourceMapId, @sourceX, @sourceY, @targetMapId, @targetX, @targetY, @name, @recoveryStatus)
                ON DUPLICATE KEY UPDATE
                    `SourceMapId` = VALUES(`SourceMapId`), `TargetMapId` = VALUES(`TargetMapId`), `Name` = VALUES(`Name`),
                    `SourceX` = VALUES(`SourceX`), `SourceY` = VALUES(`SourceY`), `TargetX` = VALUES(`TargetX`), `TargetY` = VALUES(`TargetY`),
                    `RecoveryStatus` = VALUES(`RecoveryStatus`);
                """, record, ids[record.RecordKey], Code(record), FirstString(root, "id"), extra =>
                {
                    extra("@sourceMapId", ReadMapId(root, "sourceMapId", maps));
                    extra("@targetMapId", ReadMapId(root, "destinationMapIdCandidate", maps));
                    extra("@sourceX", TryGetInt(root, ["sourcePosition", "x"]));
                    extra("@sourceY", TryGetInt(root, ["sourcePosition", "y"]));
                    extra("@targetX", TryGetInt(root, ["destinationPosition", "x"]));
                    extra("@targetY", TryGetInt(root, ["destinationPosition", "y"]));
                }, cancellationToken);
            await UpsertLegacyOfficialRuntimePayloadAsync(connection, transaction, "portals", ids[record.RecordKey].ToString(System.Globalization.CultureInfo.InvariantCulture), record, cancellationToken);
        }

        return category.RecordCount;
    }

    private static async Task<int> ImportDropTablesAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportCategory? category, IReadOnlyDictionary<string, int> monsters, CancellationToken cancellationToken)
    {
        if (category is null)
        {
            return 0;
        }

        var ids = CreateIdMap(category);
        foreach (var record in category.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            await ExecuteRuntimeUpsertAsync(connection, transaction, """
                INSERT INTO `drop_tables` (`Id`, `MonsterId`, `Name`, `RecoveryStatus`)
                VALUES (@id, @monsterId, @name, @recoveryStatus)
                ON DUPLICATE KEY UPDATE
                    `MonsterId` = VALUES(`MonsterId`), `Name` = VALUES(`Name`), `RecoveryStatus` = VALUES(`RecoveryStatus`),
                    `RecoveryStatus` = VALUES(`RecoveryStatus`);
                """, record, ids[record.RecordKey], Code(record), FirstString(root, "id"), extra => extra("@monsterId", ReadMapId(root, "monsterTemplateId", monsters)), cancellationToken);
            await UpsertLegacyOfficialRuntimePayloadAsync(connection, transaction, "drop_tables", ids[record.RecordKey].ToString(System.Globalization.CultureInfo.InvariantCulture), record, cancellationToken);
        }

        return category.RecordCount;
    }

    private static async Task<int> ImportDialogsAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportCategory? category, CancellationToken cancellationToken)
    {
        if (category is null)
        {
            return 0;
        }

        var ids = CreateIdMap(category);
        foreach (var record in category.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            await ExecuteRuntimeUpsertAsync(connection, transaction, """
                INSERT INTO `dialogs` (`Id`, `Code`, `NpcId`, `TextKey`, `RecoveryStatus`)
                VALUES (@id, @code, NULL, @textKey, @recoveryStatus)
                ON DUPLICATE KEY UPDATE
                    `NpcId` = VALUES(`NpcId`), `TextKey` = VALUES(`TextKey`), `RecoveryStatus` = VALUES(`RecoveryStatus`);
                """, record, ids[record.RecordKey], Code(record), FirstString(root, "id"), extra => extra("@textKey", TextKey(record)), cancellationToken);
            await UpsertLegacyOfficialRuntimePayloadAsync(connection, transaction, "dialogs", ids[record.RecordKey].ToString(System.Globalization.CultureInfo.InvariantCulture), record, cancellationToken);
        }

        return category.RecordCount;
    }

    private static async Task<int> ImportLocalizationAsync(MySqlConnection connection, MySqlTransaction transaction, OfficialImportCategory? category, CancellationToken cancellationToken)
    {
        if (category is null)
        {
            return 0;
        }

        foreach (var record in category.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            var textKey = TextKey(record);
            await ExecuteRuntimeUpsertAsync(connection, transaction, """
                INSERT INTO `localization_entries` (`Language`, `TextKey`, `TextValue`, `UpdatedAtUtc`)
                VALUES ('official-recovery', @textKey, @textValue, UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE
                    `TextValue` = VALUES(`TextValue`), `UpdatedAtUtc` = UTC_TIMESTAMP(6);
                """, record, 0, Code(record), FirstString(root, "id"), extra =>
                {
                    extra("@textKey", textKey);
                    extra("@textValue", FirstString(root, "sourcePath", "id"));
                }, cancellationToken);
            await UpsertLegacyOfficialRuntimePayloadAsync(connection, transaction, "localization_entries", $"official-recovery:{textKey}", record, cancellationToken);
        }

        return category.RecordCount;
    }

    private static async Task ExecuteRuntimeUpsertAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string commandText,
        OfficialImportRecord record,
        int id,
        string code,
        string name,
        Action<Action<string, object?>> addExtraParameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@name", Truncate(name, 128));
        command.Parameters.AddWithValue("@recoveryStatus", "Recovered");
        command.Parameters.AddWithValue("@sourceReference", Truncate(record.SourcePath, 512));
        command.Parameters.AddWithValue("@payloadJson", record.PayloadJson);
        command.Parameters.AddWithValue("@payloadSha256", record.PayloadSha256);
        addExtraParameters((name, value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertLegacyOfficialRuntimePayloadAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string formalTable,
        string recordIdentity,
        OfficialImportRecord record,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO `god2_research`.`legacy_official_runtime_payload_evidence`
                (`formal_table`,`record_identity`,`source_reference`,`recovery_status`,`payload_json`,`payload_sha256`)
            VALUES
                (@formalTable,@recordIdentity,@sourceReference,@recoveryStatus,@payloadJson,@payloadSha256)
            ON DUPLICATE KEY UPDATE
                `source_reference`=VALUES(`source_reference`),
                `recovery_status`=VALUES(`recovery_status`),
                `payload_json`=VALUES(`payload_json`),
                `payload_sha256`=VALUES(`payload_sha256`);
            """;
        command.Parameters.AddWithValue("@formalTable", formalTable);
        command.Parameters.AddWithValue("@recordIdentity", recordIdentity);
        command.Parameters.AddWithValue("@sourceReference", Truncate(record.SourcePath, 512));
        command.Parameters.AddWithValue("@recoveryStatus", "Recovered");
        command.Parameters.AddWithValue("@payloadJson", record.PayloadJson);
        command.Parameters.AddWithValue("@payloadSha256", record.PayloadSha256);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static OfficialImportCategory? Category(IReadOnlyList<OfficialImportCategory> categories, string category) =>
        categories.FirstOrDefault(item => string.Equals(item.Category, category, StringComparison.Ordinal));

    private static IReadOnlyDictionary<string, int> CreateIdMap(IReadOnlyList<OfficialImportCategory> categories, string category) =>
        Category(categories, category) is { } found ? CreateIdMap(found) : new Dictionary<string, int>(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, int> CreateIdMap(OfficialImportCategory category)
    {
        var used = new HashSet<int>();
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var record in category.Records)
        {
            var id = StablePositiveInt(record.RecordKey);
            while (!used.Add(id))
            {
                id++;
                if (id == int.MaxValue)
                {
                    id = 1;
                }
            }

            result[record.RecordKey] = id;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, int> CreateQuestIdMap(OfficialImportCategory category)
    {
        var used = new HashSet<int>();
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var record in category.Records)
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var clientQuestId = TryGetInt(document.RootElement, "clientQuestId");
            var id = clientQuestId is > 0 ? clientQuestId.Value : StablePositiveInt(record.RecordKey);
            while (!used.Add(id))
            {
                id++;
                if (id == int.MaxValue)
                {
                    id = 1;
                }
            }

            result[record.RecordKey] = id;
        }

        return result;
    }

    private static int StablePositiveInt(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var id = BitConverter.ToInt32(hash, 0) & 0x7fffffff;
        return id == 0 ? 1 : id;
    }

    private static int? FirstMapReference(JsonElement root, IReadOnlyDictionary<string, int> maps)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty("crossReferences", out var crossReferences) ||
            crossReferences.ValueKind != JsonValueKind.Object ||
            !crossReferences.TryGetProperty("mapReferences", out var mapReferences) ||
            mapReferences.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var reference in mapReferences.EnumerateArray())
        {
            if (reference.ValueKind == JsonValueKind.String &&
                maps.TryGetValue(reference.GetString() ?? string.Empty, out var mapId))
            {
                return mapId;
            }
        }

        return null;
    }

    private static int? ReadReferenceId(JsonElement root, string propertyName, IReadOnlyDictionary<string, int> ids) =>
        TryReadString(root, propertyName, out var key) && ids.TryGetValue(key, out var id) ? id : null;

    private static int? ReadMapId(JsonElement root, string propertyName, IReadOnlyDictionary<string, int> ids) =>
        TryReadString(root, propertyName, out var key) && ids.TryGetValue(key, out var id) ? id : null;

    private static string Code(OfficialImportRecord record)
    {
        using var document = JsonDocument.Parse(record.PayloadJson);
        var root = document.RootElement;
        var category = record.Category.ToLowerInvariant();

        var itemId = TryGetInt(root, "clientItemId");
        if (category == "items" && itemId is > 0)
        {
            return $"item_{itemId.Value}";
        }

        var questId = TryGetInt(root, "clientQuestId");
        if (category == "quests" && questId is > 0)
        {
            return $"quest_{questId.Value}";
        }

        var monsterId = TryGetInt(root, "clientMonsterId");
        if (category == "monsters" && monsterId is > 0)
        {
            return $"monster_{monsterId.Value}";
        }

        var sourceIdentity = ReadString(root, "id") ?? record.RecordKey;
        return ReadableIdentity(category.TrimEnd('s'), sourceIdentity);
    }

    private static string TextKey(OfficialImportRecord record)
    {
        using var document = JsonDocument.Parse(record.PayloadJson);
        var sourceIdentity = ReadString(document.RootElement, "id") ?? record.RecordKey;
        return $"official.{record.Category.ToLowerInvariant()}.{ReadableIdentity("source", sourceIdentity)}";
    }

    private static string ReadableIdentity(string prefix, string identity)
    {
        var builder = new StringBuilder(prefix.Length + identity.Length + 1);
        builder.Append(prefix).Append('_');
        var pendingSeparator = false;
        foreach (var character in identity.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (pendingSeparator && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(character);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        var result = builder.ToString().TrimEnd('_');
        if (result.Length <= 64)
        {
            return result;
        }

        var suffix = $"_{StablePositiveInt(identity)}";
        return result[..(64 - suffix.Length)].TrimEnd('_') + suffix;
    }

    private static string FirstString(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryReadString(root, propertyName, out var value))
            {
                return value;
            }
        }

        return "Recovered";
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        TryReadString(root, propertyName, out var value) ? value : null;

    private static bool TryReadString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            value = property.GetRawText();
            return true;
        }

        return false;
    }

    private static int? TryGetInt(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return ReadInt(property);
    }

    private static int? TryGetInt(JsonElement root, IReadOnlyList<string> path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return ReadInt(current);
    }

    private static long? TryGetLong(JsonElement root, IReadOnlyList<string> path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetInt64(out var number))
        {
            return number;
        }

        return current.ValueKind == JsonValueKind.String && long.TryParse(current.GetString(), out var parsed) ? parsed : null;
    }

    private static int? ReadInt(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
        {
            return number;
        }

        return element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed) ? parsed : null;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

public sealed record OfficialImportCategory(
    string Category,
    string SourcePath,
    string Format,
    long SourceSizeBytes,
    int RecordCount,
    string VerificationStatus,
    string RecoveryStatus,
    bool CanDirectImportToGameplay,
    IReadOnlyList<OfficialImportRecord> Records);

public sealed record OfficialImportRecord(
    string Category,
    string RecordKey,
    string OriginalId,
    string SourcePath,
    string Format,
    string VerificationStatus,
    string RecoveryStatus,
    bool CanDirectImportToGameplay,
    string PayloadJson,
    string PayloadSha256,
    string SourceHash);

public sealed record OfficialImportReferenceIssue(string Category, string RecordKey, string IssueCode, string IssueMessage);

public sealed record OfficialImportReport(
    IReadOnlyList<OfficialImportCategoryReport> Categories,
    int ImportedRecordCount,
    int FormalImportedRecordCount,
    int ReferenceIssueCount,
    string ImportBatch);

public sealed record OfficialImportCategoryReport(string Category, int RecordCount, string VerificationStatus, bool CanDirectImportToGameplay);

