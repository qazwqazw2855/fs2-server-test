using System.Security.Cryptography;
using System.Text.Json;
using God2.GameplayContentRecovery;
using MySqlConnector;

var arguments = ParseArguments(args);
var repositoryRoot = Path.GetFullPath(arguments.GetValueOrDefault("repository-root") ?? Directory.GetCurrentDirectory());
var clientRoot = Path.GetFullPath(arguments.GetValueOrDefault("client-root") ?? throw new ArgumentException("--client-root is required."));
var configPath = Path.GetFullPath(arguments.GetValueOrDefault("config") ?? Path.Combine(repositoryRoot, "config", "database.json"));

using var configDocument = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
var config = configDocument.RootElement;
var password = Environment.GetEnvironmentVariable("GOD2_DB_ADMIN_PASSWORD")
    ?? Environment.GetEnvironmentVariable("GOD2_DB_BUILDER_PASSWORD")
    ?? throw new InvalidOperationException("GOD2_DB_ADMIN_PASSWORD or GOD2_DB_BUILDER_PASSWORD is required.");
var userName = Environment.GetEnvironmentVariable("GOD2_DB_ADMIN_PASSWORD") is not null
    ? Environment.GetEnvironmentVariable("GOD2_DB_ADMIN_USERNAME") ?? "root"
    : Environment.GetEnvironmentVariable("GOD2_DB_BUILDER_USERNAME") ?? "god2_catalog_builder";

var gameDataPath = Path.Combine(clientRoot, "Data2", "Patch", "Comm", "gamedata.csvZ");
var gameData = await God2PackedFile.ReadCsvZAsync(gameDataPath, CancellationToken.None);
var romMapsMarker = gameData.Rows
    .Select((row, index) => (row, index))
    .FirstOrDefault(entry => entry.row.Fields.Count >= 2 &&
        string.Equals(entry.row.Fields[0], "RomMaps", StringComparison.Ordinal) &&
        int.TryParse(entry.row.Fields[1], out _));
if (romMapsMarker.row is null || !int.TryParse(romMapsMarker.row.Fields[1], out var romMapsCount))
{
    throw new InvalidDataException("Official gamedata RomMaps section is missing.");
}
var romMapRows = gameData.Rows.Skip(romMapsMarker.index + 1).Take(romMapsCount).ToArray();
if (romMapRows.Length != romMapsCount)
{
    throw new InvalidDataException($"Official gamedata RomMaps declares {romMapsCount} rows but only {romMapRows.Length} are available.");
}

var atlases = new List<AtlasRecord>();
foreach (var row in romMapRows)
{
    if (row.Fields.Count < 5 ||
        !int.TryParse(row.Fields[0], out var atlasId) ||
        !int.TryParse(row.Fields[1], out var minimum) ||
        !int.TryParse(row.Fields[2], out var maximum))
    {
        throw new InvalidDataException($"Invalid RomMaps row at record {row.RecordIndex}.");
    }

    var declaredPath = row.Fields[3].Replace('\\', '/');
    var resolvedPath = ResolveAssetPath(clientRoot, declaredPath);
    string? relativePath = null;
    long? fileSize = null;
    string? sha256 = null;
    if (resolvedPath is not null)
    {
        relativePath = Path.GetRelativePath(clientRoot, resolvedPath).Replace('\\', '/');
        fileSize = new FileInfo(resolvedPath).Length;
        await using var stream = File.OpenRead(resolvedPath);
        sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    atlases.Add(new AtlasRecord(atlasId, minimum, maximum, declaredPath, relativePath, fileSize, sha256));
}

var connectionString = new MySqlConnectionStringBuilder
{
    Server = config.GetProperty("host").GetString() ?? "127.0.0.1",
    Port = config.GetProperty("port").GetUInt32(),
    Database = "god2_game",
    UserID = userName,
    Password = password,
    CharacterSet = "utf8mb4",
    SslMode = MySqlSslMode.None,
    AllowUserVariables = true
}.ConnectionString;

await using var connection = new MySqlConnection(connectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync();
await ExecuteAsync(connection, transaction, "SET @god2_sync_mode=1;");
await ExecuteAsync(connection, transaction, """
    CREATE TABLE IF NOT EXISTS `god2_research`.`item_icon_atlas_asset_evidence` (
        `atlas_id` int NOT NULL,
        `resource_path_resolved` varchar(500) NULL,
        `file_size_bytes` bigint NULL,
        `file_sha256` char(64) NULL,
        `validation_status` varchar(30) NOT NULL,
        `validated_at_utc` datetime(6) NULL,
        `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        PRIMARY KEY (`atlas_id`)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

    DELETE FROM `god2_game`.`item_asset_mappings`;
    DELETE FROM `god2_research`.`item_icon_atlas_asset_evidence`;
    DELETE FROM `god2_game`.`item_icon_atlases`;
    """);

foreach (var atlas in atlases)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = """
        INSERT INTO `god2_game`.`item_icon_atlases`
            (`atlas_id`,`minimum_icon_code`,`maximum_icon_code`,`validation_status`)
        VALUES (@id,@minimum,@maximum,@status);

        INSERT INTO `god2_research`.`item_icon_atlas_asset_evidence`
            (`atlas_id`,`resource_path_resolved`,`file_size_bytes`,`file_sha256`,`validation_status`,`validated_at_utc`)
        VALUES (@id,@resolved,@size,@sha,@status,UTC_TIMESTAMP(6));
        """;
    command.Parameters.AddWithValue("@id", atlas.Id);
    command.Parameters.AddWithValue("@minimum", atlas.Minimum);
    command.Parameters.AddWithValue("@maximum", atlas.Maximum);
    command.Parameters.AddWithValue("@resolved", (object?)atlas.ResolvedPath ?? DBNull.Value);
    command.Parameters.AddWithValue("@size", (object?)atlas.FileSize ?? DBNull.Value);
    command.Parameters.AddWithValue("@sha", (object?)atlas.Sha256 ?? DBNull.Value);
    command.Parameters.AddWithValue("@status", atlas.ResolvedPath is null ? "MissingAsset" : "Verified");
    await command.ExecuteNonQueryAsync();
}

await ExecuteAsync(connection, transaction, """
    INSERT INTO `god2_game`.`item_asset_mappings`
        (`item_id`,`client_item_id`,`catalog_type`,`icon_code`,`icon_atlas_id`,`icon_sprite_index`,`model_key`,
         `icon_validation_status`,`model_validation_status`,`overall_validation_status`,`validation_note_zh_tw`,`validated_at_utc`)
    SELECT registry_row.`item_id`,registry_row.`client_item_id`,
           CASE
               WHEN weapon_row.`item_id` IS NOT NULL THEN 'Weapon'
               WHEN equipment_row.`item_id` IS NOT NULL THEN 'Equipment'
               WHEN treasure_row.`item_id` IS NOT NULL THEN 'MagicTreasure'
               ELSE 'Item'
           END,
           registry_row.`icon_id`,
           atlas_row.`atlas_id`,
           registry_row.`icon_id` - atlas_row.`minimum_icon_code`,
           registry_row.`model_key`,
           CASE WHEN atlas_row.`atlas_id` IS NULL THEN 'RangeMissing'
                WHEN atlas_row.`validation_status` <> 'Verified' THEN 'AssetMissing'
                ELSE 'Verified' END,
           CASE WHEN NULLIF(registry_row.`model_key`,'') IS NULL
                THEN 'AssetPathUnresolved' ELSE 'ReferenceVerified' END,
           CASE WHEN atlas_row.`validation_status`='Verified'
                     AND NULLIF(registry_row.`model_key`,'') IS NOT NULL
                THEN 'Verified' ELSE 'EvidenceBlocked' END,
           CASE WHEN atlas_row.`atlas_id` IS NULL THEN '圖碼不在官方 RomMaps 範圍內'
                WHEN atlas_row.`validation_status` <> 'Verified' THEN '官方 RomMaps 圖檔未在客戶端安裝目錄找到'
                WHEN NULLIF(registry_row.`model_key`,'') IS NULL THEN '模型鍵缺失'
                ELSE '官方圖碼、圖集範圍、圖檔實體與模型鍵均已核對' END,
           UTC_TIMESTAMP(6)
    FROM `god2_game`.`item_registry` registry_row

    LEFT JOIN `god2_game`.`item_icon_atlases` atlas_row
      ON registry_row.`icon_id`
         BETWEEN atlas_row.`minimum_icon_code` AND atlas_row.`maximum_icon_code`
    LEFT JOIN `god2_game`.`weapons` weapon_row ON weapon_row.`item_id`=registry_row.`item_id`
    LEFT JOIN `god2_game`.`equipment` equipment_row ON equipment_row.`item_id`=registry_row.`item_id`
    LEFT JOIN `god2_game`.`magic_treasures` treasure_row ON treasure_row.`item_id`=registry_row.`item_id`;

    UPDATE `god2_game`.`item_registry` registry_row
    JOIN `god2_game`.`item_asset_mappings` mapping_row ON mapping_row.`item_id`=registry_row.`item_id`
    SET registry_row.`icon_id`=mapping_row.`icon_code`;

    UPDATE `god2_game`.`items` content_row
    JOIN `god2_game`.`item_asset_mappings` mapping_row ON mapping_row.`item_id`=content_row.`item_id`
    SET content_row.`icon_id`=mapping_row.`icon_code`,content_row.`model_key`=mapping_row.`model_key`;

    UPDATE `god2_game`.`weapons` content_row
    JOIN `god2_game`.`item_asset_mappings` mapping_row ON mapping_row.`item_id`=content_row.`item_id`
    SET content_row.`icon_id`=mapping_row.`icon_code`,content_row.`model_key`=mapping_row.`model_key`;

    UPDATE `god2_game`.`equipment` content_row
    JOIN `god2_game`.`item_asset_mappings` mapping_row ON mapping_row.`item_id`=content_row.`item_id`
    SET content_row.`icon_id`=mapping_row.`icon_code`,content_row.`model_key`=mapping_row.`model_key`;

    UPDATE `god2_game`.`magic_treasures` content_row
    JOIN `god2_game`.`item_asset_mappings` mapping_row ON mapping_row.`item_id`=content_row.`item_id`
    SET content_row.`icon_id`=mapping_row.`icon_code`,content_row.`model_key`=mapping_row.`model_key`;
    """);

var total = await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM `god2_game`.`item_asset_mappings`;");
var verified = await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM `god2_game`.`item_asset_mappings` WHERE `overall_validation_status`='Verified';");
var duplicated = await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM (SELECT `item_id` FROM `god2_game`.`vw_all_item_definitions` GROUP BY `item_id` HAVING COUNT(*)<>1) duplicate_rows;");
var registry = await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM `god2_game`.`item_registry`;");
if (total != registry || verified != total || duplicated != 0)
{
    await transaction.RollbackAsync();
    throw new InvalidDataException($"Strict asset validation failed: registry={registry}, mappings={total}, verified={verified}, duplicateDefinitions={duplicated}.");
}

await ExecuteAsync(connection, transaction, "SET @god2_sync_mode=NULL;");
await transaction.CommitAsync();
Console.WriteLine($"status=PASS;atlases={atlases.Count};items={total};verified={verified};duplicateDefinitions={duplicated}");

static Dictionary<string, string> ParseArguments(string[] args)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < args.Length; index += 2)
    {
        if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Arguments must use --name value pairs.");
        }

        values[args[index][2..]] = args[index + 1];
    }

    return values;
}

static string? ResolveAssetPath(string clientRoot, string declaredPath)
{
    var relative = declaredPath.Replace('/', Path.DirectorySeparatorChar);
    var direct = Path.Combine(clientRoot, relative);
    if (File.Exists(direct))
    {
        return direct;
    }

    if (string.Equals(Path.GetExtension(direct), ".rom", StringComparison.OrdinalIgnoreCase))
    {
        var compressed = direct + "z";
        if (File.Exists(compressed))
        {
            return compressed;
        }
    }

    return null;
}

static async Task ExecuteAsync(MySqlConnection connection, MySqlTransaction transaction, string sql)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}

static async Task<long> ScalarAsync(MySqlConnection connection, MySqlTransaction transaction, string sql)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    return Convert.ToInt64(await command.ExecuteScalarAsync());
}

internal sealed record AtlasRecord(
    int Id,
    int Minimum,
    int Maximum,
    string DeclaredPath,
    string? ResolvedPath,
    long? FileSize,
    string? Sha256);


