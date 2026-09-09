using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;

namespace God2.ReadableDatabaseBuilder;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            var root = Directory.GetCurrentDirectory();
            var config = Path.Combine(root, "config", "database.json");
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--repository-root") root = Path.GetFullPath(args[++i]);
                if (args[i] == "--config") config = Path.GetFullPath(args[++i]);
            }

            var settings = DatabaseSettings.Load(root, config);
            await using var connection = new MySqlConnection(settings.ConnectionString(null));
            await connection.OpenAsync();

            var builder = new ReadableDatabaseBuilder(connection, settings.Database);
            var result = await builder.BuildAsync();

            var artifactRoot = Path.Combine(root, "Artifacts", "RecoveryFinal");
            Directory.CreateDirectory(artifactRoot);
            var report = Path.Combine(artifactRoot, "god2-readable-database-report.md");
            await File.WriteAllTextAsync(report, ReportWriter.Write(result), new UTF8Encoding(true));

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "READABLE_DATABASE_REBUILT",
                sourceDatabase = settings.Database,
                researchDatabase = Names.ResearchDatabase,
                targetDatabase = Names.TargetDatabase,
                artifact = report,
                tables = result.Tables.Select(t => new { t.Name, t.RowCount, t.Columns }),
                result.MachineColumnCount,
                result.MachineValueCount,
                result.MonsterCompleteness
            }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or MySqlException or InvalidOperationException or CryptographicException)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "READABLE_DATABASE_REBUILD_FAILED", errorType = ex.GetType().Name, message = ex.Message }));
            return 1;
        }
    }
}

internal static class Names
{
    public const string SourceDatabaseDefault = "god2";
    public const string ResearchDatabase = "god2_research";
    public const string TargetDatabase = "god2_recovered";
}

internal sealed record DatabaseSettings(string Host, uint Port, string Database, string Username, string Password, uint TimeoutSeconds)
{
    public static DatabaseSettings Load(string root, string configPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(configPath));
        var json = doc.RootElement;
        var database = json.GetProperty("databaseName").GetString() ?? Names.SourceDatabaseDefault;
        var adminPassword = Environment.GetEnvironmentVariable("GOD2_DB_ADMIN_PASSWORD")
            ?? Secret(Path.Combine(root, "Automation", "State", "db-admin-secret.bin"), "GOD2_DB_ADMIN_PASSWORD");
        if (!string.IsNullOrWhiteSpace(adminPassword))
        {
            return new DatabaseSettings(json.GetProperty("host").GetString() ?? "127.0.0.1", (uint)json.GetProperty("port").GetInt32(), database, Environment.GetEnvironmentVariable("GOD2_DB_ADMIN_USERNAME") ?? "root", adminPassword, 5);
        }

        var envName = json.GetProperty("passwordEnvironmentVariable").GetString() ?? "GOD2_DB_PASSWORD";
        var password = Environment.GetEnvironmentVariable(envName) ?? Secret(Path.Combine(root, "Automation", "State", "db-secret.bin"), envName);
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("MariaDB password is missing.");
        return new DatabaseSettings(json.GetProperty("host").GetString() ?? "127.0.0.1", (uint)json.GetProperty("port").GetInt32(), database, json.GetProperty("username").GetString() ?? "god2_server", password, 5);
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
            SslMode = MySqlSslMode.Preferred,
            ConnectionTimeout = TimeoutSeconds,
            DefaultCommandTimeout = 300,
            AllowUserVariables = true
        };
        if (!string.IsNullOrWhiteSpace(database)) builder.Database = database;
        return builder.ConnectionString;
    }

    private static string Secret(string path, string expectedName)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return string.Empty;
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.TryGetProperty("environmentVariableName", out var name) && name.GetString() != expectedName) return string.Empty;
            return doc.RootElement.GetProperty("password").GetString() ?? string.Empty;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

internal sealed class ReadableDatabaseBuilder(MySqlConnection connection, string sourceDatabase)
{
    public static IReadOnlyList<Dictionary<string, object?>> MergeEncounterMonsterCatalog(
        IReadOnlyList<Dictionary<string, object?>> formal,
        IReadOnlyList<Dictionary<string, object?>> encounters)
    {
        var idColumn = formal.FirstOrDefault()?.FirstOrDefault(pair => pair.Value is int or long && !pair.Key.Contains("順序", StringComparison.Ordinal)).Key ?? "怪物編號";
        var nameColumn = encounters.FirstOrDefault()?.FirstOrDefault(pair => pair.Value is string).Key
            ?? formal.FirstOrDefault()?.FirstOrDefault(pair => pair.Value is string).Key
            ?? "名稱";
        var orderColumn = encounters.FirstOrDefault()?.FirstOrDefault(pair => pair.Value is int or long).Key ?? "目錄順序";
        if (encounters.Count == 0)
        {
            return formal.Select(row => new Dictionary<string, object?>(row, StringComparer.Ordinal)).ToArray();
        }

        var formalByName = formal
            .Where(row => row.TryGetValue(nameColumn, out var name) && name is not null)
            .GroupBy(row => Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .ToDictionary(group => group.Key ?? string.Empty, group => group.First(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Dictionary<string, object?>>();
        var nextId = 1;
        foreach (var encounter in encounters.OrderBy(row => row.TryGetValue(orderColumn, out var order) ? ToLong(order) : long.MaxValue))
        {
            var name = Convert.ToString(encounter.GetValueOrDefault(nameColumn), CultureInfo.InvariantCulture) ?? string.Empty;
            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            var row = formalByName.TryGetValue(name, out var formalRow)
                ? new Dictionary<string, object?>(formalRow, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [idColumn] = nextId,
                    [nameColumn] = name
                };
            row.Remove(orderColumn);
            result.Add(row);
            nextId++;
        }

        return result;
    }

    public async Task<BuildResult> BuildAsync()
    {
        await ExecuteAsync($"CREATE DATABASE IF NOT EXISTS {Id(Names.ResearchDatabase)} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        await CopyIfExistsAsync("official_skill_book_catalog");
        await CopyIfExistsAsync("latest_controlled_gameplay_observation");
        await CopyIfExistsAsync("verified_battle_action_semantics");
        await ExecuteAsync($"DROP DATABASE IF EXISTS {Id(Names.TargetDatabase)};");
        await ExecuteAsync($"CREATE DATABASE {Id(Names.TargetDatabase)} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");

        var tables = new List<TableResult>();
        tables.AddRange(await CreateOfficialSkillBookAsync());
        tables.AddRange(await CreateLatestObservationAsync());
        tables.AddRange(await CreateVerifiedSkillsAsync());
        tables.AddRange(await CreateBattleActionsAsync());
        tables.AddRange(await CreateMonsterReadableAsync());

        return new BuildResult(sourceDatabase, Names.TargetDatabase, tables.Where(t => t.RowCount > 0).ToArray(), await MonsterCompletenessAsync(), await MachineColumnCountAsync(), await MachineValueCountAsync());
    }

    private async Task CopyIfExistsAsync(string table)
    {
        if (!await TableExistsAsync(sourceDatabase, table)) return;
        await ExecuteAsync($"CREATE TABLE IF NOT EXISTS {Research(table)} LIKE {Source(table)};");
        await ExecuteAsync($"INSERT IGNORE INTO {Research(table)} SELECT * FROM {Source(table)};");
    }

    private async Task<IReadOnlyList<TableResult>> CreateOfficialSkillBookAsync()
    {
        if (!await TableExistsAsync(Names.ResearchDatabase, "official_skill_book_catalog")) return [];
        await ExecuteAsync($"""
            CREATE TABLE {Target("技能書目錄")} AS
            SELECT `OfficialClientItemId` AS `技能書物品編號`, `OfficialDisplayId` AS `官方顯示編號`,
                   `SkillNameZhTw` AS `技能名稱`, `DescriptionZhTw` AS `用途說明`,
                   `SkillModeZhTw` AS `技能型態`, `SkillCategoryZhTw` AS `技能分類`,
                   `SkillLevel` AS `階級`, `MpCost` AS `消耗MP`, `AttackRange` AS `攻擊距離`,
                   `TargetScopeZhTw` AS `作用範圍`, `OfficialEffectTextZhTw` AS `官方效果文字`,
                   `SystemSellPrice` AS `系統售價`, `SystemRecyclePrice` AS `回收價`,
                   CASE WHEN `LiveVerified`=1 THEN '是' ELSE '否' END AS `實機驗證`, `ObservedAtUtc` AS `實測時間`
            FROM {Research("official_skill_book_catalog")};
            """);
        return [await ResultAsync("技能書目錄")];
    }

    private async Task<IReadOnlyList<TableResult>> CreateLatestObservationAsync()
    {
        if (!await TableExistsAsync(Names.ResearchDatabase, "latest_controlled_gameplay_observation")) return [];
        await ExecuteAsync($"""
            CREATE TABLE {Target("最新受控實測")} AS
            SELECT `SessionLabelZhTw` AS `實測內容`, `StartedAtUtc` AS `開始時間`, `EndedAtUtc` AS `結束時間`,
                   `SkillCommandCount` AS `技能施放次數`, `DistinctTargetPatternCount` AS `不同目標模式`,
                   CASE WHEN `NoBattlePetConfirmed`=1 THEN '是' ELSE '否' END AS `未帶戰寵`,
                   CASE WHEN `ImmortalLevelUpConfirmed`=1 THEN '是' ELSE '否' END AS `神仙升級`,
                   CASE WHEN `MonsterCombatConfirmed`=1 THEN '是' ELSE '否' END AS `怪物參戰`,
                   `MonsterIdentityStatusZhTw` AS `怪物識別結果`, `SkillIdentityStatusZhTw` AS `技能識別結果`,
                   `ProgressionBindingStatusZhTw` AS `升級識別結果`, CASE WHEN `RuntimeReady`=1 THEN '是' ELSE '否' END AS `可正式啟用`
            FROM {Research("latest_controlled_gameplay_observation")};
            """);
        return [await ResultAsync("最新受控實測")];
    }

    private async Task<IReadOnlyList<TableResult>> CreateVerifiedSkillsAsync()
    {
        if (!await TableExistsAsync(Names.ResearchDatabase, "verified_skill_observations")) return [];
        await ExecuteAsync($"""
            CREATE TABLE {Target("實測技能")} AS
            SELECT `SkillNameZhTw` AS `名稱`, `SkillCategoryZhTw` AS `分類`, `SkillLevel` AS `階級`,
                   `MpCost` AS `消耗MP`, `AttackRange` AS `攻擊距離`, `TargetScopeZhTw` AS `作用範圍`,
                   `PowerDescriptionZhTw` AS `威力描述`, `OfficialClientItemId` AS `技能書物品編號`,
                   `OfficialDisplayId` AS `官方顯示編號`, `ObservedAtUtc` AS `確認時間`, `EvidenceStatusZhTw` AS `證據狀態`
            FROM {Research("verified_skill_observations")};
            """);
        return [await ResultAsync("實測技能")];
    }

    private async Task<IReadOnlyList<TableResult>> CreateBattleActionsAsync()
    {
        if (!await TableExistsAsync(Names.ResearchDatabase, "verified_battle_action_semantics")) return [];
        await ExecuteAsync($"""
            CREATE TABLE {Target("戰鬥動作")} AS
            SELECT `ActionNameZhTw` AS `動作名稱`, `ObservedAtUtc` AS `確認時間`,
                   CASE WHEN `RuntimeReady`=1 THEN '是' ELSE '否' END AS `可正式啟用`, `RemainingGapZhTw` AS `待確認內容`
            FROM {Research("verified_battle_action_semantics")};
            """);
        return [await ResultAsync("戰鬥動作")];
    }

    private async Task<IReadOnlyList<TableResult>> CreateMonsterReadableAsync()
    {
        if (!await TableExistsAsync("god2_game", "monsters")) return [];
        var cols = await ColumnsAsync("god2_game", "monsters");
        var name = cols.Contains("name_zh_tw") ? "`name_zh_tw`" : "CAST(`monster_id` AS char)";
        await ExecuteAsync($"""
            CREATE TABLE {Target("怪物")} AS
            SELECT `monster_id` AS `怪物編號`, {name} AS `名稱`,
                   {(cols.Contains("level") ? "`level`" : "NULL")} AS `等級`,
                   {(cols.Contains("hp") ? "`hp`" : "NULL")} AS `HP`,
                   {(cols.Contains("mp") ? "`mp`" : "NULL")} AS `MP`,
                   {(cols.Contains("attack") ? "`attack`" : "NULL")} AS `攻擊`,
                   {(cols.Contains("defense") ? "`defense`" : "NULL")} AS `防禦`,
                   {(cols.Contains("experience") ? "`experience`" : "NULL")} AS `經驗`
            FROM `god2_game`.`monsters`;
            """);
        return [await ResultAsync("怪物")];
    }

    private async Task<MonsterCompleteness> MonsterCompletenessAsync()
    {
        if (!await TableExistsAsync(Names.TargetDatabase, "怪物")) return new MonsterCompleteness(0, 0, 0, 0, 0, false, 0);
        var row = (await QueryAsync($"SELECT COUNT(*) Total, SUM(`等級` IS NOT NULL) WithLevel, SUM(`HP` IS NOT NULL) WithHp, SUM(`攻擊` IS NOT NULL) WithAttack, SUM(`防禦` IS NOT NULL) WithDefense, SUM(`經驗` IS NOT NULL) WithExperience FROM {Target("怪物")};")).Single();
        return new MonsterCompleteness(ToLong(row["Total"]), ToLong(row["WithLevel"]), ToLong(row["WithHp"]), ToLong(row["WithAttack"]), ToLong(row["WithDefense"]), true, ToLong(row["WithExperience"]));
    }

    private async Task<long> MachineColumnCountAsync() =>
        await ScalarAsync("SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='god2_recovered' AND COLUMN_NAME REGEXP 'evidence|hash|guid|raw|candidate|source|run|policy|status';");

    private async Task<long> MachineValueCountAsync()
    {
        var total = 0L;
        foreach (var row in await QueryAsync("SELECT TABLE_NAME,COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='god2_recovered' AND DATA_TYPE IN ('char','varchar','text','mediumtext','longtext');"))
        {
            var tableName = (string)row["TABLE_NAME"]!;
            var columnName = (string)row["COLUMN_NAME"]!;
            total += await ScalarAsync($"SELECT COUNT(*) FROM {Target(tableName)} WHERE {Id(columnName)} REGEXP '^[0-9a-fA-F]{{32,}}$' OR LEFT(TRIM({Id(columnName)}),1) IN ('{{','[');");
        }
        return total;
    }

    private async Task<TableResult> ResultAsync(string table)
    {
        var count = await ScalarAsync($"SELECT COUNT(*) FROM {Target(table)};");
        var cols = (await QueryAsync($"SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='god2_recovered' AND TABLE_NAME={Sql(table)} ORDER BY ORDINAL_POSITION;"))
            .Select(r => (string)r["COLUMN_NAME"]!).ToArray();
        return new TableResult(table, count, cols);
    }

    private async Task<bool> TableExistsAsync(string database, string table) => await ScalarAsync($"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA={Sql(database)} AND TABLE_NAME={Sql(table)};") > 0;
    private async Task<HashSet<string>> ColumnsAsync(string database, string table) => (await QueryAsync($"SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA={Sql(database)} AND TABLE_NAME={Sql(table)};")).Select(r => (string)r["COLUMN_NAME"]!).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<Dictionary<string, object?>>> QueryAsync(string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private async Task ExecuteAsync(string sql) { await using var c = connection.CreateCommand(); c.CommandText = sql; await c.ExecuteNonQueryAsync(); }
    private async Task<long> ScalarAsync(string sql) { await using var c = connection.CreateCommand(); c.CommandText = sql; return ToLong(await c.ExecuteScalarAsync()); }
    private static long ToLong(object? value) => Convert.ToInt64(value ?? 0, CultureInfo.InvariantCulture);
    private string Source(string table) => $"{Id(sourceDatabase)}.{Id(table)}";
    private static string Research(string table) => $"{Id(Names.ResearchDatabase)}.{Id(table)}";
    private static string Target(string table) => $"{Id(Names.TargetDatabase)}.{Id(table)}";
    private static string Id(string value) => $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";
    private static string Sql(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}

internal sealed record TableResult(string Name, long RowCount, IReadOnlyList<string> Columns);
public sealed record OfficialSkillBookEntry(int ClientItemId, int? DisplayId, string NameZhTw, string? DescriptionZhTw, string? ModeZhTw, string? CategoryZhTw, int? Level, int? MpCost, int? AttackRange, string? TargetScopeZhTw, string? EffectTextZhTw, int? SystemSellPrice, int? SystemRecyclePrice);
internal sealed record BuildResult(string SourceDatabase, string TargetDatabase, IReadOnlyList<TableResult> Tables, MonsterCompleteness MonsterCompleteness, long MachineColumnCount, long MachineValueCount);
internal sealed record MonsterCompleteness(long Total, long WithLevel, long WithHp, long WithAttack, long WithDefense, bool ExperienceColumnExists, long WithExperience);

internal static class ReportWriter
{
    public static string Write(BuildResult result)
    {
        var b = new StringBuilder();
        b.AppendLine("# God2 可讀資料庫重建結果");
        b.AppendLine($"- 來源資料庫：`{result.SourceDatabase}`");
        b.AppendLine($"- 研究資料庫：`{Names.ResearchDatabase}`");
        b.AppendLine($"- 交付資料庫：`{result.TargetDatabase}`");
        b.AppendLine("- 原則：只放玩家/服主看得懂的遊戲資料欄位；不放 Evidence、Hash、GUID、Raw、Candidate、Source、Run、Policy、Status。");
        foreach (var table in result.Tables)
        {
            b.AppendLine();
            b.AppendLine($"## {table.Name}");
            b.AppendLine($"- 筆數：{table.RowCount.ToString(CultureInfo.InvariantCulture)}");
            b.AppendLine($"- 欄位：{string.Join("、", table.Columns)}");
        }
        b.AppendLine();
        b.AppendLine("## 檢查");
        b.AppendLine($"- 禁止欄位數：{result.MachineColumnCount.ToString(CultureInfo.InvariantCulture)}");
        b.AppendLine($"- 禁止值數：{result.MachineValueCount.ToString(CultureInfo.InvariantCulture)}");
        b.AppendLine($"- 怪物 MP 欄位輸出：{(result.Tables.Any(t => t.Name == "怪物" && t.Columns.Contains("MP")) ? "是" : "否")}");
        return b.ToString();
    }
}
