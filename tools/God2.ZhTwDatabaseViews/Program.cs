using System.Text.Json;
using System.Text;
using God2.GameplayContentRecovery;
using MySqlConnector;

var arguments = ParseArguments(args);
var repositoryRoot = Path.GetFullPath(arguments.GetValueOrDefault("repository-root") ?? Directory.GetCurrentDirectory());
var configPath = Path.GetFullPath(arguments.GetValueOrDefault("config") ?? Path.Combine(repositoryRoot, "config", "database.json"));
using var configDocument = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
var config = configDocument.RootElement;
var password = Environment.GetEnvironmentVariable("GOD2_DB_ADMIN_PASSWORD")
    ?? throw new InvalidOperationException("GOD2_DB_ADMIN_PASSWORD is required.");
var userName = Environment.GetEnvironmentVariable("GOD2_DB_ADMIN_USERNAME") ?? "root";

var connectionString = new MySqlConnectionStringBuilder
{
    Server = config.GetProperty("host").GetString() ?? "127.0.0.1",
    Port = config.GetProperty("port").GetUInt32(),
    UserID = userName,
    Password = password,
    CharacterSet = "utf8mb4",
    SslMode = MySqlSslMode.None
}.ConnectionString;

var schemaPairs = new[]
{
    (Source: "god2_game", Target: "god2_game_zh_tw"),
    (Source: "god2_player", Target: "god2_player_zh_tw"),
    (Source: "god2_game_meta", Target: "god2_game_meta_zh_tw")
};

var friendlyTableNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["item_registry"] = "物品共用索引",
    ["items"] = "道具",
    ["weapons"] = "武器",
    ["equipment"] = "裝備",
    ["magic_treasures"] = "法寶",
    ["item_usage_rules"] = "道具使用限制",
    ["item_effects"] = "道具效果",
    ["item_asset_mappings"] = "道具圖檔對照",
    ["item_icon_atlases"] = "道具圖集",
    ["maps"] = "地圖",
    ["portals"] = "傳送點",
    ["monsters"] = "怪物",
    ["monster_skills"] = "怪物技能",
    ["monster_spawns"] = "怪物座標",
    ["monster_drops"] = "怪物掉落",
    ["npcs"] = "NPC",
    ["npc_spawns"] = "NPC座標",
    ["npc_dialogs"] = "NPC對話",
    ["skills"] = "技能",
    ["skill_levels"] = "技能等級",
    ["skill_effects"] = "技能效果",
    ["quests"] = "任務",
    ["quest_objectives"] = "任務目標",
    ["quest_rewards"] = "任務獎勵",
    ["merchants"] = "商人",
    ["merchant_inventory"] = "商人物品",
    ["characters"] = "角色",
    ["character_inventory"] = "角色背包",
    ["character_equipment"] = "角色已裝備",
    ["equipment_instances"] = "裝備實例強化",
    ["item_use_idempotency"] = "道具使用冪等紀錄"
};

await using var connection = new MySqlConnection(connectionString);
await connection.OpenAsync();
var localization = new ZhTwLocalization(God2Glossary.Create());

if (bool.TryParse(arguments.GetValueOrDefault("formal-language-summary"), out var formalLanguageSummary) && formalLanguageSummary)
{
    var rows = await BuildFormalLanguageSummaryAsync(connection, localization);
    Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (arguments.TryGetValue("generate-formal-language-normalization-migration", out var migrationPath))
{
    var result = await GenerateFormalLanguageNormalizationMigrationAsync(
        connection,
        localization,
        Path.GetFullPath(migrationPath),
        CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (bool.TryParse(arguments.GetValueOrDefault("audit-only"), out var auditOnly) && auditOnly)
{
    var result = await FormalLanguageIntegrityAuditor.AuditAsync(
        connection,
        localization,
        CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Environment.ExitCode = result.SimplifiedFindingCount == 0 && result.MojibakeFindingCount == 0 ? 0 : 4;
    return;
}

if (!arguments.ContainsKey("__legacy-create-mirror-schemas"))
{
    throw new InvalidOperationException(
        "The legacy _zh_tw mirror-schema generator is disabled. Formal runtime database consolidation keeps only god2, god2_game, god2_game_meta, and god2_player. Use --audit-only, --formal-language-summary true, or --generate-formal-language-normalization-migration <path>.");
}

var generatedViews = 0;
var generatedFriendlyViews = 0;

foreach (var (sourceSchema, targetSchema) in schemaPairs)
{
    await ExecuteAsync(connection, $"CREATE DATABASE IF NOT EXISTS {Quote(targetSchema)} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
    var tables = await ReadTablesAsync(connection, sourceSchema);
    foreach (var table in tables)
    {
        var columns = await ReadColumnsAsync(connection, sourceSchema, table);
        var projection = BuildProjection(columns);
        await ExecuteAsync(connection,
            $"CREATE OR REPLACE ALGORITHM=MERGE SQL SECURITY INVOKER VIEW {Quote(targetSchema)}.{Quote(table)} AS SELECT {projection} FROM {Quote(sourceSchema)}.{Quote(table)};");
        generatedViews++;

        if (friendlyTableNames.TryGetValue(table, out var friendlyName))
        {
            await ExecuteAsync(connection,
                $"CREATE OR REPLACE ALGORITHM=MERGE SQL SECURITY INVOKER VIEW {Quote(targetSchema)}.{Quote(friendlyName)} AS SELECT {projection} FROM {Quote(sourceSchema)}.{Quote(table)};");
            generatedFriendlyViews++;
        }
    }
}

Console.WriteLine($"status=PASS;traditionalSchemas={schemaPairs.Length};tableViews={generatedViews};friendlyViews={generatedFriendlyViews}");

static string BuildProjection(IReadOnlyList<ColumnInfo> columns)
{
    var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var parts = new List<string>(columns.Count);
    foreach (var column in columns)
    {
        var baseAlias = NormalizeAlias(column.Comment, column.Name);
        var alias = baseAlias;
        var suffix = 2;
        while (!usedAliases.Add(alias))
        {
            var suffixText = $"_{suffix++}";
            alias = baseAlias[..Math.Min(baseAlias.Length, 60 - suffixText.Length)] + suffixText;
        }

        parts.Add($"{Quote(column.Name)} AS {Quote(alias)}");
    }

    return string.Join(',', parts);
}

static string NormalizeAlias(string comment, string columnName)
{
    var known = TranslateKnown(columnName);
    var alias = known ?? (ContainsCjk(comment) ? comment.Trim() : TranslateFallback(columnName));
    alias = alias.Replace('`', ' ').Replace('\r', ' ').Replace('\n', ' ');
    while (alias.Contains("  ", StringComparison.Ordinal))
    {
        alias = alias.Replace("  ", " ", StringComparison.Ordinal);
    }

    return alias.Length <= 60 ? alias : alias[..60];
}

static string? TranslateKnown(string name) => name.ToLowerInvariant() switch
{
    "item_id" => "物品ID",
    "npc_id" => "NPC識別ID",
    "session_id" => "連線階段ID",
    "sessionid" => "連線階段ID",
    "catalog_release_id" => "目錄發行ID",
    "catalog_fingerprint" => "目錄指紋",
    "client_item_id" => "官方客戶端道具ID",
    "inventory_id" => "背包實例ID",
    "character_id" => "角色ID",
    "code" => "可讀代碼",
    "name_zh_tw" => "繁體名稱",
    "name_original" => "原文名稱",
    "description_zh_tw" => "繁體說明",
    "item_category" => "主要分類",
    "item_family" => "細分類",
    "source_item_type" => "官方來源類型",
    "icon_id" => "圖碼",
    "model_id" => "模型ID",
    "model_key" => "模型鍵",
    "required_level" => "需求等級",
    "maximum_stack" => "最大堆疊數",
    "weight" => "重量",
    "buy_price" => "購買價格",
    "sell_price" => "出售價格",
    "droppable" => "可丟棄",
    "tradable" => "可交易",
    "storable" => "可存倉",
    "stackable" => "可堆疊",
    "usable" => "可使用",
    "equippable" => "可裝備",
    "use_on_other" => "可對他人使用",
    "evidence_status" => "證據狀態",
    "enabled" => "是否啟用",
    "admin_note" => "管理備註",
    "created_at_utc" => "建立UTC時間",
    "updated_at_utc" => "更新UTC時間",
    "validated_at_utc" => "驗證UTC時間",
    "normal_use" => "平時可用",
    "battle_use" => "戰鬥可用",
    "battle_usable" => "戰鬥可用",
    "hotkey_allowed" => "可設快捷鍵",
    "class_restriction_zh_tw" => "職業限制",
    "minimum_rebirth" => "最低轉生",
    "gender_restriction_zh_tw" => "性別限制",
    "equipment_target_restriction_zh_tw" => "裝備部位限制",
    "condition_text_zh_tw" => "限制條件",
    "field_evidence_status" => "欄位證據狀態",
    "text_rule_evidence_status" => "文字規則證據狀態",
    "source_reference_zh_tw" => "證據來源",
    "effect_index" => "效果順序",
    "effect_type" => "效果類型",
    "numeric_value" => "效果數值",
    "usage_scope" => "可使用場景",
    "target_policy" => "目標規則",
    "effect_text_zh_tw" => "效果說明",
    "weapon_type_zh_tw" => "武器類型",
    "treasure_type_zh_tw" => "法寶類型",
    "attack_range" => "攻擊距離",
    "physical_attack_bonus" => "物理攻擊加成",
    "magic_attack_bonus" => "法術攻擊加成",
    "base_durability" => "基礎耐久",
    "maximum_enhancement" => "最大強化等級",
    "socket_count" => "鑲嵌槽數",
    "effect_description_zh_tw" => "效果說明",
    "catalog_type" => "內容分類",
    "enhancement_level" => "目前強化等級",
    "refinement_level" => "目前精煉等級",
    "current_durability" => "目前耐久",
    "maximum_durability" => "最大耐久",
    "metal_bonus" => "金加成",
    "wood_bonus" => "木加成",
    "water_bonus" => "水加成",
    "fire_bonus" => "火加成",
    "earth_bonus" => "土加成",
    "magic_treasure_experience" => "法寶經驗",
    "bound" => "是否綁定",
    "atlas_id" => "圖集ID",
    "minimum_icon_code" => "最小圖碼",
    "maximum_icon_code" => "最大圖碼",
    "resource_path_original" => "官方圖檔路徑",
    "resource_path_resolved" => "本機圖檔路徑",
    "file_size_bytes" => "檔案大小位元組",
    "file_sha256" => "檔案SHA256",
    "validation_status" => "驗證狀態",
    "icon_code" => "圖碼",
    "icon_atlas_id" => "圖集ID",
    "icon_sprite_index" => "圖集內索引",
    "icon_validation_status" => "圖檔驗證狀態",
    "model_validation_status" => "模型驗證狀態",
    "overall_validation_status" => "整體驗證狀態",
    "validation_note_zh_tw" => "驗證備註",
    _ => null
};

static bool ContainsCjk(string value) => value.Any(character => character is >= '\u3400' and <= '\u9fff');

static string TranslateFallback(string name)
{
    var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = "ID",
        ["code"] = "代碼",
        ["enabled"] = "是否啟用",
        ["admin_note"] = "管理備註",
        ["created_at_utc"] = "建立UTC時間",
        ["updated_at_utc"] = "更新UTC時間",
        ["name_zh_tw"] = "繁體名稱",
        ["name_original"] = "原文名稱",
        ["description_zh_tw"] = "繁體說明",
        ["evidence_status"] = "證據狀態"
    };
    if (known.TryGetValue(name, out var translated))
    {
        return translated;
    }

    var tokenMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = "ID",
        ["map"] = "地圖",
        ["npc"] = "NPC",
        ["monster"] = "怪物",
        ["skill"] = "技能",
        ["quest"] = "任務",
        ["item"] = "物品",
        ["character"] = "角色",
        ["player"] = "玩家",
        ["name"] = "名稱",
        ["type"] = "類型",
        ["status"] = "狀態",
        ["level"] = "等級",
        ["value"] = "數值",
        ["count"] = "數量",
        ["maximum"] = "最大",
        ["minimum"] = "最小",
        ["source"] = "來源",
        ["target"] = "目標",
        ["position"] = "座標",
        ["description"] = "說明",
        ["original"] = "原文",
        ["required"] = "需求",
        ["created"] = "建立",
        ["updated"] = "更新",
        ["deleted"] = "刪除",
        ["utc"] = "UTC",
        ["enabled"] = "是否啟用"
    };
    return string.Concat(name.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(token => tokenMap.GetValueOrDefault(token, token)));
}

static async Task<IReadOnlyList<string>> ReadTablesAsync(MySqlConnection connection, string schema)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT `TABLE_NAME` FROM `information_schema`.`TABLES` WHERE `TABLE_SCHEMA`=@schema AND `TABLE_TYPE`='BASE TABLE' ORDER BY `TABLE_NAME`;";
    command.Parameters.AddWithValue("@schema", schema);
    var rows = new List<string>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(reader.GetString(0));
    }
    return rows;
}

static async Task<IReadOnlyList<ColumnInfo>> ReadColumnsAsync(MySqlConnection connection, string schema, string table)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT `COLUMN_NAME`,`COLUMN_COMMENT` FROM `information_schema`.`COLUMNS` WHERE `TABLE_SCHEMA`=@schema AND `TABLE_NAME`=@table ORDER BY `ORDINAL_POSITION`;";
    command.Parameters.AddWithValue("@schema", schema);
    command.Parameters.AddWithValue("@table", table);
    var rows = new List<ColumnInfo>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new ColumnInfo(reader.GetString(0), reader.GetString(1)));
    }
    return rows;
}

static async Task ExecuteAsync(MySqlConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}

static async Task<IReadOnlyList<object>> BuildFormalLanguageSummaryAsync(MySqlConnection connection, ZhTwLocalization localization)
{
    var result = new List<object>();
    await using var columnCommand = connection.CreateCommand();
    columnCommand.CommandText = """
        SELECT column_row.`TABLE_SCHEMA`,column_row.`TABLE_NAME`,column_row.`COLUMN_NAME`
        FROM `information_schema`.`COLUMNS` column_row
        JOIN `information_schema`.`TABLES` table_row
          ON table_row.`TABLE_SCHEMA`=column_row.`TABLE_SCHEMA`
         AND table_row.`TABLE_NAME`=column_row.`TABLE_NAME`
        WHERE column_row.`TABLE_SCHEMA` IN ('god2','god2_game','god2_player','god2_game_meta')
          AND table_row.`TABLE_TYPE`='BASE TABLE'
          AND column_row.`DATA_TYPE` IN ('char','varchar','tinytext','text','mediumtext','longtext')
        ORDER BY column_row.`TABLE_SCHEMA`,column_row.`TABLE_NAME`,column_row.`ORDINAL_POSITION`;
        """;
    var columns = new List<(string Schema, string Table, string Column)>();
    await using (var columnReader = await columnCommand.ExecuteReaderAsync())
    {
        while (await columnReader.ReadAsync())
        {
            var column = columnReader.GetString(2);
            if (LanguageIntegrityPolicy.IsSensitiveColumn(column) || !LanguageIntegrityPolicy.IsDisplayTextColumn(column))
            {
                continue;
            }

            columns.Add((columnReader.GetString(0), columnReader.GetString(1), column));
        }
    }

    foreach (var column in columns)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Quote(column.Column)} FROM {Quote(column.Schema)}.{Quote(column.Table)} WHERE {Quote(column.Column)} IS NOT NULL AND {Quote(column.Column)}<>'';";
        var rowCount = 0L;
        var simplifiedCount = 0L;
        string? sample = null;
        string? convertedSample = null;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rowCount++;
            var value = reader.GetString(0);
            if (!localization.ContainsSimplifiedGlyph(value))
            {
                continue;
            }

            simplifiedCount++;
            if (sample is null)
            {
                sample = LanguageIntegrityPolicy.SafeSample(value);
                convertedSample = LanguageIntegrityPolicy.SafeSample(localization.ConvertForFormalDatabase(value));
            }
        }

        if (simplifiedCount != 0 || column.Column.Contains("original", StringComparison.OrdinalIgnoreCase))
        {
            result.Add(new
            {
                column.Schema,
                column.Table,
                column.Column,
                RowCount = rowCount,
                SimplifiedRowCount = simplifiedCount,
                IsOriginalColumn = column.Column.Contains("original", StringComparison.OrdinalIgnoreCase),
                Sample = sample,
                ConvertedSample = convertedSample
            });
        }
    }

    return result;
}

static async Task<object> GenerateFormalLanguageNormalizationMigrationAsync(
    MySqlConnection connection,
    ZhTwLocalization localization,
    string migrationPath,
    CancellationToken cancellationToken)
{
    var generatedUpdates = 0;
    var skippedOriginalColumnUpdates = 0;
    var directory = Path.GetDirectoryName(migrationPath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    await using var writer = new StreamWriter(migrationPath, false, new UTF8Encoding(false));
    await writer.WriteLineAsync("-- Normalize formal runtime database text to Traditional Chinese only.");
    await writer.WriteLineAsync("-- Generated by God2.ZhTwDatabaseViews with evidence/source originals kept outside formal readable tables.");
    await writer.WriteLineAsync();

    var columns = await ReadFormalDisplayTextColumnsAsync(connection, cancellationToken);
    foreach (var column in columns)
    {
        if (column.Schema == "god2_game" && column.Column.Contains("original", StringComparison.OrdinalIgnoreCase))
        {
            skippedOriginalColumnUpdates++;
            continue;
        }

        var primaryKeys = await ReadPrimaryKeysAsync(connection, column.Schema, column.Table, cancellationToken);
        if (primaryKeys.Count == 0)
        {
            continue;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(',', primaryKeys.Select(Quote))},{Quote(column.Column)} FROM {Quote(column.Schema)}.{Quote(column.Table)} WHERE {Quote(column.Column)} IS NOT NULL AND {Quote(column.Column)}<>'';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = reader.GetString(primaryKeys.Count);
            if (!localization.ContainsSimplifiedGlyph(value))
            {
                continue;
            }

            var converted = localization.ConvertForFormalDatabase(value);
            if (string.Equals(value, converted, StringComparison.Ordinal))
            {
                continue;
            }

            var predicates = new List<string>(primaryKeys.Count + 1);
            for (var index = 0; index < primaryKeys.Count; index++)
            {
                predicates.Add($"{Quote(primaryKeys[index])}={SqlLiteral(reader.GetValue(index))}");
            }

            predicates.Add($"{Quote(column.Column)}={SqlLiteral(value)}");
            await writer.WriteLineAsync($"UPDATE {Quote(column.Schema)}.{Quote(column.Table)} SET {Quote(column.Column)}={SqlLiteral(converted)} WHERE {string.Join(" AND ", predicates)};");
            generatedUpdates++;
        }
    }

    await writer.WriteLineAsync();
    var originalViews = await ReadGod2GameViewsContainingOriginalAsync(connection, cancellationToken);
    var orderedOriginalViews = originalViews.Order(StringComparer.Ordinal).ToArray();
    if (orderedOriginalViews.Length != 0)
    {
        await writer.WriteLineAsync("DROP VIEW IF EXISTS");
        await writer.WriteLineAsync(string.Join(",\n", orderedOriginalViews.Select(view => $"    `god2_game`.{Quote(view)}")) + ";");
    }
    await writer.WriteLineAsync();
    await writer.WriteLineAsync("CREATE TABLE IF NOT EXISTS `god2_research`.`formal_catalog_original_text_evidence` (");
    await writer.WriteLineAsync("    `schema_name` varchar(64) NOT NULL,");
    await writer.WriteLineAsync("    `table_name` varchar(128) NOT NULL,");
    await writer.WriteLineAsync("    `column_name` varchar(128) NOT NULL,");
    await writer.WriteLineAsync("    `row_identity` varchar(256) NOT NULL,");
    await writer.WriteLineAsync("    `original_text` longtext NULL,");
    await writer.WriteLineAsync("    `captured_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),");
    await writer.WriteLineAsync("    PRIMARY KEY (`schema_name`,`table_name`,`column_name`,`row_identity`)");
    await writer.WriteLineAsync(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
    await writer.WriteLineAsync("COMMENT='Research-only original text moved out of formal Traditional Chinese runtime tables.';");
    await writer.WriteLineAsync();

    var originalColumns = await ReadGod2GameOriginalColumnsAsync(connection, cancellationToken);
    await writer.WriteLineAsync("-- Original text evidence belongs in god2_research/import evidence, not in formal readable runtime tables.");
    await writer.WriteLineAsync("-- DROP COLUMN IF EXISTS below is intentionally idempotent for already-normalized databases.");

    foreach (var group in originalColumns.GroupBy(column => (column.Schema, column.Table)).OrderBy(group => group.Key.Schema).ThenBy(group => group.Key.Table))
    {
        await writer.WriteLineAsync($"ALTER TABLE {Quote(group.Key.Schema)}.{Quote(group.Key.Table)}");
        await writer.WriteLineAsync(string.Join(",\n", group.Select(column => $"    DROP COLUMN IF EXISTS {Quote(column.Column)}")) + ";");
    }

    await writer.WriteLineAsync();
    await writer.WriteLineAsync("CREATE OR REPLACE VIEW `god2_game`.`vw_all_item_definitions` AS");
    await writer.WriteLineAsync("SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`description_zh_tw`,`item_category`,`item_family`,");
    await writer.WriteLineAsync("       `required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,");
    await writer.WriteLineAsync("       `stackable`,`usable`,`equippable`,`use_on_other`,`enabled`,`created_at_utc`,`updated_at_utc`,'道具' AS `catalog_type_zh_tw` FROM `god2_game`.`items`");
    await writer.WriteLineAsync("UNION ALL SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`description_zh_tw`,`item_category`,`item_family`,");
    await writer.WriteLineAsync("       `required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,");
    await writer.WriteLineAsync("       `stackable`,`usable`,`equippable`,`use_on_other`,`enabled`,`created_at_utc`,`updated_at_utc`,'武器' AS `catalog_type_zh_tw` FROM `god2_game`.`weapons`");
    await writer.WriteLineAsync("UNION ALL SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`description_zh_tw`,`item_category`,`item_family`,");
    await writer.WriteLineAsync("       `required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,");
    await writer.WriteLineAsync("       `stackable`,`usable`,`equippable`,`use_on_other`,`enabled`,`created_at_utc`,`updated_at_utc`,'裝備' AS `catalog_type_zh_tw` FROM `god2_game`.`equipment`");
    await writer.WriteLineAsync("UNION ALL SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`description_zh_tw`,`item_category`,`item_family`,");
    await writer.WriteLineAsync("       `required_level`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`droppable`,`tradable`,`storable`,");
    await writer.WriteLineAsync("       `stackable`,`usable`,`equippable`,`use_on_other`,`enabled`,`created_at_utc`,`updated_at_utc`,'法寶' AS `catalog_type_zh_tw` FROM `god2_game`.`magic_treasures`;");
    await writer.WriteLineAsync();
    await writer.WriteLineAsync("CREATE OR REPLACE VIEW `god2_game`.`vw_items_full` AS SELECT `item_id` AS `物品ID`,`code` AS `代碼`,`name_zh_tw` AS `名稱`,`item_category` AS `分類`,`item_family` AS `家族`,`required_level` AS `需求等級`,`maximum_stack` AS `最大堆疊`,`buy_price` AS `買價`,`sell_price` AS `賣價`,`enabled` AS `啟用狀態` FROM `god2_game`.`items`;");
    await writer.WriteLineAsync("CREATE OR REPLACE VIEW `god2_game`.`vw_items_classified` AS SELECT item_row.`client_item_id` AS `客戶端道具ID`,item_row.`code` AS `可讀代碼`,item_row.`name_zh_tw` AS `繁體名稱`,item_row.`description_zh_tw` AS `用途說明`,item_row.`catalog_type_zh_tw` AS `資料表分類`,item_row.`item_category` AS `主要分類`,item_row.`item_family` AS `細分類`,rule_row.`normal_use` AS `平時可用`,rule_row.`battle_use` AS `戰鬥可用`,item_row.`equippable` AS `可裝備`,item_row.`tradable` AS `可交易`,item_row.`droppable` AS `可丟棄`,item_row.`storable` AS `可存倉`,item_row.`stackable` AS `可堆疊`,item_row.`use_on_other` AS `可對他人使用`,rule_row.`class_restriction_zh_tw` AS `職業限制`,rule_row.`minimum_rebirth` AS `最低轉生`,rule_row.`gender_restriction_zh_tw` AS `性別限制`,rule_row.`equipment_target_restriction_zh_tw` AS `裝備部位限制`,CAST(NULL AS char(30)) AS `使用旗標證據`,CAST(NULL AS char(30)) AS `文字限制證據`,GROUP_CONCAT(effect_row.`effect_text_zh_tw` ORDER BY effect_row.`effect_index` SEPARATOR '、') AS `已實裝效果`,item_row.`enabled` AS `服務端啟用` FROM `god2_game`.`vw_all_item_definitions` item_row LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id` LEFT JOIN `god2_game`.`item_effects` effect_row ON effect_row.`item_id`=item_row.`item_id` AND effect_row.`enabled`=1 GROUP BY item_row.`item_id`,item_row.`client_item_id`,item_row.`code`,item_row.`name_zh_tw`,item_row.`description_zh_tw`,item_row.`catalog_type_zh_tw`,item_row.`item_category`,item_row.`item_family`,rule_row.`normal_use`,rule_row.`battle_use`,item_row.`equippable`,item_row.`tradable`,item_row.`droppable`,item_row.`storable`,item_row.`stackable`,item_row.`use_on_other`,rule_row.`class_restriction_zh_tw`,rule_row.`minimum_rebirth`,rule_row.`gender_restriction_zh_tw`,rule_row.`equipment_target_restriction_zh_tw`,item_row.`enabled`;");

    return new { MigrationPath = migrationPath, GeneratedUpdates = generatedUpdates, SkippedGod2GameOriginalColumnUpdates = skippedOriginalColumnUpdates, DroppedGod2GameOriginalColumns = originalColumns.Count, DroppedOriginalViews = originalViews.Count };
}

static async Task<IReadOnlyList<(string Schema, string Table, string Column)>> ReadFormalDisplayTextColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT column_row.`TABLE_SCHEMA`,column_row.`TABLE_NAME`,column_row.`COLUMN_NAME`
        FROM `information_schema`.`COLUMNS` column_row
        JOIN `information_schema`.`TABLES` table_row
          ON table_row.`TABLE_SCHEMA`=column_row.`TABLE_SCHEMA`
         AND table_row.`TABLE_NAME`=column_row.`TABLE_NAME`
        WHERE column_row.`TABLE_SCHEMA` IN ('god2','god2_game','god2_player','god2_game_meta')
          AND table_row.`TABLE_TYPE`='BASE TABLE'
          AND column_row.`DATA_TYPE` IN ('char','varchar','tinytext','text','mediumtext','longtext')
        ORDER BY column_row.`TABLE_SCHEMA`,column_row.`TABLE_NAME`,column_row.`ORDINAL_POSITION`;
        """;
    var result = new List<(string Schema, string Table, string Column)>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        var column = reader.GetString(2);
        if (!LanguageIntegrityPolicy.IsSensitiveColumn(column) && LanguageIntegrityPolicy.IsDisplayTextColumn(column))
        {
            result.Add((reader.GetString(0), reader.GetString(1), column));
        }
    }

    return result;
}

static async Task<IReadOnlyList<(string Schema, string Table, string Column)>> ReadGod2GameOriginalColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT column_row.`TABLE_SCHEMA`,column_row.`TABLE_NAME`,column_row.`COLUMN_NAME`
        FROM `information_schema`.`COLUMNS` column_row
        JOIN `information_schema`.`TABLES` table_row
          ON table_row.`TABLE_SCHEMA`=column_row.`TABLE_SCHEMA`
         AND table_row.`TABLE_NAME`=column_row.`TABLE_NAME`
        WHERE column_row.`TABLE_SCHEMA`='god2_game'
          AND table_row.`TABLE_TYPE`='BASE TABLE'
          AND LOWER(column_row.`COLUMN_NAME`) LIKE '%original%'
          AND column_row.`DATA_TYPE` IN ('char','varchar','tinytext','text','mediumtext','longtext')
        ORDER BY column_row.`TABLE_NAME`,column_row.`ORDINAL_POSITION`;
        """;
    var result = new List<(string Schema, string Table, string Column)>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        result.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
    }

    return result;
}

static async Task<IReadOnlyList<string>> ReadGod2GameViewsContainingOriginalAsync(MySqlConnection connection, CancellationToken cancellationToken)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT `TABLE_NAME`
        FROM `information_schema`.`VIEWS`
        WHERE `TABLE_SCHEMA`='god2_game'
          AND LOWER(`VIEW_DEFINITION`) LIKE '%original%'
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

static async Task<IReadOnlyList<string>> ReadPrimaryKeysAsync(MySqlConnection connection, string schema, string table, CancellationToken cancellationToken)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT `COLUMN_NAME`
        FROM `information_schema`.`KEY_COLUMN_USAGE`
        WHERE `CONSTRAINT_SCHEMA`=@schema AND `TABLE_NAME`=@table AND `CONSTRAINT_NAME`='PRIMARY'
        ORDER BY `ORDINAL_POSITION`;
        """;
    command.Parameters.AddWithValue("@schema", schema);
    command.Parameters.AddWithValue("@table", table);
    var result = new List<string>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        result.Add(reader.GetString(0));
    }

    return result;
}

static string SqlLiteral(object? value)
{
    if (value is null or DBNull)
    {
        return "NULL";
    }

    return value switch
    {
        byte or sbyte or short or ushort or int or uint or long or ulong or decimal or float or double => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "NULL",
        bool boolean => boolean ? "1" : "0",
        DateTime dateTime => $"'{dateTime:yyyy-MM-dd HH:mm:ss.ffffff}'",
        _ => "'" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal) + "'"
    };
}

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

static string Quote(string identifier) => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

internal sealed record ColumnInfo(string Name, string Comment);
