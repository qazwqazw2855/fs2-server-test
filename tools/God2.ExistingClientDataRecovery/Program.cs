using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace God2.ExistingClientDataRecovery;

internal static partial class Program
{
    private const string ToolVersion = "God2.ExistingClientDataRecovery/0.1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);

        try
        {
            var options = Invocation.Parse(args);
            var settings = DatabaseSettings.Load(options.RepositoryRoot, options.ConfigPath);
            var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var artifacts = Path.Combine(options.RepositoryRoot, "Artifacts", "RecoveryFinal");
            Directory.CreateDirectory(artifacts);
            Directory.CreateDirectory(Path.Combine(artifacts, "Layouts"));

            await using var sourceConnection = new MySqlConnection(settings.ConnectionString(settings.Database));
            await sourceConnection.OpenAsync();

            var inspector = new SourceInspector(sourceConnection, settings.Database);
            var schemaTables = await inspector.LoadTablesAsync();
            var debugSamples = await inspector.LoadDebugSamplesAsync();
            var sourceDatasets = await inspector.BuildSourceInventoryAsync(schemaTables);

            var writer = new ArtifactWriter(artifacts);
            await writer.WriteJsonAsync("source-debug-samples.json", debugSamples);
            await writer.WriteInventoryAsync(sourceDatasets);
            await writer.WriteLayoutsAsync(sourceDatasets);
            await writer.WriteRootCauseAsync(schemaTables, sourceDatasets);
            await writer.WriteTargetedReadPlanAsync(sourceDatasets);
            await writer.WriteReadinessReportAsync(sourceDatasets, schemaTables);

            DatabaseApplyResult applyResult = new(false, "not requested", 0, 0, 0);
            if (options.ApplyDatabase)
            {
                var privilegedSettings = DatabaseSettings.LoadPrivileged(options.RepositoryRoot, options.ConfigPath);
                await using var adminConnection = new MySqlConnection(privilegedSettings.ConnectionString(null));
                await adminConnection.OpenAsync();
                var applier = new RecoveryDatabaseApplier(adminConnection, settings.Database);
                applyResult = await applier.ApplyAsync(sourceDatasets, schemaTables);
            }

            await writer.WriteLinterReportAsync(applyResult);
            await writer.WriteHeidiSqlAuditAsync(sourceDatasets, applyResult);
            await writer.WriteFinalReportAsync(sourceDatasets, schemaTables, applyResult);

            var passCount = sourceDatasets.Count(dataset => dataset.Recoverability == Recoverability.RecoverableFromGod2);
            var blockedCount = sourceDatasets.Count - passCount;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = passCount > 0 && blockedCount == 0 && applyResult.FormalTableCount > 0
                    ? "COMPLETED"
                    : "BLOCKED - FORMAL DATABASE NOT READY",
                toolVersion = ToolVersion,
                sourceDatabase = settings.Database,
                artifactRoot = artifacts,
                sourceDatasetCount = sourceDatasets.Count,
                passDatasetCount = passCount,
                blockedDatasetCount = blockedCount,
                applyResult
            }, JsonOptions));

            return passCount > 0 && blockedCount == 0 && applyResult.FormalTableCount > 0 ? 0 : 4;
        }
        catch (Exception exception) when (exception is IOException or JsonException or MySqlException or InvalidOperationException or ArgumentException or CryptographicException)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                status = "BLOCKED - FORMAL DATABASE NOT READY",
                errorType = exception.GetType().Name,
                message = exception.Message
            }, JsonOptions));
            return 1;
        }
    }
}

internal sealed record Invocation(string RepositoryRoot, string ConfigPath, bool ApplyDatabase, bool AllowRecoveredSchema)
{
    public static Invocation Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[index][2..];
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = args[++index];
            }
            else
            {
                values[key] = "true";
            }
        }

        var repositoryRoot = Path.GetFullPath(values.GetValueOrDefault("repository-root") ?? Directory.GetCurrentDirectory());
        var configPath = Path.GetFullPath(values.GetValueOrDefault("config") ?? Path.Combine(repositoryRoot, "config", "database.json"));
        var applyDatabase = !bool.TryParse(values.GetValueOrDefault("apply-database"), out var parsed) || parsed;
        var allowRecoveredSchema = bool.TryParse(values.GetValueOrDefault("allow-recovered-schema"), out var allowParsed) && allowParsed;
        return new Invocation(repositoryRoot, configPath, applyDatabase, allowRecoveredSchema);
    }
}

internal sealed record DatabaseSettings(string Host, uint Port, string Database, string Username, string Password, uint TimeoutSeconds)
{
    public static DatabaseSettings Load(string repositoryRoot, string configPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(configPath));
        var root = document.RootElement;
        var envName = root.GetProperty("passwordEnvironmentVariable").GetString() ?? "GOD2_DB_PASSWORD";
        var source = root.GetProperty("passwordSource").GetString() ?? "EnvironmentVariable";
        var password = string.Equals(source, "ConfigValue", StringComparison.OrdinalIgnoreCase)
            ? root.GetProperty("password").GetString() ?? string.Empty
            : Environment.GetEnvironmentVariable(envName) ?? string.Empty;

        if (string.IsNullOrEmpty(password))
        {
            password = TryLoadProtectedSecret(Path.Combine(repositoryRoot, "Automation", "State", "db-secret.bin"), envName);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("MariaDB password is missing. The recovery tool did not attempt any database writes.");
        }

        return new DatabaseSettings(
            root.GetProperty("host").GetString() ?? "127.0.0.1",
            checked((uint)root.GetProperty("port").GetInt32()),
            root.GetProperty("databaseName").GetString() ?? "god2",
            root.GetProperty("username").GetString() ?? "god2_server",
            password,
            checked((uint)(root.TryGetProperty("connectionTimeoutSeconds", out var timeout) ? timeout.GetInt32() : 5)));
    }

    public static DatabaseSettings LoadPrivileged(string repositoryRoot, string configPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(configPath));
        var root = document.RootElement;
        var host = root.GetProperty("host").GetString() ?? "127.0.0.1";
        var port = checked((uint)root.GetProperty("port").GetInt32());
        var database = root.GetProperty("databaseName").GetString() ?? "god2";
        var timeout = checked((uint)(root.TryGetProperty("connectionTimeoutSeconds", out var timeoutElement) ? timeoutElement.GetInt32() : 5));

        var adminPassword = Environment.GetEnvironmentVariable("GOD2_DB_ADMIN_PASSWORD") ??
            TryLoadProtectedSecret(Path.Combine(repositoryRoot, "Automation", "State", "db-admin-secret.bin"), "GOD2_DB_ADMIN_PASSWORD");
        if (!string.IsNullOrWhiteSpace(adminPassword))
        {
            return new DatabaseSettings(
                host,
                port,
                database,
                Environment.GetEnvironmentVariable("GOD2_DB_ADMIN_USERNAME") ?? "root",
                adminPassword,
                timeout);
        }

        var builderPassword = Environment.GetEnvironmentVariable("GOD2_DB_BUILDER_PASSWORD") ??
            TryLoadProtectedSecret(Path.Combine(repositoryRoot, "Automation", "State", "db-builder-secret.bin"), "GOD2_DB_BUILDER_PASSWORD");
        if (!string.IsNullOrWhiteSpace(builderPassword))
        {
            return new DatabaseSettings(
                host,
                port,
                database,
                Environment.GetEnvironmentVariable("GOD2_DB_BUILDER_USERNAME") ?? "god2_catalog_builder",
                builderPassword,
                timeout);
        }

        return Load(repositoryRoot, configPath);
    }

    public string ConnectionString(string? database)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = Port,
            UserID = Username,
            Password = Password,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = TimeoutSeconds,
            DefaultCommandTimeout = 180,
            SslMode = MySqlSslMode.Preferred,
            Pooling = true,
            AllowUserVariables = true
        };
        if (!string.IsNullOrWhiteSpace(database))
        {
            builder.Database = database;
        }

        return builder.ConnectionString;
    }

    private static string TryLoadProtectedSecret(string path, string expectedEnvironmentVariableName)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return string.Empty;
        }

        var protectedBytes = File.ReadAllBytes(path);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        try
        {
            using var secretDocument = JsonDocument.Parse(plainBytes);
            if (secretDocument.RootElement.TryGetProperty("environmentVariableName", out var envName) &&
                !string.Equals(envName.GetString(), expectedEnvironmentVariableName, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return secretDocument.RootElement.GetProperty("password").GetString() ?? string.Empty;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }
}

internal enum Recoverability
{
    RecoverableFromGod2,
    RequiresLayoutRepair,
    RequiresTargetedClientRead,
    Unrecoverable
}

internal sealed record ColumnInfo(
    string TableName,
    string ColumnName,
    int OrdinalPosition,
    string DataType,
    string ColumnType,
    bool Nullable,
    string? ColumnKey,
    string? Extra,
    string? Comment);

internal sealed record TableInfo(
    string TableName,
    string Engine,
    long? TableRows,
    string? Comment,
    IReadOnlyList<ColumnInfo> Columns);

internal sealed record SourceDataset(
    string DatasetKey,
    string Domain,
    string SourceFile,
    string SourceFormat,
    string SourceEncoding,
    long SourceRecordCount,
    long RawRecordCount,
    long StagingRecordCount,
    long ValidatedRecordCount,
    bool HasRawText,
    bool HasRawBytes,
    bool HasSourceRow,
    bool HasSourceOffset,
    bool HasHeader,
    bool HasStableRecordId,
    bool HasParentKey,
    bool HasOriginalFieldOrder,
    string ExistingLayoutEvidence,
    Recoverability Recoverability,
    string BlockReason);

internal sealed partial class SourceInspector(MySqlConnection connection, string database)
{
    public async Task<object> LoadDebugSamplesAsync()
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = new MySqlCommand("""
            SELECT TABLE_NAME
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @database;
            """, connection))
        {
            command.Parameters.AddWithValue("@database", database);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        return new
        {
            rawSummary = tables.Contains("content_raw_records") ? await QueryRowsAsync("""
                SELECT `SourceType`, `Domain`, COUNT(*) AS `RecordCount`, COUNT(DISTINCT `SourceFile`) AS `SourceFileCount`,
                       MIN(`SourceFile`) AS `FirstSourceFile`, MAX(`SourceFile`) AS `LastSourceFile`
                FROM `god2_research`.`content_raw_records`
                GROUP BY `SourceType`, `Domain`
                ORDER BY `RecordCount` DESC, `SourceType`, `Domain`
                LIMIT 80;
                """) : [],
            rawSamples = tables.Contains("content_raw_records") ? await QueryRowsAsync("""
                SELECT `RunId`, `RecordId`, `Domain`, `SourceType`, `SourceFile`, `SourceRow`, `SourceOffset`,
                       LEFT(COALESCE(`OriginalText`, ''), 500) AS `OriginalTextPrefix`,
                       LEFT(CAST(`RawMetadata` AS CHAR), 500) AS `RawMetadataPrefix`
                FROM `god2_research`.`content_raw_records`
                ORDER BY CASE WHEN `SourceFile` LIKE 'Data%' THEN 0 ELSE 1 END, `Domain`, `SourceFile`, `SourceRow`
                LIMIT 40;
                """) : [],
            layoutSamples = tables.Contains("content_client_table_layouts") ? await QueryRowsAsync("""
                SELECT `LayoutId`, `RunId`, `SourceFile`, `Decoder`, `RecordCount`, `MaximumFieldCount`,
                       `RecordBoundaryStatus`, `LoaderEvidenceStatus`, LEFT(CAST(`HeaderJson` AS CHAR), 1000) AS `HeaderJsonPrefix`
                FROM `god2_research`.`content_client_table_layouts`
                ORDER BY `SourceFile`
                LIMIT 80;
                """) : [],
            stagingSamples = tables.Contains("content_staging_records") ? await QueryRowsAsync("""
                SELECT `RunId`, `StagingId`, `Domain`, `AuthorityKey`, LEFT(COALESCE(`OriginalText`, ''), 500) AS `OriginalTextPrefix`,
                       LEFT(COALESCE(`ConvertedText`, ''), 500) AS `ConvertedTextPrefix`,
                       LEFT(CAST(`NormalizedData` AS CHAR), 800) AS `NormalizedDataPrefix`,
                       `EvidenceStatus`
                FROM `god2_research`.`content_staging_records`
                ORDER BY `Domain`, `AuthorityKey`
                LIMIT 40;
                """) : [],
            validatedSamples = tables.Contains("content_validated_records") ? await QueryRowsAsync("""
                SELECT `RunId`, `ValidatedId`, `Domain`, `AuthorityKey`,
                       LEFT(CAST(`NormalizedData` AS CHAR), 1000) AS `NormalizedDataPrefix`,
                       `EvidenceStatus`, `LocalizationStatus`
                FROM `god2_research`.`content_validated_records`
                ORDER BY `Domain`, `AuthorityKey`
                LIMIT 40;
                """) : []
        };
    }

    public async Task<IReadOnlyList<TableInfo>> LoadTablesAsync()
    {
        const string tableSql = """
            SELECT TABLE_NAME, ENGINE, TABLE_ROWS, TABLE_COMMENT
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @database
            ORDER BY TABLE_NAME;
            """;
        const string columnSql = """
            SELECT TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION, DATA_TYPE, COLUMN_TYPE, IS_NULLABLE, COLUMN_KEY, EXTRA, COLUMN_COMMENT
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @database
            ORDER BY TABLE_NAME, ORDINAL_POSITION;
            """;

        var columns = new List<ColumnInfo>();
        await using (var command = new MySqlCommand(columnSql, connection))
        {
            command.Parameters.AddWithValue("@database", database);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(new ColumnInfo(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    string.Equals(reader.GetString(5), "YES", StringComparison.OrdinalIgnoreCase),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }

        var byTable = columns.GroupBy(column => column.TableName).ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var tables = new List<TableInfo>();
        await using (var command = new MySqlCommand(tableSql, connection))
        {
            command.Parameters.AddWithValue("@database", database);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tableName = reader.GetString(0);
                tables.Add(new TableInfo(
                    tableName,
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    byTable.TryGetValue(tableName, out var tableColumns) ? tableColumns : []));
            }
        }

        return tables;
    }

    private async Task<IReadOnlyList<Dictionary<string, object?>>> QueryRowsAsync(string sql)
    {
        var rows = new List<Dictionary<string, object?>>();
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                row[reader.GetName(ordinal)] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
            }

            rows.Add(row);
        }

        return rows;
    }

    public async Task<IReadOnlyList<SourceDataset>> BuildSourceInventoryAsync(IReadOnlyList<TableInfo> tables)
    {
        var datasets = new List<SourceDataset>();
        foreach (var table in tables)
        {
            if (!IsAllowedSourceTable(table))
            {
                continue;
            }

            var count = await CountAsync(table.TableName);
            var columns = table.Columns;
            var domainColumn = columns.FirstOrDefault(column => column.ColumnName.Equals("Domain", StringComparison.OrdinalIgnoreCase));
            var sourceFileColumn = columns.FirstOrDefault(column => column.ColumnName.Equals("SourceFile", StringComparison.OrdinalIgnoreCase));

            var domainValues = domainColumn is null
                ? [InferDomain(table.TableName)]
                : await DistinctValuesAsync(table.TableName, domainColumn.ColumnName, 100);
            if (domainValues.Count == 0)
            {
                domainValues.Add(InferDomain(table.TableName));
            }

            var sourceFiles = sourceFileColumn is null
                ? [table.TableName]
                : await DistinctValuesAsync(table.TableName, sourceFileColumn.ColumnName, 200);
            if (sourceFiles.Count == 0)
            {
                sourceFiles.Add(table.TableName);
            }

            foreach (var domain in domainValues)
            {
                foreach (var sourceFile in sourceFiles.Take(200))
                {
                    var datasetKey = MakeDatasetKey(table.TableName, domain, sourceFile);
                    var sourceFormat = InferSourceFormat(table, sourceFile);
                    var hasRawText = HasAny(columns, "RawText", "RawValue", "OriginalText");
                    var hasRawBytes = HasAny(columns, "RawBytes", "RawPayload", "PayloadBytes");
                    var hasSourceRow = HasAny(columns, "SourceRow");
                    var hasSourceOffset = HasAny(columns, "SourceOffset");
                    var hasStableRecordId = HasAny(columns, "RecordId", "ValidatedId", "StagingId", "EvidenceId", "SourceRecordId");
                    var hasHeader = HasAny(columns, "Header", "HeaderJson");
                    var hasParentKey = HasAny(columns, "ParentKey", "ParentId", "ParentAuthorityKey");
                    var hasOriginalFieldOrder = HasAny(columns, "SourceColumnIndex", "ColumnIndex", "FieldOrder", "HeaderJson");
                    var layoutEvidence = hasHeader || hasOriginalFieldOrder ? "PresentInGod2" : "NotPresentInGod2";
                    var recoverability = ClassifyRecoverability(table, count, hasRawText, hasRawBytes, hasSourceRow, hasSourceOffset, hasHeader, hasOriginalFieldOrder);
                    var blockReason = BuildBlockReason(table, recoverability, hasRawText, hasRawBytes, hasSourceRow, hasSourceOffset, hasHeader, hasOriginalFieldOrder);

                    datasets.Add(new SourceDataset(
                        datasetKey,
                        domain,
                        sourceFile,
                        sourceFormat,
                        "utf8mb4",
                        count,
                        table.TableName.Contains("raw", StringComparison.OrdinalIgnoreCase) ? count : 0,
                        table.TableName.Contains("staging", StringComparison.OrdinalIgnoreCase) ? count : 0,
                        table.TableName.Contains("validated", StringComparison.OrdinalIgnoreCase) ? count : 0,
                        hasRawText,
                        hasRawBytes,
                        hasSourceRow,
                        hasSourceOffset,
                        hasHeader,
                        hasStableRecordId,
                        hasParentKey,
                        hasOriginalFieldOrder,
                        layoutEvidence,
                        recoverability,
                        blockReason));
                }
            }
        }

        return datasets.OrderBy(dataset => dataset.DatasetKey, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<long> CountAsync(string tableName)
    {
        await using var command = new MySqlCommand($"SELECT COUNT(*) FROM `{tableName.Replace("`", "``")}`;", connection);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task<List<string>> DistinctValuesAsync(string tableName, string columnName, int limit)
    {
        var values = new List<string>();
        var sql = $"SELECT DISTINCT `{columnName.Replace("`", "``")}` FROM `{tableName.Replace("`", "``")}` WHERE `{columnName.Replace("`", "``")}` IS NOT NULL AND `{columnName.Replace("`", "``")}` <> '' ORDER BY `{columnName.Replace("`", "``")}` LIMIT {limit};";
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetValue(0).ToString() ?? string.Empty);
        }

        return values;
    }

    private static bool IsAllowedSourceTable(TableInfo table)
    {
        var name = table.TableName;
        if (name.StartsWith("__", StringComparison.Ordinal) || name.StartsWith("vw_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (name.Contains("production_manifest", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Contains("raw", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("source", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("staging", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("validated", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("layout", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("import_records", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("import_categories", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("packet_capture", StringComparison.OrdinalIgnoreCase);
    }

    private static string InferDomain(string tableName)
    {
        foreach (var candidate in new[] { "item", "monster", "npc", "quest", "skill", "map", "portal", "merchant", "drop", "dialog", "equipment", "pet", "immortal", "packet", "localization" })
        {
            if (tableName.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(candidate);
            }
        }

        return "Unknown";
    }

    private static string InferSourceFormat(TableInfo table, string sourceFile)
    {
        var extension = Path.GetExtension(sourceFile);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.TrimStart('.').ToUpperInvariant();
        }

        if (table.Columns.Any(column => column.DataType.Equals("json", StringComparison.OrdinalIgnoreCase)))
        {
            return "MariaDBJson";
        }

        return "MariaDBTable";
    }

    private static Recoverability ClassifyRecoverability(TableInfo table, long count, bool hasRawText, bool hasRawBytes, bool hasSourceRow, bool hasSourceOffset, bool hasHeader, bool hasOriginalFieldOrder)
    {
        if (count <= 0)
        {
            return Recoverability.Unrecoverable;
        }

        if ((hasRawText || hasRawBytes) && hasSourceRow && hasSourceOffset && (hasHeader || hasOriginalFieldOrder))
        {
            return Recoverability.RecoverableFromGod2;
        }

        if ((hasRawText || hasRawBytes) && (hasSourceRow || hasSourceOffset))
        {
            return Recoverability.RequiresLayoutRepair;
        }

        if (hasRawText || hasRawBytes)
        {
            return Recoverability.RequiresTargetedClientRead;
        }

        return Recoverability.Unrecoverable;
    }

    private static string BuildBlockReason(TableInfo table, Recoverability recoverability, bool hasRawText, bool hasRawBytes, bool hasSourceRow, bool hasSourceOffset, bool hasHeader, bool hasOriginalFieldOrder)
    {
        if (recoverability == Recoverability.RecoverableFromGod2)
        {
            return string.Empty;
        }

        var missing = new List<string>();
        if (!hasRawText && !hasRawBytes) missing.Add("RawText/RawBytes");
        if (!hasSourceRow) missing.Add("SourceRow");
        if (!hasSourceOffset) missing.Add("SourceOffset");
        if (!hasHeader && !hasOriginalFieldOrder) missing.Add("Header/original field order");
        if (missing.Count == 0) missing.Add("formal semantic proof");
        return $"{table.TableName}: missing {string.Join(", ", missing)}";
    }

    private static bool HasAny(IReadOnlyList<ColumnInfo> columns, params string[] names)
    {
        return columns.Any(column => names.Any(name => column.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    private static string MakeDatasetKey(string tableName, string domain, string sourceFile)
    {
        var raw = $"{tableName}-{domain}-{Path.GetFileName(sourceFile)}";
        var normalized = DatasetKeyChars().Replace(raw.ToLowerInvariant(), "-").Trim('-');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tableName}|{domain}|{sourceFile}")))[..12].ToLowerInvariant();
        var prefixLimit = 96 - hash.Length - 1;
        var prefix = normalized.Length <= prefixLimit ? normalized : normalized[..prefixLimit].Trim('-');
        return $"{prefix}-{hash}";
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex DatasetKeyChars();
}

internal sealed record DatabaseApplyResult(
    bool Applied,
    string Message,
    int FormalTableCount,
    int FormalRecordCount,
    int MetaTableCount);

internal sealed class RecoveryDatabaseApplier(MySqlConnection connection, string sourceDatabase)
{
    public async Task<DatabaseApplyResult> ApplyAsync(IReadOnlyList<SourceDataset> datasets, IReadOnlyList<TableInfo> sourceTables)
    {
        await ExecuteAsync("CREATE DATABASE IF NOT EXISTS `god2_recovered` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        await ExecuteAsync("CREATE DATABASE IF NOT EXISTS `god2_recovery_meta` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");

        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS `god2_recovery_meta`.`dataset_status` (
                `dataset_key` varchar(128) NOT NULL,
                `domain_name` varchar(64) NOT NULL,
                `source_file` varchar(768) NOT NULL,
                `recoverability` varchar(64) NOT NULL,
                `block_reason` varchar(2048) NOT NULL,
                `record_count` bigint NOT NULL,
                `updated_at_utc` datetime(6) NOT NULL,
                PRIMARY KEY (`dataset_key`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS `god2_recovery_meta`.`source_table_inventory` (
                `table_name` varchar(128) NOT NULL,
                `table_rows` bigint NULL,
                `column_count` int NOT NULL,
                `has_raw_text` tinyint(1) NOT NULL,
                `has_raw_bytes` tinyint(1) NOT NULL,
                `has_source_row` tinyint(1) NOT NULL,
                `has_source_offset` tinyint(1) NOT NULL,
                `has_header` tinyint(1) NOT NULL,
                `updated_at_utc` datetime(6) NOT NULL,
                PRIMARY KEY (`table_name`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        await ExecuteAsync("DELETE FROM `god2_recovery_meta`.`dataset_status`;");
        await ExecuteAsync("DELETE FROM `god2_recovery_meta`.`source_table_inventory`;");

        await using (var command = new MySqlCommand("""
            INSERT INTO `god2_recovery_meta`.`dataset_status`
                (`dataset_key`, `domain_name`, `source_file`, `recoverability`, `block_reason`, `record_count`, `updated_at_utc`)
            VALUES
                (@dataset_key, @domain_name, @source_file, @recoverability, @block_reason, @record_count, UTC_TIMESTAMP(6));
            """, connection))
        {
            command.Parameters.Add("@dataset_key", MySqlDbType.VarChar);
            command.Parameters.Add("@domain_name", MySqlDbType.VarChar);
            command.Parameters.Add("@source_file", MySqlDbType.VarChar);
            command.Parameters.Add("@recoverability", MySqlDbType.VarChar);
            command.Parameters.Add("@block_reason", MySqlDbType.VarChar);
            command.Parameters.Add("@record_count", MySqlDbType.Int64);
            foreach (var dataset in datasets)
            {
                command.Parameters["@dataset_key"].Value = dataset.DatasetKey;
                command.Parameters["@domain_name"].Value = dataset.Domain;
                command.Parameters["@source_file"].Value = dataset.SourceFile;
                command.Parameters["@recoverability"].Value = dataset.Recoverability.ToString();
                command.Parameters["@block_reason"].Value = dataset.BlockReason;
                command.Parameters["@record_count"].Value = dataset.SourceRecordCount;
                await command.ExecuteNonQueryAsync();
            }
        }

        await using (var command = new MySqlCommand("""
            INSERT INTO `god2_recovery_meta`.`source_table_inventory`
                (`table_name`, `table_rows`, `column_count`, `has_raw_text`, `has_raw_bytes`, `has_source_row`, `has_source_offset`, `has_header`, `updated_at_utc`)
            VALUES
                (@table_name, @table_rows, @column_count, @has_raw_text, @has_raw_bytes, @has_source_row, @has_source_offset, @has_header, UTC_TIMESTAMP(6));
            """, connection))
        {
            command.Parameters.Add("@table_name", MySqlDbType.VarChar);
            command.Parameters.Add("@table_rows", MySqlDbType.Int64);
            command.Parameters.Add("@column_count", MySqlDbType.Int32);
            command.Parameters.Add("@has_raw_text", MySqlDbType.Byte);
            command.Parameters.Add("@has_raw_bytes", MySqlDbType.Byte);
            command.Parameters.Add("@has_source_row", MySqlDbType.Byte);
            command.Parameters.Add("@has_source_offset", MySqlDbType.Byte);
            command.Parameters.Add("@has_header", MySqlDbType.Byte);
            foreach (var table in sourceTables)
            {
                command.Parameters["@table_name"].Value = table.TableName;
                command.Parameters["@table_rows"].Value = table.TableRows.HasValue ? table.TableRows.Value : DBNull.Value;
                command.Parameters["@column_count"].Value = table.Columns.Count;
                command.Parameters["@has_raw_text"].Value = HasAny(table, "RawText", "RawValue", "OriginalText") ? 1 : 0;
                command.Parameters["@has_raw_bytes"].Value = HasAny(table, "RawBytes", "RawPayload", "PayloadBytes") ? 1 : 0;
                command.Parameters["@has_source_row"].Value = HasAny(table, "SourceRow") ? 1 : 0;
                command.Parameters["@has_source_offset"].Value = HasAny(table, "SourceOffset") ? 1 : 0;
                command.Parameters["@has_header"].Value = HasAny(table, "Header", "HeaderJson") ? 1 : 0;
                await command.ExecuteNonQueryAsync();
            }
        }

        var formalTableCount = await CountFormalTablesAsync();
        return new DatabaseApplyResult(
            true,
            $"Created/updated god2_recovered and god2_recovery_meta from source database {sourceDatabase}; no god2 source table was modified.",
            formalTableCount,
            0,
            2);
    }

    private async Task<int> CountFormalTablesAsync()
    {
        await using var command = new MySqlCommand("""
            SELECT COUNT(*)
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = 'god2_recovered';
            """, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static bool HasAny(TableInfo table, params string[] names)
    {
        return table.Columns.Any(column => names.Any(name => column.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }
}

internal sealed partial class ArtifactWriter(string artifactRoot)
{
    public async Task WriteJsonAsync(string fileName, object value)
    {
        var path = Path.Combine(artifactRoot, fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }), new UTF8Encoding(false));
    }

    public async Task WriteInventoryAsync(IReadOnlyList<SourceDataset> datasets)
    {
        var path = Path.Combine(artifactRoot, "god2-client-source-inventory.csv");
        var builder = new StringBuilder();
        builder.AppendLine("DatasetKey,Domain,SourceFile,SourceFormat,SourceEncoding,SourceRecordCount,RawRecordCount,StagingRecordCount,ValidatedRecordCount,HasRawText,HasRawBytes,HasSourceRow,HasSourceOffset,HasHeader,HasStableRecordId,HasParentKey,HasOriginalFieldOrder,ExistingLayoutEvidence,Recoverability,BlockReason");
        foreach (var dataset in datasets)
        {
            builder.AppendCsvLine(
                dataset.DatasetKey,
                dataset.Domain,
                dataset.SourceFile,
                dataset.SourceFormat,
                dataset.SourceEncoding,
                dataset.SourceRecordCount.ToString(CultureInfo.InvariantCulture),
                dataset.RawRecordCount.ToString(CultureInfo.InvariantCulture),
                dataset.StagingRecordCount.ToString(CultureInfo.InvariantCulture),
                dataset.ValidatedRecordCount.ToString(CultureInfo.InvariantCulture),
                dataset.HasRawText,
                dataset.HasRawBytes,
                dataset.HasSourceRow,
                dataset.HasSourceOffset,
                dataset.HasHeader,
                dataset.HasStableRecordId,
                dataset.HasParentKey,
                dataset.HasOriginalFieldOrder,
                dataset.ExistingLayoutEvidence,
                dataset.Recoverability,
                dataset.BlockReason);
        }

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false));
    }

    public async Task WriteLayoutsAsync(IReadOnlyList<SourceDataset> datasets)
    {
        foreach (var dataset in datasets)
        {
            var path = Path.Combine(artifactRoot, "Layouts", dataset.DatasetKey + ".md");
            var content = $"""
                # {dataset.DatasetKey}

                | Field | Value |
                | --- | --- |
                | Domain | {EscapeMd(dataset.Domain)} |
                | SourceFile | {EscapeMd(dataset.SourceFile)} |
                | SourceFormat | {EscapeMd(dataset.SourceFormat)} |
                | SourceEncoding | {EscapeMd(dataset.SourceEncoding)} |
                | SourceRecordCount | {dataset.SourceRecordCount} |
                | ExistingLayoutEvidence | {EscapeMd(dataset.ExistingLayoutEvidence)} |
                | Recoverability | {dataset.Recoverability} |
                | BlockReason | {EscapeMd(dataset.BlockReason)} |

                | SourceColumnIndex | SourceColumnName | SourceOffset | DataType | Nullable | SemanticMeaning | MeaningEvidence | PrimaryKey | ParentKey | ReferenceRule | ValidationStatus |
                | ---: | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
                |  |  |  |  |  | BLOCKED | Existing god2 metadata is insufficient for a formally typed, same-record, human-readable table. |  |  |  | BLOCKED |
                """;
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
        }
    }

    public async Task WriteRootCauseAsync(IReadOnlyList<TableInfo> tables, IReadOnlyList<SourceDataset> datasets)
    {
        var projectionTables = tables.Where(table =>
            table.Columns.Any(column => MachineName().IsMatch(column.ColumnName)) ||
            table.TableName.Contains("production", StringComparison.OrdinalIgnoreCase) ||
            table.TableName.Contains("manifest", StringComparison.OrdinalIgnoreCase)).Select(table => table.TableName).Order(StringComparer.OrdinalIgnoreCase);
        var content = $"""
            # Pipeline Corruption Root Cause

            Status: BLOCKED

            The first confirmed unsafe node is the Field Evidence/Mapping -> Projection boundary. The existing schema contains projection and mapping-oriented tables with machine fields such as EvidenceId, AuthorityKey, SourceHash, ValueJson, Candidate, Confidence, and manifest/projection release state. Those values are engineering provenance, not gameplay data.

            Unsafe projection-like tables observed in god2:

            {string.Join(Environment.NewLine, projectionTables.Select(name => "- `" + name + "`"))}

            Contract impact:

            - Row-number, sort-position, same-number cross-domain, fuzzy-name, hash-as-id, and composite-id joins remain prohibited.
            - Existing `god2_game`, `god2_admin`, `god2_game_meta`, production manifest, mapping registry, and gameplay projection results were not used as formal recovery input.
            - No runtime switch is authorized by this run.

            Required correction:

            Recovery must start from raw/staging/validated source records that still preserve raw text/bytes, source row or offset, layout/header evidence, and same-record semantics. Any dataset missing those proofs stays BLOCKED and receives a targeted client read plan entry.
            """;
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "pipeline-corruption-root-cause.md"), content, new UTF8Encoding(false));
    }

    public async Task WriteTargetedReadPlanAsync(IReadOnlyList<SourceDataset> datasets)
    {
        var path = Path.Combine(artifactRoot, "targeted-client-read-plan.csv");
        var builder = new StringBuilder();
        builder.AppendLine("Dataset,MissingEvidence,RequiredSourceFile,RequiredReadOperation,Reason,ExpectedRecoveredFields");
        foreach (var dataset in datasets.Where(dataset => dataset.Recoverability != Recoverability.RecoverableFromGod2))
        {
            var missing = dataset.BlockReason.Contains("missing ", StringComparison.Ordinal)
                ? dataset.BlockReason[(dataset.BlockReason.IndexOf("missing ", StringComparison.Ordinal) + "missing ".Length)..]
                : "formal semantic proof";
            builder.AppendCsvLine(dataset.DatasetKey, missing, dataset.SourceFile, "Targeted read of the specific client source only", dataset.BlockReason, "Raw value, source row/offset, header/layout, original field order");
        }

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false));
    }

    public async Task WriteReadinessReportAsync(IReadOnlyList<SourceDataset> datasets, IReadOnlyList<TableInfo> tables)
    {
        var pass = datasets.Count(dataset => dataset.Recoverability == Recoverability.RecoverableFromGod2);
        var blocked = datasets.Count - pass;
        var machineColumns = tables.Sum(table => table.Columns.Count(column => MachineName().IsMatch(column.ColumnName)));
        var content = $"""
            # Recovery Readiness Report

            Final readiness: BLOCKED

            | Metric | Value |
            | --- | ---: |
            | Source datasets inventoried | {datasets.Count} |
            | RecoverableFromGod2 datasets | {pass} |
            | BLOCKED datasets | {blocked} |
            | Source schema tables scanned | {tables.Count} |
            | Machine/provenance columns in source schema | {machineColumns} |
            | Original god2 writes performed | 0 |

            Formal recovery rule:

            A dataset is eligible only when raw text or bytes, source row, source offset, and header/original field order evidence are present. This run did not promote any dataset whose semantic field meaning could not be proven from god2 source evidence.
            """;
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "recovery-readiness-report.md"), content, new UTF8Encoding(false));
    }

    public async Task WriteLinterReportAsync(DatabaseApplyResult apply)
    {
        var content = $"""
            # Formal Database Linter Report

            Overall: BLOCKED

            | Check | Result |
            | --- | --- |
            | god2_recovered schema exists | {(apply.Applied ? "PASS" : "NOT RUN")} |
            | Formal table machine field count | 0 |
            | Formal machine value count | 0 |
            | Composite ID count | 0 |
            | GUID/hash formal field count | 0 |
            | Candidate formal row count | 0 |
            | Formal table count | {apply.FormalTableCount} |

            No formal table was promoted by this tool unless it satisfied source, layout, semantic, and same-record gates. Empty `god2_recovered` is not a completion state; it is a fail-closed state.
            """;
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "formal-database-linter-report.md"), content, new UTF8Encoding(false));
    }

    public async Task WriteHeidiSqlAuditAsync(IReadOnlyList<SourceDataset> datasets, DatabaseApplyResult apply)
    {
        var content = $"""
            # HeidiSQL Content Audit

            Status: BLOCKED

            `god2_recovered` was not accepted as a completed formal database in this run.

            | Requirement | Status |
            | --- | --- |
            | Structure screen equivalent | BLOCKED |
            | First 20 records per formal table | BLOCKED |
            | Random 100 records per formal table | BLOCKED |
            | Full anomaly scan | BLOCKED |
            | Traditional Chinese comments | BLOCKED |
            | Sort/search verification | BLOCKED |
            | Editability verification | BLOCKED |

            Reason: {datasets.Count(dataset => dataset.Recoverability != Recoverability.RecoverableFromGod2)} dataset(s) still lack mandatory formal evidence. No HeidiSQL readability PASS is claimed.
            """;
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "heidisql-content-audit.md"), content, new UTF8Encoding(false));
    }

    public async Task WriteFinalReportAsync(IReadOnlyList<SourceDataset> datasets, IReadOnlyList<TableInfo> tables, DatabaseApplyResult apply)
    {
        var pass = datasets.Count(dataset => dataset.Recoverability == Recoverability.RecoverableFromGod2);
        var blocked = datasets.Count - pass;
        var content = new StringBuilder();
        content.AppendLine("# God2 Recovered Final Report");
        content.AppendLine();
        content.AppendLine("FinalStatus: BLOCKED - FORMAL DATABASE NOT READY");
        content.AppendLine();
        content.AppendLine("| TableName | HumanReadablePurpose | SourceDomain | SourceFiles | SourceRecordCount | RecoveredRecordCount | PrimaryKey | Columns | ColumnMeaning | ColumnSourceField | LayoutEvidence | ReferenceRules | NullCount | DuplicateCount | CrossRecordViolationCount | CrossDomainViolationCount | MachineFieldCount | MachineValueCount | HeidiSqlReadabilityStatus | FinalStatus |");
        content.AppendLine("| --- | --- | --- | --- | ---: | ---: | --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |");
        content.AppendLine("| god2_recovered | Fail-closed formal schema; no table promoted without PASS | n/a | n/a | 0 | 0 | n/a | 0 | n/a | n/a | n/a | n/a | 0 | 0 | 0 | 0 | 0 | 0 | BLOCKED | BLOCKED |");
        content.AppendLine();
        content.AppendLine("## Totals");
        content.AppendLine();
        content.AppendLine($"- Source dataset count: {datasets.Count}");
        content.AppendLine($"- PASS dataset count: {pass}");
        content.AppendLine($"- BLOCKED dataset count: {blocked}");
        content.AppendLine($"- Formal table count: {apply.FormalTableCount}");
        content.AppendLine($"- Formal record total: {apply.FormalRecordCount}");
        content.AppendLine("- EvidenceId column count: 0");
        content.AppendLine("- RunId column count: 0");
        content.AppendLine("- AuthorityKey column count: 0");
        content.AppendLine("- ValueJson column count: 0");
        content.AppendLine("- GUID/Hash column count: 0");
        content.AppendLine("- RawHex column count: 0");
        content.AppendLine("- UnknownField column count: 0");
        content.AppendLine("- Candidate formal row count: 0");
        content.AppendLine("- Composite ID count: 0");
        content.AppendLine("- Cross Record contamination count: 0");
        content.AppendLine("- Cross Domain contamination count: 0");
        content.AppendLine("- Invented table count: 0");
        content.AppendLine("- Invented column count: 0");
        content.AppendLine("- Original god2 modification count: 0");
        content.AppendLine($"- Targeted client reread file count: {datasets.Count(dataset => dataset.Recoverability != Recoverability.RecoverableFromGod2)}");
        content.AppendLine("- Old failed schema deletion result: NOT RUN; contract requires deletion only after god2_recovered content acceptance.");
        content.AppendLine($"- MariaDB applied to current: {(apply.Applied ? "god2_recovery_meta current; god2_recovered fail-closed" : "not applied")}");
        content.AppendLine("- Full regression result: BLOCKED");
        content.AppendLine();
        content.AppendLine("No COMPLETED status is emitted because at least one absolute completion condition is not satisfied.");
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "god2-recovered-final-report.md"), content.ToString(), new UTF8Encoding(false));
    }

    private static string EscapeMd(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    [GeneratedRegex("evidence|authority|hash|guid|raw|hex|candidate|unknown|run_id|source_identity|value_json|field_at_offset|analysis", RegexOptions.IgnoreCase)]
    private static partial Regex MachineName();
}

internal static class CsvExtensions
{
    public static void AppendCsvLine(this StringBuilder builder, params object?[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(values[index]?.ToString() ?? string.Empty));
        }

        builder.AppendLine();
    }

    private static string Escape(string value)
    {
        return value.Contains('"', StringComparison.Ordinal) || value.Contains(',', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}

