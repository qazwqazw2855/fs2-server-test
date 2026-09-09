using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using God2.AdvancedHeadlessVerification;
using God2.ClassicServer.Application.Startup;
using God2.ClassicServer.Infrastructure;
using God2.ClassicServer.Persistence;
using MySqlConnector;

internal static partial class M9ProductionBackupRestoreProbe
{
    private const string SchemaVersion = "god2-roadmap-m9-production-backup-restore-v2";
    private static readonly Regex SafeSchemaName = new("\\A[a-zA-Z0-9_]+\\z", RegexOptions.CultureInvariant);
    private static readonly Regex OwnedRestoreSchema = new("\\Agod2_m9_restore_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex OwnedTemporaryBackup = new("\\Agod2-m9-[0-9a-f]{32}\\.g2enc\\z", RegexOptions.CultureInvariant);
    private static readonly Regex OwnedLegacyPlaintextBackup = new("\\Agod2-m9-[0-9a-f]{32}\\.sql\\z", RegexOptions.CultureInvariant);
    private static readonly Regex SafeDatabaseEncodingName = new("\\A[a-zA-Z0-9_]+\\z", RegexOptions.CultureInvariant);

    internal static async Task<int> RunAsync(AutomationProbeOptions options)
    {
        var root = Path.GetFullPath(options.BaseDirectory);
        var output = ResolveOutputPath(root, options.OutputPath);
        var restoreSchema = $"god2_m9_restore_{Guid.NewGuid():N}";
        var backupPath = Path.Combine(Path.GetTempPath(), $"god2-m9-{Guid.NewGuid():N}.g2enc");
        var restoreSchemaCreated = false;
        FileStream? runLock = null;
        God2.ClassicServer.Application.Configuration.DatabaseOptions? database = null;
        RevalidationBuildIdentity? buildIdentity = null;
        byte[]? backupEncryptionKey = null;
        byte[]? backupEncryptionIv = null;

        try
        {
            runLock = AcquireRunLock();
            var paths = new AppPathProvider(root);
            var configuration = new JsonServerConfigurationLoader(paths).Load();
            if (!configuration.Succeeded || configuration.Value is null)
            {
                throw new InvalidOperationException($"Production configuration is unavailable: {configuration.Error.Code}.");
            }

            database = configuration.Value.Database;
            ValidateSchemaName(database.DatabaseName, "configured database");
            ValidateSchemaName(restoreSchema, "isolated restore database");
            var currentBuildIdentity = ResolveAndVerifyBuildIdentity(root, options.BuildIdentityPath);
            buildIdentity = currentBuildIdentity;
            Stage("configuration-loaded");

            var adminConnectionString = BuildConnectionString(database, null);
            var staleCleanup = await CleanupStaleResourcesAsync(adminConnectionString, restoreSchema, backupPath);
            Stage("stale-resources-cleaned");

            var migration = await new SqlFileMigrationRunner(database, paths.DatabaseSchemaDirectory)
                .VerifyAsync(CancellationToken.None);
            if (!migration.Succeeded || migration.Value is null ||
                migration.Value.Migrations.All(item => item.Version != "048"))
            {
                throw new InvalidOperationException($"Migration 048 is not current: {migration.Error.Code}.");
            }

            var staticData = new MariaDbStaticDataLoader(database);
            var loaded = await staticData.LoadAsync(CancellationToken.None);
            if (!loaded.Succeeded || loaded.Value is null)
            {
                throw new InvalidOperationException($"Formal MariaDB catalog load failed: {loaded.Error.Code}.");
            }

            var formalBuild = await staticData.BuildAsync(loaded.Value, CancellationToken.None);
            if (!formalBuild.Succeeded)
            {
                throw new InvalidOperationException($"Formal runtime catalog build failed: {formalBuild.Error.Code}.");
            }

            var promoted = new MariaDbPromotedGameplayContentRuntime(database, staticData);
            var promotedBuild = await promoted.BuildAsync(loaded.Value, CancellationToken.None);
            if (!promotedBuild.Succeeded)
            {
                throw new InvalidOperationException($"Promoted runtime catalog build failed: {promotedBuild.Error.Code}.");
            }

            var referential = await new MariaDbStaticDataValidator(database).ValidateAsync(CancellationToken.None);
            if (!referential.Succeeded)
            {
                throw new InvalidOperationException($"Formal referential integrity failed: {referential.Error.Code}.");
            }

            var dumpTool = LocateMariaDbTool("mariadb-dump.exe", "mysqldump.exe");
            var clientTool = LocateMariaDbTool("mariadb.exe", "mysql.exe");
            var dumpVersion = await ReadToolVersionAsync(dumpTool);
            var clientVersion = await ReadToolVersionAsync(clientTool);
            var sourceConnectionString = BuildConnectionString(database, database.DatabaseName);
            Stage("runtime-and-tools-verified");

            DatabaseFingerprint sourceBefore;
            await using (var source = new MySqlConnection(sourceConnectionString))
            {
                await source.OpenAsync();
                sourceBefore = await ReadDatabaseFingerprintAsync(source, database.DatabaseName);
            }
            EnsureTransactionalSnapshotEligible(sourceBefore);
            Stage("source-fingerprint-before-complete");

            backupEncryptionKey = RandomNumberGenerator.GetBytes(32);
            backupEncryptionIv = RandomNumberGenerator.GetBytes(16);
            await DumpAsync(dumpTool, database, backupPath, backupEncryptionKey, backupEncryptionIv);
            Stage("backup-stream-complete");
            var backupInfo = new FileInfo(backupPath);
            if (!backupInfo.Exists || backupInfo.Length == 0)
            {
                throw new InvalidDataException("MariaDB backup produced no data.");
            }

            string backupSha256;
            await using (var backupForHash = File.OpenRead(backupPath))
            {
                backupSha256 = Convert.ToHexString(await SHA256.HashDataAsync(backupForHash));
            }

            await using (var admin = new MySqlConnection(adminConnectionString))
            {
                await admin.OpenAsync();
                ValidateDatabaseEncodingName(sourceBefore.DefaultCharacterSet, "source character set");
                ValidateDatabaseEncodingName(sourceBefore.DefaultCollation, "source collation");
                restoreSchemaCreated = true;
                await ExecuteNonQueryAsync(admin,
                    $"CREATE DATABASE `{restoreSchema}` CHARACTER SET `{sourceBefore.DefaultCharacterSet}` COLLATE `{sourceBefore.DefaultCollation}`;");
            }

            await RestoreAsync(clientTool, database, restoreSchema, backupPath, backupEncryptionKey, backupEncryptionIv);
            Stage("restore-stream-complete");

            DatabaseFingerprint sourceAfter;
            await using (var source = new MySqlConnection(sourceConnectionString))
            {
                await source.OpenAsync();
                sourceAfter = await ReadDatabaseFingerprintAsync(source, database.DatabaseName);
            }
            Stage("source-fingerprint-after-complete");

            DatabaseFingerprint restoredFingerprint;
            await using (var restored = new MySqlConnection(BuildConnectionString(database, restoreSchema)))
            {
                await restored.OpenAsync();
                restoredFingerprint = await ReadDatabaseFingerprintAsync(restored, restoreSchema);
            }
            Stage("restored-fingerprint-complete");

            var sourceStable = DatabaseFingerprintsEqual(sourceBefore, sourceAfter);
            var restoreMatches = DatabaseFingerprintsEqual(sourceBefore, restoredFingerprint);
            if (!sourceStable)
            {
                throw new InvalidOperationException("Source schema or content changed during backup; the run cannot prove a stable restore identity.");
            }

            if (!restoreMatches)
            {
                throw new InvalidDataException("Restored schema, logical objects, row counts, or content checksums differ from the source backup boundary.");
            }

            var snapshot = promoted.PublishedSnapshot;
            await DropRestoreSchemaAsync(adminConnectionString, restoreSchema);
            restoreSchemaCreated = false;
            File.Delete(backupPath);

            WriteArtifact(output, new
            {
                schemaVersion = SchemaVersion,
                generatedAtUtc = DateTimeOffset.UtcNow,
                status = "PASS",
                runId = currentBuildIdentity.RunId,
                buildIdentityId = currentBuildIdentity.BuildIdentityId,
                sourceManifestHash = currentBuildIdentity.SourceManifestHash,
                buildOutputManifestHash = currentBuildIdentity.BuildOutputManifestHash,
                probeAssemblySha256 = RevalidationJson.Hash(typeof(M9ProductionBackupRestoreProbe).Assembly.Location),
                authority = "MariaDB",
                repositoryActuallyUsed = nameof(MariaDbPromotedGameplayContentRuntime),
                sourceDatabaseIdentityVerified = true,
                migrationsThrough = migration.Value.Migrations.Max(item => item.Version),
                activeRelease = new
                {
                    snapshot.ReleaseId,
                    snapshot.SourceRunId,
                    snapshot.FormalCatalogFingerprint,
                    snapshot.FormalCatalogRecordCount
                },
                runtimeCatalog = "PASS",
                referentialIntegrity = "PASS",
                backup = new
                {
                    tool = Path.GetFileName(dumpTool),
                    version = dumpVersion,
                    singleTransaction = true,
                    quickStreaming = true,
                    routines = true,
                    events = true,
                    triggers = true,
                    sha256 = backupSha256,
                    bytes = backupInfo.Length,
                    payloadEncryption = "AES-256-CBC-EPHEMERAL-KEY",
                    plaintextPayloadWritten = false,
                    encryptionKeyRetained = false,
                    tableCount = sourceBefore.Tables.Count,
                    exactRowCount = sourceBefore.Tables.Values.Sum(item => item.RowCount),
                    retained = false
                },
                restore = new
                {
                    tool = Path.GetFileName(clientTool),
                    version = clientVersion,
                    isolatedSchema = true,
                    sourceStable,
                    databaseDefaultsMatch = true,
                    tableIdentityAndExactCountsMatch = true,
                    tableDefinitionsMatch = true,
                    tableContentChecksumsMatch = true,
                    viewsMatch = true,
                    routinesMatch = true,
                    eventsMatch = true,
                    triggersMatch = true,
                    transactionalEngineEligibility = "PASS",
                    logicalObjectCounts = new
                    {
                        views = sourceBefore.Views.Count,
                        routines = sourceBefore.Routines.Count,
                        events = sourceBefore.Events.Count,
                        triggers = sourceBefore.Triggers.Count
                    },
                    cleanup = "PASS",
                    schemaRetained = false
                },
                staleCleanup,
                secretTransport = "PROCESS_ENVIRONMENT_ONLY",
                secretRetained = false,
                connectionStringRetained = false,
                backupPayloadRetained = false,
                gameplayNetworkBytesEmitted = 0,
                fakeNetworkBytes = 0
            });
            return 0;
        }
        catch (Exception exception) when (exception is MySqlException or IOException or InvalidDataException or
                                           InvalidOperationException or TimeoutException or UnauthorizedAccessException or
                                           OperationCanceledException)
        {
            var failureCleanupErrors = new List<string>();
            if (restoreSchemaCreated && database is not null)
            {
                try
                {
                    await DropRestoreSchemaAsync(BuildConnectionString(database, null), restoreSchema);
                    restoreSchemaCreated = false;
                }
                catch (Exception cleanupException) when (cleanupException is MySqlException or IOException or InvalidOperationException)
                {
                    failureCleanupErrors.Add(cleanupException.GetType().Name);
                }
            }

            try
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
                failureCleanupErrors.Add(cleanupException.GetType().Name);
            }

            WriteArtifact(output, new
            {
                schemaVersion = SchemaVersion,
                generatedAtUtc = DateTimeOffset.UtcNow,
                status = "BLOCKED",
                runId = buildIdentity?.RunId,
                buildIdentityId = buildIdentity?.BuildIdentityId,
                sourceManifestHash = buildIdentity?.SourceManifestHash,
                reason = "m9.production_backup_restore_failed",
                diagnostic = Redact(exception.Message, database?.Password),
                isolatedSchema = restoreSchema,
                cleanupStatus = !restoreSchemaCreated && !File.Exists(backupPath) && failureCleanupErrors.Count == 0
                    ? "PASS"
                    : "FAILED",
                cleanupErrorTypes = failureCleanupErrors,
                restoreSchemaRetained = restoreSchemaCreated,
                secretRetained = false,
                connectionStringRetained = false,
                backupPayloadRetained = File.Exists(backupPath),
                gameplayNetworkBytesEmitted = 0,
                fakeNetworkBytes = 0
            });
            return 61;
        }
        finally
        {
            if (restoreSchemaCreated && database is not null)
            {
                try
                {
                    await DropRestoreSchemaAsync(BuildConnectionString(database, null), restoreSchema);
                }
                catch
                {
                    // The artifact already records that cleanup was attempted. Never mask the primary failure.
                }
            }

            try
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            catch
            {
                // The primary result is already emitted; do not mask it with a second cleanup exception.
            }

            runLock?.Dispose();
            if (backupEncryptionKey is not null)
            {
                CryptographicOperations.ZeroMemory(backupEncryptionKey);
            }
            if (backupEncryptionIv is not null)
            {
                CryptographicOperations.ZeroMemory(backupEncryptionIv);
            }
        }
    }

    private static async Task DumpAsync(
        string tool,
        God2.ClassicServer.Application.Configuration.DatabaseOptions database,
        string backupPath,
        byte[] encryptionKey,
        byte[] encryptionIv)
    {
        var arguments = new[]
        {
            "--single-transaction", "--quick", "--hex-blob", "--routines", "--events", "--triggers",
            "--skip-comments", "--skip-lock-tables", "--default-character-set=utf8mb4",
            $"--host={database.Host}", $"--port={database.Port}", $"--user={database.Username}",
            database.DatabaseName
        };
        var process = CreateProcess(tool, database.Password, arguments, redirectInput: false, redirectOutput: true);
        using (process)
        await using (var backup = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan))
        using (var aes = CreateBackupCipher(encryptionKey, encryptionIv))
        await using (var encryptedBackup = new CryptoStream(backup, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true))
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("MariaDB backup process did not start.");
            }

            var errorTask = process.StandardError.ReadToEndAsync();
            await WaitForIoAndExitOrKillAsync(process,
                cancellationToken => CopyAndFinalizeEncryptedBackupAsync(
                    process.StandardOutput.BaseStream, encryptedBackup, cancellationToken),
                TimeSpan.FromMinutes(15), "backup");
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"MariaDB backup failed with exit code {process.ExitCode}: {Redact(error, database.Password)}");
            }
        }
    }

    private static async Task RestoreAsync(
        string tool,
        God2.ClassicServer.Application.Configuration.DatabaseOptions database,
        string restoreSchema,
        string backupPath,
        byte[] encryptionKey,
        byte[] encryptionIv)
    {
        var arguments = new[]
        {
            "--binary-mode", "--default-character-set=utf8mb4", $"--host={database.Host}",
            $"--port={database.Port}", $"--user={database.Username}", $"--database={restoreSchema}"
        };
        var process = CreateProcess(tool, database.Password, arguments, redirectInput: true, redirectOutput: true);
        using (process)
        await using (var backup = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.None,
                         1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan))
        using (var aes = CreateBackupCipher(encryptionKey, encryptionIv))
        await using (var decryptedBackup = new CryptoStream(backup, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true))
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("MariaDB restore process did not start.");
            }

            var errorTask = process.StandardError.ReadToEndAsync();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            await WaitForIoAndExitOrKillAsync(process,
                cancellationToken => CopyRestoreInputAsync(decryptedBackup, process.StandardInput, cancellationToken),
                TimeSpan.FromMinutes(15), "restore");
            var error = await errorTask;
            _ = await outputTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"MariaDB restore failed with exit code {process.ExitCode}: {Redact(error, database.Password)}");
            }
        }
    }

    private static Aes CreateBackupCipher(byte[] key, byte[] iv)
    {
        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        return aes;
    }

    private static async Task CopyAndFinalizeEncryptedBackupAsync(
        Stream source,
        CryptoStream destination,
        CancellationToken cancellationToken)
    {
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushFinalBlockAsync(cancellationToken);
    }

    private static async Task CopyRestoreInputAsync(
        Stream source,
        StreamWriter standardInput,
        CancellationToken cancellationToken)
    {
        await source.CopyToAsync(standardInput.BaseStream, cancellationToken);
        await standardInput.BaseStream.FlushAsync(cancellationToken);
        standardInput.Close();
    }

    private static Process CreateProcess(
        string tool,
        string password,
        IEnumerable<string> arguments,
        bool redirectInput,
        bool redirectOutput)
    {
        var start = new ProcessStartInfo
        {
            FileName = tool,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["MYSQL_PWD"] = password;
        return new Process { StartInfo = start };
    }

    private static async Task WaitForIoAndExitOrKillAsync(
        Process process,
        Func<CancellationToken, Task> ioOperation,
        TimeSpan timeout,
        string stage)
    {
        using var operationCancellation = new CancellationTokenSource();
        using var timeoutCancellation = new CancellationTokenSource();
        var ioTask = ioOperation(operationCancellation.Token);
        var processExitTask = process.WaitForExitAsync();
        var completionTask = Task.WhenAll(ioTask, processExitTask);
        var timeoutTask = Task.Delay(timeout, timeoutCancellation.Token);
        try
        {
            if (await Task.WhenAny(completionTask, timeoutTask) != completionTask)
            {
                operationCancellation.Cancel();
                await KillProcessTreeAsync(process);
                try
                {
                    await completionTask.WaitAsync(TimeSpan.FromSeconds(30));
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or
                                                   ObjectDisposedException or OperationCanceledException or TimeoutException)
                {
                    // Killing the child closes redirected pipes; the timeout remains the primary result.
                }
                throw new TimeoutException($"MariaDB {stage} process or stream exceeded the bounded {timeout.TotalMinutes:F0}-minute timeout.");
            }

            timeoutCancellation.Cancel();
            await completionTask;
        }
        catch
        {
            operationCancellation.Cancel();
            if (!process.HasExited)
            {
                await KillProcessTreeAsync(process);
            }
            throw;
        }
    }

    private static async Task KillProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            using var killTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(killTimeout.Token);
        }
        catch (InvalidOperationException)
        {
            // The process exited between observation and termination.
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException("MariaDB child process did not terminate within 30 seconds after kill.", exception);
        }
    }

    private static async Task<string> ReadToolVersionAsync(string tool)
    {
        var start = new ProcessStartInfo
        {
            FileName = tool,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--version");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("MariaDB tool version process did not start.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"MariaDB tool version failed with exit code {process.ExitCode}: {error}");
        }

        return output;
    }

    private static string LocateMariaDbTool(params string[] candidateNames)
    {
        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in pathDirectories)
            foreach (var candidate in candidateNames)
            {
                var path = Path.Combine(directory, candidate);
                if (File.Exists(path))
                {
                    return Path.GetFullPath(path);
                }
            }

        foreach (var programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 }.Where(value => !string.IsNullOrWhiteSpace(value) && Directory.Exists(value)))
        {
            foreach (var mariaDbDirectory in Directory.EnumerateDirectories(programFiles, "MariaDB *", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(value => value, StringComparer.OrdinalIgnoreCase))
                foreach (var candidate in candidateNames)
                {
                    var path = Path.Combine(mariaDbDirectory, "bin", candidate);
                    if (File.Exists(path))
                    {
                        return Path.GetFullPath(path);
                    }
                }
        }

        throw new FileNotFoundException($"Required MariaDB tool is unavailable: {string.Join(" or ", candidateNames)}.");
    }

    private static async Task<DatabaseFingerprint> ReadDatabaseFingerprintAsync(
        MySqlConnection connection,
        string schema)
    {
        string defaultCharacterSet;
        string defaultCollation;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME
                FROM INFORMATION_SCHEMA.SCHEMATA
                WHERE SCHEMA_NAME = @schema;
                """;
            command.Parameters.AddWithValue("@schema", schema);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new InvalidDataException("MariaDB schema identity disappeared during fingerprinting.");
            }
            defaultCharacterSet = reader.GetString(0);
            defaultCollation = reader.GetString(1);
        }

        var tableEngines = new SortedDictionary<string, string>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT TABLE_NAME, ENGINE
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = @schema AND TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_NAME;
                """;
            command.Parameters.AddWithValue("@schema", schema);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tableEngines.Add(reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1));
            }
        }

        var tables = new SortedDictionary<string, TableFingerprint>(StringComparer.Ordinal);
        foreach (var pair in tableEngines)
        {
            var name = pair.Key;
            if (name.Contains('`', StringComparison.Ordinal))
            {
                throw new InvalidDataException("MariaDB table identity contains an unsupported delimiter.");
            }

            long rowCount;
            await using (var count = connection.CreateCommand())
            {
                count.CommandTimeout = 900;
                count.CommandText = $"SELECT COUNT(*) FROM `{name}`;";
                rowCount = Convert.ToInt64(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            }

            string createStatement;
            await using (var create = connection.CreateCommand())
            {
                create.CommandText = $"SHOW CREATE TABLE `{name}`;";
                await using var reader = await create.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    throw new InvalidDataException($"SHOW CREATE TABLE returned no definition for '{name}'.");
                }
                createStatement = reader.GetString(1);
            }

            string contentChecksum;
            await using (var checksum = connection.CreateCommand())
            {
                checksum.CommandTimeout = 900;
                checksum.CommandText = $"CHECKSUM TABLE `{name}` EXTENDED;";
                await using var reader = await checksum.ExecuteReaderAsync();
                if (!await reader.ReadAsync() || reader.IsDBNull(1))
                {
                    throw new InvalidDataException($"MariaDB could not calculate an extended content checksum for '{name}'.");
                }
                contentChecksum = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)
                    ?? throw new InvalidDataException($"MariaDB returned an invalid content checksum for '{name}'.");
            }

            tables.Add(name, new TableFingerprint(
                pair.Value,
                rowCount,
                HashText(createStatement),
                contentChecksum));
        }

        var views = await ReadMetadataFingerprintAsync(connection, schema, """
            SELECT TABLE_NAME, VIEW_DEFINITION, CHECK_OPTION, IS_UPDATABLE, DEFINER, SECURITY_TYPE,
                   CHARACTER_SET_CLIENT, COLLATION_CONNECTION
            FROM INFORMATION_SCHEMA.VIEWS
            WHERE TABLE_SCHEMA = @schema
            ORDER BY TABLE_NAME;
            """);
        var routines = await ReadMetadataFingerprintAsync(connection, schema, """
            SELECT SPECIFIC_NAME, ROUTINE_TYPE, DTD_IDENTIFIER, ROUTINE_DEFINITION, IS_DETERMINISTIC,
                   SQL_DATA_ACCESS, SECURITY_TYPE, DEFINER, SQL_MODE, CHARACTER_SET_CLIENT,
                   COLLATION_CONNECTION, DATABASE_COLLATION
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_SCHEMA = @schema
            ORDER BY ROUTINE_TYPE, SPECIFIC_NAME;
            """);
        var routineParameters = await ReadMetadataFingerprintAsync(connection, schema, """
            SELECT SPECIFIC_NAME, ORDINAL_POSITION, PARAMETER_MODE, PARAMETER_NAME, DTD_IDENTIFIER
            FROM INFORMATION_SCHEMA.PARAMETERS
            WHERE SPECIFIC_SCHEMA = @schema
            ORDER BY SPECIFIC_NAME, ORDINAL_POSITION;
            """);
        var events = await ReadMetadataFingerprintAsync(connection, schema, """
            SELECT EVENT_NAME, EVENT_DEFINITION, EVENT_TYPE, EXECUTE_AT, INTERVAL_VALUE, INTERVAL_FIELD,
                   SQL_MODE, STARTS, ENDS, STATUS, ON_COMPLETION, DEFINER, EVENT_COMMENT,
                   CHARACTER_SET_CLIENT, COLLATION_CONNECTION, DATABASE_COLLATION
            FROM INFORMATION_SCHEMA.EVENTS
            WHERE EVENT_SCHEMA = @schema
            ORDER BY EVENT_NAME;
            """);
        var triggers = await ReadMetadataFingerprintAsync(connection, schema, """
            SELECT TRIGGER_NAME, EVENT_MANIPULATION, EVENT_OBJECT_TABLE, ACTION_ORDER, ACTION_CONDITION,
                   ACTION_STATEMENT, ACTION_ORIENTATION, ACTION_TIMING, DEFINER, SQL_MODE,
                   CHARACTER_SET_CLIENT, COLLATION_CONNECTION, DATABASE_COLLATION
            FROM INFORMATION_SCHEMA.TRIGGERS
            WHERE TRIGGER_SCHEMA = @schema
            ORDER BY TRIGGER_NAME;
            """);

        return new DatabaseFingerprint(
            defaultCharacterSet,
            defaultCollation,
            tables,
            views,
            routines,
            routineParameters,
            events,
            triggers);
    }

    private static async Task<MetadataFingerprint> ReadMetadataFingerprintAsync(
        MySqlConnection connection,
        string schema,
        string sql)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = 0;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@schema", schema);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            count++;
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                var value = reader.IsDBNull(ordinal)
                    ? "<NULL>"
                    : CanonicalMetadataValue(reader.GetValue(ordinal));
                var bytes = Encoding.UTF8.GetBytes(value);
                hash.AppendData(BitConverter.GetBytes(bytes.Length));
                hash.AppendData(bytes);
            }
        }
        return new MetadataFingerprint(count, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static string CanonicalMetadataValue(object value) => value switch
    {
        byte[] bytes => Convert.ToHexString(bytes),
        DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void EnsureTransactionalSnapshotEligible(DatabaseFingerprint fingerprint)
    {
        var unsupported = fingerprint.Tables
            .Where(pair => !string.Equals(pair.Value.Engine, "InnoDB", StringComparison.OrdinalIgnoreCase))
            .Select(pair => $"{pair.Key}:{pair.Value.Engine}")
            .ToArray();
        if (unsupported.Length != 0)
        {
            throw new InvalidDataException(
                $"Single-transaction backup is unsafe for non-InnoDB tables: {string.Join(", ", unsupported)}.");
        }
    }

    private static bool DatabaseFingerprintsEqual(DatabaseFingerprint expected, DatabaseFingerprint actual) =>
        string.Equals(expected.DefaultCharacterSet, actual.DefaultCharacterSet, StringComparison.Ordinal) &&
        string.Equals(expected.DefaultCollation, actual.DefaultCollation, StringComparison.Ordinal) &&
        DictionariesEqual(expected.Tables, actual.Tables) &&
        expected.Views == actual.Views &&
        expected.Routines == actual.Routines &&
        expected.RoutineParameters == actual.RoutineParameters &&
        expected.Events == actual.Events &&
        expected.Triggers == actual.Triggers;

    private static bool DictionariesEqual<T>(
        IReadOnlyDictionary<string, T> expected,
        IReadOnlyDictionary<string, T> actual) where T : notnull =>
        expected.Count == actual.Count && expected.All(pair =>
            actual.TryGetValue(pair.Key, out var value) && EqualityComparer<T>.Default.Equals(pair.Value, value));

    private static async Task DropRestoreSchemaAsync(string adminConnectionString, string schema)
    {
        ValidateSchemaName(schema, "isolated restore database");
        if (!OwnedRestoreSchema.IsMatch(schema))
        {
            throw new InvalidDataException("Refusing to drop a database outside the M9 isolated restore namespace.");
        }

        await using var connection = new MySqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await ExecuteNonQueryAsync(connection, $"DROP DATABASE IF EXISTS `{schema}`;");
    }

    private static async Task<object> CleanupStaleResourcesAsync(
        string adminConnectionString,
        string currentRestoreSchema,
        string currentBackupPath)
    {
        var deletedBackupFiles = 0;
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var candidate in Directory.EnumerateFiles(Path.GetTempPath(), "god2-m9-*", SearchOption.TopDirectoryOnly))
        {
            var fullPath = Path.GetFullPath(candidate);
            if (string.Equals(fullPath, currentBackupPath, StringComparison.OrdinalIgnoreCase) ||
                !fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
                (!OwnedTemporaryBackup.IsMatch(Path.GetFileName(fullPath)) &&
                 !OwnedLegacyPlaintextBackup.IsMatch(Path.GetFileName(fullPath))))
            {
                continue;
            }

            File.Delete(fullPath);
            deletedBackupFiles++;
        }

        var staleSchemas = new List<string>();
        await using (var connection = new MySqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT SCHEMA_NAME
                FROM INFORMATION_SCHEMA.SCHEMATA
                WHERE SCHEMA_NAME LIKE 'god2!_m9!_restore!_%' ESCAPE '!'
                ORDER BY SCHEMA_NAME;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var schema = reader.GetString(0);
                if (!string.Equals(schema, currentRestoreSchema, StringComparison.Ordinal) && OwnedRestoreSchema.IsMatch(schema))
                {
                    staleSchemas.Add(schema);
                }
            }
        }

        foreach (var schema in staleSchemas)
        {
            await DropRestoreSchemaAsync(adminConnectionString, schema);
        }

        return new
        {
            status = "PASS",
            deletedBackupFiles,
            droppedRestoreSchemas = staleSchemas.Count,
            productionSchemaEligibleForCleanup = false
        };
    }

    private static FileStream AcquireRunLock()
    {
        var lockPath = Path.Combine(Path.GetTempPath(), "god2-m9-production-backup-restore.lock");
        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Another M9 backup/restore verification is already running.", exception);
        }
    }

    private static async Task ExecuteNonQueryAsync(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string BuildConnectionString(
        God2.ClassicServer.Application.Configuration.DatabaseOptions database,
        string? schema)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = database.Host,
            Port = (uint)database.Port,
            UserID = database.Username,
            Password = database.Password,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = (uint)Math.Max(1, database.ConnectionTimeoutSeconds),
            DefaultCommandTimeout = 120,
            Pooling = false,
            SslMode = MySqlSslMode.Preferred
        };
        if (!string.IsNullOrWhiteSpace(schema))
        {
            builder.Database = schema;
        }

        return builder.ConnectionString;
    }

    private static RevalidationBuildIdentity ResolveAndVerifyBuildIdentity(string root, string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            throw new InvalidDataException("M9 backup/restore requires --build-identity from a fresh revalidation prepare run.");
        }

        var identityPath = Path.GetFullPath(requestedPath);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!identityPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(identityPath))
        {
            throw new InvalidDataException("M9 build identity must be an existing artifact under the repository root.");
        }

        var identity = RevalidationJson.Read<RevalidationBuildIdentity>(identityPath);
        var verification = RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        if (!verification.SourceHashMatches || !verification.AssemblyHashesUnchanged)
        {
            throw new InvalidDataException("M9 build identity no longer matches current source or build outputs.");
        }

        var assemblyPath = typeof(M9ProductionBackupRestoreProbe).Assembly.Location;
        var assemblyRelativePath = Path.GetRelativePath(root, assemblyPath).Replace('\\', '/');
        if (!identity.CriticalAssemblyHashes.TryGetValue(assemblyRelativePath, out var expectedAssemblyHash) ||
            !string.Equals(RevalidationJson.Hash(assemblyPath), expectedAssemblyHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Running M9 probe assembly is not present in the verified build identity.");
        }

        return identity;
    }

    private static string ResolveOutputPath(string root, string requested)
    {
        var output = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(root, "Artifacts", "RoadMap", "M9", "backup-restore.json")
            : Path.GetFullPath(requested);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("M9 output must remain under the repository root.");
        }

        return output;
    }

    private static void ValidateSchemaName(string value, string label)
    {
        if (!SafeSchemaName.IsMatch(value))
        {
            throw new InvalidDataException($"The {label} identity is not safe for an isolated backup/restore operation.");
        }
    }

    private static void ValidateDatabaseEncodingName(string value, string label)
    {
        if (!SafeDatabaseEncodingName.IsMatch(value))
        {
            throw new InvalidDataException($"The {label} identity is not safe for isolated restore creation.");
        }
    }

    private static string Redact(string value, string? password) =>
        string.IsNullOrEmpty(password) ? value : value.Replace(password, "<secret>", StringComparison.Ordinal);

    private static void WriteArtifact(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, new UTF8Encoding(false));
        Console.WriteLine(json);
    }

    private static void Stage(string name)
    {
        Console.WriteLine($"M9_STAGE {name} {DateTimeOffset.UtcNow:O}");
        Console.Out.Flush();
    }

    private sealed record TableFingerprint(
        string Engine,
        long RowCount,
        string DefinitionSha256,
        string ContentChecksum);

    private sealed record MetadataFingerprint(int Count, string Sha256);

    private sealed record DatabaseFingerprint(
        string DefaultCharacterSet,
        string DefaultCollation,
        IReadOnlyDictionary<string, TableFingerprint> Tables,
        MetadataFingerprint Views,
        MetadataFingerprint Routines,
        MetadataFingerprint RoutineParameters,
        MetadataFingerprint Events,
        MetadataFingerprint Triggers);
}
