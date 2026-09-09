using System.Data;
using System.Text.Json;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Persistence;
using MySqlConnector;

namespace God2.GameplayContentRecovery;

public sealed record DatabaseImportResult(
    string ServerVersion,
    string DatabaseName,
    string RepositoryType,
    string MigrationStatus,
    IReadOnlyList<object> LocalizationColumnInventory,
    IReadOnlyDictionary<string, long> ReferentialIntegrity,
    IReadOnlyDictionary<string, long> ProductionCompleteness,
    IReadOnlyDictionary<string, object?> RuntimeValidation,
    IReadOnlyDictionary<string, object?> HeadlessValidation,
    long ProductionSimplifiedDisplayRows,
    long BrokenForeignKeys,
    long ContentOrphans,
    int FormalTextRowsUpdated,
    int LocalizationRowsUpserted,
    int VerifiedGuideFieldsPromoted,
    IReadOnlyDictionary<string, object?> Phase2Metrics);

public sealed record ProductionSimplifiedFinding(
    string Table,
    string Column,
    string RowIdentity,
    string TextValue,
    string ConvertedValue,
    string? NameZhTw,
    string? OriginalName,
    string? LocalizationStatus,
    string? ContentRecoveryRunId);

public sealed class MariaDbContentRecoveryStore
{
    private static readonly (string Table, string Domain, string IdColumn)[] FormalNamedTables =
    [
        ("maps", "Map", "Id"),
        ("npcs", "NpcTemplate", "Id"),
        ("monsters", "MonsterTemplate", "Id"),
        ("items", "Item", "Id"),
        ("skills", "Skill", "Id"),
        ("quests", "Quest", "Id"),
        ("merchants", "MerchantEvidence", "Id")
    ];

    private static readonly HashSet<(string Table, string Column)> ProductionDisplayColumns = new()
    {
        ("maps", "Name"), ("maps", "NameZhTw"),
        ("npcs", "Name"), ("npcs", "NameZhTw"),
        ("monsters", "Name"), ("monsters", "NameZhTw"),
        ("items", "Name"), ("items", "DisplayName"), ("items", "NameZhTw"), ("items", "DescriptionZhTw"),
        ("skills", "Name"), ("skills", "NameZhTw"), ("skills", "DescriptionZhTw"),
        ("quests", "Name"), ("quests", "NameZhTw"), ("quests", "DescriptionZhTw"),
        ("merchants", "Name"), ("merchants", "NameZhTw"),
        ("localization_entries", "TextValue"),
        ("npc_coordinate_evidence", "NpcNameZhTw"), ("npc_coordinate_evidence", "MapNameZhTw"),
        ("skill_content_profiles", "NameZhTw"),
        ("quest_content_profiles", "NameZhTw"), ("quest_content_profiles", "DescriptionZhTw"), ("quest_content_profiles", "RewardTextZhTw"),
        ("quest_objective_candidates", "ObjectiveTextZhTw"),
        ("equipment_set_definitions", "NameZhTw"), ("equipment_set_definitions", "EffectsZhTw"),
        ("pet_content_profiles", "NameZhTw"), ("pet_innate_definitions", "NameZhTw"), ("pet_innate_definitions", "ArtifactNameZhTw"), ("pet_innate_definitions", "DescriptionZhTw"),
        ("pet_egg_relationships", "HatchResultNameZhTw")
    };

    private static readonly IReadOnlyDictionary<string, string> ProductionIdentityColumns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["maps"] = "Id",
        ["npcs"] = "Id",
        ["monsters"] = "Id",
        ["items"] = "Id",
        ["skills"] = "Id",
        ["quests"] = "Id",
        ["merchants"] = "Id",
        ["localization_entries"] = "TextKey",
        ["npc_coordinate_evidence"] = "CoordinateEvidenceId",
        ["skill_content_profiles"] = "ProfileId",
        ["quest_content_profiles"] = "ProfileId",
        ["quest_objective_candidates"] = "ObjectiveId",
        ["equipment_set_definitions"] = "SetId",
        ["pet_content_profiles"] = "ProfileId",
        ["pet_innate_definitions"] = "InnateId",
        ["pet_egg_relationships"] = "RelationshipId"
    };

    private readonly DatabaseOptions _options;
    private readonly string _schemaDirectory;
    private readonly ZhTwLocalization _localization;

    public MariaDbContentRecoveryStore(DatabaseOptions options, string schemaDirectory, ZhTwLocalization localization)
    {
        _options = options;
        _schemaDirectory = schemaDirectory;
        _localization = localization;
    }

    public async Task<IReadOnlyList<ProductionSimplifiedFinding>> FindProductionSimplifiedAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(BuildConnectionString());
        await connection.OpenAsync(cancellationToken);
        var findings = new List<ProductionSimplifiedFinding>();
        foreach (var column in ProductionDisplayColumns.OrderBy(value => value.Table, StringComparer.Ordinal).ThenBy(value => value.Column, StringComparer.Ordinal))
        {
            if (!await ColumnExistsAsync(connection, column.Table, column.Column, cancellationToken))
            {
                continue;
            }

            if (!ProductionIdentityColumns.TryGetValue(column.Table, out var identityColumn) ||
                !await ColumnExistsAsync(connection, column.Table, identityColumn, cancellationToken))
            {
                throw new InvalidOperationException($"Production display-text identity column is missing: {column.Table}.{identityColumn ?? "<unmapped>"}");
            }

            var nameColumn = await ColumnExistsAsync(connection, column.Table, "NameZhTw", cancellationToken) ? "`NameZhTw`" : "NULL";
            var originalNameColumn = await ColumnExistsAsync(connection, column.Table, "OriginalName", cancellationToken) ? "`OriginalName`" : "NULL";
            var localizationStatusColumn = await ColumnExistsAsync(connection, column.Table, "LocalizationStatus", cancellationToken) ? "`LocalizationStatus`" : "NULL";
            var runColumn = await ColumnExistsAsync(connection, column.Table, "ContentRecoveryRunId", cancellationToken) ? "`ContentRecoveryRunId`" : "NULL";
            var detailColumns = $", {nameColumn}, {originalNameColumn}, {localizationStatusColumn}, {runColumn}";
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT CAST(`{identityColumn}` AS CHAR), `{column.Column}`{detailColumns} FROM `{column.Table}` WHERE `{column.Column}` IS NOT NULL;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var value = reader.GetString(1);
                if (_localization.ContainsConvertibleSimplified(value))
                {
                    findings.Add(new ProductionSimplifiedFinding(
                        column.Table,
                        column.Column,
                        reader.GetString(0),
                        value,
                        _localization.Convert(value, ContentHash.Sha256(value)).ConvertedText,
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5)));
                }
            }
        }

        return findings;
    }

    public async Task<DatabaseImportResult> ImportAsync(RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        var migration = await new SqlFileMigrationRunner(_options, _schemaDirectory).VerifyAsync(cancellationToken);
        if (!migration.Succeeded)
        {
            throw new InvalidOperationException($"MariaDB migration failed: {migration.Error.Code}: {migration.Error.Message}");
        }

        var requiredMigration = workspace.Phase == RecoveryVersions.Phase2 ? "033" : "031";
        var currentMigration = migration.Value?.Migrations.FirstOrDefault(execution => execution.Version == requiredMigration);
        var migrationStatus = currentMigration?.Status.ToString() ?? "Current";
        await using var connection = new MySqlConnection(BuildConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var recoveryLock = await MariaDbAdvisoryLock.AcquireAsync(
            connection,
            $"god2-content-recovery-{workspace.Phase}",
            cancellationToken);
        var serverVersion = connection.ServerVersion;
        await InsertRunAsync(connection, workspace, cancellationToken);
        workspace.DatabaseBefore.Clear();
        foreach (var pair in await ReadFormalCountsAsync(connection, cancellationToken))
        {
            workspace.DatabaseBefore[pair.Key] = pair.Value;
        }

        var updated = 0;
        var localizationUpserts = 0;
        var verifiedGuideFieldsPromoted = 0;
        IReadOnlyDictionary<string, object?> phase2Metrics = new Dictionary<string, object?>(StringComparer.Ordinal);
        await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
        {
            try
            {
                await InsertGlossaryAsync(connection, transaction, _localization.Glossary, cancellationToken);
                await InsertSourcesAsync(connection, transaction, workspace, cancellationToken);
                await InsertRawAsync(connection, transaction, workspace, cancellationToken);
                await InsertStagingAsync(connection, transaction, workspace, cancellationToken);
                await InsertLocalizationAuditAsync(connection, transaction, workspace, cancellationToken);
                await InsertValidatedAsync(connection, transaction, workspace, cancellationToken);
                if (workspace.Phase == RecoveryVersions.Phase2)
                {
                    phase2Metrics = await Phase2MariaDbPromoter.PromoteAsync(connection, transaction, workspace, _localization, cancellationToken);
                }
                else
                {
                    (updated, localizationUpserts) = await NormalizeFormalDisplayTextAsync(connection, transaction, workspace, cancellationToken);
                    verifiedGuideFieldsPromoted = await ApplyVerifiedGuideFieldsAsync(connection, transaction, workspace, cancellationToken);
                }
                await InsertValidationAsync(connection, transaction, workspace, cancellationToken);
                if (workspace.Phase != RecoveryVersions.Phase2)
                {
                    await NormalizeExistingLocalizationAsync(connection, transaction, workspace, cancellationToken);
                }
                await InsertProductionManifestAsync(connection, transaction, workspace, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                await UpdateRunStatusAsync(connection, workspace.RunId, "FAILED", null, cancellationToken);
                throw;
            }
        }

        workspace.DatabaseAfter.Clear();
        foreach (var pair in await ReadFormalCountsAsync(connection, cancellationToken))
        {
            workspace.DatabaseAfter[pair.Key] = pair.Value;
        }

        var columns = await InventoryLocalizationColumnsAsync(connection, cancellationToken);
        var referential = await ReadReferentialIntegrityAsync(connection, cancellationToken);
        var productionCompleteness = await ReadProductionCompletenessAsync(connection, cancellationToken);
        var simplified = await CountProductionSimplifiedAsync(connection, cancellationToken);
        var runtime = await ValidateRuntimeAsync(connection, workspace.RunId, cancellationToken);
        var headless = await ValidateHeadlessAsync(connection, workspace.RunId, cancellationToken);
        var brokenForeignKeys = referential.Where(pair => pair.Key.StartsWith("Broken", StringComparison.Ordinal)).Sum(pair => pair.Value);
        var contentOrphans = referential.Where(pair => pair.Key.Contains("Orphan", StringComparison.Ordinal)).Sum(pair => pair.Value);
        var summary = RecoveryJson.Compact(new
        {
            updated,
            localizationUpserts,
            verifiedGuideFieldsPromoted,
            phase2Metrics,
            productionSimplifiedDisplayRows = simplified,
            brokenForeignKeys,
            contentOrphans,
            runtime,
            headless
        });
        var validationPassed = simplified == 0 && brokenForeignKeys == 0 && contentOrphans == 0 &&
            string.Equals(runtime.GetValueOrDefault("mariaDbLoad")?.ToString(), "PASS", StringComparison.Ordinal) &&
            string.Equals(runtime.GetValueOrDefault("runtimeCatalogBuild")?.ToString(), "PASS", StringComparison.Ordinal) &&
            string.Equals(runtime.GetValueOrDefault("gameplayCatalogValidation")?.ToString(), "PASS", StringComparison.Ordinal) &&
            string.Equals(runtime.GetValueOrDefault("phase2SpecializedValidation")?.ToString(), "PASS", StringComparison.Ordinal) &&
            string.Equals(headless.GetValueOrDefault("status")?.ToString(), "PASS", StringComparison.Ordinal);
        await UpdateRunStatusAsync(connection, workspace.RunId, validationPassed ? "COMPLETED" : "FAILED_VALIDATION_GATE", summary, cancellationToken);

        return new DatabaseImportResult(
            serverVersion,
            _options.DatabaseName,
            "MariaDbRuntimeRepositories + MySqlConnector physical content import",
            migrationStatus,
            columns,
            referential,
            productionCompleteness,
            runtime,
            headless,
            simplified,
            brokenForeignKeys,
            contentOrphans,
            updated,
            localizationUpserts,
            verifiedGuideFieldsPromoted,
            phase2Metrics);
    }

    private static async Task<IReadOnlyDictionary<string, long>> ReadProductionCompletenessAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MapsTotal"] = "SELECT COUNT(*) FROM `maps`;",
            ["MapsVerified"] = "SELECT COUNT(*) FROM `maps` WHERE `EvidenceStatus` IN ('Verified','Derived') AND `NameZhTw` IS NOT NULL;",
            ["NpcTemplatesTotal"] = "SELECT COUNT(*) FROM `npcs`;",
            ["NpcCoordinatesComplete"] = "SELECT COUNT(*) FROM `npcs` WHERE `MapId` IS NOT NULL AND `PositionX` IS NOT NULL AND `PositionY` IS NOT NULL;",
            ["MonsterTemplatesTotal"] = "SELECT COUNT(*) FROM `monsters`;",
            ["MonsterSpawnTemplatesComplete"] = "SELECT COUNT(DISTINCT `monster_id`) FROM `god2_game`.`monster_spawns`;",
            ["MonsterLevelComplete"] = "SELECT COUNT(*) FROM `monsters` WHERE `Level` IS NOT NULL;",
            ["MonsterHpComplete"] = "SELECT COUNT(*) FROM `monsters` WHERE `MaxHp` IS NOT NULL AND `MaxHp` > 0;",
            ["MonsterMpPolicyComplete"] = "SELECT COUNT(*) FROM `monsters` WHERE `MpPolicy` <> 'Unknown';",
            ["MonsterCombatStatsComplete"] = "SELECT COUNT(*) FROM `monsters` WHERE `Attack` IS NOT NULL AND `Defense` IS NOT NULL;",
            ["MonsterRewardComplete"] = "SELECT COUNT(*) FROM `monsters` WHERE `ExperienceReward` IS NOT NULL AND `CurrencyRewardMinimum` IS NOT NULL AND `CurrencyRewardMaximum` IS NOT NULL;",
            ["MonsterDropPolicyComplete"] = "SELECT COUNT(*) FROM `monsters` WHERE `DropPolicy` <> 'Unknown';",
            ["MonsterDropTableComplete"] = "SELECT COUNT(DISTINCT dt.`MonsterId`) FROM `drop_tables` dt INNER JOIN `drop_table_items` dti ON dti.`DropTableId`=dt.`Id`;",
            ["DropEntriesTotal"] = "SELECT COUNT(*) FROM `drop_table_items`;",
            ["DropItemReferencesComplete"] = "SELECT COUNT(*) FROM `drop_table_items` dti INNER JOIN `items` i ON i.`Id`=dti.`ItemId`;",
            ["SkillsTotal"] = "SELECT COUNT(*) FROM `skills`;",
            ["SkillsClassified"] = "SELECT COUNT(*) FROM `skills` WHERE `SkillFamily` IS NOT NULL;",
            ["SkillTargetPolicyComplete"] = "SELECT COUNT(*) FROM `skills` WHERE `TargetPolicy` IS NOT NULL;",
            ["SkillMpCostPolicyComplete"] = "SELECT COUNT(*) FROM `skills` WHERE `MpCostPolicy` <> 'Unknown';",
            ["ItemsTotal"] = "SELECT COUNT(*) FROM `items`;",
            ["ItemsRequiredByDrops"] = "SELECT COUNT(DISTINCT `ItemId`) FROM `drop_table_items`;",
            ["Phase2ItemProfiles"] = "SELECT COUNT(*) FROM `item_content_profiles`;",
            ["Phase2NpcCoordinateEvidence"] = "SELECT COUNT(*) FROM `god2_research`.`npc_coordinate_evidence`;",
            ["Phase2MonsterDropRelationships"] = "SELECT COUNT(*) FROM `monster_drop_relationships`;",
            ["Phase2DropItems"] = "SELECT COUNT(DISTINCT `ItemId`) FROM `monster_drop_relationships`;",
            ["Phase2DropMonsters"] = "SELECT COUNT(DISTINCT `MonsterId`) FROM `monster_drop_relationships`;",
            ["Phase2SkillProfiles"] = "SELECT COUNT(*) FROM `skill_content_profiles`;",
            ["Phase2MerchantCandidates"] = "SELECT COUNT(*) FROM `god2_research`.`merchant_inventory_candidates`;",
            ["Phase2QuestProfiles"] = "SELECT COUNT(*) FROM `quest_content_profiles`;",
            ["Phase2QuestObjectives"] = "SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates`;",
            ["Phase2EquipmentSets"] = "SELECT COUNT(*) FROM `equipment_set_definitions`;",
            ["Phase2EquipmentSetMembers"] = "SELECT COUNT(*) FROM `equipment_set_members`;",
            ["Phase2ContainerRelationships"] = "SELECT COUNT(*) FROM `container_item_relationships`;",
            ["Phase2PetProfiles"] = "SELECT COUNT(*) FROM `pet_content_profiles`;",
            ["Phase2PetInnates"] = "SELECT COUNT(*) FROM `pet_innate_definitions`;"
        };
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var query in queries)
        {
            result[query.Key] = await ScalarAsync(connection, query.Value, cancellationToken);
        }

        return result;
    }

    private async Task<int> ApplyVerifiedGuideFieldsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        RecoveryWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var monsterIds = await BuildFormalAuthorityMapAsync(connection, transaction, workspace, "monsters", "MonsterTemplate", cancellationToken);
        var skillIds = await BuildFormalAuthorityMapAsync(connection, transaction, workspace, "skills", "Skill", cancellationToken);
        var promoted = 0;

        foreach (var evidence in workspace.Entities.Where(entity =>
                     entity.Domain == "MonsterStatEvidence" &&
                     entity.EvidenceStatus == "Derived" &&
                     IsCurrentPatchVerified(entity)))
        {
            var identities = GetStringArray(evidence.Fields, "monsterTemplateReferences")
                .Where(monsterIds.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (identities.Length != 1)
            {
                continue;
            }

            var id = monsterIds[identities[0]];
            if (GetLong(evidence.Fields, "levelCandidate") is { } level && level >= 0 &&
                await PromoteNumericFieldAsync(connection, transaction, workspace, evidence, "monsters", id, "Level", level, cancellationToken))
            {
                promoted++;
            }

            if (GetLong(evidence.Fields, "maxHpCandidate") is { } hp && hp > 0 &&
                await PromoteNumericFieldAsync(connection, transaction, workspace, evidence, "monsters", id, "MaxHp", hp, cancellationToken))
            {
                promoted++;
            }
        }

        foreach (var evidence in workspace.Entities.Where(entity =>
                     entity.Domain == "SkillNumericEvidence" &&
                     entity.EvidenceStatus == "Derived" &&
                     IsCurrentPatchVerified(entity)))
        {
            var identities = GetStringArray(evidence.Fields, "officialClientReferences")
                .Where(skillIds.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (identities.Length != 1)
            {
                continue;
            }

            var id = skillIds[identities[0]];
            if (GetLong(evidence.Fields, "mpCost") is { } mp && mp >= 0 &&
                await PromoteNumericFieldAsync(connection, transaction, workspace, evidence, "skills", id, "MpCost", mp, cancellationToken))
            {
                promoted++;
                if (await PromoteTextFieldAsync(connection, transaction, workspace, evidence, "skills", id, "MpCostPolicy", "Fixed", cancellationToken))
                {
                    promoted++;
                }
            }

            if (evidence.Fields.TryGetValue("targetPolicy", out var targetValue) && targetValue is string targetPolicy && !string.IsNullOrWhiteSpace(targetPolicy) &&
                await PromoteTextFieldAsync(connection, transaction, workspace, evidence, "skills", id, "TargetPolicy", targetPolicy, cancellationToken))
            {
                promoted++;
            }
        }

        workspace.Diagnostics["verifiedGuideFieldsPromoted"] = promoted;
        return promoted;
    }

    private static async Task<Dictionary<string, long>> BuildFormalAuthorityMapAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        RecoveryWorkspace workspace,
        string table,
        string domain,
        CancellationToken cancellationToken)
    {
        var entities = workspace.Entities.Where(entity => entity.Domain == domain).ToArray();
        var exact = entities.GroupBy(entity => entity.AuthorityKey, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var bySourceRow = entities.Where(entity => entity.SourceRow is not null)
            .GroupBy(entity => (NormalizePath(entity.SourceFile), entity.SourceRow!.Value))
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in await ReadFormalRowsAsync(connection, transaction, table, "Id", cancellationToken))
        {
            var entity = MatchEntity(row.PayloadJson, exact, bySourceRow);
            if (entity is null || ambiguous.Contains(entity.AuthorityKey))
            {
                continue;
            }

            if (!result.TryAdd(entity.AuthorityKey, row.Id))
            {
                result.Remove(entity.AuthorityKey);
                ambiguous.Add(entity.AuthorityKey);
            }
        }

        return result;
    }

    private static async Task<bool> PromoteNumericFieldAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        RecoveryWorkspace workspace,
        ContentEntity evidence,
        string table,
        long id,
        string column,
        long value,
        CancellationToken cancellationToken)
    {
        var existing = await ReadFieldAsync(connection, transaction, table, id, column, cancellationToken);
        if (existing is not null && Convert.ToInt64(existing, System.Globalization.CultureInfo.InvariantCulture) != value)
        {
            AddGuideConflict(workspace, evidence, table, id, column, existing, value);
            return false;
        }

        if (existing is null)
        {
            await UpdateFieldAsync(connection, transaction, table, id, column, value, cancellationToken);
        }

        AddGuidePromotion(workspace, evidence, table, id, column, value);
        return true;
    }

    private static async Task<bool> PromoteTextFieldAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        RecoveryWorkspace workspace,
        ContentEntity evidence,
        string table,
        long id,
        string column,
        string value,
        CancellationToken cancellationToken)
    {
        var existing = await ReadFieldAsync(connection, transaction, table, id, column, cancellationToken);
        var existingText = existing?.ToString();
        var isUnknown = string.IsNullOrWhiteSpace(existingText) || string.Equals(existingText, "Unknown", StringComparison.Ordinal);
        if (!isUnknown && !string.Equals(existingText, value, StringComparison.Ordinal))
        {
            AddGuideConflict(workspace, evidence, table, id, column, existingText, value);
            return false;
        }

        if (isUnknown)
        {
            await UpdateFieldAsync(connection, transaction, table, id, column, value, cancellationToken);
        }

        AddGuidePromotion(workspace, evidence, table, id, column, value);
        return true;
    }

    private static async Task<object?> ReadFieldAsync(MySqlConnection connection, MySqlTransaction transaction, string table, long id, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT `{column}` FROM `{table}` WHERE `Id`=@id FOR UPDATE;";
        command.Parameters.AddWithValue("@id", id);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DBNull ? null : value;
    }

    private static async Task UpdateFieldAsync(MySqlConnection connection, MySqlTransaction transaction, string table, long id, string column, object value, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE `{table}` SET `{column}`=@value WHERE `Id`=@id;";
        command.Parameters.AddWithValue("@value", value);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddGuidePromotion(RecoveryWorkspace workspace, ContentEntity evidence, string table, long id, string column, object value) =>
        workspace.Promotions.Add(new ProductionPromotion(
            evidence.Domain,
            $"{evidence.AuthorityKey}:{column}",
            table,
            $"{id}:{column}",
            ContentHash.Sha256(value.ToString() ?? string.Empty),
            "Derived",
            "NotApplicable"));

    private static void AddGuideConflict(RecoveryWorkspace workspace, ContentEntity evidence, string table, long id, string column, object? existing, object candidate) =>
        workspace.Validation.Add(new ValidationFinding(
            ContentHash.StableId(evidence.AuthorityKey, table, id, column, "conflict"),
            evidence.Domain,
            evidence.AuthorityKey,
            "VerifiedGuideConflict",
            "FAIL",
            "EvidenceGap",
            $"Existing authoritative {table}.{column} value '{existing}' conflicts with verified-guide candidate '{candidate}'; existing value was preserved."));

    private static IReadOnlyList<string> GetStringArray(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value) && value is IEnumerable<string> values ? values.ToArray() : [];

    private static long? GetLong(IReadOnlyDictionary<string, object?> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int integer => integer,
            long integer => integer,
            JsonElement element when element.TryGetInt64(out var integer) => integer,
            _ when long.TryParse(value.ToString(), out var integer) => integer,
            _ => null
        };
    }

    private static bool IsCurrentPatchVerified(ContentEntity evidence) =>
        evidence.Fields.TryGetValue("currentPatchVerified", out var value) && value is true;

    private string BuildConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = _options.Host,
            Port = (uint)_options.Port,
            Database = _options.DatabaseName,
            UserID = _options.Username,
            Password = _options.Password,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = (uint)Math.Max(1, _options.ConnectionTimeoutSeconds),
            DefaultCommandTimeout = 120,
            Pooling = true,
            SslMode = MySqlSslMode.Preferred
        };
        return builder.ConnectionString;
    }

    private static async Task InsertRunAsync(MySqlConnection connection, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `content_recovery_runs`
                (`RunId`, `Phase`, `Status`, `ClientRootIdentity`, `ExtractorVersion`, `ConverterName`, `ConverterVersion`, `GlossaryVersion`, `StartedAtUtc`)
            VALUES (@runId, @phase, 'RUNNING', 'OfficialClient:God2', @extractor, @converter, @converterVersion, @glossary, @started);
            """;
        command.Parameters.AddWithValue("@runId", workspace.RunId);
        command.Parameters.AddWithValue("@phase", workspace.Phase);
        command.Parameters.AddWithValue("@extractor", workspace.ExtractorVersion);
        command.Parameters.AddWithValue("@converter", RecoveryVersions.ConverterName);
        command.Parameters.AddWithValue("@converterVersion", RecoveryVersions.ConverterVersion);
        command.Parameters.AddWithValue("@glossary", RecoveryVersions.GlossaryVersion);
        command.Parameters.AddWithValue("@started", workspace.StartedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertGlossaryAsync(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<GlossaryEntry> glossary, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO `god2_research`.`content_localization_glossary`
                (`GlossaryVersion`, `SimplifiedText`, `TraditionalText`, `Category`, `Source`, `SourceIdentity`, `Confidence`, `VerifiedBy`, `Notes`)
            VALUES (@version, @simplified, @traditional, @category, @source, @identity, @confidence, @verifiedBy, @notes)
            ON DUPLICATE KEY UPDATE `TraditionalText`=VALUES(`TraditionalText`), `Source`=VALUES(`Source`),
                `SourceIdentity`=VALUES(`SourceIdentity`), `Confidence`=VALUES(`Confidence`), `VerifiedBy`=VALUES(`VerifiedBy`), `Notes`=VALUES(`Notes`);
            """, "@version", "@simplified", "@traditional", "@category", "@source", "@identity", "@confidence", "@verifiedBy", "@notes");
        foreach (var entry in glossary)
        {
            Set(command, "@version", entry.GlossaryVersion);
            Set(command, "@simplified", entry.SimplifiedText);
            Set(command, "@traditional", entry.TraditionalText);
            Set(command, "@category", entry.Category);
            Set(command, "@source", entry.Source);
            Set(command, "@identity", entry.SourceIdentity);
            Set(command, "@confidence", Truncate(entry.Confidence, 32));
            Set(command, "@verifiedBy", entry.VerifiedBy);
            Set(command, "@notes", entry.Notes);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertSourcesAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO `god2_research`.`content_source_inventory`
                (`RunId`, `SourceIdentity`, `SourceType`, `SourceFile`, `SourceHash`, `SizeBytes`, `RecordCount`, `Decoder`, `ContentClassification`, `ScannedAtUtc`)
            VALUES (@runId, @identity, @type, @file, @hash, @size, @records, @decoder, @classification, UTC_TIMESTAMP(6));
            """, "@runId", "@identity", "@type", "@file", "@hash", "@size", "@records", "@decoder", "@classification");
        foreach (var entry in workspace.Sources)
        {
            Set(command, "@runId", workspace.RunId);
            Set(command, "@identity", RecoveryDatabaseIdentity.ForStorage(entry.SourceIdentity));
            Set(command, "@type", entry.SourceType);
            Set(command, "@file", Truncate(entry.SourceFile, 768));
            Set(command, "@hash", entry.SourceHash);
            Set(command, "@size", entry.SizeBytes);
            Set(command, "@records", entry.RecordCount);
            Set(command, "@decoder", entry.Decoder);
            Set(command, "@classification", entry.ContentClassification);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertRawAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO `god2_research`.`content_raw_records`
                (`RunId`, `RecordId`, `Domain`, `SourceType`, `SourceFile`, `SourceIdentity`, `SourceRow`, `SourceOffset`, `SourceHash`,
                 `ExtractorVersion`, `ExtractedAtUtc`, `OriginalLanguage`, `OriginalText`, `RawMetadata`, `SourcePayloadHash`, `Confidence`, `EvidenceStatus`)
            VALUES (@runId, @id, @domain, @type, @file, @identity, @row, @offset, @hash, @extractor, UTC_TIMESTAMP(6),
                    @language, @text, @metadata, @payloadHash, @confidence, @evidence);
            """, "@runId", "@id", "@domain", "@type", "@file", "@identity", "@row", "@offset", "@hash", "@extractor", "@language", "@text", "@metadata", "@payloadHash", "@confidence", "@evidence");
        foreach (var entry in workspace.Raw)
        {
            Set(command, "@runId", workspace.RunId);
            Set(command, "@id", entry.RecordId);
            Set(command, "@domain", entry.Domain);
            Set(command, "@type", entry.SourceType);
            Set(command, "@file", Truncate(entry.SourceFile, 768));
            Set(command, "@identity", RecoveryDatabaseIdentity.ForStorage(entry.SourceIdentity));
            Set(command, "@row", entry.SourceRow);
            Set(command, "@offset", entry.SourceOffset);
            Set(command, "@hash", entry.SourceHash);
            Set(command, "@extractor", workspace.ExtractorVersion);
            Set(command, "@language", entry.OriginalLanguage);
            Set(command, "@text", entry.OriginalText);
            Set(command, "@metadata", entry.RawMetadata);
            Set(command, "@payloadHash", entry.SourcePayloadHash);
            Set(command, "@confidence", Truncate(entry.Confidence, 32));
            Set(command, "@evidence", entry.EvidenceStatus);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertStagingAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO `god2_research`.`content_staging_records`
                (`RunId`, `StagingId`, `Domain`, `AuthorityKey`, `OriginalText`, `OriginalLanguage`, `ConvertedText`, `TargetLanguage`,
                 `ConversionMethod`, `ConverterVersion`, `GlossaryVersion`, `SourceHash`, `ConvertedTextHash`, `ConversionStatus`,
                 `LanguageReviewStatus`, `TransformationRule`, `NormalizedData`, `OpaqueMetadata`, `EvidenceStatus`, `StagedAtUtc`)
            VALUES (@runId, @id, @domain, @authority, @original, @language, @converted, 'zh-TW', @method, @converter, @glossary,
                    @sourceHash, @convertedHash, @status, @review, @rule, @normalized, @opaque, @evidence, UTC_TIMESTAMP(6));
            """, "@runId", "@id", "@domain", "@authority", "@original", "@language", "@converted", "@method", "@converter", "@glossary", "@sourceHash", "@convertedHash", "@status", "@review", "@rule", "@normalized", "@opaque", "@evidence");
        foreach (var entry in workspace.Staging)
        {
            Set(command, "@runId", workspace.RunId);
            Set(command, "@id", entry.StagingId);
            Set(command, "@domain", entry.Domain);
            Set(command, "@authority", Truncate(entry.AuthorityKey, 191));
            Set(command, "@original", entry.OriginalText);
            Set(command, "@language", entry.OriginalLanguage);
            Set(command, "@converted", entry.ConvertedText);
            Set(command, "@method", Truncate(entry.ConversionMethod, 64));
            Set(command, "@converter", RecoveryVersions.ConverterVersion);
            Set(command, "@glossary", RecoveryVersions.GlossaryVersion);
            Set(command, "@sourceHash", entry.SourceHash);
            Set(command, "@convertedHash", entry.ConvertedTextHash);
            Set(command, "@status", entry.ConversionStatus);
            Set(command, "@review", entry.LanguageReviewStatus);
            Set(command, "@rule", Truncate(entry.TransformationRule, 128));
            Set(command, "@normalized", entry.NormalizedData);
            Set(command, "@opaque", entry.OpaqueMetadata);
            Set(command, "@evidence", entry.EvidenceStatus);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertLocalizationAuditAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO `god2_research`.`content_localization_audit`
                (`RunId`, `LocalizationId`, `Domain`, `AuthorityKey`, `FieldName`, `OriginalText`, `OriginalLanguage`, `ConvertedText`,
                 `TargetLanguage`, `ConversionMethod`, `ConverterVersion`, `GlossaryVersion`, `SourceHash`, `ConvertedTextHash`,
                 `ConversionStatus`, `LanguageReviewStatus`, `PlaceholderPreserved`, `MarkupPreserved`, `ControlCodesPreserved`, `ConvertedAtUtc`)
            VALUES (@runId, @id, @domain, @authority, @field, @original, @language, @converted, 'zh-TW', @method,
                    @converter, @glossary, @sourceHash, @convertedHash, @status, @review, @placeholder, @markup, @controls, UTC_TIMESTAMP(6));
            """, "@runId", "@id", "@domain", "@authority", "@field", "@original", "@language", "@converted", "@method", "@converter", "@glossary", "@sourceHash", "@convertedHash", "@status", "@review", "@placeholder", "@markup", "@controls");
        foreach (var entry in workspace.Localization)
        {
            Set(command, "@runId", workspace.RunId);
            Set(command, "@id", entry.LocalizationId);
            Set(command, "@domain", entry.Domain);
            Set(command, "@authority", Truncate(entry.AuthorityKey, 512));
            Set(command, "@field", entry.FieldName);
            Set(command, "@original", entry.Result.OriginalText);
            Set(command, "@language", entry.Result.OriginalLanguage);
            Set(command, "@converted", entry.Result.ConvertedText);
            Set(command, "@method", Truncate(entry.Result.ConversionMethod, 64));
            Set(command, "@converter", RecoveryVersions.ConverterVersion);
            Set(command, "@glossary", RecoveryVersions.GlossaryVersion);
            Set(command, "@sourceHash", entry.Result.SourceHash);
            Set(command, "@convertedHash", entry.Result.ConvertedTextHash);
            Set(command, "@status", entry.Result.ConversionStatus);
            Set(command, "@review", entry.Result.LanguageReviewStatus);
            Set(command, "@placeholder", entry.Result.PlaceholderPreserved);
            Set(command, "@markup", entry.Result.MarkupPreserved);
            Set(command, "@controls", entry.Result.ControlCodesPreserved);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertValidationAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO `god2_research`.`content_validation_results`
                (`RunId`, `ValidationId`, `Domain`, `AuthorityKey`, `Gate`, `Status`, `Severity`, `Details`, `ValidatedAtUtc`)
            VALUES (@runId, @id, @domain, @authority, @gate, @status, @severity, @details, UTC_TIMESTAMP(6));
            """, "@runId", "@id", "@domain", "@authority", "@gate", "@status", "@severity", "@details");
        foreach (var entry in workspace.Validation)
        {
            Set(command, "@runId", workspace.RunId);
            Set(command, "@id", entry.ValidationId);
            Set(command, "@domain", entry.Domain);
            Set(command, "@authority", Truncate(entry.AuthorityKey, 512));
            Set(command, "@gate", entry.Gate);
            Set(command, "@status", entry.Status);
            Set(command, "@severity", entry.Severity);
            Set(command, "@details", Truncate(entry.Details, 2048));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertValidatedAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO `god2_research`.`content_validated_records`
                (`RunId`, `ValidatedId`, `Domain`, `AuthorityKey`, `NormalizedData`, `NormalizedHash`, `EvidenceStatus`, `LocalizationStatus`, `SourceHash`, `ValidatedAtUtc`)
            VALUES (@runId, @id, @domain, @authority, @normalized, @hash, @evidence, @localization, @sourceHash, UTC_TIMESTAMP(6));
            """, "@runId", "@id", "@domain", "@authority", "@normalized", "@hash", "@evidence", "@localization", "@sourceHash");
        foreach (var entry in workspace.Validated)
        {
            Set(command, "@runId", workspace.RunId);
            Set(command, "@id", entry.ValidatedId);
            Set(command, "@domain", entry.Domain);
            Set(command, "@authority", Truncate(entry.AuthorityKey, 512));
            Set(command, "@normalized", entry.NormalizedData);
            Set(command, "@hash", entry.NormalizedHash);
            Set(command, "@evidence", entry.EvidenceStatus);
            Set(command, "@localization", entry.LocalizationStatus);
            Set(command, "@sourceHash", entry.SourceHash);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<(int Updated, int LocalizationUpserts)> NormalizeFormalDisplayTextAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        RecoveryWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var updated = 0;
        var localizationUpserts = 0;
        foreach (var table in FormalNamedTables)
        {
            var entities = workspace.Entities.Where(entity => entity.Domain == table.Domain).ToArray();
            var exact = entities.GroupBy(entity => entity.AuthorityKey, StringComparer.Ordinal).Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
            var bySourceRow = entities.Where(entity => entity.SourceRow is not null)
                .GroupBy(entity => (NormalizePath(entity.SourceFile), entity.SourceRow!.Value))
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());

            var rows = await ReadFormalRowsAsync(connection, transaction, table.Table, table.IdColumn, cancellationToken);
            foreach (var row in rows)
            {
                ContentEntity? matched = MatchEntity(row.PayloadJson, exact, bySourceRow);
                var sourceHash = matched?.SourceHash ?? row.PayloadSha256 ?? ContentHash.Sha256(row.Name);
                var originalName = matched?.Name ?? row.Name;
                var name = _localization.Convert(originalName, sourceHash);
                var originalDescription = matched?.Description;
                var description = string.IsNullOrWhiteSpace(originalDescription) ? null : _localization.Convert(originalDescription, sourceHash);
                if ((name.ConversionStatus == "ConversionFailed" || description?.ConversionStatus == "ConversionFailed") && matched is not null)
                {
                    matched = null;
                    sourceHash = row.PayloadSha256 ?? ContentHash.Sha256(row.Name);
                    originalName = row.Name;
                    name = _localization.Convert(originalName, sourceHash);
                    originalDescription = null;
                    description = null;
                }

                if (name.ConversionStatus == "ConversionFailed" || description?.ConversionStatus == "ConversionFailed")
                {
                    continue;
                }

                var evidenceStatus = matched is not null && matched.MissingRequiredFields.Count == 0 ? matched.EvidenceStatus : "EvidenceBlocked";
                await UpdateFormalRowAsync(connection, transaction, table.Table, row.Id, originalName, name, originalDescription, description, evidenceStatus, workspace.RunId, cancellationToken);
                var key = $"content.{table.Table}.{row.Id}.name";
                await UpsertLocalizationAsync(connection, transaction, key, originalName, name, workspace.RunId, cancellationToken);
                localizationUpserts++;
                if (description is not null && originalDescription is not null)
                {
                    await UpsertLocalizationAsync(connection, transaction, $"content.{table.Table}.{row.Id}.description", originalDescription, description, workspace.RunId, cancellationToken);
                    localizationUpserts++;
                }

                workspace.Promotions.Add(new ProductionPromotion(
                    table.Domain,
                    matched?.AuthorityKey ?? $"existing:{table.Table}:{row.Id}",
                    table.Table,
                    row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ContentHash.Sha256(RecoveryJson.Compact(new { name = name.ConvertedText, description = description?.ConvertedText })),
                    evidenceStatus,
                    name.ConversionStatus));
                updated++;
            }
        }

        return (updated, localizationUpserts);
    }

    private async Task NormalizeExistingLocalizationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        RecoveryWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var rows = new List<(string Language, string Key, string Value)>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT `Language`, `TextKey`, `TextValue` FROM `localization_entries` ORDER BY `Language`, `TextKey`;";
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        foreach (var row in rows)
        {
            var sourceHash = ContentHash.Sha256(row.Value);
            var converted = _localization.Convert(row.Value, sourceHash);
            if (converted.ConversionStatus == "ConversionFailed")
            {
                continue;
            }

            await UpsertLocalizationAsync(connection, transaction, row.Key, row.Value, converted, workspace.RunId, cancellationToken);
            if (!string.Equals(row.Language, "zh-TW", StringComparison.Ordinal))
            {
                await using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM `localization_entries` WHERE `Language`=@language AND `TextKey`=@key;";
                delete.Parameters.AddWithValue("@language", row.Language);
                delete.Parameters.AddWithValue("@key", row.Key);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static async Task InsertProductionManifestAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO `god2_research`.`content_production_manifest`
                (`Domain`, `AuthorityKey`, `RunId`, `TargetTable`, `TargetRowIdentity`, `NormalizedHash`, `EvidenceStatus`, `LocalizationStatus`, `PromotedAtUtc`)
            VALUES (@domain, @authority, @runId, @table, @row, @hash, @evidence, @localization, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`), `TargetTable`=VALUES(`TargetTable`), `TargetRowIdentity`=VALUES(`TargetRowIdentity`),
                `NormalizedHash`=VALUES(`NormalizedHash`), `EvidenceStatus`=VALUES(`EvidenceStatus`),
                `LocalizationStatus`=VALUES(`LocalizationStatus`), `PromotedAtUtc`=UTC_TIMESTAMP(6);
            """, "@domain", "@authority", "@runId", "@table", "@row", "@hash", "@evidence", "@localization");
        foreach (var entry in workspace.Promotions.GroupBy(entry => (entry.Domain, entry.AuthorityKey)).Select(group => group.Last()))
        {
            Set(command, "@domain", entry.Domain);
            Set(command, "@authority", Truncate(entry.AuthorityKey, 191));
            Set(command, "@runId", workspace.RunId);
            Set(command, "@table", entry.TargetTable);
            Set(command, "@row", entry.TargetRowIdentity);
            Set(command, "@hash", entry.NormalizedHash);
            Set(command, "@evidence", entry.EvidenceStatus);
            Set(command, "@localization", entry.LocalizationStatus);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<List<FormalRow>> ReadFormalRowsAsync(MySqlConnection connection, MySqlTransaction transaction, string table, string idColumn, CancellationToken cancellationToken)
    {
        var result = new List<FormalRow>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT `{idColumn}`, `Name`, `PayloadJson`, `PayloadSha256` FROM `{table}` ORDER BY `{idColumn}`;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new FormalRow(
                Convert.ToInt64(reader.GetValue(0)),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return result;
    }

    private static ContentEntity? MatchEntity(
        string? payloadJson,
        IReadOnlyDictionary<string, ContentEntity> exact,
        IReadOnlyDictionary<(string Path, int Row), ContentEntity> bySourceRow)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (FindString(document.RootElement, "id") is { } id && exact.TryGetValue(id, out var exactMatch))
            {
                return exactMatch;
            }

            var row = FindInt(document.RootElement, "sourceRecordIndex") ?? FindInt(document.RootElement, "sourceLine");
            var path = FindString(document.RootElement, "sourcePath");
            if (row is not null && path is not null && bySourceRow.TryGetValue((NormalizePath(path), row.Value), out var rowMatch))
            {
                return rowMatch;
            }

            if (row is not null)
            {
                var matches = bySourceRow.Where(pair => pair.Key.Row == row.Value).Select(pair => pair.Value).Distinct().Take(2).ToArray();
                return matches.Length == 1 ? matches[0] : null;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static async Task UpdateFormalRowAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string table,
        long id,
        string originalName,
        LocalizationResult name,
        string? originalDescription,
        LocalizationResult? description,
        string evidenceStatus,
        string runId,
        CancellationToken cancellationToken)
    {
        var hasOriginalNameColumn = await ColumnExistsAsync(connection, table, "OriginalName", cancellationToken);
        var hasOriginalDescriptionColumn = table is "items" or "skills" or "quests" &&
            await ColumnExistsAsync(connection, table, "OriginalDescription", cancellationToken);
        var originalNameSql = hasOriginalNameColumn ? "`OriginalName`=@originalName, " : string.Empty;
        var descriptionSql = table is "items" or "skills" or "quests"
            ? $"{(hasOriginalDescriptionColumn ? ", `OriginalDescription`=@originalDescription" : string.Empty)}, `DescriptionZhTw`=@description"
            : string.Empty;
        var displayNameSql = table == "items" ? ", `DisplayName`=@nameZhTw" : string.Empty;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE `{table}`
            SET {originalNameSql}`NameZhTw`=@nameZhTw, `Name`=@nameZhTw{displayNameSql}{descriptionSql},
                `EvidenceStatus`=@evidence, `LocalizationStatus`=@localization, `ContentRecoveryRunId`=@runId,
                `RecoveryStatus`=CASE WHEN @evidence='EvidenceBlocked' THEN 'EvidenceOnly' ELSE `RecoveryStatus` END
            WHERE `Id`=@id;
            """;
        if (hasOriginalNameColumn)
        {
            command.Parameters.AddWithValue("@originalName", originalName);
        }
        command.Parameters.AddWithValue("@nameZhTw", Truncate(name.ConvertedText, 128));
        command.Parameters.AddWithValue("@evidence", evidenceStatus);
        command.Parameters.AddWithValue("@localization", name.ConversionStatus);
        command.Parameters.AddWithValue("@runId", runId);
        command.Parameters.AddWithValue("@id", id);
        if (table is "items" or "skills" or "quests")
        {
            if (hasOriginalDescriptionColumn)
            {
                command.Parameters.AddWithValue("@originalDescription", (object?)originalDescription ?? DBNull.Value);
            }
            command.Parameters.AddWithValue("@description", (object?)description?.ConvertedText ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertLocalizationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string key,
        string original,
        LocalizationResult converted,
        string runId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var hasLocalizationMetadata = await ColumnExistsAsync(connection, "localization_entries", "ContentRecoveryRunId", cancellationToken);
        command.CommandText = hasLocalizationMetadata
            ? """
                INSERT INTO `localization_entries`
                    (`Language`, `TextKey`, `TextValue`, `UpdatedAtUtc`, `OriginalText`, `OriginalLanguage`, `ConversionMethod`, `ConverterVersion`,
                     `GlossaryVersion`, `SourceHash`, `ConvertedTextHash`, `ConversionStatus`, `LanguageReviewStatus`, `ContentRecoveryRunId`)
                VALUES ('zh-TW', @key, @value, UTC_TIMESTAMP(6), @original, @language, @method, @converter, @glossary, @sourceHash,
                        @convertedHash, @status, @review, @runId)
                ON DUPLICATE KEY UPDATE
                    `TextValue`=IF(`localization_entries`.`ContentRecoveryRunId` IS NULL, `localization_entries`.`TextValue`, VALUES(`TextValue`)),
                    `UpdatedAtUtc`=IF(`localization_entries`.`ContentRecoveryRunId` IS NULL, `localization_entries`.`UpdatedAtUtc`, UTC_TIMESTAMP(6)),
                    `OriginalText`=IF(`localization_entries`.`ContentRecoveryRunId` IS NULL, `localization_entries`.`OriginalText`, VALUES(`OriginalText`)),
                    `OriginalLanguage`=IF(`localization_entries`.`ContentRecoveryRunId` IS NULL, 'zh-TW', VALUES(`OriginalLanguage`)),
                    `ConversionMethod`=IF(`localization_entries`.`ContentRecoveryRunId` IS NULL, 'ExistingOfficialZhTw', VALUES(`ConversionMethod`)),
                    `ConverterVersion`=IF(`localization_entries`.`ContentRecoveryRunId` IS NULL, `localization_entries`.`ConverterVersion`, VALUES(`ConverterVersion`)),
                    `GlossaryVersion`=IF(`localization_entries`.`ContentRecoveryRunId` IS NULL, `localization_entries`.`GlossaryVersion`, VALUES(`GlossaryVersion`)),
                    `SourceHash`=IF(`localization_entries`.`ContentRecoveryRunId` IS NULL, `localization_entries`.`SourceHash`, VALUES(`SourceHash`)),
                    `ConvertedTextHash`=IF(`localization_entries`.`ContentRecoveryRunId` IS NULL, `localization_entries`.`ConvertedTextHash`, VALUES(`ConvertedTextHash`)),
                    `ConversionStatus`=IF(`localization_entries`.`ContentRecoveryRunId` IS NULL, 'TraditionalVerified', VALUES(`ConversionStatus`)),
                    `LanguageReviewStatus`=IF(`localization_entries`.`ContentRecoveryRunId` IS NULL, 'NotRequired', VALUES(`LanguageReviewStatus`)),
                    `ContentRecoveryRunId`=IF(`localization_entries`.`ContentRecoveryRunId` IS NULL, `localization_entries`.`ContentRecoveryRunId`, VALUES(`ContentRecoveryRunId`));
                """
            : """
                INSERT INTO `localization_entries`
                    (`Language`, `TextKey`, `TextValue`, `UpdatedAtUtc`)
                VALUES ('zh-TW', @key, @value, UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE
                    `TextValue`=VALUES(`TextValue`),
                    `UpdatedAtUtc`=UTC_TIMESTAMP(6);
                """;
        command.Parameters.AddWithValue("@key", Truncate(key, 128));
        command.Parameters.AddWithValue("@value", converted.ConvertedText);
        if (hasLocalizationMetadata)
        {
            command.Parameters.AddWithValue("@original", original);
            command.Parameters.AddWithValue("@language", converted.OriginalLanguage);
            command.Parameters.AddWithValue("@method", Truncate(converted.ConversionMethod, 64));
            command.Parameters.AddWithValue("@converter", RecoveryVersions.ConverterVersion);
            command.Parameters.AddWithValue("@glossary", RecoveryVersions.GlossaryVersion);
            command.Parameters.AddWithValue("@sourceHash", converted.SourceHash);
            command.Parameters.AddWithValue("@convertedHash", converted.ConvertedTextHash);
            command.Parameters.AddWithValue("@status", converted.ConversionStatus);
            command.Parameters.AddWithValue("@review", converted.LanguageReviewStatus);
            command.Parameters.AddWithValue("@runId", runId);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<object>> InventoryLocalizationColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var columns = new List<(string Table, string Column, string Type)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT `TABLE_NAME`, `COLUMN_NAME`, `DATA_TYPE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA`=@schema
                  AND `DATA_TYPE` IN ('char','varchar','text','mediumtext','longtext','json')
                  AND (`TABLE_NAME` IN ('maps','npcs','monsters','items','skills','quests','merchants','dialogs','localization_entries','drop_tables')
                       OR `TABLE_NAME` LIKE 'content_%')
                ORDER BY `TABLE_NAME`, `ORDINAL_POSITION`;
                """;
            command.Parameters.AddWithValue("@schema", _options.DatabaseName);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var result = new List<object>();
        foreach (var column in columns)
        {
            var rowCount = await ScalarAsync(connection, $"SELECT COUNT(*) FROM `{column.Table}`;", cancellationToken);
            var isDisplay = ProductionDisplayColumns.Contains((column.Table, column.Column));
            var isOriginal = column.Column.StartsWith("Original", StringComparison.Ordinal) || column.Table is "content_raw_records" or "content_localization_audit";
            long simplified = 0;
            long mixed = 0;
            if (isDisplay)
            {
                var values = await ReadStringsAsync(connection, column.Table, column.Column, cancellationToken);
                simplified = values.LongCount(_localization.ContainsConvertibleSimplified);
                mixed = values.LongCount(value =>
                {
                    var converted = _localization.Convert(value, ContentHash.Sha256(value));
                    return converted.OriginalLanguage == "Mixed-zh-Hans-zh-Hant";
                });
            }

            result.Add(new
            {
                schema = _options.DatabaseName,
                table = column.Table,
                column = column.Column,
                dataType = column.Type,
                rowCount,
                displayTextClassification = isDisplay ? "ProductionDisplayText" : isOriginal ? "RawOrOriginalProvenance" : column.Type == "json" ? "JsonNotBlindlyConverted" : "NonDisplayOrUnclassified",
                simplifiedCandidateCount = simplified,
                mixedLanguageCount = mixed,
                conversionRequired = isDisplay && simplified > 0,
                excludedReason = isDisplay ? null : isOriginal ? "Original evidence/provenance must remain unchanged" : column.Type == "json" ? "Unknown JSON blobs are not blindly replaced" : "Identifier, metadata, audit, or unclassified text"
            });
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, long>> ReadReferentialIntegrityAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NpcMissingMapOrCoordinates"] = "SELECT COUNT(*) FROM `npcs` WHERE `MapId` IS NULL OR `PositionX` IS NULL OR `PositionY` IS NULL;",
            ["BrokenNpcMapForeignKeys"] = "SELECT COUNT(*) FROM `npcs` n LEFT JOIN `maps` m ON m.`Id`=n.`MapId` WHERE n.`MapId` IS NOT NULL AND m.`Id` IS NULL;",
            ["MonsterMissingLevelOrHpOrStats"] = "SELECT COUNT(*) FROM `monsters` WHERE `Level` IS NULL OR `MaxHp` IS NULL OR `Attack` IS NULL OR `Defense` IS NULL;",
            ["MonsterUnknownMpPolicy"] = "SELECT COUNT(*) FROM `monsters` WHERE `MpPolicy`='Unknown';",
            ["MonsterUnknownDropPolicy"] = "SELECT COUNT(*) FROM `monsters` WHERE `DropPolicy`='Unknown';",
            ["BrokenSpawnMonsterForeignKeys"] = "SELECT COUNT(*) FROM `god2_game`.`monster_spawns` s LEFT JOIN `god2_game`.`monsters` m ON m.`monster_id`=s.`monster_id` WHERE m.`monster_id` IS NULL;",
            ["BrokenSpawnMapForeignKeys"] = "SELECT COUNT(*) FROM `god2_game`.`monster_spawns` s LEFT JOIN `god2_game`.`maps` m ON m.`map_id`=s.`map_id` WHERE m.`map_id` IS NULL;",
            ["BrokenDropMonsterForeignKeys"] = "SELECT COUNT(*) FROM `drop_tables` d LEFT JOIN `monsters` m ON m.`Id`=d.`MonsterId` WHERE d.`MonsterId` IS NOT NULL AND m.`Id` IS NULL;",
            ["BrokenDropItemForeignKeys"] = "SELECT COUNT(*) FROM `drop_table_items` d LEFT JOIN `items` i ON i.`Id`=d.`ItemId` WHERE i.`Id` IS NULL;",
            ["BrokenMerchantNpcForeignKeys"] = "SELECT COUNT(*) FROM `merchants` x LEFT JOIN `npcs` n ON n.`Id`=x.`NpcId` WHERE x.`NpcId` IS NOT NULL AND n.`Id` IS NULL;",
            ["BrokenMerchantItemForeignKeys"] = "SELECT COUNT(*) FROM `merchant_items` x LEFT JOIN `items` i ON i.`Id`=x.`ItemId` WHERE i.`Id` IS NULL;",
            ["BrokenPhase2DropMonsterForeignKeys"] = "SELECT COUNT(*) FROM `monster_drop_relationships` x LEFT JOIN `monsters` m ON m.`Id`=x.`MonsterId` WHERE m.`Id` IS NULL;",
            ["BrokenPhase2DropItemForeignKeys"] = "SELECT COUNT(*) FROM `monster_drop_relationships` x LEFT JOIN `items` i ON i.`Id`=x.`ItemId` WHERE i.`Id` IS NULL;",
            ["BrokenPhase2NpcForeignKeys"] = "SELECT COUNT(*) FROM `god2_research`.`npc_coordinate_evidence` x LEFT JOIN `npcs` n ON n.`Id`=x.`NpcId` WHERE x.`NpcId` IS NOT NULL AND n.`Id` IS NULL;",
            ["BrokenPhase2NpcMapForeignKeys"] = "SELECT COUNT(*) FROM `god2_research`.`npc_coordinate_evidence` x LEFT JOIN `maps` m ON m.`Id`=x.`MapId` WHERE x.`MapId` IS NOT NULL AND m.`Id` IS NULL;",
            ["BrokenPhase2SkillForeignKeys"] = "SELECT COUNT(*) FROM `skill_content_profiles` x LEFT JOIN `skills` s ON s.`Id`=x.`SkillId` WHERE x.`SkillId` IS NOT NULL AND s.`Id` IS NULL;",
            ["BrokenPhase2MerchantItemForeignKeys"] = "SELECT COUNT(*) FROM `god2_research`.`merchant_inventory_candidates` x LEFT JOIN `items` i ON i.`Id`=x.`ItemId` WHERE i.`Id` IS NULL;",
            ["BrokenPhase2QuestItemForeignKeys"] = "SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` x LEFT JOIN `items` i ON i.`Id`=x.`ItemId` WHERE x.`ItemId` IS NOT NULL AND i.`Id` IS NULL;",
            ["BrokenPhase2EquipmentSetItemForeignKeys"] = "SELECT COUNT(*) FROM `equipment_set_members` x LEFT JOIN `items` i ON i.`Id`=x.`ItemId` WHERE i.`Id` IS NULL;",
            ["BrokenPhase2ContainerForeignKeys"] = "SELECT COUNT(*) FROM `container_item_relationships` x LEFT JOIN `items` c ON c.`Id`=x.`ContainerItemId` LEFT JOIN `items` i ON i.`Id`=x.`ContainedItemId` WHERE c.`Id` IS NULL OR i.`Id` IS NULL;",
            ["BrokenPhase2PetItemForeignKeys"] = "SELECT COUNT(*) FROM `pet_content_profiles` x LEFT JOIN `items` i ON i.`Id`=x.`ItemId` WHERE x.`ItemId` IS NOT NULL AND i.`Id` IS NULL;",
            ["BrokenDefaultDisabledDropSemantics"] = "SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `ChanceEvidenceStatus`='DefaultDisabledZero' AND (`DeclaredDropChance` IS NOT NULL OR `EffectiveDropChance`<>0 OR `IsDropEnabled`<>0);",
            ["BrokenDefaultDisabledContainerSemantics"] = "SELECT COUNT(*) FROM `container_item_relationships` WHERE `ProbabilityEvidenceStatus`='DefaultDisabledZero' AND (`DeclaredProbability` IS NOT NULL OR `EffectiveProbability`<>0 OR `Enabled`<>0);",
            ["ItemOrphanReferences"] = "SELECT COUNT(*) FROM `drop_table_items` d LEFT JOIN `items` i ON i.`Id`=d.`ItemId` WHERE i.`Id` IS NULL;",
            ["DuplicateMapIds"] = "SELECT COUNT(*) FROM (SELECT `Id` FROM `maps` GROUP BY `Id` HAVING COUNT(*)>1) d;",
            ["DuplicateNpcIds"] = "SELECT COUNT(*) FROM (SELECT `Id` FROM `npcs` GROUP BY `Id` HAVING COUNT(*)>1) d;",
            ["DuplicateMonsterIds"] = "SELECT COUNT(*) FROM (SELECT `Id` FROM `monsters` GROUP BY `Id` HAVING COUNT(*)>1) d;",
            ["DuplicateItemIds"] = "SELECT COUNT(*) FROM (SELECT `Id` FROM `items` GROUP BY `Id` HAVING COUNT(*)>1) d;",
            ["DuplicateSkillIds"] = "SELECT COUNT(*) FROM (SELECT `Id` FROM `skills` GROUP BY `Id` HAVING COUNT(*)>1) d;"
        };
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pair in queries)
        {
            result[pair.Key] = await ScalarAsync(connection, pair.Value, cancellationToken);
        }

        return result;
    }

    private async Task<long> CountProductionSimplifiedAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        long count = 0;
        foreach (var column in ProductionDisplayColumns)
        {
            if (!await ColumnExistsAsync(connection, column.Table, column.Column, cancellationToken))
            {
                continue;
            }

            var values = await ReadStringsAsync(connection, column.Table, column.Column, cancellationToken);
            count += values.LongCount(_localization.ContainsConvertibleSimplified);
        }

        return count;
    }

    private async Task<IReadOnlyDictionary<string, object?>> ValidateRuntimeAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var staticLoader = new MariaDbStaticDataLoader(_options);
        var load = await staticLoader.LoadAsync(cancellationToken);
        if (!load.Succeeded || load.Value is null)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["mariaDbLoad"] = "FAIL",
                ["repositoryMapping"] = "FAIL",
                ["runtimeCatalogBuild"] = "NOT RUN",
                ["failureCode"] = load.Error.Code,
                ["failureMessage"] = load.Error.Message
            };
        }

        var build = await staticLoader.BuildAsync(load.Value, cancellationToken);
        var gameplay = await new MariaDbGameplayContentCatalogRepository(_options).LoadAsync(cancellationToken);
        var phase2Issues = await ReadPhase2SemanticIssuesAsync(connection, runId, cancellationToken);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["mariaDbLoad"] = "PASS",
            ["repositoryMapping"] = "PASS",
            ["runtimeCatalogBuild"] = build.Succeeded ? "PASS" : "FAIL_CLOSED",
            ["runtimeCatalogFailureCode"] = build.Succeeded ? null : build.Error.Code,
            ["formalCounts"] = load.Value.ToDictionary(item => item.DataType, item => item.Count, StringComparer.Ordinal),
            ["publishedItems"] = staticLoader.PublishedSnapshot.Items.Count,
            ["validatedGameplayItems"] = gameplay.ItemCatalog.Definitions.Count,
            ["quarantinedGameplayItems"] = gameplay.QuarantinedItems.Count,
            ["gameplayContentIssues"] = gameplay.Issues.Count,
            ["gameplayCatalogValidation"] = gameplay.QuarantinedItems.Count == 0 && gameplay.Issues.Count == 0 ? "PASS" : "FAIL_CLOSED",
            ["phase2SpecializedCatalogLoad"] = "PASS",
            ["phase2SpecializedValidation"] = phase2Issues.Values.Sum() == 0 ? "PASS" : "FAIL_CLOSED",
            ["phase2SpecializedIssues"] = phase2Issues
        };
    }

    private async Task<IReadOnlyDictionary<string, object?>> ValidateHeadlessAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var displayValues = new List<string>();
        foreach (var column in ProductionDisplayColumns)
        {
            if (await ColumnExistsAsync(connection, column.Table, column.Column, cancellationToken))
            {
                displayValues.AddRange(await ReadStringsAsync(connection, column.Table, column.Column, cancellationToken));
            }
        }

        var invalidUnicode = displayValues.LongCount(value => value.Contains('\ufffd'));
        var simplified = displayValues.LongCount(_localization.ContainsConvertibleSimplified);
        var dropEntries = await ScalarAsync(connection, "SELECT COUNT(*) FROM `drop_table_items`;", cancellationToken);
        var invalidDrops = await ScalarAsync(connection, "SELECT COUNT(*) FROM `drop_table_items` WHERE `ChancePerMillion`<0 OR `ChancePerMillion`>1000000 OR `MinQuantity`<=0 OR `MaxQuantity`<`MinQuantity`;", cancellationToken);
        var phase2Drops = await ScalarAsync(connection, "SELECT COUNT(*) FROM `monster_drop_relationships`;", cancellationToken);
        var invalidPhase2Drops = await ScalarAsync(connection, "SELECT COUNT(*) FROM `monster_drop_relationships` WHERE (`MinimumQuantity` IS NOT NULL AND `MinimumQuantity`<=0) OR (`MaximumQuantity` IS NOT NULL AND (`MinimumQuantity` IS NULL OR `MaximumQuantity`<`MinimumQuantity`)) OR (`ChanceEvidenceStatus`='DefaultDisabledZero' AND (`DeclaredDropChance` IS NOT NULL OR `EffectiveDropChance`<>0 OR `IsDropEnabled`<>0));", cancellationToken);
        var phase2Issues = await ReadPhase2SemanticIssuesAsync(connection, runId, cancellationToken);
        var phase2IssueCount = phase2Issues.Values.Sum();
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["scenario"] = "MariaDB formal and Phase 2 specialized content lookup + deterministic disabled-drop boundary validation",
            ["displayRowsRead"] = displayValues.Count,
            ["simplifiedDisplayRows"] = simplified,
            ["invalidUnicodeRows"] = invalidUnicode,
            ["placeholderAndMarkup"] = "Validated during conversion audit",
            ["dropEntries"] = dropEntries,
            ["dropSimulation"] = dropEntries == 0 ? "PASS_NOT_APPLICABLE_NO_PROMOTED_DROP_ENTRIES" : invalidDrops == 0 ? "PASS" : "FAIL",
            ["invalidDropEntries"] = invalidDrops,
            ["phase2DropRelationships"] = phase2Drops,
            ["phase2DropValidation"] = phase2Drops == 0 ? "PASS_NOT_APPLICABLE" : invalidPhase2Drops == 0 ? "PASS_DISABLED_UNVERIFIED_RELATIONSHIPS" : "FAIL",
            ["invalidPhase2DropRelationships"] = invalidPhase2Drops,
            ["phase2SpecializedIssues"] = phase2Issues,
            ["status"] = simplified == 0 && invalidUnicode == 0 && invalidDrops == 0 && invalidPhase2Drops == 0 && phase2IssueCount == 0 ? "PASS" : "FAIL"
        };
    }

    private static async Task<Dictionary<string, long>> ReadPhase2SemanticIssuesAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["InvalidEquipmentRequiredPieces"] = "SELECT COUNT(*) FROM `equipment_set_definitions` WHERE `RequiredPieces`<1 OR `RequiredPieces`>5;",
            ["IncompleteEquipmentSets"] = "SELECT COUNT(*) FROM `equipment_set_definitions` d LEFT JOIN (SELECT `SetId`,COUNT(*) c FROM `equipment_set_members` GROUP BY `SetId`) m ON m.`SetId`=d.`SetId` WHERE COALESCE(m.c,0)<d.`RequiredPieces`;",
            ["UnresolvedEquipmentSlots"] = "SELECT COUNT(*) FROM `equipment_set_members` WHERE `SlotName`='UnresolvedSlot';",
            ["PetInnateDescriptorRows"] = "SELECT COUNT(*) FROM `pet_innate_definitions` WHERE `NameZhTw` IN ('娉曞鍚嶇ū','娉曞疂鍚嶇О') OR `DescriptionZhTw` IN ('娉曞瑾槑','娉曞疂璇存槑');",
            ["DefaultDisabledDropViolations"] = "SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `ChanceEvidenceStatus`='DefaultDisabledZero' AND (`DeclaredDropChance` IS NOT NULL OR `EffectiveDropChance`<>0 OR `IsDropEnabled`<>0);",
            ["DefaultDisabledContainerViolations"] = "SELECT COUNT(*) FROM `container_item_relationships` WHERE `ProbabilityEvidenceStatus`='DefaultDisabledZero' AND (`DeclaredProbability` IS NOT NULL OR `EffectiveProbability`<>0 OR `Enabled`<>0);",
            ["EnabledEvidenceBlockedCandidates"] = "SELECT (SELECT COUNT(*) FROM `god2_research`.`npc_coordinate_evidence` WHERE `ProductionSpawnEnabled`<>0 AND (`NpcId` IS NULL OR `MapId` IS NULL)) + (SELECT COUNT(*) FROM `god2_research`.`merchant_inventory_candidates` WHERE `Enabled`<>0 AND `MerchantId` IS NULL) + (SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` WHERE `Enabled`<>0 AND `EvidenceStatus`<>'Verified') + (SELECT COUNT(*) FROM `pet_egg_relationships` WHERE `Enabled`<>0 AND `RelationshipStatus`<>'Verified');",
            ["MisclassifiedStagingProvenance"] = "SELECT COUNT(*) FROM `god2_research`.`content_staging_records` WHERE `RunId`=@run AND ((JSON_UNQUOTE(JSON_EXTRACT(`NormalizedData`,'$.sourceFile')) LIKE 'http%' OR JSON_UNQUOTE(JSON_EXTRACT(`NormalizedData`,'$.sourceFile')) LIKE 'historical:%') AND JSON_UNQUOTE(JSON_EXTRACT(`NormalizedData`,'$.sourceType'))='OfficialClientResource');",
            ["ValidationErrors"] = "SELECT COUNT(*) FROM `god2_research`.`content_validation_results` WHERE `RunId`=@run AND `Status`='FAIL' AND `Severity`='Error';",
            ["LocalizationIntegrityFailures"] = "SELECT COUNT(*) FROM `god2_research`.`content_localization_audit` WHERE `RunId`=@run AND (`ConversionStatus`='ConversionFailed' OR `PlaceholderPreserved`=0 OR `MarkupPreserved`=0 OR `ControlCodesPreserved`=0);",
            ["StaleSpecializedSnapshotRows"] = "SELECT (SELECT COUNT(*) FROM `item_content_profiles` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `god2_research`.`npc_coordinate_evidence` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `skill_content_profiles` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `god2_research`.`merchant_inventory_candidates` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `quest_content_profiles` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `equipment_set_definitions` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `equipment_set_members` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `container_item_relationships` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `pet_content_profiles` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `pet_innate_definitions` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `pet_egg_relationships` WHERE `RunId`<>@run) + (SELECT COUNT(*) FROM `god2_research`.`historical_gameplay_observations` WHERE `RunId`<>@run);"
        };

        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var query in queries)
        {
            result[query.Key] = query.Value.Contains("@run", StringComparison.Ordinal)
                ? await ScalarAsync(connection, query.Value, cancellationToken, ("@run", runId))
                : await ScalarAsync(connection, query.Value, cancellationToken);
        }
        return result;
    }

    private static async Task<Dictionary<string, long>> ReadFormalCountsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var tables = new (string Name, string SqlName)[]
        {
            ("maps", "`maps`"),
            ("npcs", "`npcs`"),
            ("monsters", "`monsters`"),
            ("monster_spawns", "`god2_game`.`monster_spawns`"),
            ("items", "`items`"),
            ("skills", "`skills`"),
            ("quests", "`quests`"),
            ("merchants", "`merchants`"),
            ("merchant_items", "`merchant_items`"),
            ("drop_tables", "`drop_tables`"),
            ("drop_table_items", "`drop_table_items`"),
            ("localization_entries", "`localization_entries`"),
            ("content_raw_records", "`god2_research`.`content_raw_records`"),
            ("content_staging_records", "`god2_research`.`content_staging_records`"),
            ("content_validated_records", "`god2_research`.`content_validated_records`"),
            ("content_client_table_layouts", "`god2_research`.`content_client_table_layouts`"),
            ("content_field_evidence", "`god2_research`.`content_field_evidence`"),
            ("item_content_profiles", "`item_content_profiles`"),
            ("npc_coordinate_evidence", "`god2_research`.`npc_coordinate_evidence`"),
            ("monster_drop_relationships", "`monster_drop_relationships`"),
            ("skill_content_profiles", "`skill_content_profiles`"),
            ("merchant_inventory_candidates", "`god2_research`.`merchant_inventory_candidates`"),
            ("quest_content_profiles", "`quest_content_profiles`"),
            ("quest_objective_candidates", "`god2_research`.`quest_objective_candidates`"),
            ("equipment_set_definitions", "`equipment_set_definitions`"),
            ("equipment_set_members", "`equipment_set_members`"),
            ("container_item_relationships", "`container_item_relationships`"),
            ("pet_content_profiles", "`pet_content_profiles`"),
            ("pet_innate_definitions", "`pet_innate_definitions`"),
            ("pet_egg_relationships", "`pet_egg_relationships`"),
            ("historical_gameplay_observations", "`god2_research`.`historical_gameplay_observations`")
        };
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            result[table.Name] = await ScalarAsync(connection, $"SELECT COUNT(*) FROM {table.SqlName};", cancellationToken);
        }

        return result;
    }

    private static async Task<long> ScalarAsync(MySqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(MySqlConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT `{column}` FROM `{table}` WHERE `{column}` IS NOT NULL AND `{column}`<>'';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static async Task<bool> ColumnExistsAsync(MySqlConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`COLUMNS` WHERE `TABLE_SCHEMA`=@schema AND `TABLE_NAME`=@table AND `COLUMN_NAME`=@column;";
        command.Parameters.AddWithValue("@schema", connection.Database);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task UpdateRunStatusAsync(MySqlConnection connection, string runId, string status, string? summary, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE `content_recovery_runs` SET `Status`=@status, `CompletedAtUtc`=UTC_TIMESTAMP(6) WHERE `RunId`=@runId;";
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@runId", runId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var summaryCommand = connection.CreateCommand();
        summaryCommand.CommandText = """
            INSERT INTO `god2_research`.`content_recovery_run_summary_archive`
                (`RunId`,`Status`,`SummaryJson`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
            VALUES (@runId,@status,@summary,'Recovery run 摘要 JSON 已移入 research；正式 run ledger 只保留流程狀態與時間。',UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `Status`=VALUES(`Status`),`SummaryJson`=VALUES(`SummaryJson`),
                `ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`);
            """;
        summaryCommand.Parameters.AddWithValue("@runId", runId);
        summaryCommand.Parameters.AddWithValue("@status", status);
        summaryCommand.Parameters.AddWithValue("@summary", (object?)summary ?? DBNull.Value);
        await summaryCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MySqlCommand CreateCommand(MySqlConnection connection, MySqlTransaction transaction, string sql, params string[] parameterNames)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var name in parameterNames)
        {
            command.Parameters.Add(new MySqlParameter(name, DBNull.Value));
        }

        return command;
    }

    private static void Set(MySqlCommand command, string parameter, object? value) => command.Parameters[parameter].Value = value ?? DBNull.Value;

    private static string? FindString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                if (FindString(property.Value, propertyName) is { } nested)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (FindString(item, propertyName) is { } nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static int? FindInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName) && property.Value.TryGetInt32(out var value))
                {
                    return value;
                }

                if (FindInt(property.Value, propertyName) is { } nested)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (FindInt(item, propertyName) is { } nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string NormalizePath(string path)
    {
        var value = path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        var originalIndex = value.IndexOf("original/", StringComparison.Ordinal);
        return originalIndex >= 0 ? value[(originalIndex + "original/".Length)..] : value;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private sealed class MariaDbAdvisoryLock : IAsyncDisposable
    {
        private readonly MySqlConnection _connection;
        private readonly string _name;

        private MariaDbAdvisoryLock(MySqlConnection connection, string name)
        {
            _connection = connection;
            _name = name;
        }

        public static async Task<MariaDbAdvisoryLock> AcquireAsync(MySqlConnection connection, string name, CancellationToken cancellationToken)
        {
            if (name.Length > 64)
            {
                throw new InvalidOperationException("MariaDB content-recovery lock name exceeds 64 characters.");
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT GET_LOCK(@name, 0);";
            command.Parameters.AddWithValue("@name", name);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            {
                throw new InvalidOperationException("Another gameplay content recovery import is already active.");
            }

            return new MariaDbAdvisoryLock(connection, name);
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection.State != ConnectionState.Open)
            {
                return;
            }

            try
            {
                await using var command = _connection.CreateCommand();
                command.CommandText = "SELECT RELEASE_LOCK(@name);";
                command.Parameters.AddWithValue("@name", _name);
                await command.ExecuteScalarAsync();
            }
            catch (MySqlException)
            {
                // Closing the owning connection also releases the advisory lock.
            }
        }
    }

    private sealed record FormalRow(long Id, string Name, string? PayloadJson, string? PayloadSha256);
}





