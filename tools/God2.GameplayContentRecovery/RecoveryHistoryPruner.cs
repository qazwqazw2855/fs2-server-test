using System.Data;
using God2.ClassicServer.Application.Configuration;
using MySqlConnector;

namespace God2.GameplayContentRecovery;

public sealed record RecoveryHistoryPruneResult(
    IReadOnlyDictionary<string, string> KeptRuns,
    int RemovedRuns,
    long RemovedDependentRows,
    long ClearedStalePromotionReferences);

public sealed class RecoveryHistoryPruner(DatabaseOptions options)
{
    public async Task<RecoveryHistoryPruneResult> PruneAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(BuildConnectionString());
        await connection.OpenAsync(cancellationToken);
        if (await ScalarAsync(connection, "SELECT GET_LOCK('god2-content-recovery-history-prune',0);", cancellationToken) != 1)
        {
            throw new InvalidOperationException("Another recovery history prune is already active.");
        }

        try
        {
            var keptRuns = await ReadLatestTerminalRunsAsync(connection, cancellationToken);
            if (keptRuns.Count == 0)
            {
                throw new InvalidOperationException("No terminal recovery runs exist; history was not changed.");
            }

            var runIds = keptRuns.Values.Distinct(StringComparer.Ordinal).ToArray();
            var references = await ReadRunReferencesAsync(connection, cancellationToken);
            var promotionTables = await ReadPromotionReferenceTablesAsync(connection, cancellationToken);
            long removedDependentRows = 0;
            long clearedPromotionReferences = 0;
            int removedRuns;

            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                foreach (var table in references.GroupBy(value => value.Table, StringComparer.Ordinal))
                {
                    var predicates = table.Select(value => $"{Id(value.Column)} NOT IN ({Parameters(runIds.Length)})");
                    removedDependentRows += await ExecuteAsync(
                        connection,
                        transaction,
                        $"DELETE FROM {Id(table.Key)} WHERE {string.Join(" OR ", predicates)};",
                        runIds,
                        cancellationToken);
                }

                foreach (var table in promotionTables)
                {
                    clearedPromotionReferences += await ExecuteAsync(
                        connection,
                        transaction,
                        $"UPDATE {Id(table)} SET `PromotionRunId`=NULL WHERE `PromotionRunId` IS NOT NULL AND `PromotionRunId` NOT IN ({Parameters(runIds.Length)});",
                        runIds,
                        cancellationToken);
                }

                removedRuns = await ExecuteAsync(
                    connection,
                    transaction,
                    $"DELETE FROM `content_recovery_runs` WHERE `RunId` NOT IN ({Parameters(runIds.Length)});",
                    runIds,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }

            return new RecoveryHistoryPruneResult(keptRuns, removedRuns, removedDependentRows, clearedPromotionReferences);
        }
        finally
        {
            if (connection.State == ConnectionState.Open)
            {
                await ScalarAsync(connection, "SELECT RELEASE_LOCK('god2-content-recovery-history-prune');", CancellationToken.None);
            }
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadLatestTerminalRunsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `Phase`,`RunId`
            FROM `content_recovery_runs`
            WHERE `CompletedAtUtc` IS NOT NULL AND `Status`<>'RUNNING'
            ORDER BY `Phase`,`CompletedAtUtc` DESC,`StartedAtUtc` DESC,`RunId` DESC;
            """;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var phase = Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!;
            var runId = Convert.ToString(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture)!;
            result.TryAdd(phase, runId);
        }

        return result;
    }

    private static async Task<IReadOnlyList<(string Table, string Column)>> ReadRunReferencesAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT `TABLE_NAME`,`COLUMN_NAME`
            FROM `information_schema`.`KEY_COLUMN_USAGE`
            WHERE `TABLE_SCHEMA`=DATABASE()
              AND `REFERENCED_TABLE_SCHEMA`=DATABASE()
              AND `REFERENCED_TABLE_NAME`='content_recovery_runs'
            ORDER BY `TABLE_NAME`,`COLUMN_NAME`;
            """;
        var result = new List<(string, string)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add((reader.GetString(0), reader.GetString(1)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<string>> ReadPromotionReferenceTablesAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `TABLE_NAME`
            FROM `information_schema`.`COLUMNS`
            WHERE `TABLE_SCHEMA`=DATABASE() AND `COLUMN_NAME`='PromotionRunId'
            ORDER BY `TABLE_NAME`;
            """;
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static async Task<int> ExecuteAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sql,
        IReadOnlyList<string> runIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 300;
        command.CommandText = sql;
        for (var index = 0; index < runIds.Count; index++)
        {
            command.Parameters.AddWithValue($"@run{index}", runIds[index]);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarAsync(MySqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private string BuildConnectionString() => new MySqlConnectionStringBuilder
    {
        Server = options.Host,
        Port = (uint)options.Port,
        Database = options.DatabaseName,
        UserID = options.Username,
        Password = options.Password,
        CharacterSet = "utf8mb4",
        ConnectionTimeout = (uint)Math.Max(1, options.ConnectionTimeoutSeconds),
        DefaultCommandTimeout = 300,
        Pooling = true,
        SslMode = MySqlSslMode.Preferred
    }.ConnectionString;

    private static string Parameters(int count) => string.Join(',', Enumerable.Range(0, count).Select(index => $"@run{index}"));

    private static string Id(string value) => $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";
}
