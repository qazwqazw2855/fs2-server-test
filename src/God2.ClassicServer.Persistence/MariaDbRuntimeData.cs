using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Application.Contracts;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed record MapStaticData(
    int Id,
    string Code,
    string Name,
    int? Width,
    int? Height,
    string? ResourceIdentity = null,
    string? SourceIdentity = null,
    string? PayloadSha256 = null,
    string? EvidenceStatus = null);

public sealed record ClientMapIdentityStaticData(
    int MapId,
    string ClientBuildId,
    ushort ClientMapId,
    byte ClientAreaId,
    string ResourceIdentity,
    decimal CoordinateScaleX,
    decimal CoordinateScaleY,
    decimal CoordinateOffsetX,
    decimal CoordinateOffsetY,
    string IdentityEvidenceStatus,
    string CoordinateEvidenceStatus,
    bool ProductionEnabled,
    string EvidenceReference);

public sealed record PortalStaticData(
    long Id,
    int? SourceMapId,
    int? SourceX,
    int? SourceY,
    int? TargetMapId,
    int? TargetX,
    int? TargetY,
    string Name,
    int SourceRadius = 0);

public sealed record NpcStaticData(int Id, string Code, string Name, int? MapId, int? PositionX, int? PositionY);

public sealed record NpcSpawnStaticData(
    int Id,
    int NpcId,
    int MapId,
    int PositionX,
    int PositionY,
    int? Direction,
    string SpawnCondition,
    string ClientBuildId,
    uint? ObservedClientEntityHandle,
    int? OfficialResourceType,
    int? OfficialResourceOrdinal,
    int? OfficialSelectorHighBits,
    int? OfficialDirectionCode,
    int? OfficialStateCode,
    string WireEvidenceStatus,
    string? ApplicationMessageSha256,
    string? OpaqueTemplateSha256,
    string Code,
    string Name,
    string ResourceKey,
    string NpcType,
    string InteractionFamily,
    string IdentityEvidenceStatus,
    string CoordinateEvidenceStatus,
    string ServiceEvidenceStatus,
    bool ProductionEnabled,
    string EvidenceReference)
{
    public NpcSpawnStaticData(
        int id,
        int npcId,
        int mapId,
        int positionX,
        int positionY,
        int? direction,
        string spawnCondition,
        string clientBuildId,
        uint? observedClientEntityHandle,
        string code,
        string name,
        string resourceKey,
        string npcType,
        string interactionFamily,
        string identityEvidenceStatus,
        string coordinateEvidenceStatus,
        string serviceEvidenceStatus,
        bool productionEnabled,
        string evidenceReference)
        : this(
            id,
            npcId,
            mapId,
            positionX,
            positionY,
            direction,
            spawnCondition,
            clientBuildId,
            observedClientEntityHandle,
            null,
            null,
            null,
            null,
            null,
            "EvidenceBlocked",
            null,
            null,
            code,
            name,
            resourceKey,
            npcType,
            interactionFamily,
            identityEvidenceStatus,
            coordinateEvidenceStatus,
            serviceEvidenceStatus,
            productionEnabled,
            evidenceReference)
    {
    }
}

public sealed record MonsterStaticData(
    int Id,
    string Code,
    string Name,
    int? Level,
    long? MaxHp,
    int? Attack,
    int? Defense,
    long? MaxMp = null,
    int? MagicAttack = null,
    int? MagicDefense = null,
    int? Metal = null,
    int? Wood = null,
    int? Water = null,
    int? Fire = null,
    int? Earth = null);

public sealed record SpawnStaticData(
    long Id,
    int MapId,
    int MonsterId,
    int PositionX,
    int PositionY,
    int RespawnSeconds,
    int? SpawnRadius = null,
    int? SpawnCount = null,
    string EvidenceStatus = "EvidenceBlocked",
    bool ProductionEnabled = false);

public sealed record ItemStaticData(int Id, string Code, string Name, string? ItemType, int? MaxStack, long? SellPrice);

public sealed record SkillStaticData(int Id, string Code, string Name, int? MaxLevel, int? RequiredLevel);

public sealed record QuestStaticData(int Id, string Code, string Name, int? RequiredLevel, int? StartNpcId, int? EndNpcId);

public sealed record MerchantStaticData(int Id, int? NpcId, string Name);

public sealed record MerchantItemStaticData(
    int MerchantId,
    int ItemId,
    long Price,
    long? PurchasingPrice = null);

public sealed record DialogStaticData(int Id, string Code, int? NpcId, string? TextKey);

public sealed record DropTableStaticData(
    int Id,
    int? MonsterId,
    string Name,
    int? ItemId = null,
    int MinimumQuantity = 0,
    int MaximumQuantity = 0,
    decimal? DropRate = null,
    bool IsGuaranteed = false,
    string Source = "MariaDB:monster_drops");

public sealed record LocalizationStaticData(string Language, string TextKey, string TextValue);

public sealed record FormalRuntimeStaticSnapshot(
    IReadOnlyDictionary<int, MapStaticData> Maps,
    IReadOnlyDictionary<long, PortalStaticData> Portals,
    IReadOnlyDictionary<int, NpcStaticData> Npcs,
    IReadOnlyDictionary<int, NpcSpawnStaticData> NpcSpawns,
    IReadOnlyDictionary<int, MonsterStaticData> Monsters,
    IReadOnlyDictionary<long, SpawnStaticData> Spawns,
    IReadOnlyDictionary<int, ItemStaticData> Items,
    IReadOnlyDictionary<int, SkillStaticData> Skills,
    IReadOnlyDictionary<int, QuestStaticData> Quests,
    IReadOnlyDictionary<int, MerchantStaticData> Merchants,
    IReadOnlyDictionary<string, MerchantItemStaticData> MerchantItems,
    IReadOnlyDictionary<int, DialogStaticData> Dialogs,
    IReadOnlyDictionary<int, DropTableStaticData> DropTables,
    IReadOnlyDictionary<string, LocalizationStaticData> Localization,
    IReadOnlyDictionary<string, ClientMapIdentityStaticData> ClientMapIdentities,
    IReadOnlyList<StaticDataLoadCount> Counts,
    long EstimatedManagedBytes)
{
    public IReadOnlyDictionary<string, string> TableFingerprints { get; init; } =
        FrozenDictionary<string, string>.Empty;

    public static FormalRuntimeStaticSnapshot Empty { get; } = new(
        FrozenDictionary<int, MapStaticData>.Empty,
        FrozenDictionary<long, PortalStaticData>.Empty,
        FrozenDictionary<int, NpcStaticData>.Empty,
        FrozenDictionary<int, NpcSpawnStaticData>.Empty,
        FrozenDictionary<int, MonsterStaticData>.Empty,
        FrozenDictionary<long, SpawnStaticData>.Empty,
        FrozenDictionary<int, ItemStaticData>.Empty,
        FrozenDictionary<int, SkillStaticData>.Empty,
        FrozenDictionary<int, QuestStaticData>.Empty,
        FrozenDictionary<int, MerchantStaticData>.Empty,
        FrozenDictionary<string, MerchantItemStaticData>.Empty,
        FrozenDictionary<int, DialogStaticData>.Empty,
        FrozenDictionary<int, DropTableStaticData>.Empty,
        FrozenDictionary<string, LocalizationStaticData>.Empty,
        FrozenDictionary<string, ClientMapIdentityStaticData>.Empty,
        Array.AsReadOnly(Array.Empty<StaticDataLoadCount>()),
        0);
}

public sealed record FormalRuntimeCatalogManifest(
    string Version,
    string Sha256,
    long RecordCount);

public static class FormalRuntimeCatalogManifestBuilder
{
    public const string Version = "god2-formal-runtime-catalog-manifest-v2";

    public static FormalRuntimeCatalogManifest Build(FormalRuntimeStaticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (ReferenceEquals(snapshot, FormalRuntimeStaticSnapshot.Empty) || snapshot.Counts.Count == 0)
        {
            throw new InvalidOperationException("A loaded formal runtime snapshot is required to build a release manifest.");
        }

        if (snapshot.TableFingerprints.Count != FormalRuntimeDataCatalog.Tables.Count ||
            FormalRuntimeDataCatalog.Tables.Any(table => !snapshot.TableFingerprints.ContainsKey(table.TableName)))
        {
            throw new InvalidOperationException("Every formal runtime table requires a deterministic content fingerprint.");
        }

        var canonical = new
        {
            schemaVersion = Version,
            counts = snapshot.Counts.OrderBy(entry => entry.DataType, StringComparer.Ordinal).ToArray(),
            tableFingerprints = snapshot.TableFingerprints.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray(),
            maps = snapshot.Maps.Values.OrderBy(entry => entry.Id).ToArray(),
            portals = snapshot.Portals.Values.OrderBy(entry => entry.Id).ToArray(),
            npcs = snapshot.Npcs.Values.OrderBy(entry => entry.Id).ToArray(),
            npcSpawns = snapshot.NpcSpawns.Values.OrderBy(entry => entry.Id).ToArray(),
            monsters = snapshot.Monsters.Values.OrderBy(entry => entry.Id).ToArray(),
            spawns = snapshot.Spawns.Values.OrderBy(entry => entry.Id).ToArray(),
            items = snapshot.Items.Values.OrderBy(entry => entry.Id).ToArray(),
            skills = snapshot.Skills.Values.OrderBy(entry => entry.Id).ToArray(),
            quests = snapshot.Quests.Values.OrderBy(entry => entry.Id).ToArray(),
            merchants = snapshot.Merchants.Values.OrderBy(entry => entry.Id).ToArray(),
            merchantItems = snapshot.MerchantItems.Values
                .OrderBy(entry => entry.MerchantId)
                .ThenBy(entry => entry.ItemId)
                .ToArray(),
            dialogs = snapshot.Dialogs.Values.OrderBy(entry => entry.Id).ToArray(),
            dropTables = snapshot.DropTables.Values.OrderBy(entry => entry.Id).ToArray(),
            localization = snapshot.Localization.Values
                .OrderBy(entry => entry.Language, StringComparer.Ordinal)
                .ThenBy(entry => entry.TextKey, StringComparer.Ordinal)
                .ToArray(),
            clientMapIdentities = snapshot.ClientMapIdentities.Values
                .OrderBy(entry => entry.MapId)
                .ThenBy(entry => entry.ClientBuildId, StringComparer.Ordinal)
                .ToArray()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical);
        try
        {
            return new FormalRuntimeCatalogManifest(
                Version,
                Convert.ToHexString(SHA256.HashData(bytes)),
                snapshot.Counts.Sum(entry => (long)entry.Count));
        }
        finally
        {
            Array.Clear(bytes);
        }
    }
}

public static class FormalRuntimeDataCatalog
{
    public static IReadOnlyList<(string DataType, string SchemaName, string TableName)> Tables { get; } =
    [
        ("Map", "god2_game", "maps"),
        ("Portal", "god2_game", "portals"),
        ("NPC", "god2_game", "npcs"),
        ("NPC Spawn", "god2_game", "npc_spawns"),
        ("Monster", "god2_game", "monsters"),
        ("Spawn", "god2_game", "monster_spawns"),
        ("Monster Combat Stat Design Rule", "god2_game", "monster_combat_stat_design_rules"),
        ("Monster Drop Design Rule", "god2_game", "monster_drop_design_rules"),
        ("Monster Spawn Design Rule", "god2_game", "monster_spawn_design_rules"),
        ("Item", "god2_game", "items"),
        ("Weapon", "god2_game", "weapons"),
        ("Equipment", "god2_game", "equipment"),
        ("Magic Treasure", "god2_game", "magic_treasures"),
        ("Item Set Bonus", "god2_game", "item_set_bonuses"),
        ("Skill", "god2_game", "skills"),
        ("Class Stat Growth", "god2_game", "class_stat_growth"),
        ("Immortal Rank", "god2_game", "immortal_ranks"),
        ("Immortal", "god2_game", "immortal_templates"),
        ("Pet Growth Archetype", "god2_game", "pet_growth_archetypes"),
        ("Battle Pet", "god2_game", "pet_templates"),
        ("Quest", "god2_game", "quests"),
        ("Merchant", "god2_game", "merchants"),
        ("Merchant Item", "god2_game", "merchant_inventory"),
        ("Drop Table", "god2_game", "monster_drops"),
        ("Reward", "god2_game", "quest_rewards"),
        ("Dialog", "god2_game", "npc_dialogs"),
        ("Status Effect", "god2_game", "status_effects")
    ];

    public static IReadOnlySet<string> RequiredPopulatedDataTypes { get; } = new HashSet<string>(
    [
        "Map",
        "NPC",
        "NPC Spawn"
    ],
    StringComparer.Ordinal);

    public static string BuildCountQuery() => string.Join(
        "\nUNION ALL\n",
        Tables.Select(entry =>
            $"SELECT '{entry.DataType.Replace("'", "''", StringComparison.Ordinal)}' AS `DataType`, COUNT(*) AS `RecordCount` FROM `{entry.SchemaName}`.`{entry.TableName}` WHERE `enabled`=1"));
}

public sealed class MariaDbStaticDataLoader : IStaticDataLoader, IRuntimeCacheBuilder
{
    private readonly DatabaseOptions _options;
    private FormalRuntimeStaticSnapshot? _candidateSnapshot;
    private FormalRuntimeStaticSnapshot _publishedSnapshot = FormalRuntimeStaticSnapshot.Empty;

    public MariaDbStaticDataLoader(DatabaseOptions options)
    {
        _options = options;
    }

    public FormalRuntimeStaticSnapshot PublishedSnapshot => Volatile.Read(ref _publishedSnapshot);

    public async Task<OperationResult<IReadOnlyList<StaticDataLoadCount>>> LoadAsync(CancellationToken cancellationToken)
    {
        var password = MariaDbDatabaseBootstrapper.ResolvePassword(_options);
        if (string.IsNullOrEmpty(password))
        {
            return OperationResult<IReadOnlyList<StaticDataLoadCount>>.Failure(
                "mariadb.password_missing",
                "MariaDB password is not set.",
                "database.json:password");
        }

        try
        {
            await using var connection = new MySqlConnection(
                MariaDbDatabaseBootstrapper.BuildConnectionString(_options, password, _options.DatabaseName));
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 30;
            command.CommandText = FormalRuntimeDataCatalog.BuildCountQuery();

            var counts = new List<StaticDataLoadCount>(FormalRuntimeDataCatalog.Tables.Count);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                counts.Add(new StaticDataLoadCount(reader.GetString(0), checked((int)reader.GetInt64(1))));
            }

            await reader.DisposeAsync();
            if (counts.Count != FormalRuntimeDataCatalog.Tables.Count)
            {
                return OperationResult<IReadOnlyList<StaticDataLoadCount>>.Failure(
                    "static_data.catalog_incomplete",
                    "Formal runtime table count query returned an incomplete catalog.",
                    "formal-runtime-tables");
            }

            var maps = await QueryAsync(
                connection,
                """
                SELECT `map_id`, COALESCE(`code`,CAST(`map_id` AS CHAR)), `name_zh_tw`, `width`, `height`,
                       `resource_identity`, COALESCE(`code`,CAST(`map_id` AS CHAR)), NULL, 'canonical-map'
                FROM `god2_game`.`maps`
                WHERE `enabled`=1
                ORDER BY `map_id`;
                """,
                MaterializeMap,
                cancellationToken);
            var portals = await QueryAsync(
                connection,
                """
                SELECT `portal_id`, `source_map_id`, `source_x`, `source_y`, `destination_map_id`,
                       `destination_x`, `destination_y`, `name_zh_tw`, COALESCE(`source_radius`,0)
                FROM `god2_game`.`portals` WHERE `enabled`=1 ORDER BY `portal_id`;
                """,
                MaterializePortal,
                cancellationToken);
            var npcs = await QueryAsync(
                connection,
                "SELECT `npc_id`,COALESCE(`code`,CAST(`npc_id` AS CHAR)),`name_zh_tw`,NULL,NULL,NULL FROM `god2_game`.`npcs` WHERE `enabled`=1 ORDER BY `npc_id`;",
                MaterializeNpc,
                cancellationToken);
            var npcSpawns = await QueryAsync(
                connection,
                """
                SELECT spawn.`spawn_id`,spawn.`npc_id`,spawn.`map_id`,spawn.`position_x`,spawn.`position_y`,
                       spawn.`direction`,'Always',spawn.`client_build_id`,spawn.`observed_client_entity_handle`,
                       spawn.`official_resource_type`,spawn.`official_resource_ordinal`,spawn.`official_selector_high_bits`,
                       spawn.`official_direction_code`,spawn.`official_state_code`,'canonical-npc-spawn-wire',
                       NULL,NULL,
                       COALESCE(npc.`code`,CAST(npc.`npc_id` AS CHAR)),npc.`name_zh_tw`,
                       COALESCE(npc.`resource_key`,CAST(npc.`npc_id` AS CHAR)),npc.`npc_type`,
                       CASE COALESCE(npc.`interaction_family`,'未知')
                           WHEN '未知' THEN 'Unknown'
                           WHEN '商店' THEN 'Merchant'
                           WHEN '對話' THEN 'Dialog'
                           WHEN '任務' THEN 'Quest'
                           WHEN '商店任務' THEN 'MerchantQuest'
                           ELSE COALESCE(npc.`interaction_family`,'Unknown')
                       END,'canonical-npc-spawn-identity',
                       'canonical-npc-spawn-coordinate','canonical-npc-spawn-service',spawn.`enabled`,
                       CONCAT('god2_game.npc_spawns:',spawn.`spawn_id`)
                FROM `god2_game`.`npc_spawns` spawn
                JOIN `god2_game`.`npcs` npc ON npc.`npc_id`=spawn.`npc_id` AND npc.`enabled`=1
                JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=spawn.`map_id` AND map_row.`enabled`=1
                WHERE spawn.`enabled`=1
                ORDER BY spawn.`spawn_id`;
                """,
                MaterializeNpcSpawn,
                cancellationToken);
            var monsters = await QueryAsync(
                connection,
                "SELECT `monster_id`,COALESCE(`code`,CAST(`monster_id` AS CHAR)),`name_zh_tw`,`level`,`max_hp`,`physical_attack`,`physical_defense`,`max_mp`,`magic_attack`,`magic_defense`,`metal`,`wood`,`water`,`fire`,`earth` FROM `god2_game`.`monsters` WHERE `enabled`=1 ORDER BY `monster_id`;",
                MaterializeMonster,
                cancellationToken);
            var spawns = await QueryAsync(
                connection,
                "SELECT `spawn_id`,`map_id`,`monster_id`,`position_x`,`position_y`,`respawn_seconds_min`,`spawn_radius`,`spawn_count`,'canonical-monster-spawn',`enabled` FROM `god2_game`.`monster_spawns` WHERE `enabled`=1 ORDER BY `spawn_id`;",
                MaterializeSpawn,
                cancellationToken);
            var items = await QueryAsync(
                connection,
                "SELECT `item_id`,COALESCE(`code`,CAST(`item_id` AS CHAR)),`name_zh_tw`,`item_category`,`maximum_stack`,`sell_price` FROM `god2_game`.`items` WHERE `enabled`=1 ORDER BY `item_id`;",
                MaterializeItem,
                cancellationToken);
            var skills = await QueryAsync(
                connection,
                "SELECT `skill_id`,COALESCE(`code`,CAST(`skill_id` AS CHAR)),`name_zh_tw`,`maximum_level`,`required_level` FROM `god2_game`.`skills` WHERE `enabled`=1 ORDER BY `skill_id`;",
                MaterializeSkill,
                cancellationToken);
            var quests = await QueryAsync(
                connection,
                "SELECT `quest_id`,COALESCE(`code`,CAST(`quest_id` AS CHAR)),`name_zh_tw`,`required_level`,`start_npc_id`,`end_npc_id` FROM `god2_game`.`quests` WHERE `enabled`=1 ORDER BY `quest_id`;",
                MaterializeQuest,
                cancellationToken);
            var merchants = await QueryAsync(
                connection,
                "SELECT `merchant_id`,`npc_id`,`name_zh_tw` FROM `god2_game`.`merchants` WHERE `enabled`=1 ORDER BY `merchant_id`;",
                MaterializeMerchant,
                cancellationToken);
            var merchantItems = await QueryAsync(
                connection,
                "SELECT `merchant_id`,`item_id`,`selling_price`,`purchasing_price` FROM `god2_game`.`merchant_inventory` WHERE `enabled`=1 ORDER BY `merchant_id`,`item_id`;",
                MaterializeMerchantItem,
                cancellationToken);
            var dialogs = await QueryAsync(
                connection,
                "SELECT `dialog_id`,COALESCE(`code`,CAST(`dialog_id` AS CHAR)),`npc_id`,NULL FROM `god2_game`.`npc_dialogs` WHERE `enabled`=1 ORDER BY `dialog_id`;",
                MaterializeDialog,
                cancellationToken);
            var dropTables = await QueryAsync(
                connection,
                "SELECT `drop_id`,`monster_id`,CONCAT('Drop ',`drop_id`),`item_id`,`minimum_quantity`,`maximum_quantity`,`drop_rate`,`is_guaranteed`,COALESCE(`drop_source_zh_tw`,'MariaDB:monster_drops') FROM `god2_game`.`monster_drops` WHERE `enabled`=1 ORDER BY `drop_id`;",
                MaterializeDropTable,
                cancellationToken);
            var localization = await QueryAsync(
                connection,
                "SELECT CAST(NULL AS CHAR),CAST(NULL AS CHAR),CAST(NULL AS CHAR) WHERE 1=0;",
                row => new LocalizationStaticData(row.GetString(0), row.GetString(1), row.GetString(2)),
                cancellationToken);
            var clientMapIdentities = await QueryAsync(
                connection,
                """
                SELECT `map_id`,`client_build_id`,`client_map_id`,`client_area_id`,`resource_identity`,
                       `coordinate_scale_x`,`coordinate_scale_y`,`coordinate_offset_x`,`coordinate_offset_y`,
                       'canonical-map-identity','canonical-map-coordinate',`enabled`,
                       CONCAT('god2_game.maps:',`map_id`)
                FROM `god2_game`.`maps`
                WHERE `enabled`=1 AND `client_build_id` IS NOT NULL
                ORDER BY `map_id`,`client_build_id`;
                """,
                MaterializeClientMapIdentity,
                cancellationToken);

            ValidateMaterializedCounts(
                counts,
                ("Map", maps.Count),
                ("Portal", portals.Count),
                ("NPC", npcs.Count),
                ("NPC Spawn", npcSpawns.Count),
                ("Monster", monsters.Count),
                ("Spawn", spawns.Count),
                ("Item", items.Count),
                ("Skill", skills.Count),
                ("Quest", quests.Count),
                ("Merchant", merchants.Count),
                ("Merchant Item", merchantItems.Count),
                ("Dialog", dialogs.Count),
                ("Drop Table", dropTables.Count));

            var immutableCounts = Array.AsReadOnly(counts.ToArray());
            var tableFingerprints = await BuildTableFingerprintsAsync(connection, cancellationToken);
            var snapshot = new FormalRuntimeStaticSnapshot(
                maps.ToFrozenDictionary(entry => entry.Id),
                portals.ToFrozenDictionary(entry => entry.Id),
                npcs.ToFrozenDictionary(entry => entry.Id),
                npcSpawns.ToFrozenDictionary(entry => entry.Id),
                monsters.ToFrozenDictionary(entry => entry.Id),
                spawns.ToFrozenDictionary(entry => entry.Id),
                items.ToFrozenDictionary(entry => entry.Id),
                skills.ToFrozenDictionary(entry => entry.Id),
                quests.ToFrozenDictionary(entry => entry.Id),
                merchants.ToFrozenDictionary(entry => entry.Id),
                merchantItems.ToFrozenDictionary(
                    entry => $"{entry.MerchantId}\u001f{entry.ItemId}",
                    StringComparer.Ordinal),
                dialogs.ToFrozenDictionary(entry => entry.Id),
                dropTables.ToFrozenDictionary(entry => entry.Id),
                localization.ToFrozenDictionary(
                    entry => $"{entry.Language}\u001f{entry.TextKey}",
                    StringComparer.Ordinal),
                clientMapIdentities.ToFrozenDictionary(
                    entry => $"{entry.MapId}\u001f{entry.ClientBuildId}",
                    StringComparer.Ordinal),
                immutableCounts,
                EstimateManagedBytes(
                    maps,
                    portals,
                    npcs,
                    npcSpawns,
                    monsters,
                    spawns,
                    items,
                    skills,
                    quests,
                    merchants,
                    merchantItems,
                    dialogs,
                    dropTables,
                    localization,
                    clientMapIdentities))
            {
                TableFingerprints = tableFingerprints
            };
            Volatile.Write(ref _candidateSnapshot, snapshot);
            return OperationResult<IReadOnlyList<StaticDataLoadCount>>.Success(immutableCounts);
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            return OperationResult<IReadOnlyList<StaticDataLoadCount>>.Failure(
                "static_data.load_failed",
                "Formal runtime data could not be loaded from MariaDB.",
                exception.GetType().Name);
        }
    }

    public Task<OperationResult> BuildAsync(
        IReadOnlyList<StaticDataLoadCount> staticData,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = Volatile.Read(ref _candidateSnapshot);
        if (candidate is null ||
            candidate.Counts.Count != staticData.Count ||
            candidate.Counts.Any(expected =>
                staticData.SingleOrDefault(actual => actual.DataType == expected.DataType)?.Count != expected.Count))
        {
            return Task.FromResult(OperationResult.Failure(
                "runtime_cache.candidate_invalid",
                "Formal static data candidate is missing or does not match validated load counts.",
                "formal-runtime-cache"));
        }

        Interlocked.Exchange(ref _publishedSnapshot, candidate);
        Volatile.Write(ref _candidateSnapshot, null);
        return Task.FromResult(OperationResult.Success);
    }

    private static async Task<IReadOnlyDictionary<string, string>> BuildTableFingerprintsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in FormalRuntimeDataCatalog.Tables)
        {
            var columns = new List<(string Name, string DataType)>();
            await using (var schema = connection.CreateCommand())
            {
                schema.CommandTimeout = 30;
                schema.CommandText = """
                    SELECT `COLUMN_NAME`,`DATA_TYPE`
                    FROM `information_schema`.`COLUMNS`
                    WHERE `TABLE_SCHEMA`=@schema AND `TABLE_NAME`=@table
                    ORDER BY `ORDINAL_POSITION`;
                    """;
                schema.Parameters.AddWithValue("@schema", table.SchemaName);
                schema.Parameters.AddWithValue("@table", table.TableName);
                await using var reader = await schema.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    columns.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            if (columns.Count == 0)
            {
                throw new InvalidOperationException($"Formal runtime table '{table.TableName}' has no readable columns.");
            }

            var rowHashes = new List<string>();
            await using (var rows = connection.CreateCommand())
            {
                rows.CommandTimeout = 30;
                rows.CommandText = $"SELECT {string.Join(',', columns.Select(column => QuoteIdentifier(column.Name)))} FROM {QuoteIdentifier(table.SchemaName)}.{QuoteIdentifier(table.TableName)} WHERE `enabled`=1;";
                await using var reader = await rows.ExecuteReaderAsync(cancellationToken);
                var rawValues = new object[columns.Count];
                while (await reader.ReadAsync(cancellationToken))
                {
                    reader.GetValues(rawValues);
                    var canonicalValues = new object?[rawValues.Length];
                    for (var index = 0; index < rawValues.Length; index++)
                    {
                        canonicalValues[index] = rawValues[index] is DBNull ? null : rawValues[index];
                    }

                    var bytes = JsonSerializer.SerializeToUtf8Bytes(canonicalValues);
                    try
                    {
                        rowHashes.Add(Convert.ToHexString(SHA256.HashData(bytes)));
                    }
                    finally
                    {
                        Array.Clear(bytes);
                        Array.Clear(rawValues);
                        Array.Clear(canonicalValues);
                    }
                }
            }

            rowHashes.Sort(StringComparer.Ordinal);
            var canonical = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = table.SchemaName,
                table = table.TableName,
                columns = columns.Select(column => new { column.Name, column.DataType }).ToArray(),
                rows = rowHashes
            });
            try
            {
                result.Add(table.TableName, Convert.ToHexString(SHA256.HashData(canonical)));
            }
            finally
            {
                Array.Clear(canonical);
            }
        }

        return new ReadOnlyDictionary<string, string>(result);
    }

    private static string QuoteIdentifier(string value) => $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";

    private static async Task<List<T>> QueryAsync<T>(
        MySqlConnection connection,
        string sql,
        Func<MySqlDataReader, T> materialize,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = sql;
        var records = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(materialize(reader));
        }

        return records;
    }

    internal static MapStaticData MaterializeMap(IDataRecord row) => new(
        row.GetInt32(0),
        row.GetString(1),
        row.GetString(2),
        GetNullableInt32(row, 3),
        GetNullableInt32(row, 4),
        GetNullableString(row, 5),
        GetNullableString(row, 6),
        GetNullableString(row, 7),
        GetNullableString(row, 8));

    internal static ClientMapIdentityStaticData MaterializeClientMapIdentity(IDataRecord row) => new(
        row.GetInt32(0),
        row.GetString(1),
        checked((ushort)row.GetInt32(2)),
        checked((byte)row.GetInt32(3)),
        row.GetString(4),
        row.GetDecimal(5),
        row.GetDecimal(6),
        row.GetDecimal(7),
        row.GetDecimal(8),
        row.GetString(9),
        row.GetString(10),
        row.GetBoolean(11),
        row.GetString(12));

    internal static PortalStaticData MaterializePortal(IDataRecord row) => new(
        row.GetInt64(0),
        GetNullableInt32(row, 1),
        GetNullableInt32(row, 2),
        GetNullableInt32(row, 3),
        GetNullableInt32(row, 4),
        GetNullableInt32(row, 5),
        GetNullableInt32(row, 6),
        row.GetString(7),
        row.FieldCount > 8 && !row.IsDBNull(8) ? row.GetInt32(8) : 0);

    internal static NpcStaticData MaterializeNpc(IDataRecord row) => new(
        row.GetInt32(0),
        row.GetString(1),
        row.GetString(2),
        GetNullableInt32(row, 3),
        GetNullableInt32(row, 4),
        GetNullableInt32(row, 5));

    internal static NpcSpawnStaticData MaterializeNpcSpawn(IDataRecord row) => new(
        row.GetInt32(0),
        row.GetInt32(1),
        row.GetInt32(2),
        row.GetInt32(3),
        row.GetInt32(4),
        GetNullableInt32(row, 5),
        row.GetString(6),
        row.GetString(7),
        row.IsDBNull(8) ? null : checked((uint)row.GetInt64(8)),
        GetNullableInt32(row, 9),
        GetNullableInt32(row, 10),
        GetNullableInt32(row, 11),
        GetNullableInt32(row, 12),
        GetNullableInt32(row, 13),
        row.GetString(14),
        row.IsDBNull(15) ? null : row.GetString(15),
        row.IsDBNull(16) ? null : row.GetString(16),
        row.GetString(17),
        row.GetString(18),
        row.GetString(19),
        row.GetString(20),
        row.GetString(21),
        row.GetString(22),
        row.GetString(23),
        row.GetString(24),
        row.GetBoolean(25),
        row.GetString(26));

    internal static MonsterStaticData MaterializeMonster(IDataRecord row) => new(
        row.GetInt32(0),
        row.GetString(1),
        row.GetString(2),
        GetNullableInt32(row, 3),
        GetNullableInt64(row, 4),
        GetNullableInt32(row, 5),
        GetNullableInt32(row, 6),
        row.FieldCount > 7 ? GetNullableInt64(row, 7) : null,
        row.FieldCount > 8 ? GetNullableInt32(row, 8) : null,
        row.FieldCount > 9 ? GetNullableInt32(row, 9) : null,
        row.FieldCount > 10 ? GetNullableInt32(row, 10) : null,
        row.FieldCount > 11 ? GetNullableInt32(row, 11) : null,
        row.FieldCount > 12 ? GetNullableInt32(row, 12) : null,
        row.FieldCount > 13 ? GetNullableInt32(row, 13) : null,
        row.FieldCount > 14 ? GetNullableInt32(row, 14) : null);

    internal static SpawnStaticData MaterializeSpawn(IDataRecord row) => new(
        row.GetInt64(0),
        row.GetInt32(1),
        row.GetInt32(2),
        row.GetInt32(3),
        row.GetInt32(4),
        row.GetInt32(5),
        GetNullableInt32(row, 6),
        GetNullableInt32(row, 7),
        row.GetString(8),
        row.GetBoolean(9));

    internal static ItemStaticData MaterializeItem(IDataRecord row) => new(
        row.GetInt32(0),
        row.GetString(1),
        row.GetString(2),
        GetNullableString(row, 3),
        GetNullableInt32(row, 4),
        GetNullableInt64(row, 5));

    internal static SkillStaticData MaterializeSkill(IDataRecord row) => new(
        row.GetInt32(0),
        row.GetString(1),
        row.GetString(2),
        GetNullableInt32(row, 3),
        GetNullableInt32(row, 4));

    internal static QuestStaticData MaterializeQuest(IDataRecord row) => new(
        row.GetInt32(0),
        row.GetString(1),
        row.GetString(2),
        GetNullableInt32(row, 3),
        GetNullableInt32(row, 4),
        GetNullableInt32(row, 5));

    internal static MerchantStaticData MaterializeMerchant(IDataRecord row) => new(
        row.GetInt32(0),
        GetNullableInt32(row, 1),
        row.GetString(2));

    internal static MerchantItemStaticData MaterializeMerchantItem(IDataRecord row) => new(
        row.GetInt32(0),
        row.GetInt32(1),
        row.GetInt64(2),
        GetNullableInt64(row, 3));

    internal static DialogStaticData MaterializeDialog(IDataRecord row) => new(
        row.GetInt32(0),
        row.GetString(1),
        GetNullableInt32(row, 2),
        GetNullableString(row, 3));

    internal static DropTableStaticData MaterializeDropTable(IDataRecord row) => new(
        row.GetInt32(0),
        GetNullableInt32(row, 1),
        row.GetString(2),
        GetNullableInt32(row, 3),
        row.IsDBNull(4) ? 0 : row.GetInt32(4),
        row.IsDBNull(5) ? 0 : row.GetInt32(5),
        row.IsDBNull(6) ? null : row.GetDecimal(6),
        !row.IsDBNull(7) && row.GetBoolean(7),
        row.IsDBNull(8) ? "MariaDB:monster_drops" : row.GetString(8));

    private static int? GetNullableInt32(IDataRecord row, int ordinal) =>
        row.IsDBNull(ordinal) ? null : row.GetInt32(ordinal);

    private static long? GetNullableInt64(IDataRecord row, int ordinal) =>
        row.IsDBNull(ordinal) ? null : row.GetInt64(ordinal);

    private static string? GetNullableString(IDataRecord row, int ordinal) =>
        row.IsDBNull(ordinal) ? null : row.GetString(ordinal);

    private static void ValidateMaterializedCounts(
        IReadOnlyList<StaticDataLoadCount> counts,
        params (string DataType, int Count)[] materialized)
    {
        var expected = counts.ToDictionary(entry => entry.DataType, entry => entry.Count, StringComparer.Ordinal);
        foreach (var actual in materialized)
        {
            if (!expected.TryGetValue(actual.DataType, out var expectedCount) || expectedCount != actual.Count)
            {
                throw new InvalidOperationException($"Materialized static data count mismatch: {actual.DataType}.");
            }
        }
    }

    private static long EstimateManagedBytes(
        IReadOnlyList<MapStaticData> maps,
        IReadOnlyList<PortalStaticData> portals,
        IReadOnlyList<NpcStaticData> npcs,
        IReadOnlyList<NpcSpawnStaticData> npcSpawns,
        IReadOnlyList<MonsterStaticData> monsters,
        IReadOnlyList<SpawnStaticData> spawns,
        IReadOnlyList<ItemStaticData> items,
        IReadOnlyList<SkillStaticData> skills,
        IReadOnlyList<QuestStaticData> quests,
        IReadOnlyList<MerchantStaticData> merchants,
        IReadOnlyList<MerchantItemStaticData> merchantItems,
        IReadOnlyList<DialogStaticData> dialogs,
        IReadOnlyList<DropTableStaticData> dropTables,
        IReadOnlyList<LocalizationStaticData> localization,
        IReadOnlyList<ClientMapIdentityStaticData> clientMapIdentities)
    {
        long characters =
            maps.Sum(entry =>
                entry.Code.Length + entry.Name.Length + (entry.ResourceIdentity?.Length ?? 0) +
                (entry.SourceIdentity?.Length ?? 0) + (entry.PayloadSha256?.Length ?? 0) +
                (entry.EvidenceStatus?.Length ?? 0)) +
            portals.Sum(entry => entry.Name.Length) +
            npcs.Sum(entry => entry.Code.Length + entry.Name.Length) +
            npcSpawns.Sum(entry =>
                entry.Code.Length + entry.Name.Length + entry.ResourceKey.Length + entry.NpcType.Length +
                entry.InteractionFamily.Length + entry.IdentityEvidenceStatus.Length +
                entry.CoordinateEvidenceStatus.Length + entry.ServiceEvidenceStatus.Length +
                entry.ClientBuildId.Length + entry.WireEvidenceStatus.Length +
                (entry.ApplicationMessageSha256?.Length ?? 0) + (entry.OpaqueTemplateSha256?.Length ?? 0) +
                entry.EvidenceReference.Length) +
            monsters.Sum(entry => entry.Code.Length + entry.Name.Length) +
            items.Sum(entry => entry.Code.Length + entry.Name.Length + (entry.ItemType?.Length ?? 0)) +
            skills.Sum(entry => entry.Code.Length + entry.Name.Length) +
            quests.Sum(entry => entry.Code.Length + entry.Name.Length) +
            merchants.Sum(entry => entry.Name.Length) +
            dialogs.Sum(entry => entry.Code.Length + (entry.TextKey?.Length ?? 0)) +
            dropTables.Sum(entry => entry.Name.Length) +
            localization.Sum(entry => entry.Language.Length + entry.TextKey.Length + entry.TextValue.Length) +
            clientMapIdentities.Sum(entry =>
                entry.ClientBuildId.Length + entry.ResourceIdentity.Length + entry.IdentityEvidenceStatus.Length +
                entry.CoordinateEvidenceStatus.Length + entry.EvidenceReference.Length);
        long records =
            maps.Count + portals.Count + npcs.Count + npcSpawns.Count + monsters.Count + spawns.Count + items.Count + skills.Count +
            quests.Count + merchants.Count + merchantItems.Count + dialogs.Count + dropTables.Count + localization.Count +
            clientMapIdentities.Count;
        return checked((characters * sizeof(char)) + (records * 96L));
    }
}

public sealed class MariaDbStaticDataValidator : IStaticDataValidator
{
    private static readonly IReadOnlyList<(string Name, string Sql)> ReferenceChecks =
    [
        (
            "portals.maps",
            """
            SELECT COUNT(*) FROM `god2_game`.`portals` portal_row
            LEFT JOIN `god2_game`.`maps` source_map ON source_map.`map_id`=portal_row.`source_map_id`
            LEFT JOIN `god2_game`.`maps` destination_map ON destination_map.`map_id`=portal_row.`destination_map_id`
            WHERE portal_row.`enabled`=1 AND (source_map.`enabled`<>1 OR destination_map.`enabled`<>1);
            """),
        (
            "npc_spawns.authority",
            """
            SELECT COUNT(*) FROM `god2_game`.`npc_spawns` spawn_row
            LEFT JOIN `god2_game`.`npcs` npc_row ON npc_row.`npc_id`=spawn_row.`npc_id`
            LEFT JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=spawn_row.`map_id`
            WHERE spawn_row.`enabled`=1 AND (npc_row.`enabled`<>1 OR map_row.`enabled`<>1
               OR spawn_row.`position_x` IS NULL OR spawn_row.`position_y` IS NULL
               OR spawn_row.`client_build_id` IS NULL);
            """),
        (
            "monster_spawns.authority",
            """
            SELECT COUNT(*) FROM `god2_game`.`monster_spawns` spawn_row
            LEFT JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=spawn_row.`monster_id`
            LEFT JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=spawn_row.`map_id`
            WHERE spawn_row.`enabled`=1 AND (monster_row.`enabled`<>1 OR map_row.`enabled`<>1
               OR spawn_row.`position_x` IS NULL OR spawn_row.`position_y` IS NULL
               OR spawn_row.`respawn_seconds_min` IS NULL OR spawn_row.`respawn_seconds_min`<=0);
            """),
        (
            "monster_drops.authority",
            """
            SELECT COUNT(*) FROM `god2_game`.`monster_drops` drop_row
            LEFT JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=drop_row.`monster_id`
            LEFT JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=drop_row.`item_id`
            WHERE drop_row.`enabled`=1 AND (monster_row.`enabled`<>1 OR item_row.`enabled`<>1
               OR (drop_row.`drop_rate` IS NULL AND COALESCE(drop_row.`is_guaranteed`,0)=0));
            """),
        (
            "merchant_inventory.authority",
            """
            SELECT COUNT(*) FROM `god2_game`.`merchant_inventory` inventory_row
            LEFT JOIN `god2_game`.`merchants` merchant_row ON merchant_row.`merchant_id`=inventory_row.`merchant_id`
            LEFT JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=inventory_row.`item_id`
            WHERE inventory_row.`enabled`=1 AND (merchant_row.`enabled`<>1 OR item_row.`enabled`<>1 OR inventory_row.`selling_price` IS NULL);
            """),
        (
            "pet_templates.growth_authority",
            """
            SELECT COUNT(*) FROM `god2_game`.`pet_templates` template_row
            LEFT JOIN `god2_game`.`pet_categories` category_row ON category_row.`category_id`=template_row.`pet_category_id`
            WHERE template_row.`enabled`=1
              AND template_row.`pet_category_id` IS NOT NULL
              AND (category_row.`category_id` IS NULL
                   OR (category_row.`enabled`=1 AND category_row.`growth_archetype_id` IS NULL));
            """),
        (
            "character_pets.authority",
            """
            SELECT COUNT(*) FROM `god2_player`.`character_pets` pet_row
            LEFT JOIN `god2_player`.`characters` owner_row ON owner_row.`character_id`=pet_row.`owner_character_id`
            LEFT JOIN `god2_game`.`pet_templates` template_row ON template_row.`pet_template_id`=pet_row.`pet_template_id`
            WHERE pet_row.`enabled`=1 AND (owner_row.`character_id` IS NULL OR template_row.`enabled`<>1
               OR pet_row.`level` NOT BETWEEN 1 AND 99
               OR pet_row.`strength_base` IS NULL OR pet_row.`constitution_base` IS NULL
               OR pet_row.`intelligence_base` IS NULL OR pet_row.`speed_base` IS NULL);
            """),
        (
            "canonical.evidence_gate",
            """
            SELECT 0;
            """)
    ];

    private readonly DatabaseOptions _options;
    private readonly MariaDbStaticDataLoader _loader;

    public MariaDbStaticDataValidator(DatabaseOptions options)
    {
        _options = options;
        _loader = new MariaDbStaticDataLoader(options);
    }

    public async Task<OperationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        var loaded = await _loader.LoadAsync(cancellationToken);
        if (!loaded.Succeeded || loaded.Value is null)
        {
            return OperationResult.Failure(loaded.Error.Code, loaded.Error.Message, loaded.Error.Source);
        }

        var emptyRequired = loaded.Value
            .Where(entry => FormalRuntimeDataCatalog.RequiredPopulatedDataTypes.Contains(entry.DataType) && entry.Count == 0)
            .Select(entry => entry.DataType)
            .ToArray();
        if (emptyRequired.Length > 0)
        {
            return OperationResult.Failure(
                "static_data.required_table_empty",
                "Required formal runtime tables are empty.",
                string.Join(',', emptyRequired));
        }

        var password = MariaDbDatabaseBootstrapper.ResolvePassword(_options)!;
        try
        {
            await using var connection = new MySqlConnection(
                MariaDbDatabaseBootstrapper.BuildConnectionString(_options, password, _options.DatabaseName));
            await connection.OpenAsync(cancellationToken);
            foreach (var check in ReferenceChecks)
            {
                await using var command = connection.CreateCommand();
                command.CommandTimeout = 30;
                command.CommandText = check.Sql;
                var invalidReferences = Convert.ToInt64(
                    await command.ExecuteScalarAsync(cancellationToken),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (invalidReferences > 0)
                {
                    return OperationResult.Failure(
                        "static_data.reference_invalid",
                        "Formal runtime data contains invalid references.",
                        check.Name);
                }
            }

            return OperationResult.Success;
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            return OperationResult.Failure(
                "static_data.validation_failed",
                "Formal runtime data reference validation failed.",
                exception.GetType().Name);
        }
    }
}
