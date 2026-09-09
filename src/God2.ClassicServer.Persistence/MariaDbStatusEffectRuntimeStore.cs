using System.Collections.Concurrent;
using System.Text.Json;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbStatusDefinitionRepository :
    MariaDbRuntimeRepository,
    IStatusDefinitionRepository
{
    public MariaDbStatusDefinitionRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<IReadOnlyList<StatusDefinitionRecord>> LoadAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var records = new List<StatusDefinitionRecord>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
            SELECT `Id`, `Code`, `Name`, `DurationSeconds`
            FROM `status_effects`
            ORDER BY `Id`;
            """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt32("Id");
                var durationSeconds = reader.GetInt32("DurationSeconds");
                var raw = JsonSerializer.Serialize(new
                {
                    Source = "database.status_effects",
                    DurationSecondsCandidate = durationSeconds,
                    Classification = "UnknownVisualOrLegacyCandidate",
                    GameplayPromotion = "EvidenceBlocked"
                });
                records.Add(new StatusDefinitionRecord(
                    id,
                    reader.GetString("Code"),
                    reader.GetString("Name"),
                    "",
                    StatusCategory.Unknown,
                    StatusPolarity.Unknown,
                    StatusApplicationPolicyType.EvidenceBlocked,
                    new StatusStackDefinition(
                        StatusStackPolicyType.Unknown,
                        null,
                        CombatPolicyStatus.EvidenceBlocked,
                        raw),
                    new StatusDurationDefinition(
                        StatusDurationPolicyType.Unknown,
                        null,
                        CombatPolicyStatus.EvidenceBlocked,
                        raw),
                    [],
                    [],
                    [],
                    new StatusDispelDefinition(
                        false,
                        false,
                        null,
                        null,
                        CombatPolicyStatus.EvidenceBlocked,
                        raw),
                    true,
                    $"database-status-{id}-raw-v1",
                    CombatPolicyStatus.EvidenceBlocked,
                    CombatPolicyStatus.EvidenceBlocked,
                    raw));
            }
        }

        if (!await PublicBetaSkillEffectTableExistsAsync(connection, cancellationToken))
        {
            return records;
        }

        records.AddRange(PublicBetaDebuffDefinitions());
        await using (var command = connection.CreateCommand())
        {
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                SELECT `global_record_id`,`name_zh_tw`,`description_zh_tw`,`skill_level` AS `level_candidate`,
                       `implementation_family`,`status_or_axis`,`numeric_effect_fields`,
                       `minimum_working_server_rule`,`enabled`
                FROM `god2_game`.`public_beta_skill_effect_v0`
                WHERE `enabled`=1
                  AND `effect_kind` IN ('buff','增益')
                ORDER BY `global_record_id`;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                records.Add(MapPublicBetaBuffStatus(reader));
            }
        }

        return records;
    }

    private static async Task<bool> PublicBetaSkillEffectTableExistsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM `information_schema`.`TABLES`
            WHERE `TABLE_SCHEMA`='god2_game'
              AND `TABLE_NAME`='public_beta_skill_effect_v0';
            """;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value) > 0;
    }

    private static IReadOnlyList<StatusDefinitionRecord> PublicBetaDebuffDefinitions()
    {
        return
        [
            Debuff("seal", "封印", StatusCategory.Control, [StatusActionRestrictionType.PreventSkillAction], []),
            Debuff("sleep", "睡眠", StatusCategory.Control, [StatusActionRestrictionType.PreventAllAction], []),
            Debuff("petrify", "石化", StatusCategory.Control, [StatusActionRestrictionType.PreventAllAction], []),
            Debuff(
                "poison",
                "中毒",
                StatusCategory.PeriodicDamage,
                [],
                [new StatusTriggerDefinition(
                    "public-beta-status-poison-round-closing",
                    MariaDbSkillDefinitionRepository.StatusId("poison"),
                    0,
                    StatusTriggerPhase.RoundClosing,
                    0,
                    StatusTriggerEffectType.PeriodicDamage,
                    "current_hp_div_10_user_formula",
                    1,
                    "target",
                    null,
                    CombatPolicyStatus.ContentBacked,
                    "{\"source\":\"user_provided_poison_formula\",\"formula\":\"floor(current_hp/10)\"}")]),
            Debuff("confusion", "混亂", StatusCategory.Control, [StatusActionRestrictionType.PreventTargeting], [])
        ];

        static StatusDefinitionRecord Debuff(
            string axis,
            string name,
            StatusCategory category,
            IReadOnlyList<StatusActionRestrictionType> restrictions,
            IReadOnlyList<StatusTriggerDefinition> triggers)
        {
            var id = MariaDbSkillDefinitionRepository.StatusId(axis);
            var raw = JsonSerializer.Serialize(new
            {
                Source = "public_beta_skill_effect_v0",
                Axis = axis,
                Function = axis switch
                {
                    "seal" => "block active skill command family",
                    "sleep" => "skip action until removed or expired",
                    "petrify" => "skip action and provisional cannot act",
                    "poison" => "periodic damage based on current HP / 10",
                    "confusion" => "provisional target/action disruption",
                    _ => "negative status"
                }
            });
            return new StatusDefinitionRecord(
                id,
                $"public-beta-status-{axis}",
                name,
                raw,
                category,
                StatusPolarity.Negative,
                StatusApplicationPolicyType.ContentBacked,
                new StatusStackDefinition(StatusStackPolicyType.RefreshDuration, 1, CombatPolicyStatus.ContentBacked, raw),
                new StatusDurationDefinition(StatusDurationPolicyType.Rounds, 3, CombatPolicyStatus.ContentBacked, raw),
                triggers,
                [],
                restrictions.Select((value, index) => new StatusActionRestrictionDefinition(
                    $"public-beta-status-{axis}-restriction-{index}",
                    index,
                    value,
                    CombatPolicyStatus.ContentBacked,
                    raw)).ToArray(),
                new StatusDispelDefinition(true, true, "Negative", false, CombatPolicyStatus.ContentBacked, raw),
                true,
                "public-beta-status-effect-v0",
                CombatPolicyStatus.ContentBacked,
                CombatPolicyStatus.ContentBacked,
                raw);
        }
    }

    private static StatusDefinitionRecord MapPublicBetaBuffStatus(MySqlDataReader reader)
    {
        var skillId = reader.GetInt32("global_record_id");
        var statusId = checked(920000 + skillId);
        var raw = JsonSerializer.Serialize(new
        {
            Source = "public_beta_skill_effect_v0",
            SkillRecordId = skillId,
            ImplementationFamily = NormalizeImplementationFamily(reader.GetString("implementation_family")),
            StatusOrAxis = NormalizeStatusAxis(NullableString(reader, "status_or_axis") ?? ""),
            NumericEffectFields = NullableString(reader, "numeric_effect_fields"),
            MinimumWorkingServerRule = reader.GetString("minimum_working_server_rule"),
            EvidenceBoundary = "god2_game.public_beta_skill_effect_v0"
        });
        var modifiers = ParsePublicBetaBuffModifiers(
            statusId,
            NullableString(reader, "numeric_effect_fields") ?? "",
            raw);
        if (modifiers.Count == 0)
        {
            modifiers =
            [
                new StatusModifierDefinition(
                    $"public-beta-status-{statusId}-metadata-marker",
                    statusId,
                    0,
                    StatusModifierType.AttackModifier,
                    0,
                    0,
                    false,
                    CombatPolicyStatus.ContentBacked,
                    raw)
            ];
        }

        return new StatusDefinitionRecord(
            statusId,
            $"public-beta-buff-{skillId}",
            NullableString(reader, "name_zh_tw") ?? NullableString(reader, "description_zh_tw") ?? $"public-beta-buff-{skillId}",
            NullableString(reader, "description_zh_tw") ?? "",
            StatusCategory.Buff,
            StatusPolarity.Positive,
            StatusApplicationPolicyType.ContentBacked,
            new StatusStackDefinition(StatusStackPolicyType.RefreshDuration, 1, CombatPolicyStatus.ContentBacked, raw),
            new StatusDurationDefinition(StatusDurationPolicyType.Rounds, 3, CombatPolicyStatus.ContentBacked, raw),
            [],
            modifiers,
            [],
            new StatusDispelDefinition(true, true, "Positive", false, CombatPolicyStatus.ContentBacked, raw),
            true,
            "public-beta-status-effect-v0",
            CombatPolicyStatus.ContentBacked,
            CombatPolicyStatus.ContentBacked,
            raw);
    }

    private static IReadOnlyList<StatusModifierDefinition> ParsePublicBetaBuffModifiers(
        int statusId,
        string fields,
        string raw)
    {
        var values = new List<StatusModifierDefinition>();
        foreach (var part in fields.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !long.TryParse(pair[1], out var value))
            {
                continue;
            }

            var type = pair[0] switch
            {
                "wrist_s" or "腕力" or
                    "intelligence_s" or "智力" or
                    "physical_attack_s" or "物理攻擊" or
                    "magical_attack_s" or "法術攻擊" =>
                    StatusModifierType.AttackModifier,
                "constitution_s" or "體質" or
                    "physical_defense_s" or "物理防禦" or
                    "magical_defense_s" or "法術防禦" or
                    "metal_s" or "金" or
                    "wood_s" or "木" or
                    "earth_s" or "土" or
                    "water_s" or "水" or
                    "fire_s" or "火" =>
                    StatusModifierType.DefenseModifier,
                "speed_s" or "速度" => StatusModifierType.InitiativeModifier,
                _ => StatusModifierType.Unknown
            };
            if (type == StatusModifierType.Unknown)
            {
                continue;
            }

            values.Add(new StatusModifierDefinition(
                $"public-beta-status-{statusId}-modifier-{values.Count}",
                statusId,
                values.Count,
                type,
                value,
                0,
                false,
                CombatPolicyStatus.ContentBacked,
                raw));
        }

        return values;
    }

    private static string? NullableString(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(name);

    private static string NormalizeImplementationFamily(string value) => value switch
    {
        "屬性增益" => "stat_buff",
        "五行陣法增益" => "element_formation_buff",
        "五行符咒攻擊增益" => "element_talisman_attack_buff",
        "五行轉換增益" => "element_transform_buff",
        _ => value
    };
    private static string NormalizeStatusAxis(string value) => value switch
    {
        "屬性" => "stats",
        "五行" => "element",
        "五行攻擊" => "element_attack",
        "生命值" => "hp",
        "混亂" => "confusion",
        "石化" => "petrify",
        "中毒" => "poison",
        "封印" => "seal",
        "睡眠" => "sleep",
        "全部負面狀態" => "all_negative",
        "死亡友方" => "dead_ally",
        _ => value
    };
}

public sealed class MariaDbStatusRuntimeStore :
    MariaDbRuntimeRepository,
    IStatusApplicationStore,
    IStatusTriggerExecutionStore,
    IStatusInstanceStore,
    IStatusAuditLedger,
    IStatusInspectorSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, StatusApplicationRecord> _applications =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StatusRemovalRecord> _removals =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, StatusTriggerPlanRecord> _triggerPlans = [];
    private readonly ConcurrentDictionary<Guid, StatusInstance> _instances = [];
    private readonly ConcurrentQueue<StatusAuditRecord> _audit = [];

    public MariaDbStatusRuntimeStore(DatabaseOptions options)
        : base(options)
    {
    }

    public IReadOnlyList<StatusApplicationRecord> Applications =>
        _applications.Values.OrderBy(value => value.Request.CreatedAtUtc).ToArray();

    public IReadOnlyList<StatusRemovalRecord> Removals =>
        _removals.Values.OrderBy(value => value.Request.CreatedAtUtc).ToArray();

    public IReadOnlyList<StatusTriggerPlanRecord> TriggerPlans =>
        _triggerPlans.Values.OrderBy(value => value.Plan.CreatedAtUtc).ToArray();

    public IReadOnlyList<StatusInstance> StatusInstances
    {
        get
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                SELECT `InstanceJson`
                FROM `battle_status_instances`
                ORDER BY `AppliedAtUtc`, `StatusInstanceId`;
                """;
            using var reader = command.ExecuteReader();
            var values = new List<StatusInstance>();
            while (reader.Read())
            {
                var value = JsonSerializer.Deserialize<StatusInstance>(
                    reader.GetString(0),
                    JsonOptions);
                if (value is not null)
                {
                    _instances[value.StatusInstanceId] = value;
                    values.Add(value);
                }
            }

            return values;
        }
    }

    public IReadOnlyList<StatusTriggerPlanRecord> StatusTriggerPlans => TriggerPlans;

    public IReadOnlyList<StatusModifierInspectorItem> StatusModifierSnapshots => [];

    public IReadOnlyList<StatusRestrictionInspectorItem> StatusRestrictionResults => [];

    public IReadOnlyList<StatusAuditRecord> Snapshot => _audit.ToArray();

    public StatusApplicationRecord? GetApplication(string idempotencySafeId)
    {
        if (_applications.TryGetValue(idempotencySafeId, out var cached))
        {
            return cached;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `ApplicationJson`
            FROM `battle_status_applications`
            WHERE `IdempotencyKeyHash` = @idempotencyKeyHash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@idempotencyKeyHash", idempotencySafeId);
        var scalar = command.ExecuteScalar();
        var value = scalar is null or DBNull
            ? null
            : JsonSerializer.Deserialize<StatusApplicationRecord>((string)scalar, JsonOptions);
        if (value is not null)
        {
            _applications[idempotencySafeId] = value;
            _instances[value.ProposedInstance.StatusInstanceId] = value.ProposedInstance;
        }

        return value;
    }

    public StatusRemovalRecord? GetRemoval(string idempotencySafeId)
    {
        if (_removals.TryGetValue(idempotencySafeId, out var cached))
        {
            return cached;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `RemovalJson`
            FROM `battle_status_removals`
            WHERE `IdempotencyKeyHash` = @idempotencyKeyHash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@idempotencyKeyHash", idempotencySafeId);
        var scalar = command.ExecuteScalar();
        var value = scalar is null or DBNull
            ? null
            : JsonSerializer.Deserialize<StatusRemovalRecord>((string)scalar, JsonOptions);
        if (value is not null)
        {
            _removals[idempotencySafeId] = value;
        }

        return value;
    }

    public OperationResult SaveApplication(
        StatusApplicationRecord record,
        StatusMutationState expectedState)
    {
        var key = BattleRuntimeHash.SafeId(record.Request.IdempotencyKey);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                var existing = ApplicationExists(connection, transaction, record.Request.StatusApplicationId);
                command.CommandText = !existing
                    ? """
                      INSERT INTO `battle_status_applications`
                          (`StatusApplicationId`, `StatusApplicationPlanId`, `StatusInstanceId`,
                           `BattleInstanceId`, `RoundNumber`, `TargetParticipantId`,
                           `StatusDefinitionId`, `IdempotencyKeyHash`, `ApplicationFingerprintSha256`, `State`,
                           `RecoveryState`, `ApplicationJson`, `ResultJson`,
                           `CreatedAtUtc`, `UpdatedAtUtc`, `CompletedAtUtc`)
                      VALUES
                          (@statusApplicationId, @statusApplicationPlanId, @statusInstanceId,
                           @battleInstanceId, @roundNumber, @targetParticipantId,
                           @statusDefinitionId, @idempotencyKeyHash, @payloadHash, @state,
                           @recoveryState, @applicationJson, @resultJson,
                           @createdAtUtc, @updatedAtUtc, @completedAtUtc);
                      """
                    : """
                      UPDATE `battle_status_applications`
                      SET `State` = @state,
                          `RecoveryState` = @recoveryState,
                          `ApplicationJson` = @applicationJson,
                          `ResultJson` = @resultJson,
                          `UpdatedAtUtc` = @updatedAtUtc,
                          `CompletedAtUtc` = @completedAtUtc
                      WHERE `StatusApplicationId` = @statusApplicationId
                        AND `State` = @expectedState;
                      """;
                AddApplicationParameters(command, record, key);
                command.Parameters.AddWithValue("@expectedState", expectedState.ToString());
                if (command.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return OperationResult.Failure(
                        "status.application_version_conflict",
                        "Persisted status application state changed before commit.");
                }
            }

            if (record.State is StatusMutationState.RuntimeCommitted or StatusMutationState.Completed)
            {
                UpsertInstance(connection, transaction, record.ProposedInstance);
            }

            if (record.State == StatusMutationState.RuntimeCommitted &&
                record.Plan.Operation == StatusApplicationOperation.ReplaceExisting &&
                record.Plan.ExistingStatusInstanceId is Guid replacedStatusInstanceId)
            {
                var replaced = UpdateInstanceLifecycle(
                    connection,
                    transaction,
                    replacedStatusInstanceId,
                    record.Plan.ExpectedStatusVersion,
                    checked(record.Plan.ExpectedStatusVersion + 1),
                    StatusLifecycleState.Removed,
                    StatusRemovalReason.Replaced,
                    record.UpdatedAtUtc);
                if (!replaced.Succeeded)
                {
                    transaction.Rollback();
                    return replaced;
                }
            }

            if (record.Result is not null)
            {
                UpsertParticipantStatusVersion(
                    connection,
                    transaction,
                    record.Plan.BattleInstanceId,
                    record.Plan.TargetParticipantId,
                    record.Result.TargetVersionAfter,
                    record.UpdatedAtUtc);
            }

            UpsertRecovery(
                connection,
                transaction,
                "Application",
                record.Request.StatusApplicationId,
                record.Plan.BattleInstanceId,
                record.RecoveryState,
                record.Result?.FailureCode ?? "",
                0,
                record,
                record.Request.CreatedAtUtc,
                record.UpdatedAtUtc);

            UpsertIdempotency(
                connection,
                transaction,
                "Application",
                key,
                record.PayloadHash,
                record.Plan.BattleInstanceId,
                record.Request.StatusApplicationId,
                record.Result,
                record.Request.CreatedAtUtc,
                record.Result?.CompletedAtUtc);
            transaction.Commit();
            _applications[key] = record;
            if (record.State is StatusMutationState.RuntimeCommitted or StatusMutationState.Completed)
            {
                _instances[record.ProposedInstance.StatusInstanceId] = record.ProposedInstance;
            }

            return OperationResult.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            transaction.Rollback();
            return OperationResult.Failure(
                "status.application_duplicate",
                "Status application or idempotency key already exists.");
        }
        catch (Exception exception) when (
            exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            transaction.Rollback();
            return OperationResult.Failure("status.persistence_failure", exception.Message);
        }
    }

    public OperationResult SaveRemoval(
        StatusRemovalRecord record,
        StatusMutationState expectedState)
    {
        var key = BattleRuntimeHash.SafeId(record.Request.IdempotencyKey);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                var existing = RemovalExists(connection, transaction, record.Request.StatusRemovalId);
                command.CommandText = !existing
                    ? """
                      INSERT INTO `battle_status_removals`
                          (`StatusRemovalId`, `StatusRemovalPlanId`, `StatusInstanceId`,
                           `BattleInstanceId`, `RoundNumber`, `TargetParticipantId`,
                           `StatusDefinitionId`, `RemovalReason`, `IdempotencyKeyHash`,
                           `RemovalFingerprintSha256`, `State`, `RecoveryState`, `RemovalJson`, `ResultJson`,
                           `CreatedAtUtc`, `UpdatedAtUtc`, `CompletedAtUtc`)
                      VALUES
                          (@statusRemovalId, @statusRemovalPlanId, @statusInstanceId,
                           @battleInstanceId, @roundNumber, @targetParticipantId,
                           @statusDefinitionId, @removalReason, @idempotencyKeyHash,
                           @payloadHash, @state, @recoveryState, @removalJson, @resultJson,
                           @createdAtUtc, @updatedAtUtc, @completedAtUtc);
                      """
                    : """
                      UPDATE `battle_status_removals`
                      SET `State` = @state,
                          `RecoveryState` = @recoveryState,
                          `RemovalJson` = @removalJson,
                          `ResultJson` = @resultJson,
                          `UpdatedAtUtc` = @updatedAtUtc,
                          `CompletedAtUtc` = @completedAtUtc
                      WHERE `StatusRemovalId` = @statusRemovalId
                        AND `State` = @expectedState;
                      """;
                AddRemovalParameters(command, record, key);
                command.Parameters.AddWithValue("@expectedState", expectedState.ToString());
                if (command.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return OperationResult.Failure(
                        "status.removal_version_conflict",
                        "Persisted status removal state changed before commit.");
                }
            }

            if (record.State is StatusMutationState.RuntimeCommitted or StatusMutationState.Completed &&
                record.Result is not null)
            {
                var removed = UpdateInstanceLifecycle(
                    connection,
                    transaction,
                    record.Plan.StatusInstanceId,
                    record.Plan.ExpectedStatusVersion,
                    record.Result.StatusVersionAfter,
                    record.Plan.RemovalReason == StatusRemovalReason.Expired
                        ? StatusLifecycleState.Expired
                        : StatusLifecycleState.Removed,
                    record.Plan.RemovalReason,
                    record.Result.CompletedAtUtc);
                if (!removed.Succeeded)
                {
                    transaction.Rollback();
                    return removed;
                }

                UpsertParticipantStatusVersion(
                    connection,
                    transaction,
                    record.Plan.BattleInstanceId,
                    record.Plan.TargetParticipantId,
                    record.Result.TargetVersionAfter,
                    record.UpdatedAtUtc);
            }

            UpsertRecovery(
                connection,
                transaction,
                "Removal",
                record.Request.StatusRemovalId,
                record.Plan.BattleInstanceId,
                record.RecoveryState,
                record.Result?.FailureCode ?? "",
                0,
                record,
                record.Request.CreatedAtUtc,
                record.UpdatedAtUtc);

            UpsertIdempotency(
                connection,
                transaction,
                "Removal",
                key,
                record.PayloadHash,
                record.Plan.BattleInstanceId,
                record.Request.StatusRemovalId,
                record.Result,
                record.Request.CreatedAtUtc,
                record.Result?.CompletedAtUtc);
            transaction.Commit();
            _removals[key] = record;
            if (record.Result is not null &&
                _instances.TryGetValue(record.Plan.StatusInstanceId, out var current))
            {
                _instances[record.Plan.StatusInstanceId] = current with
                {
                    LifecycleState = record.Plan.RemovalReason == StatusRemovalReason.Expired
                        ? StatusLifecycleState.Expired
                        : StatusLifecycleState.Removed,
                    RuntimeVersion = record.Result.StatusVersionAfter,
                    UpdatedAtUtc = record.UpdatedAtUtc,
                    RemovedAtUtc = record.Result.CompletedAtUtc,
                    RemovalReason = record.Plan.RemovalReason
                };
            }

            return OperationResult.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            transaction.Rollback();
            return OperationResult.Failure(
                "status.removal_duplicate",
                "Status removal or idempotency key already exists.");
        }
        catch (Exception exception) when (
            exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            transaction.Rollback();
            return OperationResult.Failure("status.persistence_failure", exception.Message);
        }
    }

    public StatusInstance? GetStatus(Guid statusInstanceId)
    {
        if (_instances.TryGetValue(statusInstanceId, out var cached))
        {
            return cached;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `InstanceJson`
            FROM `battle_status_instances`
            WHERE `StatusInstanceId` = @statusInstanceId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@statusInstanceId", statusInstanceId.ToString());
        var scalar = command.ExecuteScalar();
        var value = scalar is null or DBNull
            ? null
            : JsonSerializer.Deserialize<StatusInstance>((string)scalar, JsonOptions);
        if (value is not null)
        {
            _instances[value.StatusInstanceId] = value;
        }

        return value;
    }

    public IReadOnlyList<StatusInstance> LoadBattle(Guid battleInstanceId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `InstanceJson`
            FROM `battle_status_instances`
            WHERE `BattleInstanceId` = @battleInstanceId
            ORDER BY `TargetParticipantId`, `AppliedRound`, `StatusInstanceId`;
            """;
        command.Parameters.AddWithValue("@battleInstanceId", battleInstanceId.ToString());
        using var reader = command.ExecuteReader();
        var values = new List<StatusInstance>();
        while (reader.Read())
        {
            var value = JsonSerializer.Deserialize<StatusInstance>(
                reader.GetString(0),
                JsonOptions);
            if (value is not null)
            {
                _instances[value.StatusInstanceId] = value;
                values.Add(value);
            }
        }

        return values;
    }

    public StatusTriggerPlanRecord? GetTriggerPlan(Guid planId)
    {
        if (_triggerPlans.TryGetValue(planId, out var cached))
        {
            return cached;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `PlanJson`
            FROM `battle_status_trigger_plans`
            WHERE `StatusTriggerExecutionPlanId` = @planId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@planId", planId.ToString());
        var scalar = command.ExecuteScalar();
        var value = scalar is null or DBNull
            ? null
            : JsonSerializer.Deserialize<StatusTriggerPlanRecord>((string)scalar, JsonOptions);
        if (value is not null)
        {
            _triggerPlans[planId] = value;
        }

        return value;
    }

    public StatusTriggerExecutionResult? GetTriggerResult(Guid triggerExecutionId)
    {
        var cached = _triggerPlans.Values
            .SelectMany(value => value.Results)
            .FirstOrDefault(value => value.TriggerExecutionId == triggerExecutionId);
        if (cached is not null)
        {
            return cached;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `ResultJson`
            FROM `battle_status_trigger_results`
            WHERE `TriggerExecutionId` = @triggerExecutionId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@triggerExecutionId", triggerExecutionId.ToString());
        var scalar = command.ExecuteScalar();
        return scalar is null or DBNull
            ? null
            : JsonSerializer.Deserialize<StatusTriggerExecutionResult>((string)scalar, JsonOptions);
    }

    public OperationResult SaveTriggerPlan(StatusTriggerPlanRecord record, int expectedCursor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                command.CommandText = expectedCursor < 0
                    ? """
                      INSERT INTO `battle_status_trigger_plans`
                          (`StatusTriggerExecutionPlanId`, `BattleInstanceId`, `RoundNumber`,
                           `TriggerPhase`, `SourceActionId`, `ResolutionCursor`, `State`,
                           `RecoveryState`, `PlanJson`, `CreatedAtUtc`, `UpdatedAtUtc`)
                      VALUES
                          (@planId, @battleInstanceId, @roundNumber,
                           @triggerPhase, @sourceActionId, @resolutionCursor, @state,
                           @recoveryState, @planJson, @createdAtUtc, @updatedAtUtc);
                      """
                    : """
                      UPDATE `battle_status_trigger_plans`
                      SET `ResolutionCursor` = @resolutionCursor,
                          `State` = @state,
                          `RecoveryState` = @recoveryState,
                          `PlanJson` = @planJson,
                          `UpdatedAtUtc` = @updatedAtUtc
                      WHERE `StatusTriggerExecutionPlanId` = @planId
                        AND `ResolutionCursor` = @expectedCursor;
                      """;
                AddTriggerPlanParameters(command, record);
                command.Parameters.AddWithValue("@expectedCursor", expectedCursor);
                if (command.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return OperationResult.Failure(
                        "status.trigger_cursor_conflict",
                        "Persisted status trigger cursor changed before commit.");
                }
            }

            foreach (var result in record.Results)
            {
                UpsertTriggerResult(connection, transaction, result);
            }

            UpsertRecovery(
                connection,
                transaction,
                "Trigger",
                record.Plan.StatusTriggerExecutionPlanId,
                record.Plan.BattleInstanceId,
                record.RecoveryState,
                record.Results.LastOrDefault()?.FailureCode ?? "",
                record.ResolutionCursor,
                record,
                record.Plan.CreatedAtUtc,
                record.UpdatedAtUtc);

            transaction.Commit();
            _triggerPlans[record.Plan.StatusTriggerExecutionPlanId] = record;
            return OperationResult.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            transaction.Rollback();
            return OperationResult.Failure(
                "status.trigger_plan_duplicate",
                "Status trigger plan or execution order already exists.");
        }
        catch (Exception exception) when (
            exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            transaction.Rollback();
            return OperationResult.Failure("status.persistence_failure", exception.Message);
        }
    }

    public void Append(StatusAuditRecord record)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `battle_status_audit`
                (`AuditId`, `BattleInstanceId`, `RoundNumber`, `StatusApplicationId`,
                 `StatusRemovalId`, `StatusTriggerExecutionPlanId`, `TriggerExecutionId`,
                 `StatusInstanceId`, `StatusDefinitionId`, `IdempotencySafeId`,
                 `Operation`, `Result`, `FailureCode`, `RecoveryState`, `AuditJson`,
                 `CreatedAtUtc`, `CompletedAtUtc`)
            VALUES
                (@auditId, @battleInstanceId, @roundNumber, @statusApplicationId,
                 @statusRemovalId, @planId, @triggerExecutionId,
                 @statusInstanceId, @statusDefinitionId, @idempotencySafeId,
                 @operation, @result, @failureCode, @recoveryState, @auditJson,
                 @createdAtUtc, @completedAtUtc);
            """;
        command.Parameters.AddWithValue("@auditId", record.AuditId.ToString());
        command.Parameters.AddWithValue("@battleInstanceId", record.BattleInstanceId.ToString());
        command.Parameters.AddWithValue("@roundNumber", record.RoundNumber);
        command.Parameters.AddWithValue(
            "@statusApplicationId",
            record.StatusApplicationId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "@statusRemovalId",
            record.StatusRemovalId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "@planId",
            record.StatusTriggerExecutionPlanId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "@triggerExecutionId",
            record.TriggerExecutionId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "@statusInstanceId",
            record.StatusInstanceId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "@statusDefinitionId",
            record.StatusDefinitionId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@idempotencySafeId", record.IdempotencySafeId);
        command.Parameters.AddWithValue("@operation", record.Operation);
        command.Parameters.AddWithValue("@result", record.Result.ToString());
        command.Parameters.AddWithValue("@failureCode", record.FailureCode);
        command.Parameters.AddWithValue("@recoveryState", record.RecoveryState.ToString());
        command.Parameters.AddWithValue("@auditJson", JsonSerializer.Serialize(record, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", record.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@completedAtUtc", record.CompletedAtUtc.UtcDateTime);
        command.ExecuteNonQuery();
        _audit.Enqueue(record);
    }

    private MySqlConnection OpenConnection() =>
        OpenConnectionAsync(CancellationToken.None).GetAwaiter().GetResult();

    private static bool ApplicationExists(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid applicationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM `battle_status_applications`
            WHERE `StatusApplicationId` = @applicationId;
            """;
        command.Parameters.AddWithValue("@applicationId", applicationId.ToString());
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static bool RemovalExists(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid removalId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM `battle_status_removals`
            WHERE `StatusRemovalId` = @removalId;
            """;
        command.Parameters.AddWithValue("@removalId", removalId.ToString());
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static void AddApplicationParameters(
        MySqlCommand command,
        StatusApplicationRecord record,
        string key)
    {
        command.Parameters.AddWithValue(
            "@statusApplicationId",
            record.Request.StatusApplicationId.ToString());
        command.Parameters.AddWithValue(
            "@statusApplicationPlanId",
            record.Plan.StatusApplicationPlanId.ToString());
        command.Parameters.AddWithValue("@statusInstanceId", record.Plan.StatusInstanceId.ToString());
        command.Parameters.AddWithValue("@battleInstanceId", record.Plan.BattleInstanceId.ToString());
        command.Parameters.AddWithValue("@roundNumber", record.Plan.RoundNumber);
        command.Parameters.AddWithValue("@targetParticipantId", record.Plan.TargetParticipantId.ToString());
        command.Parameters.AddWithValue("@statusDefinitionId", record.Plan.StatusDefinitionId);
        command.Parameters.AddWithValue("@idempotencyKeyHash", key);
        command.Parameters.AddWithValue("@payloadHash", record.PayloadHash);
        command.Parameters.AddWithValue("@state", record.State.ToString());
        command.Parameters.AddWithValue("@recoveryState", record.RecoveryState.ToString());
        command.Parameters.AddWithValue("@applicationJson", JsonSerializer.Serialize(record, JsonOptions));
        command.Parameters.AddWithValue(
            "@resultJson",
            record.Result is null
                ? DBNull.Value
                : JsonSerializer.Serialize(record.Result, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", record.Request.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAtUtc", record.UpdatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "@completedAtUtc",
            record.Result?.CompletedAtUtc.UtcDateTime ?? (object)DBNull.Value);
    }

    private static void AddRemovalParameters(
        MySqlCommand command,
        StatusRemovalRecord record,
        string key)
    {
        command.Parameters.AddWithValue("@statusRemovalId", record.Request.StatusRemovalId.ToString());
        command.Parameters.AddWithValue("@statusRemovalPlanId", record.Plan.StatusRemovalPlanId.ToString());
        command.Parameters.AddWithValue("@statusInstanceId", record.Plan.StatusInstanceId.ToString());
        command.Parameters.AddWithValue("@battleInstanceId", record.Plan.BattleInstanceId.ToString());
        command.Parameters.AddWithValue("@roundNumber", record.Plan.RoundNumber);
        command.Parameters.AddWithValue("@targetParticipantId", record.Plan.TargetParticipantId.ToString());
        command.Parameters.AddWithValue("@statusDefinitionId", record.Plan.StatusDefinitionId);
        command.Parameters.AddWithValue("@removalReason", record.Plan.RemovalReason.ToString());
        command.Parameters.AddWithValue("@idempotencyKeyHash", key);
        command.Parameters.AddWithValue("@payloadHash", record.PayloadHash);
        command.Parameters.AddWithValue("@state", record.State.ToString());
        command.Parameters.AddWithValue("@recoveryState", record.RecoveryState.ToString());
        command.Parameters.AddWithValue("@removalJson", JsonSerializer.Serialize(record, JsonOptions));
        command.Parameters.AddWithValue(
            "@resultJson",
            record.Result is null
                ? DBNull.Value
                : JsonSerializer.Serialize(record.Result, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", record.Request.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAtUtc", record.UpdatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "@completedAtUtc",
            record.Result?.CompletedAtUtc.UtcDateTime ?? (object)DBNull.Value);
    }

    private static void UpsertInstance(
        MySqlConnection connection,
        MySqlTransaction transaction,
        StatusInstance instance)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `battle_status_instances`
                (`StatusInstanceId`, `StatusDefinitionId`, `BattleInstanceId`,
                 `TargetParticipantId`, `SourceParticipantId`, `SourceActionId`,
                 `SourceSkillExecutionId`, `SourceEffectExecutionId`,
                 `StackCount`, `MaximumStacks`, `AppliedRound`, `LastRefreshedRound`,
                 `ExpiresAfterRound`, `RemainingRounds`, `LifecycleState`, `TriggerCursor`,
                 `RuntimeVersion`, `DefinitionContentVersion`, `PolicyStatus`, `InstanceJson`,
                 `AppliedAtUtc`, `UpdatedAtUtc`, `RemovedAtUtc`, `RemovalReason`)
            VALUES
                (@statusInstanceId, @statusDefinitionId, @battleInstanceId,
                 @targetParticipantId, @sourceParticipantId, @sourceActionId,
                 @sourceSkillExecutionId, @sourceEffectExecutionId,
                 @stackCount, @maximumStacks, @appliedRound, @lastRefreshedRound,
                 @expiresAfterRound, @remainingRounds, @lifecycleState, @triggerCursor,
                 @runtimeVersion, @definitionContentVersion, @policyStatus, @instanceJson,
                 @appliedAtUtc, @updatedAtUtc, @removedAtUtc, @removalReason)
            ON DUPLICATE KEY UPDATE
                `StackCount` = VALUES(`StackCount`),
                `LastRefreshedRound` = VALUES(`LastRefreshedRound`),
                `ExpiresAfterRound` = VALUES(`ExpiresAfterRound`),
                `RemainingRounds` = VALUES(`RemainingRounds`),
                `LifecycleState` = VALUES(`LifecycleState`),
                `TriggerCursor` = VALUES(`TriggerCursor`),
                `RuntimeVersion` = VALUES(`RuntimeVersion`),
                `InstanceJson` = VALUES(`InstanceJson`),
                `UpdatedAtUtc` = VALUES(`UpdatedAtUtc`),
                `RemovedAtUtc` = VALUES(`RemovedAtUtc`),
                `RemovalReason` = VALUES(`RemovalReason`);
            """;
        command.Parameters.AddWithValue("@statusInstanceId", instance.StatusInstanceId.ToString());
        command.Parameters.AddWithValue("@statusDefinitionId", instance.StatusDefinitionId);
        command.Parameters.AddWithValue("@battleInstanceId", instance.BattleInstanceId.ToString());
        command.Parameters.AddWithValue("@targetParticipantId", instance.TargetParticipantId.ToString());
        command.Parameters.AddWithValue("@sourceParticipantId", instance.SourceParticipantId.ToString());
        command.Parameters.AddWithValue("@sourceActionId", instance.SourceActionId.ToString());
        command.Parameters.AddWithValue(
            "@sourceSkillExecutionId",
            instance.SourceSkillExecutionId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "@sourceEffectExecutionId",
            instance.SourceEffectExecutionId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@stackCount", instance.StackCount);
        command.Parameters.AddWithValue("@maximumStacks", instance.MaximumStacks);
        command.Parameters.AddWithValue("@appliedRound", instance.AppliedRound);
        command.Parameters.AddWithValue("@lastRefreshedRound", instance.LastRefreshedRound);
        command.Parameters.AddWithValue(
            "@expiresAfterRound",
            instance.ExpiresAfterRound ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "@remainingRounds",
            instance.RemainingRounds ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@lifecycleState", instance.LifecycleState.ToString());
        command.Parameters.AddWithValue("@triggerCursor", instance.TriggerCursor.CurrentExecutionOrder);
        command.Parameters.AddWithValue("@runtimeVersion", instance.RuntimeVersion);
        command.Parameters.AddWithValue("@definitionContentVersion", instance.DefinitionContentVersion);
        command.Parameters.AddWithValue("@policyStatus", instance.PolicyStatus.ToString());
        command.Parameters.AddWithValue("@instanceJson", JsonSerializer.Serialize(instance, JsonOptions));
        command.Parameters.AddWithValue("@appliedAtUtc", instance.AppliedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAtUtc", instance.UpdatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "@removedAtUtc",
            instance.RemovedAtUtc?.UtcDateTime ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "@removalReason",
            instance.RemovalReason?.ToString() ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static OperationResult UpdateInstanceLifecycle(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid statusInstanceId,
        long expectedRuntimeVersion,
        long runtimeVersionAfter,
        StatusLifecycleState lifecycleState,
        StatusRemovalReason removalReason,
        DateTimeOffset removedAtUtc)
    {
        StatusInstance? current;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandTimeout = CommandTimeoutSeconds;
            select.CommandText = """
                SELECT `InstanceJson`
                FROM `battle_status_instances`
                WHERE `StatusInstanceId` = @statusInstanceId
                LIMIT 1
                FOR UPDATE;
                """;
            select.Parameters.AddWithValue("@statusInstanceId", statusInstanceId.ToString());
            var scalar = select.ExecuteScalar();
            current = scalar is null or DBNull
                ? null
                : JsonSerializer.Deserialize<StatusInstance>((string)scalar, JsonOptions);
        }

        if (current is null)
        {
            return OperationResult.Failure(
                "status.instance_persistence_missing",
                "Persisted status instance was not found.");
        }

        if (current.RuntimeVersion == runtimeVersionAfter &&
            current.LifecycleState == lifecycleState)
        {
            return OperationResult.Success;
        }

        if (current.RuntimeVersion != expectedRuntimeVersion)
        {
            return OperationResult.Failure(
                "status.instance_version_conflict",
                "Persisted status instance version changed before lifecycle update.");
        }

        var updated = current with
        {
            LifecycleState = lifecycleState,
            RuntimeVersion = runtimeVersionAfter,
            UpdatedAtUtc = removedAtUtc,
            RemovedAtUtc = removedAtUtc,
            RemovalReason = removalReason
        };
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `battle_status_instances`
            SET `LifecycleState` = @lifecycleState,
                `RuntimeVersion` = @runtimeVersion,
                `InstanceJson` = @instanceJson,
                `UpdatedAtUtc` = @updatedAtUtc,
                `RemovedAtUtc` = @removedAtUtc,
                `RemovalReason` = @removalReason
            WHERE `StatusInstanceId` = @statusInstanceId
              AND `RuntimeVersion` = @expectedRuntimeVersion;
            """;
        command.Parameters.AddWithValue("@lifecycleState", lifecycleState.ToString());
        command.Parameters.AddWithValue("@runtimeVersion", runtimeVersionAfter);
        command.Parameters.AddWithValue("@instanceJson", JsonSerializer.Serialize(updated, JsonOptions));
        command.Parameters.AddWithValue("@updatedAtUtc", removedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@removedAtUtc", removedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@removalReason", removalReason.ToString());
        command.Parameters.AddWithValue("@statusInstanceId", statusInstanceId.ToString());
        command.Parameters.AddWithValue("@expectedRuntimeVersion", expectedRuntimeVersion);
        return command.ExecuteNonQuery() == 1
            ? OperationResult.Success
            : OperationResult.Failure(
                "status.instance_version_conflict",
                "Persisted status instance version changed before lifecycle update.");
    }

    private static void UpsertParticipantStatusVersion(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid battleInstanceId,
        Guid participantId,
        long statusVersion,
        DateTimeOffset updatedAtUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `battle_status_participant_versions`
                (`BattleInstanceId`, `ParticipantId`, `StatusVersion`, `UpdatedAtUtc`)
            VALUES
                (@battleInstanceId, @participantId, @statusVersion, @updatedAtUtc)
            ON DUPLICATE KEY UPDATE
                `StatusVersion` = GREATEST(`StatusVersion`, VALUES(`StatusVersion`)),
                `UpdatedAtUtc` = IF(
                    VALUES(`StatusVersion`) >= `StatusVersion`,
                    VALUES(`UpdatedAtUtc`),
                    `UpdatedAtUtc`);
            """;
        command.Parameters.AddWithValue("@battleInstanceId", battleInstanceId.ToString());
        command.Parameters.AddWithValue("@participantId", participantId.ToString());
        command.Parameters.AddWithValue("@statusVersion", statusVersion);
        command.Parameters.AddWithValue("@updatedAtUtc", updatedAtUtc.UtcDateTime);
        command.ExecuteNonQuery();
    }

    private static void UpsertRecovery<T>(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string operationType,
        Guid operationId,
        Guid battleInstanceId,
        StatusRecoveryState recoveryState,
        string failureCode,
        int resolutionCursor,
        T record,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        if (recoveryState == StatusRecoveryState.NotRequired)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `battle_status_recovery`
                (`RecoveryId`, `BattleInstanceId`, `OperationType`, `OperationId`,
                 `RecoveryState`, `FailureCode`, `ResolutionCursor`, `RecoveryJson`,
                 `CreatedAtUtc`, `UpdatedAtUtc`, `CompletedAtUtc`)
            VALUES
                (@recoveryId, @battleInstanceId, @operationType, @operationId,
                 @recoveryState, @failureCode, @resolutionCursor, @recoveryJson,
                 @createdAtUtc, @updatedAtUtc, @completedAtUtc)
            ON DUPLICATE KEY UPDATE
                `RecoveryState` = VALUES(`RecoveryState`),
                `FailureCode` = VALUES(`FailureCode`),
                `ResolutionCursor` = VALUES(`ResolutionCursor`),
                `RecoveryJson` = VALUES(`RecoveryJson`),
                `UpdatedAtUtc` = VALUES(`UpdatedAtUtc`),
                `CompletedAtUtc` = VALUES(`CompletedAtUtc`);
            """;
        command.Parameters.AddWithValue(
            "@recoveryId",
            BattleRuntimeHash.DeterministicGuid(
                $"status-recovery:{operationType}:{operationId:N}").ToString());
        command.Parameters.AddWithValue("@battleInstanceId", battleInstanceId.ToString());
        command.Parameters.AddWithValue("@operationType", operationType);
        command.Parameters.AddWithValue("@operationId", operationId.ToString());
        command.Parameters.AddWithValue("@recoveryState", recoveryState.ToString());
        command.Parameters.AddWithValue(
            "@failureCode",
            string.IsNullOrWhiteSpace(failureCode)
                ? "status.recovery_pending"
                : failureCode);
        command.Parameters.AddWithValue("@resolutionCursor", resolutionCursor);
        command.Parameters.AddWithValue("@recoveryJson", JsonSerializer.Serialize(record, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAtUtc", updatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "@completedAtUtc",
            recoveryState == StatusRecoveryState.Recovered
                ? updatedAtUtc.UtcDateTime
                : DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void UpsertIdempotency<T>(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string scope,
        string key,
        string payloadHash,
        Guid battleId,
        Guid operationId,
        T? result,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? completedAtUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `battle_status_idempotency`
                (`Scope`, `IdempotencyKeyHash`, `OperationFingerprintSha256`, `BattleInstanceId`,
                 `OperationId`, `ResultJson`, `CreatedAtUtc`, `CompletedAtUtc`)
            VALUES
                (@scope, @idempotencyKeyHash, @payloadHash, @battleInstanceId,
                 @operationId, @resultJson, @createdAtUtc, @completedAtUtc)
            ON DUPLICATE KEY UPDATE
                `ResultJson` = VALUES(`ResultJson`),
                `CompletedAtUtc` = VALUES(`CompletedAtUtc`);
            """;
        command.Parameters.AddWithValue("@scope", scope);
        command.Parameters.AddWithValue("@idempotencyKeyHash", key);
        command.Parameters.AddWithValue("@payloadHash", payloadHash);
        command.Parameters.AddWithValue("@battleInstanceId", battleId.ToString());
        command.Parameters.AddWithValue("@operationId", operationId.ToString());
        command.Parameters.AddWithValue(
            "@resultJson",
            result is null ? DBNull.Value : JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "@completedAtUtc",
            completedAtUtc?.UtcDateTime ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void AddTriggerPlanParameters(
        MySqlCommand command,
        StatusTriggerPlanRecord record)
    {
        command.Parameters.AddWithValue(
            "@planId",
            record.Plan.StatusTriggerExecutionPlanId.ToString());
        command.Parameters.AddWithValue("@battleInstanceId", record.Plan.BattleInstanceId.ToString());
        command.Parameters.AddWithValue("@roundNumber", record.Plan.RoundNumber);
        command.Parameters.AddWithValue("@triggerPhase", record.Plan.TriggerPhase.ToString());
        command.Parameters.AddWithValue(
            "@sourceActionId",
            (record.Plan.SourceActionId ?? Guid.Empty).ToString());
        command.Parameters.AddWithValue("@resolutionCursor", record.ResolutionCursor);
        command.Parameters.AddWithValue("@state", record.State.ToString());
        command.Parameters.AddWithValue("@recoveryState", record.RecoveryState.ToString());
        command.Parameters.AddWithValue("@planJson", JsonSerializer.Serialize(record, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", record.Plan.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAtUtc", record.UpdatedAtUtc.UtcDateTime);
    }

    private static void UpsertTriggerResult(
        MySqlConnection connection,
        MySqlTransaction transaction,
        StatusTriggerExecutionResult result)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `battle_status_trigger_results`
                (`TriggerExecutionId`, `StatusTriggerExecutionPlanId`, `StatusInstanceId`,
                 `TriggerIndex`, `ExecutionOrder`, `State`, `Result`, `FailureCode`,
                 `Damage`, `Heal`, `HpBefore`, `HpAfter`, `RecoveryState`,
                 `ResultJson`, `CompletedAtUtc`)
            VALUES
                (@triggerExecutionId, @planId, @statusInstanceId,
                 @triggerIndex, @executionOrder, @state, @result, @failureCode,
                 @damage, @heal, @hpBefore, @hpAfter, @recoveryState,
                 @resultJson, @completedAtUtc)
            ON DUPLICATE KEY UPDATE
                `State` = VALUES(`State`),
                `Result` = VALUES(`Result`),
                `FailureCode` = VALUES(`FailureCode`),
                `Damage` = VALUES(`Damage`),
                `Heal` = VALUES(`Heal`),
                `HpBefore` = VALUES(`HpBefore`),
                `HpAfter` = VALUES(`HpAfter`),
                `RecoveryState` = VALUES(`RecoveryState`),
                `ResultJson` = VALUES(`ResultJson`),
                `CompletedAtUtc` = VALUES(`CompletedAtUtc`);
            """;
        command.Parameters.AddWithValue("@triggerExecutionId", result.TriggerExecutionId.ToString());
        command.Parameters.AddWithValue("@planId", result.StatusTriggerExecutionPlanId.ToString());
        command.Parameters.AddWithValue("@statusInstanceId", result.StatusInstanceId.ToString());
        command.Parameters.AddWithValue("@triggerIndex", result.TriggerIndex);
        command.Parameters.AddWithValue("@executionOrder", result.ExecutionOrder);
        command.Parameters.AddWithValue("@state", result.State.ToString());
        command.Parameters.AddWithValue("@result", result.ResultCode.ToString());
        command.Parameters.AddWithValue("@failureCode", result.FailureCode);
        command.Parameters.AddWithValue("@damage", result.Damage);
        command.Parameters.AddWithValue("@heal", result.Heal);
        command.Parameters.AddWithValue("@hpBefore", result.HpBefore);
        command.Parameters.AddWithValue("@hpAfter", result.HpAfter);
        command.Parameters.AddWithValue("@recoveryState", result.RecoveryState.ToString());
        command.Parameters.AddWithValue("@resultJson", JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("@completedAtUtc", result.CompletedAtUtc.UtcDateTime);
        command.ExecuteNonQuery();
    }
}


