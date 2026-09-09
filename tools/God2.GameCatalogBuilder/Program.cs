using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace God2.GameCatalogBuilder;

internal static class Program
{
    private const string BuilderVersion = "God2.GameCatalogBuilder/1.0";

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            var invocation = Invocation.Parse(args);
            var settings = DatabaseSettings.Load(invocation.ConfigPath);
            var builder = new CatalogBuilder(settings);
            object result = invocation.Command switch
            {
                "rebuild" => await builder.RebuildAsync(invocation.CancellationToken),
                "validate" => await builder.ValidateAsync(activate: false, invocation.CancellationToken),
                "reload" => await builder.ReloadAsync(invocation.CancellationToken),
                "refresh-caches" => await builder.RefreshCachesAsync(invocation.CancellationToken),
                "install-triggers" => await builder.InstallAdminTriggersAsync(invocation.CancellationToken),
                "world-coverage" => await builder.WorldCoverageAsync(invocation.CancellationToken),
                _ => throw new ArgumentException($"Unknown command '{invocation.Command}'.")
            };
            Console.WriteLine(JsonSerializer.Serialize(result, result.GetType(), JsonOptions));
            return result switch
            {
                CatalogCommandResult catalog => catalog.Succeeded ? 0 : 2,
                WorldContentCoverageResult coverage => coverage.Succeeded ? 0 : 2,
                _ => 2
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or JsonException or MySqlException)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                succeeded = false,
                errorType = exception.GetType().Name,
                message = exception.Message
            }, JsonOptions));
            return 1;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static string Version => BuilderVersion;
}

internal sealed record Invocation(string Command, string ConfigPath, bool AllowResearchSchema, CancellationToken CancellationToken)
{
    public bool RequiresResearchSchema =>
        Command is "rebuild" or "world-coverage";

    public static Invocation Parse(string[] args)
    {
        string[] supported = ["rebuild", "validate", "reload", "refresh-caches", "install-triggers", "world-coverage"];
        var command = args.FirstOrDefault(value => supported.Contains(value, StringComparer.OrdinalIgnoreCase)) ?? "validate";
        var configIndex = Array.FindIndex(args, value => string.Equals(value, "--config", StringComparison.OrdinalIgnoreCase));
        var configPath = configIndex >= 0 && configIndex + 1 < args.Length
            ? Path.GetFullPath(args[configIndex + 1])
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "config", "database.json"));
        var allowResearchSchema = args.Any(value => string.Equals(value, "--allow-research-schema", StringComparison.OrdinalIgnoreCase));
        return new Invocation(command.ToLowerInvariant(), configPath, allowResearchSchema, CancellationToken.None);
    }
}

internal sealed record DatabaseSettings(string Host, uint Port, string Database, string Username, string Password)
{
    public static DatabaseSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Database configuration is missing: {path}");
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var source = root.GetProperty("passwordSource").GetString() ?? "EnvironmentVariable";
        var builderPassword = Environment.GetEnvironmentVariable("GOD2_DB_BUILDER_PASSWORD");
        var password = !string.IsNullOrEmpty(builderPassword)
            ? builderPassword
            : string.Equals(source, "ConfigValue", StringComparison.OrdinalIgnoreCase)
                ? root.GetProperty("password").GetString()
                : Environment.GetEnvironmentVariable(root.GetProperty("passwordEnvironmentVariable").GetString() ?? "GOD2_DB_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("MariaDB password is missing. Resolve GOD2_DB_PASSWORD before running the catalog builder.");
        }

        return new DatabaseSettings(
            root.GetProperty("host").GetString() ?? "127.0.0.1",
            checked((uint)root.GetProperty("port").GetInt32()),
            root.GetProperty("databaseName").GetString() ?? "god2",
            Environment.GetEnvironmentVariable("GOD2_DB_BUILDER_USERNAME") ??
                root.GetProperty("username").GetString() ?? "god2_server",
            password);
    }

    public string ConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = Port,
            Database = Database,
            UserID = Username,
            Password = Password,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 120,
            AllowUserVariables = true,
            Pooling = true,
            SslMode = MySqlSslMode.Preferred
        };
        return builder.ConnectionString;
    }
}

internal sealed record CatalogCommandResult(
    bool Succeeded,
    string Command,
    string? CatalogReleaseId,
    string? CatalogFingerprint,
    long CatalogRecordCount,
    int ValidationErrorCount,
    int ValidationWarningCount,
    long MappedValueCount,
    long BlockedCandidateCount,
    long ConflictCount,
    long UnmappedFieldCount,
    int AdminTriggerCount,
    IReadOnlyList<string> Messages);

internal sealed record ValidationIssue(
    string Severity,
    string? Schema,
    string? Table,
    string? EntityId,
    string? Field,
    string RuleCode,
    string MessageZhTw);

internal sealed record ValidationOutcome(
    bool Succeeded,
    string ValidationRunId,
    string? CatalogReleaseId,
    string Fingerprint,
    long RecordCount,
    IReadOnlyList<ValidationIssue> Issues);

internal sealed record SyncOutcome(long Mapped, long Blocked, long Conflicts, long Unmapped);

internal sealed record WorldContentCoverageMetric(
    string Domain,
    long? ExpectedTotal,
    long CatalogTotal,
    long ProductionReady,
    long IncompleteEnabled,
    long MissingProductionBindings);

internal sealed record WorldContentCoverageResult(
    bool Succeeded,
    string Command,
    string Scope,
    IReadOnlyList<WorldContentCoverageMetric> Metrics,
    long PlaceholderRows,
    IReadOnlyList<string> Gaps);

internal sealed record FieldMapping(
    long MappingId,
    string SourceSchema,
    string SourceTable,
    string SourceKeyColumn,
    string SourceValueColumn,
    string? SourceStatusColumn,
    string? SourceGateColumn,
    string TargetSchema,
    string TargetTable,
    string TargetKeyColumn,
    string TargetField,
    string ValueType,
    bool FillNullOnly);

internal sealed class CatalogBuilder(DatabaseSettings settings)
{
    private const long ExpectedMapResourceCount = 303;
    private const long ExpectedClientMapResourceCount = 303;
    private const long ExpectedNpcTemplateCount = 319;
    private const long ExpectedMonsterTemplateCount = 208;

    private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant);
    private static readonly string[] RuntimeTables =
    [
        "item_registry", "items", "weapons", "equipment", "magic_treasures", "item_usage_rules", "item_effects", "consumables", "maps", "client_map_resources", "portals", "monsters", "monster_skills",
        "monster_spawns", "monster_drops", "monster_ai_profiles", "monster_ai_rules", "npcs", "npc_spawns",
        "npc_dialogs", "npc_dialog_options", "merchants", "merchant_inventory", "skills", "skill_levels",
        "skill_effects", "status_effects", "quests", "quest_prerequisites", "quest_objectives", "quest_rewards",
        "pet_templates", "pet_template_skills", "immortal_templates", "immortal_ranks", "immortal_template_skills",
        "mount_templates", "mount_template_skills", "formations", "formation_slots", "formation_bonuses", "life_skills",
        "crafting_recipes", "crafting_recipe_materials", "crafting_recipe_outputs",
        "character_creation_profiles"
    ];

    public async Task<CatalogCommandResult> RebuildAsync(CancellationToken cancellationToken)
    {
        var triggerCount = await InstallTriggersCoreAsync(cancellationToken);
        var sync = await SynchronizeAsync(cancellationToken);
        await RefreshCachesCoreAsync(cancellationToken);
        var validation = await ValidateCoreAsync(activate: true, cancellationToken);
        return ToResult("rebuild", validation, sync, triggerCount,
            validation.Succeeded ? "Canonical Catalog rebuild、驗證與 atomic activation 完成。" : "Rebuild 驗證失敗，舊 Active Catalog 已保留。");
    }

    public async Task<CatalogCommandResult> ReloadAsync(CancellationToken cancellationToken)
    {
        await RefreshCachesCoreAsync(cancellationToken);
        var validation = await ValidateCoreAsync(activate: true, cancellationToken);
        return ToResult("reload", validation, new SyncOutcome(0, 0, 0, 0), await CountAdminTriggersAsync(cancellationToken),
            validation.Succeeded ? "Gameplay Catalog 已驗證並原子切換；執行中 Server 可重新讀取此 Active release。" : "Reload 驗證失敗，舊 Active Catalog 已保留。");
    }

    public async Task<CatalogCommandResult> ValidateAsync(bool activate, CancellationToken cancellationToken)
    {
        var validation = await ValidateCoreAsync(activate, cancellationToken);
        return ToResult("validate", validation, new SyncOutcome(0, 0, 0, 0), await CountAdminTriggersAsync(cancellationToken),
            validation.Succeeded ? "Headless Catalog Validation PASS。" : "Headless Catalog Validation FAILED。");
    }

    public async Task<CatalogCommandResult> RefreshCachesAsync(CancellationToken cancellationToken)
    {
        await RefreshCachesCoreAsync(cancellationToken);
        return new CatalogCommandResult(true, "refresh-caches", null, null, 0, 0, 0, 0, 0, 0, 0,
            await CountAdminTriggersAsync(cancellationToken), ["顯示名稱快取已由權威 ID 關聯刷新。"]);
    }

    public async Task<CatalogCommandResult> InstallAdminTriggersAsync(CancellationToken cancellationToken)
    {
        var count = await InstallTriggersCoreAsync(cancellationToken);
        return new CatalogCommandResult(true, "install-triggers", null, null, 0, 0, 0, 0, 0, 0, 0, count,
            [$"已安裝 {count} 個逐欄位 Admin Lock／Audit trigger。"]);
    }

    public async Task<WorldContentCoverageResult> WorldCoverageAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var mapResourceInventoryReady = await CountAsync(connection, """
            SELECT COUNT(*)
            FROM `god2_game`.`client_map_resources` resource_row
            JOIN `god2_research`.`client_map_resource_evidence` evidence_row
              ON evidence_row.`resource_key`=resource_row.`resource_key`
            WHERE NULLIF(TRIM(resource_row.`resource_key`),'') IS NOT NULL
              AND NULLIF(TRIM(resource_row.`area_code`),'') IS NOT NULL
              AND NULLIF(TRIM(resource_row.`resource_name`),'') IS NOT NULL
              AND NULLIF(TRIM(evidence_row.`can_relative_path`),'') IS NOT NULL
              AND NULLIF(TRIM(evidence_row.`can_sha256`),'') IS NOT NULL
              AND evidence_row.`resource_evidence_status` IN ('Verified','Derived');
            """, cancellationToken);
        var mapResources = new WorldContentCoverageMetric(
            "ClientResources",
            ExpectedClientMapResourceCount,
            await CountAsync(connection, "SELECT COUNT(*) FROM `god2_game`.`client_map_resources`;", cancellationToken),
            mapResourceInventoryReady,
            0,
            Math.Max(0, ExpectedClientMapResourceCount - mapResourceInventoryReady));

        var playableMapBindingReady = await CountAsync(connection, """
            SELECT COUNT(*)
            FROM `god2_game`.`client_map_resources` resource_row
            JOIN `god2_research`.`client_map_resource_evidence` evidence_row
              ON evidence_row.`resource_key`=resource_row.`resource_key`
            WHERE resource_row.`enabled`=1
              AND resource_row.`canonical_map_id` IS NOT NULL
              AND resource_row.`client_map_id` IS NOT NULL AND resource_row.`client_area_id` IS NOT NULL
              AND resource_row.`grid_width`>0 AND resource_row.`grid_height`>0
              AND resource_row.`minimum_x` IS NOT NULL AND resource_row.`maximum_x` IS NOT NULL
              AND resource_row.`minimum_y` IS NOT NULL AND resource_row.`maximum_y` IS NOT NULL
              AND evidence_row.`resource_evidence_status` IN ('Verified','Derived')
              AND NULLIF(TRIM(evidence_row.`resource_file_relative_path`),'') IS NOT NULL
              AND NULLIF(TRIM(evidence_row.`resource_file_sha256`),'') IS NOT NULL
              AND NULLIF(TRIM(evidence_row.`navigation_relative_path`),'') IS NOT NULL
              AND NULLIF(TRIM(evidence_row.`navigation_sha256`),'') IS NOT NULL
              AND evidence_row.`navigation_evidence_status` IN ('Verified','Derived')
              AND evidence_row.`map_identity_evidence_status` IN ('Verified','Derived');
            """, cancellationToken);
        var playableMapBindings = new WorldContentCoverageMetric(
            "PlayableMapBindings",
            ExpectedMapResourceCount,
            await CountAsync(connection, "SELECT COUNT(*) FROM `god2_game`.`client_map_resources`;", cancellationToken),
            playableMapBindingReady,
            await CountAsync(connection, """
                SELECT COUNT(*)
                FROM `god2_game`.`client_map_resources` resource_row
                LEFT JOIN `god2_research`.`client_map_resource_evidence` evidence_row
                  ON evidence_row.`resource_key`=resource_row.`resource_key`
                WHERE resource_row.`enabled`=1 AND (
                    resource_row.`canonical_map_id` IS NULL OR
                    resource_row.`client_map_id` IS NULL OR resource_row.`client_area_id` IS NULL OR
                    resource_row.`grid_width` IS NULL OR resource_row.`grid_width`<=0 OR
                    resource_row.`grid_height` IS NULL OR resource_row.`grid_height`<=0 OR
                    resource_row.`minimum_x` IS NULL OR resource_row.`maximum_x` IS NULL OR
                    resource_row.`minimum_y` IS NULL OR resource_row.`maximum_y` IS NULL OR
                    evidence_row.`resource_key` IS NULL OR
                    evidence_row.`resource_evidence_status` NOT IN ('Verified','Derived') OR
                    NULLIF(TRIM(evidence_row.`resource_file_relative_path`),'') IS NULL OR
                    NULLIF(TRIM(evidence_row.`resource_file_sha256`),'') IS NULL OR
                    NULLIF(TRIM(evidence_row.`navigation_relative_path`),'') IS NULL OR
                    NULLIF(TRIM(evidence_row.`navigation_sha256`),'') IS NULL OR
                    evidence_row.`navigation_evidence_status` NOT IN ('Verified','Derived') OR
                    evidence_row.`map_identity_evidence_status` NOT IN ('Verified','Derived'));
                """, cancellationToken),
            Math.Max(0, ExpectedMapResourceCount - playableMapBindingReady));
        var mapReady = await CountAsync(connection, """
            SELECT COUNT(*)
            FROM `god2_game`.`maps` map_row
            JOIN `god2_research`.`map_catalog_evidence` evidence_row
              ON evidence_row.`map_id`=map_row.`map_id`
            WHERE map_row.`enabled`=1
              AND NULLIF(TRIM(map_row.`name_zh_tw`),'') IS NOT NULL
              AND NULLIF(TRIM(map_row.`resource_identity`),'') IS NOT NULL
              AND map_row.`width`>0 AND map_row.`height`>0
              AND map_row.`minimum_x` IS NOT NULL AND map_row.`maximum_x` IS NOT NULL
              AND map_row.`minimum_y` IS NOT NULL AND map_row.`maximum_y` IS NOT NULL
              AND map_row.`client_map_id` IS NOT NULL AND map_row.`client_area_id` IS NOT NULL
              AND map_row.`coordinate_scale_x`>0 AND map_row.`coordinate_scale_y`>0
              AND evidence_row.`identity_evidence_status` IN ('Verified','Derived')
              AND evidence_row.`coordinate_evidence_status` IN ('Verified','Derived');
            """, cancellationToken);
        var maps = new WorldContentCoverageMetric(
            "Maps",
            ExpectedMapResourceCount,
            await CountAsync(connection, "SELECT COUNT(*) FROM `god2_game`.`maps`;", cancellationToken),
            mapReady,
            await CountAsync(connection, """
                SELECT COUNT(*)
                FROM `god2_game`.`maps` map_row
                LEFT JOIN `god2_research`.`map_catalog_evidence` evidence_row
                  ON evidence_row.`map_id`=map_row.`map_id`
                WHERE map_row.`enabled`=1 AND (
                    NULLIF(TRIM(map_row.`name_zh_tw`),'') IS NULL OR
                    NULLIF(TRIM(map_row.`resource_identity`),'') IS NULL OR
                    map_row.`width` IS NULL OR map_row.`width`<=0 OR map_row.`height` IS NULL OR map_row.`height`<=0 OR
                    map_row.`minimum_x` IS NULL OR map_row.`maximum_x` IS NULL OR
                    map_row.`minimum_y` IS NULL OR map_row.`maximum_y` IS NULL OR
                    map_row.`client_map_id` IS NULL OR map_row.`client_area_id` IS NULL OR
                    map_row.`coordinate_scale_x` IS NULL OR map_row.`coordinate_scale_x`<=0 OR
                    map_row.`coordinate_scale_y` IS NULL OR map_row.`coordinate_scale_y`<=0 OR
                    evidence_row.`map_id` IS NULL OR
                    evidence_row.`identity_evidence_status` NOT IN ('Verified','Derived') OR
                    evidence_row.`coordinate_evidence_status` NOT IN ('Verified','Derived'));
                """, cancellationToken),
            Math.Max(0, ExpectedMapResourceCount - mapReady));

        var npcCatalogTotal = await CountAsync(connection, "SELECT COUNT(*) FROM `god2_game`.`npcs`;", cancellationToken);
        var npcReady = await CountAsync(connection, """
            SELECT COUNT(DISTINCT npc.`npc_id`)
            FROM `god2_game`.`npcs` npc
            JOIN `god2_game`.`npc_spawns` spawn ON spawn.`npc_id`=npc.`npc_id` AND spawn.`enabled`=1
            JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=spawn.`map_id` AND map_row.`enabled`=1
            JOIN `god2_research`.`npc_spawn_evidence` evidence_row ON evidence_row.`spawn_id`=spawn.`spawn_id`
            WHERE npc.`enabled`=1
              AND NULLIF(TRIM(npc.`name_zh_tw`),'') IS NOT NULL
              AND NULLIF(TRIM(npc.`npc_type`),'') IS NOT NULL
              AND spawn.`position_x` IS NOT NULL AND spawn.`position_y` IS NOT NULL
              AND evidence_row.`identity_evidence_status` IN ('Verified','Derived')
              AND evidence_row.`coordinate_evidence_status` IN ('Verified','Derived');
            """, cancellationToken);
        var npcs = new WorldContentCoverageMetric(
            "NPCs",
            ExpectedNpcTemplateCount,
            npcCatalogTotal,
            npcReady,
            await CountAsync(connection, """
                SELECT COUNT(*)
                FROM `god2_game`.`npc_spawns` spawn
                LEFT JOIN `god2_research`.`npc_spawn_evidence` evidence_row ON evidence_row.`spawn_id`=spawn.`spawn_id`
                WHERE spawn.`enabled`=1 AND (spawn.`position_x` IS NULL OR spawn.`position_y` IS NULL OR
                    evidence_row.`spawn_id` IS NULL OR
                    evidence_row.`identity_evidence_status` NOT IN ('Verified','Derived') OR
                    evidence_row.`coordinate_evidence_status` NOT IN ('Verified','Derived'));
                """, cancellationToken),
            Math.Max(0, ExpectedNpcTemplateCount - npcReady));

        var monsterCatalogTotal = await CountAsync(connection, "SELECT COUNT(*) FROM `god2_game`.`monsters`;", cancellationToken);
        var monsterReady = await CountAsync(connection, """
            SELECT COUNT(DISTINCT monster.`monster_id`)
            FROM `god2_game`.`monsters` monster
            JOIN `god2_research`.`monster_catalog_evidence` monster_evidence
              ON monster_evidence.`catalog_table`='monsters' AND monster_evidence.`catalog_row_id`=monster.`monster_id`
            JOIN `god2_game`.`monster_spawns` spawn ON spawn.`monster_id`=monster.`monster_id` AND spawn.`enabled`=1
            JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=spawn.`map_id` AND map_row.`enabled`=1
            WHERE monster.`enabled`=1
              AND monster_evidence.`evidence_status` IN ('Verified','Recovered')
              AND monster.`level`>0 AND monster.`max_hp`>0 AND monster.`max_mp` IS NOT NULL
              AND monster.`strength` IS NOT NULL AND monster.`constitution` IS NOT NULL
              AND monster.`intelligence` IS NOT NULL AND monster.`speed` IS NOT NULL
              AND monster.`physical_attack` IS NOT NULL AND monster.`physical_defense` IS NOT NULL
              AND monster.`magic_attack` IS NOT NULL AND monster.`magic_defense` IS NOT NULL
              AND spawn.`position_x` IS NOT NULL AND spawn.`position_y` IS NOT NULL;
            """, cancellationToken);
        var monsters = new WorldContentCoverageMetric(
            "Monsters",
            ExpectedMonsterTemplateCount,
            monsterCatalogTotal,
            monsterReady,
            await CountAsync(connection, """
                SELECT COUNT(*) FROM `god2_game`.`monster_spawns`
                WHERE `enabled`=1 AND (`position_x` IS NULL OR `position_y` IS NULL OR
                    `spawn_count` IS NULL OR `spawn_count`<=0 OR
                    `respawn_seconds_min` IS NULL OR `respawn_seconds_max` IS NULL);
                """, cancellationToken),
            Math.Max(0, ExpectedMonsterTemplateCount - monsterReady));

        var portalCatalogTotal = await CountAsync(connection, "SELECT COUNT(*) FROM `god2_game`.`portals`;", cancellationToken);
        var portalReady = await CountAsync(connection, """
            SELECT COUNT(*) FROM `god2_game`.`portals` portal
            JOIN `god2_game`.`maps` source_map ON source_map.`map_id`=portal.`source_map_id` AND source_map.`enabled`=1
            JOIN `god2_game`.`maps` destination_map ON destination_map.`map_id`=portal.`destination_map_id` AND destination_map.`enabled`=1
            JOIN `god2_research`.`portal_evidence` evidence_row ON evidence_row.`portal_id`=portal.`portal_id`
            WHERE portal.`enabled`=1
              AND portal.`source_x` IS NOT NULL AND portal.`source_y` IS NOT NULL
              AND portal.`source_radius` IS NOT NULL AND portal.`source_radius`>=0
              AND portal.`destination_x` IS NOT NULL AND portal.`destination_y` IS NOT NULL
              AND evidence_row.`client_build_id` IS NOT NULL
              AND evidence_row.`identity_evidence_status` IN ('Verified','Derived')
              AND evidence_row.`source_coordinate_evidence_status` IN ('Verified','Derived')
              AND evidence_row.`destination_coordinate_evidence_status` IN ('Verified','Derived')
              AND evidence_row.`trigger_evidence_status` IN ('Verified','Derived')
              AND NULLIF(TRIM(evidence_row.`source_reference`),'') IS NOT NULL
              AND NULLIF(TRIM(evidence_row.`source_sha256`),'') IS NOT NULL;
            """, cancellationToken);
        var portals = new WorldContentCoverageMetric(
            "Portals",
            null,
            portalCatalogTotal,
            portalReady,
            await CountAsync(connection, """
                SELECT COUNT(*)
                FROM `god2_game`.`portals` portal
                LEFT JOIN `god2_research`.`portal_evidence` evidence_row ON evidence_row.`portal_id`=portal.`portal_id`
                WHERE portal.`enabled`=1 AND (portal.`source_map_id` IS NULL OR portal.`source_x` IS NULL OR portal.`source_y` IS NULL OR
                    portal.`source_radius` IS NULL OR portal.`source_radius`<0 OR portal.`destination_map_id` IS NULL OR
                    portal.`destination_x` IS NULL OR portal.`destination_y` IS NULL OR
                    evidence_row.`portal_id` IS NULL OR evidence_row.`client_build_id` IS NULL OR
                    evidence_row.`identity_evidence_status` NOT IN ('Verified','Derived') OR
                    evidence_row.`source_coordinate_evidence_status` NOT IN ('Verified','Derived') OR
                    evidence_row.`destination_coordinate_evidence_status` NOT IN ('Verified','Derived') OR
                    evidence_row.`trigger_evidence_status` NOT IN ('Verified','Derived') OR
                    NULLIF(TRIM(evidence_row.`source_reference`),'') IS NULL OR
                    NULLIF(TRIM(evidence_row.`source_sha256`),'') IS NULL);
                """, cancellationToken),
            0);

        var placeholderRows = await CountAsync(connection, """
            SELECT COALESCE(SUM(row_count),0) FROM (
                SELECT COUNT(*) row_count FROM `god2_game`.`maps` WHERE `enabled`=1 AND LOWER(CONCAT_WS(' ',`code`,`name_zh_tw`)) REGEXP '(unknown|candidate|placeholder|mock|(^|[^a-z])test([^a-z]|$))'
                UNION ALL SELECT COUNT(*) FROM `god2_game`.`npcs` WHERE `enabled`=1 AND LOWER(CONCAT_WS(' ',`code`,`name_zh_tw`)) REGEXP '(unknown|candidate|placeholder|mock|(^|[^a-z])test([^a-z]|$))'
                UNION ALL SELECT COUNT(*) FROM `god2_game`.`monsters` WHERE `enabled`=1 AND LOWER(CONCAT_WS(' ',`code`,`name_zh_tw`)) REGEXP '(unknown|candidate|placeholder|mock|(^|[^a-z])test([^a-z]|$))'
                UNION ALL SELECT COUNT(*) FROM `god2_game`.`portals` WHERE `enabled`=1 AND LOWER(`name_zh_tw`) REGEXP '(unknown|candidate|placeholder|mock|(^|[^a-z])test([^a-z]|$))'
            ) placeholder_counts;
            """, cancellationToken);

        WorldContentCoverageMetric[] metrics = [mapResources, playableMapBindings, maps, portals, npcs, monsters];
        var gaps = new List<string>();
        AddExpectedGap(gaps, mapResources);
        AddExpectedGap(gaps, playableMapBindings);
        AddExpectedGap(gaps, maps);
        gaps.Add("PortalPlacementManifestPending");
        AddExpectedGap(gaps, npcs);
        gaps.Add("NpcPlacementManifestPending");
        AddExpectedGap(gaps, monsters);
        gaps.Add("MonsterPlacementManifestPending");
        foreach (var metric in metrics.Where(value => value.IncompleteEnabled > 0))
        {
            gaps.Add($"{metric.Domain}.IncompleteEnabled={metric.IncompleteEnabled}");
        }
        if (placeholderRows > 0)
        {
            gaps.Add($"ProductionPlaceholderRows={placeholderRows}");
        }

        return new WorldContentCoverageResult(
            gaps.Count == 0,
            "world-coverage",
            "All canonical maps, every portal endpoint, every NPC placement and every monster placement/stat profile",
            Array.AsReadOnly(metrics),
            placeholderRows,
            gaps.AsReadOnly());
    }

    private static void AddExpectedGap(ICollection<string> gaps, WorldContentCoverageMetric metric)
    {
        if (metric.ExpectedTotal is long expected && metric.ProductionReady < expected)
        {
            gaps.Add($"{metric.Domain}.ProductionReady={metric.ProductionReady}/{expected}");
        }
    }

    private static async Task<long> CountAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken) =>
        Convert.ToInt64(await ScalarAsync(connection, sql, cancellationToken) ?? 0, CultureInfo.InvariantCulture);

    private static CatalogCommandResult ToResult(
        string command,
        ValidationOutcome validation,
        SyncOutcome sync,
        int triggerCount,
        string message)
    {
        return new CatalogCommandResult(
            validation.Succeeded,
            command,
            validation.CatalogReleaseId,
            validation.Fingerprint,
            validation.RecordCount,
            validation.Issues.Count(value => value.Severity == "Error"),
            validation.Issues.Count(value => value.Severity == "Warning"),
            sync.Mapped,
            sync.Blocked,
            sync.Conflicts,
            sync.Unmapped,
            triggerCount,
            [message, .. validation.Issues.Where(value => value.Severity == "Error").Take(20).Select(value => $"{value.RuleCode}: {value.MessageZhTw}")]);
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(settings.ConnectionString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task<int> InstallTriggersCoreAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var tables = new List<(string Schema, string Table)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT `TABLE_SCHEMA`,`TABLE_NAME`
                FROM `information_schema`.`TABLES`
                WHERE `TABLE_TYPE`='BASE TABLE'
                  AND (`TABLE_SCHEMA`='god2_game' OR (`TABLE_SCHEMA`='god2_player' AND `TABLE_NAME`<>'accounts'))
                ORDER BY `TABLE_SCHEMA`,`TABLE_NAME`;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var table in tables)
        {
            var columns = await LoadColumnsAsync(connection, table.Schema, table.Table, cancellationToken);
            var primaryKey = columns.Where(value => value.Primary).Select(value => value.Name).ToArray();
            if (primaryKey.Length == 0)
            {
                throw new InvalidOperationException($"Admin audit requires a primary key: {table.Schema}.{table.Table}");
            }

            var auditedColumns = columns
                .Where(value => !value.Primary)
                .Where(value => value.Name is not "created_at_utc" and not "updated_at_utc" and not "password_hash")
                .ToArray();
            var triggerName = TriggerName(table.Schema, table.Table);
            await ExecuteAsync(connection, $"DROP TRIGGER IF EXISTS {Q(table.Schema)}.{Q(triggerName)};", cancellationToken);
            if (auditedColumns.Length == 0)
            {
                continue;
            }

            var entityId = primaryKey.Length == 1
                ? $"COALESCE(CAST(NEW.{Q(primaryKey[0])} AS CHAR),'<NULL>')"
                : $"CONCAT_WS('|',{string.Join(',', primaryKey.Select(name => $"CONCAT('{EscapeSql(name)}=',COALESCE(CAST(NEW.{Q(name)} AS CHAR),'<NULL>'))"))})";
            var body = new StringBuilder();
            body.Append($"CREATE TRIGGER {Q(table.Schema)}.{Q(triggerName)} AFTER UPDATE ON {Q(table.Schema)}.{Q(table.Table)} FOR EACH ROW BEGIN ");
            body.Append("IF COALESCE(@god2_sync_mode,0)=0 AND COALESCE(@god2_runtime_mode,0)=0 THEN ");
            foreach (var column in auditedColumns)
            {
                body.Append($"IF NOT (OLD.{Q(column.Name)} <=> NEW.{Q(column.Name)}) THEN ");
                body.Append("INSERT INTO `god2_game_meta`.`admin_change_audit` (`entity_schema`,`entity_table`,`entity_id`,`field_name`,`old_value`,`new_value`,`changed_at_utc`,`changed_by`,`change_source`,`note`) VALUES (");
                body.Append($"'{EscapeSql(table.Schema)}','{EscapeSql(table.Table)}',{entityId},'{EscapeSql(column.Name)}',CAST(OLD.{Q(column.Name)} AS CHAR),CAST(NEW.{Q(column.Name)} AS CHAR),UTC_TIMESTAMP(6),CURRENT_USER(),'AdminDirectSql','直接 SQL 修改自動稽核'); ");
                body.Append("INSERT INTO `god2_game_meta`.`admin_field_locks` (`entity_schema`,`entity_table`,`entity_id`,`field_name`,`locked_value_hash`,`locked_at_utc`,`locked_by`,`note`) VALUES (");
                body.Append($"'{EscapeSql(table.Schema)}','{EscapeSql(table.Table)}',{entityId},'{EscapeSql(column.Name)}',SHA2(COALESCE(CAST(NEW.{Q(column.Name)} AS CHAR),'<NULL>'),256),UTC_TIMESTAMP(6),CURRENT_USER(),'直接 SQL 修改自動鎖定') ");
                body.Append("ON DUPLICATE KEY UPDATE `locked_value_hash`=VALUES(`locked_value_hash`),`locked_at_utc`=VALUES(`locked_at_utc`),`locked_by`=VALUES(`locked_by`); END IF; ");
            }
            body.Append("END IF; END");
            await ExecuteAsync(connection, body.ToString(), cancellationToken);
        }

        return await CountAdminTriggersAsync(connection, cancellationToken);
    }

    private async Task<SyncOutcome> SynchronizeAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("D");
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(connection, """
            INSERT INTO `god2_research`.`sync_runs`
                (`sync_run_id`,`started_at_utc`,`status`,`mapped_value_count`,`blocked_source_value_count`,`conflict_count`,`unmapped_count`)
            VALUES (@run,UTC_TIMESTAMP(6),'Running',0,0,0,0);
            """, cancellationToken, ("@run", runId));
        long mapped = 0;
        long blocked = 0;
        long conflicts = 0;
        long unmapped = 0;
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await ExecuteAsync(connection, "SET @god2_sync_mode=1;", cancellationToken, transaction);
            var mappings = await LoadMappingsAsync(connection, transaction, cancellationToken);
            foreach (var mapping in mappings)
            {
                ValidateMapping(mapping);
                var statusExpression = mapping.SourceStatusColumn is null ? "'Verified'" : Q(mapping.SourceStatusColumn);
                var gateExpression = mapping.SourceGateColumn is null ? "NULL" : Q(mapping.SourceGateColumn);
                var sourceSql = $"SELECT {Q(mapping.SourceKeyColumn)},{Q(mapping.SourceValueColumn)},{statusExpression},{gateExpression} FROM {Q(mapping.SourceSchema)}.{Q(mapping.SourceTable)} WHERE {Q(mapping.SourceKeyColumn)} IS NOT NULL ORDER BY {Q(mapping.SourceKeyColumn)};";
                var rows = new List<(object Key, object? Value, string Status, bool Gate)>();
                await using (var sourceCommand = connection.CreateCommand())
                {
                    sourceCommand.Transaction = transaction;
                    sourceCommand.CommandText = sourceSql;
                    await using var reader = await sourceCommand.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var value = reader.IsDBNull(1) ? null : reader.GetValue(1);
                        var status = reader.IsDBNull(2) ? "Unknown" : reader.GetString(2);
                        var gate = !reader.IsDBNull(3) && Convert.ToBoolean(reader.GetValue(3), CultureInfo.InvariantCulture);
                        if (status == "Verified" || (status == "Recovered" && mapping.SourceGateColumn is not null && gate))
                        {
                            rows.Add((reader.GetValue(0), value, status, gate));
                        }
                        else
                        {
                            blocked++;
                        }
                    }
                }

                foreach (var group in rows.GroupBy(value => Canonical(value.Key), StringComparer.Ordinal))
                {
                    var distinct = group.Select(value => Canonical(value.Value)).Distinct(StringComparer.Ordinal).ToArray();
                    if (distinct.Length > 1)
                    {
                        conflicts++;
                        await InsertConflictAsync(connection, transaction, runId, mapping, group.Key, null,
                            string.Join(" | ", distinct), "SameGradeEvidenceConflict", group.First().Status, cancellationToken);
                        continue;
                    }

                    var source = group.First();
                    var entityId = Canonical(source.Key);
                    var targetValue = await LoadTargetValueAsync(connection, transaction, mapping, source.Key, cancellationToken);
                    if (targetValue.MissingRow)
                    {
                        blocked++;
                        continue;
                    }

                    if (await IsLockedAsync(connection, transaction, mapping, entityId, cancellationToken))
                    {
                        conflicts++;
                        await InsertConflictAsync(connection, transaction, runId, mapping, entityId, targetValue.Value,
                            source.Value, "AdminFieldLock", source.Status, cancellationToken);
                        continue;
                    }

                    if (mapping.FillNullOnly && targetValue.Value is not null)
                    {
                        continue;
                    }

                    var converted = ConvertValue(source.Value, mapping.ValueType);
                    var updateSql = $"UPDATE {Q(mapping.TargetSchema)}.{Q(mapping.TargetTable)} SET {Q(mapping.TargetField)}=@value WHERE {Q(mapping.TargetKeyColumn)}=@key;";
                    var affected = await ExecuteAsync(connection, updateSql, cancellationToken, transaction, ("@value", converted), ("@key", source.Key));
                    if (affected == 1)
                    {
                        mapped++;
                        await UpsertProvenanceAsync(connection, transaction, runId, mapping, entityId, converted, source.Status, cancellationToken);
                    }
                }
            }

            unmapped = await InsertUnmappedSummaryAsync(connection, transaction, runId, cancellationToken);
            await ExecuteAsync(connection, """
                UPDATE `god2_research`.`sync_runs`
                SET `completed_at_utc`=UTC_TIMESTAMP(6),`status`='Completed',`mapped_value_count`=@mapped,
                    `blocked_source_value_count`=@blocked,`conflict_count`=@conflicts,`unmapped_count`=@unmapped
                WHERE `sync_run_id`=@run;
                """, cancellationToken, transaction,
                ("@mapped", mapped), ("@blocked", blocked), ("@conflicts", conflicts), ("@unmapped", unmapped), ("@run", runId));
            await ExecuteAsync(connection, "SET @god2_sync_mode=0;", cancellationToken, transaction);
            await transaction.CommitAsync(cancellationToken);
            return new SyncOutcome(mapped, blocked, conflicts, unmapped);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            await ExecuteAsync(connection, """
                UPDATE `god2_research`.`sync_runs`
                SET `completed_at_utc`=UTC_TIMESTAMP(6),`status`='Failed',`error_message`=@message
                WHERE `sync_run_id`=@run;
                """, cancellationToken, ("@message", exception.Message), ("@run", runId));
            throw;
        }
    }

    private async Task RefreshCachesCoreAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await ExecuteAsync(connection, "SET @god2_sync_mode=1;", cancellationToken, transaction);
        string[] updates =
        [
            "UPDATE `god2_game`.`items` row_value JOIN `god2_game`.`item_registry` registry_value ON registry_value.`item_id`=row_value.`item_id` SET row_value.`code`=registry_value.`code`,row_value.`name_zh_tw`=registry_value.`name_zh_tw`,row_value.`description_zh_tw`=registry_value.`description_zh_tw`,row_value.`sell_price`=registry_value.`sell_price`,row_value.`icon_id`=registry_value.`icon_id`,row_value.`updated_at_utc`=registry_value.`updated_at_utc`;",
            "UPDATE `god2_game`.`weapons` row_value JOIN `god2_game`.`item_registry` registry_value ON registry_value.`item_id`=row_value.`item_id` SET row_value.`code`=registry_value.`code`,row_value.`name_zh_tw`=registry_value.`name_zh_tw`,row_value.`description_zh_tw`=registry_value.`description_zh_tw`,row_value.`sell_price`=registry_value.`sell_price`,row_value.`icon_id`=registry_value.`icon_id`,row_value.`updated_at_utc`=registry_value.`updated_at_utc`;",
            "UPDATE `god2_game`.`equipment` row_value JOIN `god2_game`.`item_registry` registry_value ON registry_value.`item_id`=row_value.`item_id` SET row_value.`code`=registry_value.`code`,row_value.`name_zh_tw`=registry_value.`name_zh_tw`,row_value.`description_zh_tw`=registry_value.`description_zh_tw`,row_value.`sell_price`=registry_value.`sell_price`,row_value.`icon_id`=registry_value.`icon_id`,row_value.`updated_at_utc`=registry_value.`updated_at_utc`;",
            "UPDATE `god2_game`.`magic_treasures` row_value JOIN `god2_game`.`item_registry` registry_value ON registry_value.`item_id`=row_value.`item_id` SET row_value.`code`=registry_value.`code`,row_value.`name_zh_tw`=registry_value.`name_zh_tw`,row_value.`description_zh_tw`=registry_value.`description_zh_tw`,row_value.`sell_price`=registry_value.`sell_price`,row_value.`icon_id`=registry_value.`icon_id`,row_value.`updated_at_utc`=registry_value.`updated_at_utc`;",
            "UPDATE `god2_game`.`monster_skills` row_value JOIN `god2_game`.`monsters` entity_value ON entity_value.`monster_id`=row_value.`monster_id` JOIN `god2_game`.`skills` skill_value ON skill_value.`skill_id`=row_value.`skill_id` SET row_value.`monster_name_cache`=entity_value.`name_zh_tw`,row_value.`skill_name_cache`=skill_value.`name_zh_tw`;",
            "UPDATE `god2_game`.`monster_spawns` row_value JOIN `god2_game`.`monsters` entity_value ON entity_value.`monster_id`=row_value.`monster_id` JOIN `god2_game`.`maps` map_value ON map_value.`map_id`=row_value.`map_id` SET row_value.`monster_name_cache`=entity_value.`name_zh_tw`,row_value.`map_name_cache`=map_value.`name_zh_tw`;",
            "UPDATE `god2_game`.`monster_drops` row_value JOIN `god2_game`.`monsters` entity_value ON entity_value.`monster_id`=row_value.`monster_id` JOIN `god2_game`.`item_registry` item_value ON item_value.`item_id`=row_value.`item_id` SET row_value.`monster_name_cache`=entity_value.`name_zh_tw`,row_value.`item_name_cache`=item_value.`name_zh_tw`;",
            "UPDATE `god2_game`.`npc_spawns` row_value JOIN `god2_game`.`npcs` entity_value ON entity_value.`npc_id`=row_value.`npc_id` JOIN `god2_game`.`maps` map_value ON map_value.`map_id`=row_value.`map_id` SET row_value.`npc_name_cache`=entity_value.`name_zh_tw`,row_value.`map_name_cache`=map_value.`name_zh_tw`;",
            "UPDATE `god2_game`.`merchant_inventory` row_value JOIN `god2_game`.`merchants` entity_value ON entity_value.`merchant_id`=row_value.`merchant_id` JOIN `god2_game`.`item_registry` item_value ON item_value.`item_id`=row_value.`item_id` SET row_value.`merchant_name_cache`=entity_value.`name_zh_tw`,row_value.`item_name_cache`=item_value.`name_zh_tw`;",
            "UPDATE `god2_game`.`crafting_recipe_materials` row_value JOIN `god2_game`.`item_registry` item_value ON item_value.`item_id`=row_value.`item_id` SET row_value.`item_name_cache`=item_value.`name_zh_tw`;",
            "UPDATE `god2_game`.`crafting_recipe_outputs` row_value JOIN `god2_game`.`item_registry` item_value ON item_value.`item_id`=row_value.`item_id` SET row_value.`item_name_cache`=item_value.`name_zh_tw`;",
            "UPDATE `god2_player`.`character_inventory` row_value JOIN `god2_game`.`item_registry` item_value ON item_value.`item_id`=row_value.`item_id` SET row_value.`item_name_cache`=item_value.`name_zh_tw`;"
        ];
        foreach (var update in updates)
        {
            await ExecuteAsync(connection, update, cancellationToken, transaction);
        }
        await ExecuteAsync(connection, "SET @god2_sync_mode=0;", cancellationToken, transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<ValidationOutcome> ValidateCoreAsync(bool activate, CancellationToken cancellationToken)
    {
        var validationRunId = Guid.NewGuid().ToString("D");
        await using var connection = await OpenAsync(cancellationToken);
        var issues = new List<ValidationIssue>();
        await AddCountIssueAsync(connection, issues, "canonical.engine", "Error", "正式表必須全部使用 InnoDB。", """
            SELECT COUNT(*) FROM `information_schema`.`TABLES`
            WHERE `TABLE_SCHEMA` IN ('god2_game','god2_player','god2_game_meta') AND `TABLE_TYPE`='BASE TABLE' AND `ENGINE`<>'InnoDB';
            """, cancellationToken);
        await AddCountIssueAsync(connection, issues, "canonical.collation", "Error", "正式表必須使用 utf8mb4_unicode_ci。", """
            SELECT COUNT(*) FROM `information_schema`.`TABLES`
            WHERE `TABLE_SCHEMA` IN ('god2_game','god2_player','god2_game_meta') AND `TABLE_TYPE`='BASE TABLE' AND `TABLE_COLLATION`<>'utf8mb4_unicode_ci';
            """, cancellationToken);
        await AddCountIssueAsync(connection, issues, "canonical.column_comments", "Error", "正式表欄位缺少繁體中文 COMMENT。", """
            SELECT COUNT(*) FROM `information_schema`.`COLUMNS` column_row
            JOIN `information_schema`.`TABLES` table_row ON table_row.`TABLE_SCHEMA`=column_row.`TABLE_SCHEMA` AND table_row.`TABLE_NAME`=column_row.`TABLE_NAME`
            WHERE column_row.`TABLE_SCHEMA` IN ('god2_game','god2_player','god2_game_meta') AND table_row.`TABLE_TYPE`='BASE TABLE' AND column_row.`COLUMN_COMMENT`='';
            """, cancellationToken);
        await AddCountIssueAsync(connection, issues, "canonical.eav_forbidden", "Error", "Gameplay／Player 正式表不得包含 EAV／Raw 欄位。", """
            SELECT COUNT(*) FROM `information_schema`.`COLUMNS`
            WHERE `TABLE_SCHEMA` IN ('god2_game','god2_player') AND LOWER(`COLUMN_NAME`) IN ('evidenceid','valuejson','rawpayload','runid','authoritykey','packethex');
            """, cancellationToken);
        await AddCountIssueAsync(connection, issues, "canonical.sensitive_forbidden", "Error", "god2_game 不得含帳密、Token、Session Key 或 Raw Packet 欄位。", """
            SELECT COUNT(*) FROM `information_schema`.`COLUMNS`
            WHERE `TABLE_SCHEMA`='god2_game' AND LOWER(`COLUMN_NAME`) REGEXP '(password|credential|login_token|session_key|raw_packet|packet_payload)';
            """, cancellationToken);
        await AddCountIssueAsync(connection, issues, "canonical.candidate_enabled", "Error", "Candidate、Derived、EvidenceBlocked 或 Unknown 列不得自動啟用。", """
            SELECT COALESCE(SUM(violation_count),0) FROM (
                SELECT COUNT(*) violation_count FROM `god2_game`.`vw_all_item_definitions` WHERE `enabled`=1 AND `evidence_status` IN ('Derived','Candidate','EvidenceBlocked','Unknown')
                UNION ALL SELECT COUNT(*) FROM `god2_game`.`monsters` WHERE `enabled`=1 AND `evidence_status` IN ('Derived','Candidate','EvidenceBlocked','Unknown')
                UNION ALL SELECT COUNT(*) FROM `god2_game`.`skills` WHERE `enabled`=1 AND `evidence_status` IN ('Derived','Candidate','EvidenceBlocked','Unknown')
                UNION ALL SELECT COUNT(*) FROM `god2_game`.`quests` WHERE `enabled`=1 AND `evidence_status` IN ('Derived','Candidate','EvidenceBlocked','Unknown')
                UNION ALL SELECT COUNT(*) FROM `god2_game`.`monster_drops` WHERE `enabled`=1 AND `evidence_status` IN ('Derived','Candidate','EvidenceBlocked','Unknown')
                UNION ALL SELECT COUNT(*) FROM `god2_game`.`crafting_recipes` WHERE `enabled`=1 AND `evidence_status` IN ('Derived','Candidate','EvidenceBlocked','Unknown')
            ) violations;
            """, cancellationToken);
        await AddCountIssueAsync(connection, issues, "canonical.unknown_probability_enabled", "Error", "未知掉率、製作率或產出率不得以正式 0% 啟用。", """
            SELECT
                (SELECT COUNT(*) FROM `god2_game`.`monster_drops` WHERE `enabled`=1 AND `drop_rate` IS NULL AND COALESCE(`is_guaranteed`,0)=0) +
                (SELECT COUNT(*) FROM `god2_game`.`crafting_recipes` WHERE `enabled`=1 AND `base_success_rate` IS NULL) +
                (SELECT COUNT(*) FROM `god2_game`.`crafting_recipe_outputs` WHERE `enabled`=1 AND `output_probability` IS NULL) +
                (SELECT COUNT(*) FROM `god2_game`.`container_rewards` WHERE `enabled`=1 AND `reward_probability` IS NULL);
            """, cancellationToken);
        await AddCountIssueAsync(connection, issues, "canonical.enabled_required_fields", "Error", "啟用列缺少 Runtime 必要欄位。", """
            SELECT
                (SELECT COUNT(*) FROM `god2_game`.`vw_all_item_definitions` WHERE `enabled`=1 AND (`name_zh_tw`='' OR `item_category`='')) +
                (SELECT COUNT(*)
                 FROM `god2_game`.`maps` map_row
                 LEFT JOIN `god2_research`.`map_catalog_evidence` evidence_row ON evidence_row.`map_id`=map_row.`map_id`
                 WHERE map_row.`enabled`=1 AND (map_row.`name_zh_tw`='' OR map_row.`client_map_id` IS NULL
                    OR evidence_row.`map_id` IS NULL OR evidence_row.`identity_evidence_status`<>'Verified')) +
                (SELECT COUNT(*) FROM `god2_game`.`npcs` WHERE `enabled`=1 AND (`name_zh_tw`='' OR `npc_type`='')) +
                (SELECT COUNT(*)
                 FROM `god2_game`.`npc_spawns` spawn_row
                 LEFT JOIN `god2_research`.`npc_spawn_evidence` evidence_row ON evidence_row.`spawn_id`=spawn_row.`spawn_id`
                 WHERE spawn_row.`enabled`=1 AND (spawn_row.`position_x` IS NULL OR spawn_row.`position_y` IS NULL
                    OR evidence_row.`spawn_id` IS NULL OR evidence_row.`wire_evidence_status` NOT IN ('Verified','Derived'))) +
                (SELECT COUNT(*) FROM `god2_game`.`monsters` WHERE `enabled`=1 AND (`name_zh_tw`='' OR `max_hp` IS NULL OR `max_hp`<=0)) +
                (SELECT COUNT(*) FROM `god2_game`.`skills` WHERE `enabled`=1 AND `name_zh_tw`='');
            """, cancellationToken);
        await AddCountIssueAsync(connection, issues, "canonical.life_skill_seed", "Error", "四項固定生活技能 Seed 必須各存在一筆且代碼唯一。", """
            SELECT IF(COUNT(*)=4 AND COUNT(DISTINCT `code`)=4 AND SUM(`enabled`=1)=4,0,1)
            FROM `god2_game`.`life_skills`
            WHERE `code` IN ('ArmorForging','WeaponForging','PillAlchemy','MagicTreasureForging');
            """, cancellationToken);
        await AddCountIssueAsync(connection, issues, "canonical.character_life_skill_rows", "Error", "每個角色必須具備四項生活技能資料列。", """
            SELECT COUNT(*) FROM (
                SELECT character_row.`character_id`
                FROM `god2_player`.`characters` character_row
                LEFT JOIN `god2_player`.`character_life_skills` progress_row ON progress_row.`character_id`=character_row.`character_id`
                GROUP BY character_row.`character_id` HAVING COUNT(progress_row.`life_skill_id`)<>4
            ) invalid_character;
            """, cancellationToken);

        var triggerExpected = Convert.ToInt32(await ScalarAsync(connection, """
            SELECT COUNT(*) FROM `information_schema`.`TABLES` table_row
            WHERE table_row.`TABLE_TYPE`='BASE TABLE'
              AND (table_row.`TABLE_SCHEMA`='god2_game' OR (table_row.`TABLE_SCHEMA`='god2_player' AND table_row.`TABLE_NAME`<>'accounts'))
              AND EXISTS (
                  SELECT 1 FROM `information_schema`.`COLUMNS` column_row
                  WHERE column_row.`TABLE_SCHEMA`=table_row.`TABLE_SCHEMA`
                    AND column_row.`TABLE_NAME`=table_row.`TABLE_NAME`
                    AND column_row.`COLUMN_KEY`<>'PRI'
                    AND column_row.`COLUMN_NAME` NOT IN ('created_at_utc','updated_at_utc','password_hash')
              );
            """, cancellationToken), CultureInfo.InvariantCulture);
        var triggerActual = await CountAdminTriggersAsync(connection, cancellationToken);
        if (triggerActual != triggerExpected)
        {
            issues.Add(new ValidationIssue("Error", "god2_game_meta", "admin_field_locks", null, null,
                "canonical.admin_trigger_coverage", $"Admin trigger 覆蓋不完整：預期 {triggerExpected}，實際 {triggerActual}。"));
        }

        var (fingerprint, recordCount) = await BuildFingerprintAsync(connection, cancellationToken);
        await StoreValidationIssuesAsync(connection, validationRunId, issues, cancellationToken);
        var succeeded = issues.All(value => value.Severity != "Error");
        string? releaseId = null;
        if (activate && succeeded)
        {
            releaseId = await ActivateAsync(connection, validationRunId, fingerprint, recordCount, cancellationToken);
        }

        return new ValidationOutcome(succeeded, validationRunId, releaseId, fingerprint, recordCount, issues.AsReadOnly());
    }

    private static async Task<(string Fingerprint, long RecordCount)> BuildFingerprintAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        foreach (var table in RuntimeTables)
        {
            var columns = await LoadColumnsAsync(connection, "god2_game", table, cancellationToken);
            var primary = columns.Where(value => value.Primary).Select(value => value.Name).ToArray();
            var hasEnabled = columns.Any(value => value.Name == "enabled");
            var sql = $"SELECT {string.Join(',', columns.Select(value => Q(value.Name)))} FROM `god2_game`.{Q(table)}" +
                      (hasEnabled ? " WHERE `enabled`=1" : string.Empty) +
                      $" ORDER BY {string.Join(',', primary.Select(Q))};";
            Append(hash, $"table:{table}\n");
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                total++;
                Append(hash, "row:");
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    Append(hash, columns[index].Name);
                    Append(hash, "=");
                    Append(hash, reader.IsDBNull(index) ? "<NULL>" : Canonical(reader.GetValue(index)));
                    Append(hash, "\u001f");
                }
                Append(hash, "\n");
            }
        }

        return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), total);
    }

    private static async Task<string> ActivateAsync(
        MySqlConnection connection,
        string validationRunId,
        string fingerprint,
        long recordCount,
        CancellationToken cancellationToken)
    {
        var existing = await ScalarAsync(connection, "SELECT `catalog_release_id` FROM `god2_game_meta`.`runtime_catalog_releases` WHERE `status`='Active' AND `catalog_fingerprint`=@fingerprint;", cancellationToken, ("@fingerprint", fingerprint));
        if (existing is not null and not DBNull)
        {
            return Convert.ToString(existing, CultureInfo.InvariantCulture) ?? throw new InvalidOperationException("Active release identity is empty.");
        }

        var releaseId = Guid.NewGuid().ToString("D");
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await ExecuteAsync(connection, "UPDATE `god2_game_meta`.`runtime_catalog_releases` SET `status`='Superseded' WHERE `status`='Active';", cancellationToken, transaction);
        await ExecuteAsync(connection, """
            INSERT INTO `god2_game_meta`.`runtime_catalog_releases`
                (`catalog_release_id`,`catalog_fingerprint`,`catalog_record_count`,`status`,`built_at_utc`,`activated_at_utc`,`validation_run_id`,`builder_version`)
            VALUES (@release,@fingerprint,@count,'Active',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),@validation,@version);
            """, cancellationToken, transaction,
            ("@release", releaseId), ("@fingerprint", fingerprint), ("@count", recordCount),
            ("@validation", validationRunId), ("@version", Program.Version));
        await transaction.CommitAsync(cancellationToken);
        return releaseId;
    }

    private static async Task StoreValidationIssuesAsync(
        MySqlConnection connection,
        string validationRunId,
        IEnumerable<ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        foreach (var issue in issues)
        {
            await ExecuteAsync(connection, """
                INSERT INTO `god2_game_meta`.`catalog_validation`
                    (`validation_run_id`,`severity`,`entity_schema`,`entity_table`,`entity_id`,`field_name`,`rule_code`,`message_zh_tw`)
                VALUES (@run,@severity,@schema,@table,@entity,@field,@rule,@message);
                """, cancellationToken,
                ("@run", validationRunId), ("@severity", issue.Severity), ("@schema", issue.Schema),
                ("@table", issue.Table), ("@entity", issue.EntityId), ("@field", issue.Field),
                ("@rule", issue.RuleCode), ("@message", issue.MessageZhTw));
        }
    }

    private static async Task AddCountIssueAsync(
        MySqlConnection connection,
        ICollection<ValidationIssue> issues,
        string rule,
        string severity,
        string message,
        string sql,
        CancellationToken cancellationToken)
    {
        var count = Convert.ToInt64(await ScalarAsync(connection, sql, cancellationToken), CultureInfo.InvariantCulture);
        if (count != 0)
        {
            issues.Add(new ValidationIssue(severity, null, null, null, null, rule, $"{message} 違規數：{count}。"));
        }
    }

    private static async Task<IReadOnlyList<FieldMapping>> LoadMappingsAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        var result = new List<FieldMapping>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT `mapping_id`,`source_schema`,`source_table`,`source_key_column`,`source_value_column`,
                   `source_status_column`,`source_gate_column`,`target_schema`,`target_table`,`target_key_column`,
                   `target_field`,`value_type`,`fill_null_only`
            FROM `god2_research`.`field_mappings` WHERE `enabled`=1 ORDER BY `mapping_id`;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new FieldMapping(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetBoolean(12)));
        }
        return result.AsReadOnly();
    }

    private static void ValidateMapping(FieldMapping mapping)
    {
        string?[] identifiers =
        [
            mapping.SourceSchema, mapping.SourceTable, mapping.SourceKeyColumn, mapping.SourceValueColumn,
            mapping.SourceStatusColumn, mapping.SourceGateColumn, mapping.TargetSchema, mapping.TargetTable,
            mapping.TargetKeyColumn, mapping.TargetField
        ];
        if (identifiers.Where(value => value is not null).Any(value => !SafeIdentifier.IsMatch(value!)))
        {
            throw new InvalidOperationException($"Unsafe field mapping identifier: {mapping.MappingId}");
        }
        if (mapping.TargetSchema is not ("god2_game" or "god2_player"))
        {
            throw new InvalidOperationException($"Field mapping target is not canonical: {mapping.MappingId}");
        }
    }

    private static async Task<(bool MissingRow, object? Value)> LoadTargetValueAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        FieldMapping mapping,
        object key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {Q(mapping.TargetField)} FROM {Q(mapping.TargetSchema)}.{Q(mapping.TargetTable)} WHERE {Q(mapping.TargetKeyColumn)}=@key;";
        command.Parameters.AddWithValue("@key", key);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? (true, null) : value is DBNull ? (false, null) : (false, value);
    }

    private static async Task<bool> IsLockedAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        FieldMapping mapping,
        string entityId,
        CancellationToken cancellationToken)
    {
        var count = await ScalarAsync(connection, """
            SELECT COUNT(*) FROM `god2_game_meta`.`admin_field_locks`
            WHERE `entity_schema`=@schema AND `entity_table`=@table AND `entity_id`=@entity AND `field_name`=@field;
            """, cancellationToken, transaction,
            ("@schema", mapping.TargetSchema), ("@table", mapping.TargetTable), ("@entity", entityId), ("@field", mapping.TargetField));
        return Convert.ToInt64(count, CultureInfo.InvariantCulture) != 0;
    }

    private static async Task InsertConflictAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string runId,
        FieldMapping mapping,
        string entityId,
        object? existing,
        object? candidate,
        string conflictType,
        string sourceStatus,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, """
            INSERT INTO `god2_research`.`sync_conflicts`
                (`sync_run_id`,`entity_schema`,`entity_table`,`entity_id`,`field_name`,`existing_value`,`proposed_source_value`,`conflict_type`,`source_status`)
            VALUES (@run,@schema,@table,@entity,@field,@existing,@candidate,@type,@status);
            """, cancellationToken, transaction,
            ("@run", runId), ("@schema", mapping.TargetSchema), ("@table", mapping.TargetTable),
            ("@entity", entityId), ("@field", mapping.TargetField), ("@existing", Canonical(existing)),
            ("@candidate", Canonical(candidate)), ("@type", conflictType), ("@status", sourceStatus));
    }

    private static async Task UpsertProvenanceAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string runId,
        FieldMapping mapping,
        string entityId,
        object? value,
        string status,
        CancellationToken cancellationToken)
    {
        var canonical = Canonical(value);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        await ExecuteAsync(connection, """
            INSERT INTO `god2_research`.`field_provenance`
                (`entity_schema`,`entity_table`,`entity_id`,`field_name`,`source_schema`,`source_table`,`source_identity`,`source_status`,`source_hash`,`sync_run_id`)
            VALUES (@schema,@table,@entity,@field,@sourceSchema,@sourceTable,@sourceIdentity,@status,@hash,@run)
            ON DUPLICATE KEY UPDATE `source_schema`=VALUES(`source_schema`),`source_table`=VALUES(`source_table`),
                `source_identity`=VALUES(`source_identity`),`source_status`=VALUES(`source_status`),
                `source_hash`=VALUES(`source_hash`),`sync_run_id`=VALUES(`sync_run_id`),`recorded_at_utc`=UTC_TIMESTAMP(6);
            """, cancellationToken, transaction,
            ("@schema", mapping.TargetSchema), ("@table", mapping.TargetTable), ("@entity", entityId),
            ("@field", mapping.TargetField), ("@sourceSchema", mapping.SourceSchema), ("@sourceTable", mapping.SourceTable),
            ("@sourceIdentity", entityId), ("@status", status), ("@hash", hash), ("@run", runId));
    }

    private static async Task<long> InsertUnmappedSummaryAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string runId,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, """
            INSERT INTO `god2_research`.`unmapped_fields`
                (`sync_run_id`,`source_domain`,`source_field`,`observed_count`,`first_source_identity`)
            SELECT @run,evidence_row.`Domain`,evidence_row.`FieldName`,COUNT(*),MIN(evidence_row.`SourceIdentity`)
            FROM `god2_research`.`content_field_evidence` evidence_row
            WHERE NOT EXISTS (
                SELECT 1 FROM `god2_research`.`field_mappings` mapping_row
                WHERE mapping_row.`enabled`=1 AND mapping_row.`source_schema`='god2_research'
                  AND mapping_row.`source_table`='content_field_evidence'
                  AND mapping_row.`source_value_column`=evidence_row.`FieldName`)
            GROUP BY evidence_row.`Domain`,evidence_row.`FieldName`;
            """, cancellationToken, transaction, ("@run", runId));
        var value = await ScalarAsync(connection,
            "SELECT COUNT(*) FROM `god2_research`.`unmapped_fields` WHERE `sync_run_id`=@run;",
            cancellationToken, transaction, ("@run", runId));
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static object? ConvertValue(object? value, string valueType)
    {
        if (value is null or DBNull)
        {
            return null;
        }
        return valueType switch
        {
            "Int32" => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            "Int64" => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            "Decimal" => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            "Boolean" => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            "String" => Convert.ToString(value, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unsupported mapping value type: {valueType}")
        };
    }

    private static async Task<IReadOnlyList<(string Name, bool Primary)>> LoadColumnsAsync(
        MySqlConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        var result = new List<(string Name, bool Primary)>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `COLUMN_NAME`,CASE WHEN `COLUMN_KEY`='PRI' THEN 1 ELSE 0 END
            FROM `information_schema`.`COLUMNS`
            WHERE `TABLE_SCHEMA`=@schema AND `TABLE_NAME`=@table ORDER BY `ORDINAL_POSITION`;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add((reader.GetString(0), reader.GetBoolean(1)));
        }
        if (result.Count == 0)
        {
            throw new InvalidOperationException($"Canonical table is missing: {schema}.{table}");
        }
        return result.AsReadOnly();
    }

    private async Task<int> CountAdminTriggersAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await CountAdminTriggersAsync(connection, cancellationToken);
    }

    private static async Task<int> CountAdminTriggersAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var value = await ScalarAsync(connection, """
            SELECT COUNT(*) FROM `information_schema`.`TRIGGERS`
            WHERE `TRIGGER_SCHEMA` IN ('god2_game','god2_player') AND `TRIGGER_NAME` LIKE 'trg_adm_u_%';
            """, cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string TriggerName(string schema, string table)
    {
        var raw = $"trg_adm_u_{schema}_{table}";
        if (raw.Length <= 64)
        {
            return raw;
        }
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..12];
        return string.Concat(raw.AsSpan(0, 51), "_", suffix);
    }

    private static async Task<object?> ScalarAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters) =>
        await ScalarAsync(connection, sql, cancellationToken, null, parameters);

    private static async Task<object?> ScalarAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        MySqlTransaction? transaction,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<int> ExecuteAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters) =>
        await ExecuteAsync(connection, sql, cancellationToken, null, parameters);

    private static async Task<int> ExecuteAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        MySqlTransaction? transaction,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Q(string identifier)
    {
        if (!SafeIdentifier.IsMatch(identifier))
        {
            throw new InvalidOperationException($"Unsafe SQL identifier: {identifier}");
        }
        return $"`{identifier}`";
    }

    private static string EscapeSql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string Canonical(object? value) => value switch
    {
        null or DBNull => "<NULL>",
        DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToHexString(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(bytes);
        Array.Clear(bytes);
    }
}
