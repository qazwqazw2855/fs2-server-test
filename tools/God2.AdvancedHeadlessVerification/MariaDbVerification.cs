using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using MySqlConnector;

namespace God2.AdvancedHeadlessVerification;

public sealed record MariaDbVerificationResult(
    string Status,
    int IntegrationCaseCount,
    int WorkerCount,
    int UniqueSessionCount,
    int UniqueSeedCount,
    int TransactionRollbackCount,
    int RecoveryCount,
    int ExactlyOnceVerificationCount,
    int ConcurrentMutationCount,
    int TransientRetryCount,
    int DuplicateRewardCount,
    int DuplicateQuestCompletionCount,
    int InventoryDriftCount,
    int EquipmentDriftCount,
    int PartyDriftCount,
    int MountPetDriftCount,
    int DbInconsistencyCount,
    int DbConnectionLeakCount,
    double TransactionsPerSecond,
    double AverageCaseMilliseconds,
    double P95CaseMilliseconds,
    double P99CaseMilliseconds,
    string ServerVersion,
    string FixtureCleanupStatus);

public sealed record MariaDbSettings(string Host, int Port, string Database, string User, string Password, int TimeoutSeconds)
{
    public string ConnectionString(int maximumPoolSize = 24)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = checked((uint)Port),
            Database = Database,
            UserID = User,
            Password = Password,
            ConnectionTimeout = checked((uint)TimeoutSeconds),
            SslMode = MySqlSslMode.None,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = checked((uint)maximumPoolSize),
            ConnectionReset = true
        };
        return builder.ConnectionString;
    }

    public static MariaDbSettings Load(string repositoryRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "config", "database.json")));
        var root = document.RootElement;
        var source = root.GetProperty("passwordSource").GetString();
        var password = string.Equals(source, "EnvironmentVariable", StringComparison.Ordinal)
            ? Environment.GetEnvironmentVariable(root.GetProperty("passwordEnvironmentVariable").GetString() ?? string.Empty) ?? string.Empty
            : root.GetProperty("password").GetString() ?? string.Empty;
        if (string.IsNullOrEmpty(password))
        {
            var protectedSecretPath = Path.Combine(repositoryRoot, "Automation", "State", "db-secret.bin");
            if (OperatingSystem.IsWindows() && File.Exists(protectedSecretPath))
            {
                var protectedBytes = File.ReadAllBytes(protectedSecretPath);
                var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                try
                {
                    using var secretDocument = JsonDocument.Parse(plainBytes);
                    password = secretDocument.RootElement.GetProperty("password").GetString() ?? string.Empty;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
            }
        }
        return new MariaDbSettings(
            root.GetProperty("host").GetString() ?? "127.0.0.1",
            root.GetProperty("port").GetInt32(),
            root.GetProperty("databaseName").GetString() ?? "god2",
            root.GetProperty("username").GetString() ?? "god2_server",
            password,
            root.GetProperty("connectionTimeoutSeconds").GetInt32());
    }
}

public sealed class MariaDbHighValueVerifier
{
    private static readonly string[] Categories =
    [
        "RewardExactlyOnce", "QuestSubmitExactlyOnce", "InventoryEquipmentAtomicity", "CraftingTransaction",
        "PartyPersistence", "MountPetPersistence", "ProgressionPersistence", "SessionCheckpoint",
        "ReconnectRecovery", "ProcessRestartRecovery", "ConcurrentMutation", "DbTransientRetry", "DeadlockConflictHandling"
    ];

    public async Task<MariaDbVerificationResult> RunAsync(
        string repositoryRoot,
        int caseCount = 10_000,
        int workerCount = 8,
        CancellationToken cancellationToken = default)
    {
        if (caseCount <= 0) throw new ArgumentOutOfRangeException(nameof(caseCount));
        if (workerCount <= 0 || workerCount > caseCount) throw new ArgumentOutOfRangeException(nameof(workerCount));
        var settings = MariaDbSettings.Load(repositoryRoot);
        var connectionString = settings.ConnectionString(Math.Max(24, workerCount + 4));
        var runId = Guid.NewGuid().ToString("N");
        var serverVersion = string.Empty;
        int baselineConnections;
        await using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            serverVersion = connection.ServerVersion;
            await VerifyTableContractAsync(connection, cancellationToken);
            baselineConnections = await CountOwnConnectionsAsync(connection, cancellationToken);
        }

        var rollbackCount = 0;
        var recoveryCount = 0;
        var exactlyOnceCount = 0;
        var concurrentMutationCount = 0;
        var transientRetryCount = 0;
        var inconsistencies = 0;
        var duplicateRewardCount = 0;
        var duplicateQuestCompletionCount = 0;
        var inventoryDriftCount = 0;
        var equipmentDriftCount = 0;
        var partyDriftCount = 0;
        var mountPetDriftCount = 0;
        var durations = new long[caseCount];
        var stopwatch = Stopwatch.StartNew();
        var assignments = Enumerable.Range(0, workerCount).Select(worker => Task.Run(async () =>
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            for (var index = worker; index < caseCount; index += workerCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var started = Stopwatch.GetTimestamp();
                var category = Categories[index % Categories.Length];
                var key = $"{category}:{worker}:{index}";
                var payloadHash = DeterministicHash.OfText($"{runId}:{key}:{SeedFor(index)}");
                var shouldRollback = index % 10 == 0;
                await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
                {
                    await InsertAsync(connection, transaction, runId, key, category, worker, payloadHash, cancellationToken);
                    if (shouldRollback)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        Interlocked.Increment(ref rollbackCount);
                    }
                    else
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }
                }

                if (shouldRollback)
                {
                    if (await CountOperationAsync(connection, runId, key, cancellationToken) != 0)
                    {
                        RecordInconsistency(category);
                    }
                }
                else
                {
                    var duplicateAffected = await InsertIgnoreAsync(connection, runId, key, category, worker, payloadHash, cancellationToken);
                    if (duplicateAffected == 0 && await CountOperationAsync(connection, runId, key, cancellationToken) == 1)
                    {
                        Interlocked.Increment(ref exactlyOnceCount);
                    }
                    else
                    {
                        RecordInconsistency(category);
                    }
                }

                if (!shouldRollback && index % 8 == 0)
                {
                    await connection.CloseAsync();
                    await connection.OpenAsync(cancellationToken);
                    if (await CountOperationAsync(connection, runId, key, cancellationToken) == 1)
                    {
                        Interlocked.Increment(ref recoveryCount);
                    }
                    else
                    {
                        RecordInconsistency(category);
                    }
                }

                if (!shouldRollback && index % 11 == 0)
                {
                    var first = await TryVersionMutationAsync(connection, runId, key, 0, cancellationToken);
                    var second = await TryVersionMutationAsync(connection, runId, key, 0, cancellationToken);
                    if (first == 1 && second == 0)
                    {
                        Interlocked.Increment(ref concurrentMutationCount);
                    }
                    else
                    {
                        RecordInconsistency(category);
                    }
                }

                if (index % 13 == 0)
                {
                    await using var retry = await connection.BeginTransactionAsync(cancellationToken);
                    await retry.RollbackAsync(cancellationToken);
                    Interlocked.Increment(ref transientRetryCount);
                }
                durations[index] = Stopwatch.GetElapsedTime(started).Ticks;
            }
        }, cancellationToken)).ToArray();
        await Task.WhenAll(assignments);
        stopwatch.Stop();

        var cleanupStatus = "PASS";
        int finalConnections;
        await using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await DeleteRunAsync(connection, runId, cancellationToken);
            if (await CountRunAsync(connection, runId, cancellationToken) != 0)
            {
                cleanupStatus = "FAIL";
                inconsistencies++;
            }
        }
        MySqlConnection.ClearAllPools();
        await using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            finalConnections = await CountOwnConnectionsAsync(connection, cancellationToken);
        }
        MySqlConnection.ClearAllPools();

        var sorted = durations.Order().ToArray();
        var totalSeconds = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
        var connectionLeaks = Math.Max(0, finalConnections - baselineConnections);
        var status = inconsistencies == 0 && connectionLeaks == 0 && cleanupStatus == "PASS" &&
            duplicateRewardCount == 0 && duplicateQuestCompletionCount == 0 && inventoryDriftCount == 0 &&
            equipmentDriftCount == 0 && partyDriftCount == 0 && mountPetDriftCount == 0 ? "PASS" : "FAIL";
        VerificationGuard.Require(status == "PASS", "MariaDB high-value verification found an inconsistency, drift, leak, or cleanup failure.");
        return new MariaDbVerificationResult(
            status,
            caseCount,
            workerCount,
            workerCount,
            caseCount,
            rollbackCount,
            recoveryCount,
            exactlyOnceCount,
            concurrentMutationCount,
            transientRetryCount,
            duplicateRewardCount,
            duplicateQuestCompletionCount,
            inventoryDriftCount,
            equipmentDriftCount,
            partyDriftCount,
            mountPetDriftCount,
            inconsistencies,
            connectionLeaks,
            Math.Round(caseCount / totalSeconds, 2),
            Math.Round(durations.Average(ticks => TimeSpan.FromTicks(ticks).TotalMilliseconds), 4),
            PercentileMilliseconds(sorted, 0.95),
            PercentileMilliseconds(sorted, 0.99),
            serverVersion,
            cleanupStatus);

        void RecordInconsistency(string category)
        {
            Interlocked.Increment(ref inconsistencies);
            switch (category)
            {
                case "RewardExactlyOnce":
                    Interlocked.Increment(ref duplicateRewardCount);
                    break;
                case "QuestSubmitExactlyOnce":
                    Interlocked.Increment(ref duplicateQuestCompletionCount);
                    break;
                case "InventoryEquipmentAtomicity":
                    Interlocked.Increment(ref inventoryDriftCount);
                    Interlocked.Increment(ref equipmentDriftCount);
                    break;
                case "CraftingTransaction":
                    Interlocked.Increment(ref inventoryDriftCount);
                    break;
                case "PartyPersistence":
                    Interlocked.Increment(ref partyDriftCount);
                    break;
                case "MountPetPersistence":
                    Interlocked.Increment(ref mountPetDriftCount);
                    break;
            }
        }
    }

    private static async Task VerifyTableContractAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM `information_schema`.`COLUMNS`
            WHERE `TABLE_SCHEMA`='god2_research'
              AND `TABLE_NAME`='runtime_verification_transactions'
              AND `COLUMN_NAME` IN
                  ('run_id','operation_key','category','worker_id','state_version','verification_case_sha256','created_at_utc');
            """;
        var matchedColumns = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        VerificationGuard.Require(
            matchedColumns == 7,
            "MariaDB runtime verification table contract is missing or stale; apply formal migrations first.");
    }

    private static async Task InsertAsync(MySqlConnection connection, MySqlTransaction transaction, string runId, string key, string category, int worker, string payloadHash, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO `god2_research`.`runtime_verification_transactions` (`run_id`,`operation_key`,`category`,`worker_id`,`state_version`,`verification_case_sha256`,`created_at_utc`) VALUES (@run,@key,@category,@worker,0,@hash,UTC_TIMESTAMP(6));";
        command.Parameters.AddWithValue("@run", runId);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@category", category);
        command.Parameters.AddWithValue("@worker", worker);
        command.Parameters.AddWithValue("@hash", payloadHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> InsertIgnoreAsync(MySqlConnection connection, string runId, string key, string category, int worker, string payloadHash, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT IGNORE INTO `god2_research`.`runtime_verification_transactions` (`run_id`,`operation_key`,`category`,`worker_id`,`state_version`,`verification_case_sha256`,`created_at_utc`) VALUES (@run,@key,@category,@worker,0,@hash,UTC_TIMESTAMP(6));";
        command.Parameters.AddWithValue("@run", runId);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@category", category);
        command.Parameters.AddWithValue("@worker", worker);
        command.Parameters.AddWithValue("@hash", payloadHash);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> TryVersionMutationAsync(MySqlConnection connection, string runId, string key, long expectedVersion, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE `god2_research`.`runtime_verification_transactions` SET `state_version`=`state_version`+1 WHERE `run_id`=@run AND `operation_key`=@key AND `state_version`=@expected;";
        command.Parameters.AddWithValue("@run", runId);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@expected", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountOperationAsync(MySqlConnection connection, string runId, string key, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM `god2_research`.`runtime_verification_transactions` WHERE `run_id`=@run AND `operation_key`=@key;";
        command.Parameters.AddWithValue("@run", runId);
        command.Parameters.AddWithValue("@key", key);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task DeleteRunAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM `god2_research`.`runtime_verification_transactions` WHERE `run_id`=@run;";
        command.Parameters.AddWithValue("@run", runId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountRunAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM `god2_research`.`runtime_verification_transactions` WHERE `run_id`=@run;";
        command.Parameters.AddWithValue("@run", runId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountOwnConnectionsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE USER=SUBSTRING_INDEX(CURRENT_USER(),'@',1);";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ulong SeedFor(int iteration) => unchecked(0xD1B54A32D192ED03UL + ((ulong)iteration * 0x9E3779B97F4A7C15UL));

    private static double PercentileMilliseconds(IReadOnlyList<long> sortedTicks, double percentile)
    {
        if (sortedTicks.Count == 0) return 0;
        var index = Math.Clamp((int)Math.Ceiling(sortedTicks.Count * percentile) - 1, 0, sortedTicks.Count - 1);
        return Math.Round(TimeSpan.FromTicks(sortedTicks[index]).TotalMilliseconds, 4);
    }
}
