using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Application.Contracts;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed partial class MariaDbDatabaseBootstrapper : IMariaDbConnectionProbe
{
    public async Task<OperationResult<DatabaseConnectionReport>> ProbeAsync(DatabaseOptions options, CancellationToken cancellationToken)
    {
        var password = ResolvePassword(options);
        if (string.IsNullOrEmpty(password))
        {
            return OperationResult<DatabaseConnectionReport>.Failure(
                "mariadb.password_missing",
                "MariaDB password is not set.",
                "database.json:password");
        }

        if (!SafeIdentifier().IsMatch(options.DatabaseName))
        {
            return OperationResult<DatabaseConnectionReport>.Failure(
                "mariadb.database_name_invalid",
                "Database name must contain only letters, numbers, and underscores.",
                "database.databaseName");
        }

        try
        {
            await using var serverConnection = new MySqlConnection(BuildConnectionString(options, password, databaseName: null));
            await serverConnection.OpenAsync(cancellationToken);

            var created = !await DatabaseExistsAsync(serverConnection, options.DatabaseName, cancellationToken);
            if (created)
            {
                await using var create = serverConnection.CreateCommand();
                create.CommandText = $"CREATE DATABASE {QuoteIdentifier(options.DatabaseName)} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var databaseConnection = new MySqlConnection(BuildConnectionString(options, password, options.DatabaseName));
            await databaseConnection.OpenAsync(cancellationToken);
            await EnsureSchemaVersionTableAsync(databaseConnection, cancellationToken);

            return OperationResult<DatabaseConnectionReport>.Success(new DatabaseConnectionReport(created));
        }
        catch (Exception ex) when (ex is MySqlException or TimeoutException or InvalidOperationException)
        {
            return OperationResult<DatabaseConnectionReport>.Failure("mariadb.connection_failed", ex.Message, $"{options.Host}:{options.Port}");
        }
    }

    internal static string BuildConnectionString(DatabaseOptions options, string password, string? databaseName)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = options.Host,
            Port = (uint)options.Port,
            UserID = options.Username,
            Password = password,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = (uint)Math.Max(1, options.ConnectionTimeoutSeconds),
            DefaultCommandTimeout = 30,
            AllowUserVariables = true,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = 100,
            SslMode = MySqlSslMode.Preferred
        };

        if (!string.IsNullOrWhiteSpace(databaseName))
        {
            builder.Database = databaseName;
        }

        return builder.ConnectionString;
    }

    internal static string? ResolvePassword(DatabaseOptions options) => options.Password;

    internal static async Task EnsureSchemaVersionTableAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS `__SchemaVersion` (
                `Version` varchar(32) NOT NULL,
                `Name` varchar(255) NOT NULL,
                `Checksum` char(64) NULL,
                `AppliedAtUtc` datetime(6) NOT NULL,
                PRIMARY KEY (`Version`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var checksumCommand = connection.CreateCommand();
        checksumCommand.CommandText = """
            ALTER TABLE `__SchemaVersion`
                ADD COLUMN IF NOT EXISTS `Checksum` char(64) NULL AFTER `Name`;
            """;
        await checksumCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static string QuoteIdentifier(string identifier) => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

    private static async Task<bool> DatabaseExistsAsync(MySqlConnection connection, string databaseName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @databaseName;";
        command.Parameters.AddWithValue("@databaseName", databaseName);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return count > 0;
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex SafeIdentifier();
}

/// <summary>Normal runtime connection check. It never creates databases, tables, or columns.</summary>
public sealed class MariaDbRuntimeConnectionProbe : IMariaDbConnectionProbe
{
    public async Task<OperationResult<DatabaseConnectionReport>> ProbeAsync(DatabaseOptions options, CancellationToken cancellationToken)
    {
        var password = MariaDbDatabaseBootstrapper.ResolvePassword(options);
        if (string.IsNullOrEmpty(password))
        {
            return OperationResult<DatabaseConnectionReport>.Failure("mariadb.password_missing", "MariaDB password is not set.", "database.json:password");
        }

        try
        {
            await using var connection = new MySqlConnection(
                MariaDbDatabaseBootstrapper.BuildConnectionString(options, password, options.DatabaseName));
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM `information_schema`.`SCHEMATA`
                WHERE `SCHEMA_NAME` IN ('god2_game','god2_player','god2_game_meta');
                """;
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 3)
            {
                return OperationResult<DatabaseConnectionReport>.Failure(
                    "mariadb.canonical_schema_missing", "One or more canonical schemas are missing.", "god2_game,god2_player,god2_game_meta");
            }

            return OperationResult<DatabaseConnectionReport>.Success(new DatabaseConnectionReport(false));
        }
        catch (Exception exception) when (exception is MySqlException or TimeoutException or InvalidOperationException)
        {
            return OperationResult<DatabaseConnectionReport>.Failure("mariadb.connection_failed", exception.Message, $"{options.Host}:{options.Port}");
        }
    }
}

/// <summary>Read-only checksum verification for normal runtime startup.</summary>
public sealed class ReadOnlySqlMigrationVerifier(DatabaseOptions options, string schemaDirectory) : IMigrationRunner
{
    public async Task<OperationResult<MigrationReport>> VerifyAsync(CancellationToken cancellationToken)
    {
        var password = MariaDbDatabaseBootstrapper.ResolvePassword(options);
        if (string.IsNullOrEmpty(password))
        {
            return OperationResult<MigrationReport>.Failure("mariadb.password_missing", "MariaDB password is not set.", "database.json:password");
        }

        try
        {
            var migrations = SqlMigrationFile.Discover(schemaDirectory);
            await using var connection = new MySqlConnection(
                MariaDbDatabaseBootstrapper.BuildConnectionString(options, password, options.DatabaseName));
            await connection.OpenAsync(cancellationToken);
            var applied = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT `Version`,`Checksum` FROM `__SchemaVersion` ORDER BY `Version`;";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    applied[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
                }
            }

            var executions = new List<MigrationExecution>(migrations.Count);
            foreach (var migration in migrations)
            {
                if (!applied.TryGetValue(migration.Version, out var storedChecksum))
                {
                    return OperationResult<MigrationReport>.Failure("migration.pending", $"Migration is not applied: {migration.Name}", migration.Name);
                }
                var sql = await File.ReadAllTextAsync(migration.Path, Encoding.UTF8, cancellationToken);
                var checksum = SqlMigrationFile.ComputeChecksum(sql);
                if (string.IsNullOrWhiteSpace(storedChecksum) || !string.Equals(storedChecksum, checksum, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult<MigrationReport>.Failure("migration.checksum_mismatch", $"Migration checksum mismatch: {migration.Name}", migration.Name);
                }
                executions.Add(new MigrationExecution(migration.Version, migration.Name, MigrationStatus.Current));
            }
            return OperationResult<MigrationReport>.Success(new MigrationReport(executions));
        }
        catch (Exception exception) when (exception is MySqlException or IOException or InvalidOperationException)
        {
            return OperationResult<MigrationReport>.Failure("migration.verify_failed", exception.Message, schemaDirectory);
        }
    }
}

public sealed class SqlFileMigrationRunner : IMigrationRunner
{
    private readonly DatabaseOptions _options;
    private readonly string _schemaDirectory;

    public SqlFileMigrationRunner(DatabaseOptions options, string schemaDirectory)
    {
        _options = options;
        _schemaDirectory = schemaDirectory;
    }

    public async Task<OperationResult<MigrationReport>> VerifyAsync(CancellationToken cancellationToken)
    {
        var password = MariaDbDatabaseBootstrapper.ResolvePassword(_options);
        if (string.IsNullOrEmpty(password))
        {
            return OperationResult<MigrationReport>.Failure(
                "mariadb.password_missing",
                "MariaDB password is not set.",
                "database.json:password");
        }

        if (!Directory.Exists(_schemaDirectory))
        {
            return OperationResult<MigrationReport>.Failure("migration.schema_directory_missing", "Schema directory is missing.", _schemaDirectory);
        }

        var migrations = SqlMigrationFile.Discover(_schemaDirectory);
        if (migrations.Count == 0)
        {
            return OperationResult<MigrationReport>.Failure("migration.none_found", "No schema migrations were found.", _schemaDirectory);
        }

        try
        {
            await using var connection = new MySqlConnection(MariaDbDatabaseBootstrapper.BuildConnectionString(_options, password, _options.DatabaseName));
            await connection.OpenAsync(cancellationToken);
            await MariaDbDatabaseBootstrapper.EnsureSchemaVersionTableAsync(connection, cancellationToken);

            var applied = await LoadAppliedMigrationsAsync(connection, cancellationToken);
            var executions = new List<MigrationExecution>();

            foreach (var migration in migrations)
            {
                var sql = await File.ReadAllTextAsync(migration.Path, Encoding.UTF8, cancellationToken);
                var checksum = SqlMigrationFile.ComputeChecksum(sql);
                if (applied.TryGetValue(migration.Version, out var appliedChecksum))
                {
                    await VerifyOrBackfillChecksumAsync(connection, migration, checksum, appliedChecksum, cancellationToken);
                    executions.Add(new MigrationExecution(migration.Version, migration.Name, MigrationStatus.Current));
                    continue;
                }

                await ApplyMigrationAsync(connection, migration, sql, checksum, cancellationToken);
                executions.Add(new MigrationExecution(migration.Version, migration.Name, MigrationStatus.Applied));
            }

            return OperationResult<MigrationReport>.Success(new MigrationReport(executions));
        }
        catch (MigrationSqlException ex)
        {
            return OperationResult<MigrationReport>.Failure(
                "migration.failed",
                $"Migration: {ex.MigrationName}; SQL: {ex.SqlPreview}; 錯誤原因: {ex.Reason}",
                ex.MigrationName);
        }
        catch (Exception ex) when (ex is MySqlException or IOException or InvalidOperationException)
        {
            return OperationResult<MigrationReport>.Failure("migration.failed", ex.Message, _schemaDirectory);
        }
    }

    private static async Task<Dictionary<string, string?>> LoadAppliedMigrationsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT `Version`, `Checksum` FROM `__SchemaVersion` ORDER BY `Version`;";

        var versions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            versions[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        return versions;
    }

    private static async Task VerifyOrBackfillChecksumAsync(
        MySqlConnection connection,
        SqlMigrationFile migration,
        string checksum,
        string? appliedChecksum,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appliedChecksum))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE `__SchemaVersion` SET `Checksum` = @checksum WHERE `Version` = @version;";
            command.Parameters.AddWithValue("@checksum", checksum);
            command.Parameters.AddWithValue("@version", migration.Version);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        if (!string.Equals(appliedChecksum, checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Migration checksum mismatch: {migration.Name}");
        }
    }

    private static async Task ApplyMigrationAsync(
        MySqlConnection connection,
        SqlMigrationFile migration,
        string sql,
        string checksum,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidOperationException($"Migration is empty: {migration.Name}");
        }

        try
        {
            var executableSql = await PrepareRecoverableMigrationSqlAsync(connection, migration, sql, cancellationToken);
            await using var migrationCommand = connection.CreateCommand();
            migrationCommand.CommandText = executableSql;
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken);

            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = """
                INSERT INTO `__SchemaVersion` (`Version`, `Name`, `Checksum`, `AppliedAtUtc`)
                VALUES (@version, @name, @checksum, UTC_TIMESTAMP(6));
                """;
            versionCommand.Parameters.AddWithValue("@version", migration.Version);
            versionCommand.Parameters.AddWithValue("@name", migration.Name);
            versionCommand.Parameters.AddWithValue("@checksum", checksum);
            await versionCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is MySqlException or InvalidOperationException)
        {
            throw new MigrationSqlException(migration.Name, Preview(sql), ex.Message, ex);
        }
    }

    private static async Task<string> PrepareRecoverableMigrationSqlAsync(
        MySqlConnection connection,
        SqlMigrationFile migration,
        string sql,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(migration.Name, "041_roadmap_m3_npc_wire_identity.sql", StringComparison.Ordinal))
        {
            return sql;
        }

        string[] expectedColumns =
        [
            "OfficialResourceType",
            "OfficialResourceOrdinal",
            "OfficialSelectorHighBits",
            "OfficialDirectionCode",
            "OfficialStateCode",
            "WireEvidenceStatus",
            "ApplicationMessageSha256",
            "OpaqueTemplateSha256"
        ];
        string[] expectedConstraints =
        [
            "CK_NpcSpawns_OfficialResourceType",
            "CK_NpcSpawns_OfficialResourceOrdinal",
            "CK_NpcSpawns_OfficialSelectorHighBits",
            "CK_NpcSpawns_OfficialDirectionCode",
            "CK_NpcSpawns_OfficialStateCode",
            "CK_NpcSpawns_WireEvidenceStatus"
        ];
        var columnCount = await CountSchemaObjectsAsync(
            connection,
            "INFORMATION_SCHEMA.COLUMNS",
            "TABLE_SCHEMA=DATABASE() AND TABLE_NAME='npc_spawns' AND COLUMN_NAME",
            expectedColumns,
            cancellationToken);
        var constraintCount = await CountSchemaObjectsAsync(
            connection,
            "INFORMATION_SCHEMA.TABLE_CONSTRAINTS",
            "CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='npc_spawns' AND CONSTRAINT_NAME",
            expectedConstraints,
            cancellationToken);
        if (columnCount == 0 && constraintCount == 0)
        {
            return sql;
        }

        if (columnCount != expectedColumns.Length || constraintCount != expectedConstraints.Length)
        {
            throw new InvalidOperationException(
                $"Migration 041 has an inconsistent partial schema state: columns={columnCount}/{expectedColumns.Length}; constraints={constraintCount}/{expectedConstraints.Length}.");
        }

        return SqlAfterFirstStatement(sql);
    }

    private static async Task<int> CountSchemaObjectsAsync(
        MySqlConnection connection,
        string metadataTable,
        string predicate,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var parameters = new List<string>(names.Count);
        for (var index = 0; index < names.Count; index++)
        {
            var parameterName = $"@name{index}";
            parameters.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, names[index]);
        }

        command.CommandText = $"SELECT COUNT(*) FROM {metadataTable} WHERE {predicate} IN ({string.Join(',', parameters)});";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string SqlAfterFirstStatement(string sql)
    {
        var terminator = sql.IndexOf(';', StringComparison.Ordinal);
        if (terminator < 0 || string.IsNullOrWhiteSpace(sql[(terminator + 1)..]))
        {
            throw new InvalidOperationException("Recoverable migration does not contain statements after its atomic schema change.");
        }

        return sql[(terminator + 1)..].TrimStart();
    }

    private static string Preview(string sql)
    {
        var compact = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 240 ? compact : string.Concat(compact.AsSpan(0, 240), "...");
    }
}

public sealed class MigrationSqlException : Exception
{
    public MigrationSqlException(string migrationName, string sqlPreview, string reason, Exception innerException)
        : base(reason, innerException)
    {
        MigrationName = migrationName;
        SqlPreview = sqlPreview;
        Reason = reason;
    }

    public string MigrationName { get; }

    public string SqlPreview { get; }

    public string Reason { get; }
}

public sealed record SqlMigrationFile(string Version, string Name, string Path)
{
    public static IReadOnlyList<SqlMigrationFile> Discover(string schemaDirectory)
    {
        return Directory.EnumerateFiles(schemaDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(Create)
            .OrderBy(file => file.Version, StringComparer.Ordinal)
            .ToArray();
    }

    private static SqlMigrationFile Create(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var separator = name.IndexOf('_', StringComparison.Ordinal);
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var end = separator > 0 ? separator : dot;
        if (end <= 0)
        {
            throw new InvalidOperationException($"Migration filename must start with a version: {name}");
        }

        return new SqlMigrationFile(name[..end], name, path);
    }

    public static string ComputeChecksum(string sql)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sql));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}

public interface IIdempotencyStore
{
    Task<bool> TryBeginAsync(string key, CancellationToken cancellationToken);
}
