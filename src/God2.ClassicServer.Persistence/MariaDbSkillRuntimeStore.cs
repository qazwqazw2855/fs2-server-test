using System.Collections.Concurrent;
using System.Text.Json;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbSkillDefinitionRepository :
    MariaDbRuntimeRepository,
    ISkillDefinitionRepository
{
    public MariaDbSkillDefinitionRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<IReadOnlyList<SkillDefinitionRecord>> LoadAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var records = new List<SkillDefinitionRecord>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
            SELECT `Id`, `Code`, COALESCE(`NameZhTw`, `Name`) AS `Name`, `MaxLevel`, `RequiredLevel`,
                   `RecoveryStatus`
            FROM `skills`
            ORDER BY `Id`;
            """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt32("Id");
                const string raw = "{}";
                records.Add(new SkillDefinitionRecord(
                    id,
                    reader.GetString("Code"),
                    reader.GetString("Name"),
                    "",
                    reader.IsDBNull(reader.GetOrdinal("MaxLevel")) ? null : reader.GetInt32("MaxLevel"),
                    reader.IsDBNull(reader.GetOrdinal("RequiredLevel")) ? null : reader.GetInt32("RequiredLevel"),
                    SkillCategory.Unknown,
                    SkillActionCategory.Unknown,
                    SkillTargetPolicyType.Unknown,
                    new SkillCostDefinition(
                        SkillResourceType.Unknown,
                        0,
                        CombatPolicyStatus.EvidenceBlocked,
                        raw),
                    new SkillCooldownDefinition(
                        null,
                        null,
                        null,
                        null,
                        CombatPolicyStatus.EvidenceBlocked,
                        raw),
                    new SkillUsageDefinition(
                        null,
                        CombatPolicyStatus.EvidenceBlocked,
                        raw),
                    [],
                    true,
                    "legacy-skill-catalog-formal",
                    CombatPolicyStatus.EvidenceBlocked,
                    CombatPolicyStatus.EvidenceBlocked,
                    raw));
            }
        }

        if (!await PublicBetaSkillEffectTableExistsAsync(connection, cancellationToken))
        {
            return records;
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                SELECT `global_record_id`,`name_zh_tw`,`description_zh_tw`,`skill_level` AS `level_candidate`,
                       `implementation_family`,`effect_kind`,`status_or_axis`,`mp_cost`,
                       `target_context`,`target_scope`,`numeric_effect_fields`,`v0_to_v23_nonzero`,
                       `minimum_working_server_rule`,`provisional_success_rule`,
                       `provisional_duration_rule`,`enabled`
                FROM `god2_game`.`public_beta_skill_effect_v0`
                ORDER BY `global_record_id`;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                records.Add(MapPublicBetaSkill(reader));
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

    private static SkillDefinitionRecord MapPublicBetaSkill(MySqlDataReader reader)
    {
        var id = reader.GetInt32("global_record_id");
        var family = NormalizeImplementationFamily(reader.GetString("implementation_family"));
        var effectKind = NormalizeEffectKind(reader.GetString("effect_kind"));
        var axis = NormalizeStatusAxis(NullableString(reader, "status_or_axis") ?? "");
        var level = NullableInt(reader, "level_candidate") ?? 1;
        var mpCost = NullableInt(reader, "mp_cost") ?? 0;
        var enabled = reader.GetBoolean("enabled") && !string.Equals(family, "revive", StringComparison.Ordinal);
        var raw = JsonSerializer.Serialize(new
        {
            Source = "Data2/Fight/Spg/cbspec2.bin public beta skill effect v0",
            GlobalRecordId = id,
            ImplementationFamily = family,
            EffectKind = effectKind,
            StatusOrAxis = axis,
            MpCost = mpCost,
            TargetContext = NullableInt(reader, "target_context"),
            TargetScope = NullableInt(reader, "target_scope"),
            NumericEffectFields = NullableString(reader, "numeric_effect_fields"),
            RawEffectWords = NullableString(reader, "v0_to_v23_nonzero"),
            MinimumWorkingServerRule = reader.GetString("minimum_working_server_rule"),
            ProvisionalSuccessRule = NullableString(reader, "provisional_success_rule"),
            ProvisionalDurationRule = NullableString(reader, "provisional_duration_rule"),
            EvidenceBoundary = "god2_game.public_beta_skill_effect_v0"
        });
        var effectType = ResolveEffectType(effectKind, family);
        var target = ResolveTargetPolicy(effectKind);
        var policy = enabled ? CombatPolicyStatus.ContentBacked : CombatPolicyStatus.EvidenceBlocked;
        var effects = BuildPublicBetaEffects(id, family, effectKind, axis, level, mpCost, target, policy, raw);
        return new SkillDefinitionRecord(
            id,
            $"public-beta-skill-{id}",
            NullableString(reader, "name_zh_tw") ?? NullableString(reader, "description_zh_tw") ?? $"public-beta-skill-{id}",
            NullableString(reader, "description_zh_tw") ?? "",
            level,
            0,
            effectKind switch
            {
                "heal" => SkillCategory.Healing,
                "buff" or "cleanse" => SkillCategory.Support,
                "debuff" => SkillCategory.Offensive,
                "revive" => SkillCategory.Healing,
                _ => SkillCategory.Unknown
            },
            effectKind switch
            {
                "heal" => SkillActionCategory.Heal,
                "revive" => SkillActionCategory.Revive,
                "buff" or "debuff" => SkillActionCategory.Status,
                "cleanse" => SkillActionCategory.Utility,
                _ => SkillActionCategory.Unknown
            },
            target,
            new SkillCostDefinition(SkillResourceType.None, 0, CombatPolicyStatus.ContentBacked, raw),
            new SkillCooldownDefinition(0, null, null, null, CombatPolicyStatus.ContentBacked, raw),
            new SkillUsageDefinition(null, CombatPolicyStatus.ContentBacked, raw),
            effects,
            enabled,
            $"public-beta-skill-effect-v0-{reader.GetString("source_sha256")[..12]}",
            policy,
            CombatPolicyStatus.ContentBacked,
            raw);

        static SkillEffectType ResolveEffectType(string kind, string implementationFamily) =>
            kind switch
            {
                "heal" => SkillEffectType.Heal,
                "buff" or "debuff" => SkillEffectType.ApplyStatus,
                "cleanse" => SkillEffectType.RemoveStatus,
                "revive" => SkillEffectType.Revive,
                _ when implementationFamily.Contains("buff", StringComparison.Ordinal) => SkillEffectType.ApplyStatus,
                _ => SkillEffectType.NoOp
            };
    }

    private static IReadOnlyList<SkillEffectDefinition> BuildPublicBetaEffects(
        int id,
        string family,
        string effectKind,
        string axis,
        int level,
        int mpCost,
        SkillTargetPolicyType target,
        CombatPolicyStatus policy,
        string raw)
    {
        if (effectKind == "cleanse" && axis == "all_negative")
        {
            return new[] { "seal", "sleep", "petrify", "poison", "confusion" }
                .Select((value, index) => Effect(id, index, SkillEffectType.RemoveStatus, target, StatusId(value), policy, raw))
                .ToArray();
        }

        var value = effectKind switch
        {
            "heal" => Math.Max(1, checked((level * 25) + (mpCost * 2))),
            "buff" => checked(920000 + id),
            "debuff" => StatusId(axis),
            "cleanse" => StatusId(axis),
            "revive" => level switch { 1 => 25, 2 => 50, _ => 75 },
            _ when family.Contains("buff", StringComparison.Ordinal) => checked(920000 + id),
            _ => 0
        };
        return [Effect(id, 0, ResolveEffectType(effectKind, family), target, value, policy, raw)];

        static SkillEffectType ResolveEffectType(string kind, string implementationFamily) =>
            kind switch
            {
                "heal" => SkillEffectType.Heal,
                "buff" or "debuff" => SkillEffectType.ApplyStatus,
                "cleanse" => SkillEffectType.RemoveStatus,
                "revive" => SkillEffectType.Revive,
                _ when implementationFamily.Contains("buff", StringComparison.Ordinal) => SkillEffectType.ApplyStatus,
                _ => SkillEffectType.NoOp
            };
    }

    private static SkillEffectDefinition Effect(
        int skillId,
        int index,
        SkillEffectType type,
        SkillTargetPolicyType target,
        long value,
        CombatPolicyStatus policy,
        string raw) =>
        new(
            $"public-beta-skill-{skillId}-effect-{index}",
            skillId,
            index,
            type,
            target,
            type == SkillEffectType.Heal ? "public_beta_provisional_flat_heal" : "public_beta_status_reference",
            value,
            null,
            CombatDamageType.Unknown,
            target == SkillTargetPolicyType.DeadAlly,
            true,
            target is SkillTargetPolicyType.SingleAlly or SkillTargetPolicyType.AllAllies or SkillTargetPolicyType.DeadAlly,
            target is SkillTargetPolicyType.SingleEnemy or SkillTargetPolicyType.AllEnemies,
            target is SkillTargetPolicyType.AllAllies or SkillTargetPolicyType.AllEnemies ? null : 1,
            policy,
            "public-beta-skill-effect-v0",
            raw);

    private static SkillTargetPolicyType ResolveTargetPolicy(string effectKind) =>
        effectKind switch
        {
            "debuff" => SkillTargetPolicyType.SingleEnemy,
            "revive" => SkillTargetPolicyType.DeadAlly,
            _ => SkillTargetPolicyType.SingleAlly
        };

    internal static int StatusId(string axis) =>
        axis switch
        {
            "seal" => 910001,
            "sleep" => 910002,
            "petrify" => 910003,
            "poison" => 910004,
            "confusion" => 910005,
            _ => 0
        };

    private static string? NullableString(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(name);

    private static int? NullableInt(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt32(name);

    private static string NormalizeEffectKind(string value) => value switch
    {
        "增益" => "buff",
        "負面" => "debuff",
        "淨化" => "cleanse",
        "治療" => "heal",
        "復活" => "revive",
        _ => value
    };
    private static string NormalizeImplementationFamily(string value) => value switch
    {
        "屬性增益" => "stat_buff",
        "五行陣法增益" => "element_formation_buff",
        "五行符咒攻擊增益" => "element_talisman_attack_buff",
        "五行轉換增益" => "element_transform_buff",
        "生命治療" => "heal_hp",
        "通用淨化" => "cleanse_general",
        "混亂負面" => "debuff_confusion",
        "石化負面" => "debuff_petrify",
        "中毒負面" => "debuff_poison",
        "封印負面" => "debuff_seal",
        "睡眠負面" => "debuff_sleep",
        "復活" => "revive",
        "淨化混亂" => "cleanse_confusion",
        "淨化石化" => "cleanse_petrify",
        "淨化中毒" => "cleanse_poison",
        "淨化封印" => "cleanse_seal",
        "淨化睡眠" => "cleanse_sleep",
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

public sealed class MariaDbSkillRuntimeStore :
    MariaDbRuntimeRepository,
    ISkillExecutionStore,
    ISkillCostReservationStore,
    ISkillUsageStore,
    ISkillAuditLedger,
    ISkillInspectorSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, SkillExecutionRecord> _executions = [];
    private readonly ConcurrentDictionary<Guid, SkillCostReservation> _costs = [];
    private readonly ConcurrentDictionary<
        (Guid BattleInstanceId, Guid ParticipantId, int SkillId),
        SkillUsageState> _usage = [];
    private readonly ConcurrentQueue<SkillAuditRecord> _audit = [];

    public MariaDbSkillRuntimeStore(DatabaseOptions options)
        : base(options)
    {
    }

    public IReadOnlyList<SkillExecutionRecord> Snapshot => Executions;

    public IReadOnlyList<SkillExecutionRecord> Executions =>
        _executions.Values.OrderBy(value => value.Plan.CreatedAtUtc).ToArray();

    public IReadOnlyList<SkillCostReservation> Reservations => CostReservations;

    public IReadOnlyList<SkillCostReservation> CostReservations =>
        _costs.Values.OrderBy(value => value.CreatedAtUtc).ToArray();

    public IReadOnlyList<SkillUsageState> Usage =>
        _usage.Values.OrderBy(value => value.ParticipantId).ToArray();

    public IReadOnlyList<SkillAuditRecord> Audit => _audit.ToArray();

    IReadOnlyList<SkillUsageState> ISkillUsageStore.Snapshot => Usage;

    IReadOnlyList<SkillAuditRecord> ISkillAuditLedger.Snapshot => Audit;

    public SkillExecutionRecord? Get(Guid skillExecutionId)
    {
        if (_executions.TryGetValue(skillExecutionId, out var cached))
        {
            return cached;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `PlanJson`
            FROM `skill_executions`
            WHERE `SkillExecutionId` = @skillExecutionId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@skillExecutionId", skillExecutionId.ToString());
        var scalar = command.ExecuteScalar();
        var record = scalar is null or DBNull
            ? null
            : JsonSerializer.Deserialize<SkillExecutionRecord>((string)scalar, JsonOptions);
        if (record is not null)
        {
            _executions[record.Plan.SkillExecutionId] = record;
            CacheDetails(record);
        }

        return record;
    }

    public SkillExecutionRecord? FindByIdempotency(string idempotencyKey) =>
        Get(BattleRuntimeHash.DeterministicGuid(idempotencyKey));

    public OperationResult Save(SkillExecutionRecord record, int expectedCursor)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                if (expectedCursor < 0)
                {
                    command.CommandText = """
                        INSERT INTO `skill_executions`
                            (`SkillExecutionId`, `BattleInstanceId`, `RoundNumber`, `BattleActionId`,
                             `ParticipantId`, `SkillDefinitionId`, `State`, `ResolutionCursor`,
                             `RecoveryState`, `ExecutionFingerprintSha256`, `PlanJson`, `ResultJson`,
                             `StartedAtUtc`, `UpdatedAtUtc`, `CompletedAtUtc`)
                        VALUES
                            (@skillExecutionId, @battleInstanceId, @roundNumber, @battleActionId,
                             @participantId, @skillDefinitionId, @state, @resolutionCursor,
                             @recoveryState, @payloadHash, @planJson, @resultJson,
                             @startedAtUtc, @updatedAtUtc, @completedAtUtc);
                        """;
                }
                else
                {
                    command.CommandText = """
                        UPDATE `skill_executions`
                        SET `State` = @state,
                            `ResolutionCursor` = @resolutionCursor,
                            `RecoveryState` = @recoveryState,
                            `ExecutionFingerprintSha256` = @payloadHash,
                            `PlanJson` = @planJson,
                            `ResultJson` = @resultJson,
                            `UpdatedAtUtc` = @updatedAtUtc,
                            `CompletedAtUtc` = @completedAtUtc
                        WHERE `SkillExecutionId` = @skillExecutionId
                          AND `ResolutionCursor` = @expectedCursor;
                        """;
                    command.Parameters.AddWithValue("@expectedCursor", expectedCursor);
                }

                AddExecutionParameters(command, record);
                if (command.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return OperationResult.Failure(
                        "skill.execution_version_conflict",
                        "Persisted skill execution cursor changed before commit.");
                }
            }

            SyncEffects(connection, transaction, record);
            if (record.CostReservation is not null)
            {
                UpsertCost(connection, transaction, record.CostReservation);
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                command.CommandText = """
                    INSERT INTO `skill_idempotency`
                        (`Scope`, `IdempotencyKeyHash`, `OperationFingerprintSha256`, `SkillExecutionId`,
                         `ResultJson`, `CreatedAtUtc`, `CompletedAtUtc`)
                    VALUES
                        ('Execution', @idempotencyKeyHash, @payloadHash, @skillExecutionId,
                         @resultJson, @createdAtUtc, @completedAtUtc)
                    ON DUPLICATE KEY UPDATE
                        `ResultJson` = VALUES(`ResultJson`),
                        `CompletedAtUtc` = VALUES(`CompletedAtUtc`);
                    """;
                command.Parameters.AddWithValue(
                    "@idempotencyKeyHash",
                    BattleRuntimeHash.PersistenceKey(record.Plan.SkillExecutionId.ToString("N")));
                command.Parameters.AddWithValue("@payloadHash", record.PayloadHash);
                command.Parameters.AddWithValue("@skillExecutionId", record.Plan.SkillExecutionId.ToString());
                command.Parameters.AddWithValue(
                    "@resultJson",
                    record.Result is null ? DBNull.Value : JsonSerializer.Serialize(record.Result, JsonOptions));
                command.Parameters.AddWithValue("@createdAtUtc", record.Plan.CreatedAtUtc.UtcDateTime);
                command.Parameters.AddWithValue(
                    "@completedAtUtc",
                    record.Result?.CompletedAtUtc?.UtcDateTime ?? (object)DBNull.Value);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            _executions[record.Plan.SkillExecutionId] = record;
            CacheDetails(record);
            return OperationResult.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            transaction.Rollback();
            return OperationResult.Failure("skill.execution_duplicate", "Skill execution already exists.");
        }
        catch (Exception exception) when (
            exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            transaction.Rollback();
            return OperationResult.Failure("skill.persistence_failure", exception.Message);
        }
    }

    public void SeedTestResource(Guid participantId, long amount) =>
        throw new InvalidOperationException("TestOnly battle resources are not stored in MariaDB.");

    public long GetAvailableTestResource(Guid participantId) => 0;

    public OperationResult<SkillCostReservation> Reserve(
        SkillExecutionPlan plan,
        DateTimeOffset now)
    {
        if (plan.CostPlan.ResourceType != SkillResourceType.None ||
            plan.CostPlan.Amount != 0)
        {
            return OperationResult<SkillCostReservation>.Failure(
                "skill.cost_evidence_blocked",
                "Production resource reservation is not evidence-backed.");
        }

        var reservationId = BattleRuntimeHash.DeterministicGuid($"skill-cost:{plan.SkillExecutionId:N}");
        if (_costs.TryGetValue(reservationId, out var replay))
        {
            return OperationResult<SkillCostReservation>.Success(replay);
        }

        var reservation = new SkillCostReservation(
            reservationId,
            plan.SkillExecutionId,
            plan.BattleInstanceId,
            plan.RoundNumber,
            plan.ParticipantId,
            plan.SkillDefinitionId,
            plan.CostPlan.ResourceType,
            plan.CostPlan.Amount,
            plan.ExpectedParticipantVersion,
            SkillCostReservationState.Reserved,
            now,
            null,
            null,
            BattleRuntimeHash.SafeId(plan.SkillExecutionId.ToString("N")),
            plan.CorrelationId);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        UpsertCost(connection, transaction, reservation);
        transaction.Commit();
        _costs[reservationId] = reservation;
        return OperationResult<SkillCostReservation>.Success(reservation);
    }

    public OperationResult<SkillCostReservation> Commit(Guid reservationId, DateTimeOffset now) =>
        ChangeCostState(
            reservationId,
            SkillCostReservationState.Reserved,
            SkillCostReservationState.Committed,
            now);

    public OperationResult<SkillCostReservation> Release(Guid reservationId, DateTimeOffset now) =>
        ChangeCostState(
            reservationId,
            SkillCostReservationState.Reserved,
            SkillCostReservationState.Released,
            now);

    SkillCostReservation? ISkillCostReservationStore.Get(Guid reservationId) =>
        _costs.GetValueOrDefault(reservationId);

    public SkillUsageState? Get(
        Guid battleInstanceId,
        Guid participantId,
        int skillDefinitionId)
    {
        var key = (battleInstanceId, participantId, skillDefinitionId);
        if (_usage.TryGetValue(key, out var cached))
        {
            return cached;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `UsageJson`
            FROM `battle_skill_usage`
            WHERE `BattleInstanceId` = @battleInstanceId
              AND `ParticipantId` = @participantId
              AND `SkillDefinitionId` = @skillDefinitionId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@battleInstanceId", battleInstanceId.ToString());
        command.Parameters.AddWithValue("@participantId", participantId.ToString());
        command.Parameters.AddWithValue("@skillDefinitionId", skillDefinitionId);
        var scalar = command.ExecuteScalar();
        var state = scalar is null or DBNull
            ? null
            : JsonSerializer.Deserialize<SkillUsageState>((string)scalar, JsonOptions);
        if (state is not null)
        {
            _usage[(state.BattleInstanceId, state.ParticipantId, state.SkillDefinitionId)] = state;
        }

        return state;
    }

    public OperationResult<SkillUsageState> Save(SkillUsageState state, long expectedVersion)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = expectedVersion == 0
            ? """
              INSERT INTO `battle_skill_usage`
                  (`BattleInstanceId`, `ParticipantId`, `SkillDefinitionId`, `LastUsedRound`,
                   `AvailableAtRound`, `UsageCount`, `RuntimeVersion`, `IdempotencySafeId`,
                   `PolicyStatus`, `UsageJson`, `UpdatedAtUtc`)
              VALUES
                  (@battleInstanceId, @participantId, @skillDefinitionId, @lastUsedRound,
                   @availableAtRound, @usageCount, @runtimeVersion, @idempotencySafeId,
                   @policyStatus, @usageJson, UTC_TIMESTAMP(6));
              """
            : """
              UPDATE `battle_skill_usage`
              SET `LastUsedRound` = @lastUsedRound,
                  `AvailableAtRound` = @availableAtRound,
                  `UsageCount` = @usageCount,
                  `RuntimeVersion` = @runtimeVersion,
                  `IdempotencySafeId` = @idempotencySafeId,
                  `PolicyStatus` = @policyStatus,
                  `UsageJson` = @usageJson,
                  `UpdatedAtUtc` = UTC_TIMESTAMP(6)
              WHERE `BattleInstanceId` = @battleInstanceId
                AND `ParticipantId` = @participantId
                AND `SkillDefinitionId` = @skillDefinitionId
                AND `RuntimeVersion` = @expectedVersion;
              """;
        AddUsageParameters(command, state);
        command.Parameters.AddWithValue("@expectedVersion", expectedVersion);
        try
        {
            if (command.ExecuteNonQuery() != 1)
            {
                return OperationResult<SkillUsageState>.Failure(
                    "skill.usage_version_conflict",
                    "Persisted skill usage version changed before commit.");
            }

            _usage[(state.BattleInstanceId, state.ParticipantId, state.SkillDefinitionId)] = state;
            return OperationResult<SkillUsageState>.Success(state);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            return OperationResult<SkillUsageState>.Failure(
                "skill.usage_version_conflict",
                "Persisted skill usage already exists.");
        }
    }

    public void Append(SkillAuditRecord record)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `skill_audit`
                (`AuditId`, `SkillExecutionId`, `BattleInstanceId`, `RoundNumber`,
                 `EffectExecutionId`, `ParticipantId`, `TargetParticipantId`,
                 `SkillDefinitionId`, `IdempotencySafeId`, `Result`, `FailureCode`,
                 `ResolutionCursor`, `RecoveryState`, `AuditJson`, `CreatedAtUtc`, `CompletedAtUtc`)
            VALUES
                (@auditId, @skillExecutionId, @battleInstanceId, @roundNumber,
                 @effectExecutionId, @participantId, @targetParticipantId,
                 @skillDefinitionId, @idempotencySafeId, @result, @failureCode,
                 @resolutionCursor, @recoveryState, @auditJson, @createdAtUtc, @completedAtUtc);
            """;
        command.Parameters.AddWithValue("@auditId", record.AuditId.ToString());
        command.Parameters.AddWithValue("@skillExecutionId", record.SkillExecutionId.ToString());
        command.Parameters.AddWithValue("@battleInstanceId", record.BattleInstanceId.ToString());
        command.Parameters.AddWithValue("@roundNumber", record.RoundNumber);
        command.Parameters.AddWithValue(
            "@effectExecutionId",
            record.EffectExecutionId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@participantId", record.ParticipantId.ToString());
        command.Parameters.AddWithValue(
            "@targetParticipantId",
            record.TargetParticipantId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@skillDefinitionId", record.SkillDefinitionId);
        command.Parameters.AddWithValue("@idempotencySafeId", record.IdempotencySafeId);
        command.Parameters.AddWithValue("@result", record.Result.ToString());
        command.Parameters.AddWithValue("@failureCode", record.FailureCode);
        command.Parameters.AddWithValue("@resolutionCursor", record.ResolutionCursor);
        command.Parameters.AddWithValue("@recoveryState", record.RecoveryState.ToString());
        command.Parameters.AddWithValue("@auditJson", JsonSerializer.Serialize(record, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", record.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@completedAtUtc", record.CompletedAtUtc.UtcDateTime);
        command.ExecuteNonQuery();
        _audit.Enqueue(record);
    }

    private MySqlConnection OpenConnection() =>
        OpenConnectionAsync(CancellationToken.None).GetAwaiter().GetResult();

    private OperationResult<SkillCostReservation> ChangeCostState(
        Guid reservationId,
        SkillCostReservationState expected,
        SkillCostReservationState next,
        DateTimeOffset now)
    {
        if (!_costs.TryGetValue(reservationId, out var current))
        {
            return OperationResult<SkillCostReservation>.Failure(
                "skill.cost_reservation_missing",
                "Cost reservation was not found.");
        }

        if (current.State == next)
        {
            return OperationResult<SkillCostReservation>.Success(current);
        }

        if (current.State != expected)
        {
            return OperationResult<SkillCostReservation>.Failure(
                "skill.cost_reservation_conflict",
                "Cost reservation state conflicts with the requested transition.");
        }

        var updated = current with
        {
            State = next,
            CommittedAtUtc = next == SkillCostReservationState.Committed ? now : current.CommittedAtUtc,
            ReleasedAtUtc = next == SkillCostReservationState.Released ? now : current.ReleasedAtUtc
        };
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        UpsertCost(connection, transaction, updated);
        transaction.Commit();
        _costs[reservationId] = updated;
        return OperationResult<SkillCostReservation>.Success(updated);
    }

    private static void AddExecutionParameters(
        MySqlCommand command,
        SkillExecutionRecord record)
    {
        command.Parameters.AddWithValue("@skillExecutionId", record.Plan.SkillExecutionId.ToString());
        command.Parameters.AddWithValue("@battleInstanceId", record.Plan.BattleInstanceId.ToString());
        command.Parameters.AddWithValue("@roundNumber", record.Plan.RoundNumber);
        command.Parameters.AddWithValue("@battleActionId", record.Plan.BattleActionId.ToString());
        command.Parameters.AddWithValue("@participantId", record.Plan.ParticipantId.ToString());
        command.Parameters.AddWithValue("@skillDefinitionId", record.Plan.SkillDefinitionId);
        command.Parameters.AddWithValue("@state", record.State.ToString());
        command.Parameters.AddWithValue("@resolutionCursor", record.ResolutionCursor);
        command.Parameters.AddWithValue("@recoveryState", record.RecoveryState.ToString());
        command.Parameters.AddWithValue("@payloadHash", record.PayloadHash);
        command.Parameters.AddWithValue("@planJson", JsonSerializer.Serialize(record, JsonOptions));
        command.Parameters.AddWithValue(
            "@resultJson",
            record.Result is null ? DBNull.Value : JsonSerializer.Serialize(record.Result, JsonOptions));
        command.Parameters.AddWithValue("@startedAtUtc", record.Plan.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAtUtc", record.UpdatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "@completedAtUtc",
            record.Result?.CompletedAtUtc?.UtcDateTime ?? (object)DBNull.Value);
    }

    private static void SyncEffects(
        MySqlConnection connection,
        MySqlTransaction transaction,
        SkillExecutionRecord record)
    {
        foreach (var effect in record.EffectResults)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `skill_effect_executions`
                    (`EffectExecutionId`, `SkillExecutionId`, `EffectDefinitionId`,
                     `EffectIndex`, `TargetIndex`, `TargetParticipantId`, `EffectType`,
                     `State`, `HpBefore`, `Damage`, `Heal`, `HpAfter`,
                     `RuntimeVersionBefore`, `RuntimeVersionAfter`, `FailureCode`,
                     `EffectJson`, `CompletedAtUtc`)
                VALUES
                    (@effectExecutionId, @skillExecutionId, @effectDefinitionId,
                     @effectIndex, @targetIndex, @targetParticipantId, @effectType,
                     @state, @hpBefore, @damage, @heal, @hpAfter,
                     @runtimeVersionBefore, @runtimeVersionAfter, @failureCode,
                     @effectJson, @completedAtUtc)
                ON DUPLICATE KEY UPDATE
                    `State` = VALUES(`State`),
                    `HpBefore` = VALUES(`HpBefore`),
                    `Damage` = VALUES(`Damage`),
                    `Heal` = VALUES(`Heal`),
                    `HpAfter` = VALUES(`HpAfter`),
                    `RuntimeVersionBefore` = VALUES(`RuntimeVersionBefore`),
                    `RuntimeVersionAfter` = VALUES(`RuntimeVersionAfter`),
                    `FailureCode` = VALUES(`FailureCode`),
                    `EffectJson` = VALUES(`EffectJson`),
                    `CompletedAtUtc` = VALUES(`CompletedAtUtc`);
                """;
            command.Parameters.AddWithValue("@effectExecutionId", effect.EffectExecutionId.ToString());
            command.Parameters.AddWithValue("@skillExecutionId", effect.SkillExecutionId.ToString());
            command.Parameters.AddWithValue("@effectDefinitionId", effect.EffectDefinitionId);
            command.Parameters.AddWithValue("@effectIndex", effect.EffectIndex);
            command.Parameters.AddWithValue("@targetIndex", effect.TargetIndex);
            command.Parameters.AddWithValue("@targetParticipantId", effect.TargetParticipantId.ToString());
            command.Parameters.AddWithValue("@effectType", effect.EffectType.ToString());
            command.Parameters.AddWithValue("@state", effect.State.ToString());
            command.Parameters.AddWithValue("@hpBefore", effect.HpBefore);
            command.Parameters.AddWithValue("@damage", effect.Damage);
            command.Parameters.AddWithValue("@heal", effect.Heal);
            command.Parameters.AddWithValue("@hpAfter", effect.HpAfter);
            command.Parameters.AddWithValue("@runtimeVersionBefore", effect.RuntimeVersionBefore);
            command.Parameters.AddWithValue("@runtimeVersionAfter", effect.RuntimeVersionAfter);
            command.Parameters.AddWithValue("@failureCode", effect.FailureCode);
            command.Parameters.AddWithValue("@effectJson", JsonSerializer.Serialize(effect, JsonOptions));
            command.Parameters.AddWithValue("@completedAtUtc", effect.CompletedAtUtc.UtcDateTime);
            command.ExecuteNonQuery();
        }
    }

    private static void UpsertCost(
        MySqlConnection connection,
        MySqlTransaction transaction,
        SkillCostReservation reservation)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `skill_cost_reservations`
                (`ReservationId`, `SkillExecutionId`, `ParticipantId`, `SkillDefinitionId`,
                 `ResourceType`, `Amount`, `State`, `RuntimeVersionBefore`,
                 `IdempotencySafeId`, `ReservationJson`, `CreatedAtUtc`,
                 `CommittedAtUtc`, `ReleasedAtUtc`)
            VALUES
                (@reservationId, @skillExecutionId, @participantId, @skillDefinitionId,
                 @resourceType, @amount, @state, @runtimeVersionBefore,
                 @idempotencySafeId, @reservationJson, @createdAtUtc,
                 @committedAtUtc, @releasedAtUtc)
            ON DUPLICATE KEY UPDATE
                `State` = VALUES(`State`),
                `ReservationJson` = VALUES(`ReservationJson`),
                `CommittedAtUtc` = VALUES(`CommittedAtUtc`),
                `ReleasedAtUtc` = VALUES(`ReleasedAtUtc`);
            """;
        command.Parameters.AddWithValue("@reservationId", reservation.ReservationId.ToString());
        command.Parameters.AddWithValue("@skillExecutionId", reservation.SkillExecutionId.ToString());
        command.Parameters.AddWithValue("@participantId", reservation.ParticipantId.ToString());
        command.Parameters.AddWithValue("@skillDefinitionId", reservation.SkillDefinitionId);
        command.Parameters.AddWithValue("@resourceType", reservation.ResourceType.ToString());
        command.Parameters.AddWithValue("@amount", reservation.Amount);
        command.Parameters.AddWithValue("@state", reservation.State.ToString());
        command.Parameters.AddWithValue("@runtimeVersionBefore", reservation.RuntimeVersionBefore);
        command.Parameters.AddWithValue("@idempotencySafeId", reservation.IdempotencySafeId);
        command.Parameters.AddWithValue("@reservationJson", JsonSerializer.Serialize(reservation, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", reservation.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "@committedAtUtc",
            reservation.CommittedAtUtc?.UtcDateTime ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "@releasedAtUtc",
            reservation.ReleasedAtUtc?.UtcDateTime ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void AddUsageParameters(MySqlCommand command, SkillUsageState state)
    {
        command.Parameters.AddWithValue("@battleInstanceId", state.BattleInstanceId.ToString());
        command.Parameters.AddWithValue("@participantId", state.ParticipantId.ToString());
        command.Parameters.AddWithValue("@skillDefinitionId", state.SkillDefinitionId);
        command.Parameters.AddWithValue("@lastUsedRound", state.LastUsedRound ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@availableAtRound", state.AvailableAtRound);
        command.Parameters.AddWithValue("@usageCount", state.UsageCount);
        command.Parameters.AddWithValue("@runtimeVersion", state.RuntimeVersion);
        command.Parameters.AddWithValue("@idempotencySafeId", state.IdempotencySafeId);
        command.Parameters.AddWithValue("@policyStatus", state.PolicyStatus.ToString());
        command.Parameters.AddWithValue("@usageJson", JsonSerializer.Serialize(state, JsonOptions));
    }

    private void CacheDetails(SkillExecutionRecord record)
    {
        if (record.CostReservation is not null)
        {
            _costs[record.CostReservation.ReservationId] = record.CostReservation;
        }
    }
}


