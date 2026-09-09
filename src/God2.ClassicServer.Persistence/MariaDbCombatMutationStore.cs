using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbCombatMutationStore : MariaDbRuntimeRepository, ICombatMutationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MariaDbCombatMutationStore(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<MonsterCombatRuntimeState> LoadMonsterAsync(
        long runtimeEntityId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `RuntimeEntityId`, `MonsterTemplateId`, `SpawnDefinitionId`, `SpawnGroupId`,
                   `WorldInstanceId`, `MapId`, `PositionX`, `PositionY`, `PositionZ`, `Direction`,
                   `LifecycleState`, `CombatState`, `Level`, `MaximumHp`, `CurrentHp`, `MaximumMp`, `CurrentMp`,
                   `AttackPower`, `Defense`, `MagicAttackPower`, `MagicDefense`, `Metal`, `Wood`, `Water`, `Fire`, `Earth`,
                   `Accuracy`, `Evasion`, `AttackIntervalPolicy`, `AggroState`,
                   `CurrentTargetRuntimeEntityId`, `RuntimeVersion`, `DirtyFlags`, `SpawnedAtUtc`,
                   `LastCombatAtUtc`, `DiedAtUtc`, `RespawnDueAtUtc`, `StatPolicyStatus`,
                   `ContentVersion`, `RuntimeMetadataJson`
            FROM `monster_combat_runtime_state`
            WHERE `RuntimeEntityId` = @runtimeEntityId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@runtimeEntityId", runtimeEntityId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new KeyNotFoundException($"Monster combat state {runtimeEntityId} was not found.");
        }

        return ReadMonster(reader);
    }

    public async Task<CombatReplayLookup> FindCompletedAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `CombatFingerprintSha256`, `ResultJson`, `PendingRewardPlanJson`
            FROM `combat_idempotency`
            WHERE `IdempotencyKeyHash` = @idempotencyKeyHash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@idempotencyKeyHash", Hash(idempotencyKey));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new CombatReplayLookup(false, true, null, null);
        }

        var payloadMatches = string.Equals(reader.GetString("CombatFingerprintSha256"), payloadHash, StringComparison.Ordinal);
        if (!payloadMatches)
        {
            return new CombatReplayLookup(true, false, null, null);
        }

        var result = JsonSerializer.Deserialize<CombatResult>(reader.GetString("ResultJson"), JsonOptions);
        CombatRewardPlan? pendingReward = null;
        if (!reader.IsDBNull(reader.GetOrdinal("PendingRewardPlanJson")))
        {
            pendingReward = JsonSerializer.Deserialize<CombatRewardPlan>(
                reader.GetString("PendingRewardPlanJson"),
                JsonOptions);
        }

        return new CombatReplayLookup(true, true, result, pendingReward);
    }

    public async Task<OperationResult> CommitAsync(
        CombatPersistenceCommit commit,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var replay = await FindWithinTransactionAsync(
                connection,
                transaction,
                commit.Intent.IdempotencyKey,
                cancellationToken);
            if (replay is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return string.Equals(replay.Value.PayloadHash, commit.PayloadHash, StringComparison.Ordinal)
                    ? OperationResult.Failure("combat.duplicate_completed", "Combat intent was already committed.")
                    : OperationResult.Failure("combat.replay_conflict", "Combat key was reused with another payload.");
            }

            var updated = await UpdateMonsterAsync(
                connection,
                transaction,
                commit.MonsterBefore,
                commit.MonsterAfter,
                cancellationToken);
            if (!updated)
            {
                await transaction.RollbackAsync(cancellationToken);
                return OperationResult.Failure(
                    "combat.version_conflict",
                    "Persisted monster runtime version changed before combat commit.");
            }

            if (commit.Death is not null)
            {
                await InsertDeathAsync(connection, transaction, commit.Death, cancellationToken);
            }

            if (commit.RespawnPlan is not null)
            {
                await InsertRespawnAsync(connection, transaction, commit.RespawnPlan, cancellationToken);
            }

            await InsertIdempotencyAsync(connection, transaction, commit, cancellationToken);
            await InsertAuditAsync(connection, transaction, commit.Audit, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Failure(
                "combat.replay_conflict",
                "A duplicate combat intent, death, respawn, or audit record was rejected.");
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Failure("combat.persistence_failure", exception.Message);
        }
    }

    public async Task UpdateResultAsync(
        string idempotencyKey,
        CombatResult result,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                command.CommandText = """
                    UPDATE `combat_idempotency`
                    SET `ResultJson` = @resultJson,
                        `PendingRewardPlanJson` = CASE
                            WHEN @rewardCommitted = 1 THEN NULL
                            ELSE `PendingRewardPlanJson`
                        END,
                        `CompletedAtUtc` = @completedAtUtc
                    WHERE `IdempotencyKeyHash` = @idempotencyKeyHash;
                    """;
                command.Parameters.AddWithValue("@resultJson", JsonSerializer.Serialize(result, JsonOptions));
                command.Parameters.AddWithValue("@rewardCommitted", result.RewardCommitted);
                command.Parameters.AddWithValue("@completedAtUtc", DateTime.UtcNow);
                command.Parameters.AddWithValue("@idempotencyKeyHash", Hash(idempotencyKey));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var audit = connection.CreateCommand())
            {
                audit.Transaction = transaction;
                audit.CommandTimeout = CommandTimeoutSeconds;
                audit.CommandText = """
                    UPDATE `combat_audit`
                    SET `Result` = @result,
                        `FailureCode` = @failureCode,
                        `RewardCommitted` = @rewardCommitted,
                        `CompletedAtUtc` = @completedAtUtc
                    WHERE `CombatIntentId` = @combatIntentId;
                    """;
                audit.Parameters.AddWithValue("@result", result.Code.ToString());
                audit.Parameters.AddWithValue("@failureCode", result.FailureCode);
                audit.Parameters.AddWithValue("@rewardCommitted", result.RewardCommitted);
                audit.Parameters.AddWithValue("@completedAtUtc", DateTime.UtcNow);
                audit.Parameters.AddWithValue("@combatIntentId", result.CombatIntentId.ToString());
                await audit.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task MarkRespawnCompletedAsync(
        MonsterRespawnPlan plan,
        MonsterRespawnResult result,
        MonsterCombatRuntimeState? activeState,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (activeState is not null)
            {
                await UpsertMonsterAsync(connection, transaction, activeState, cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                UPDATE `monster_respawn_schedules`
                SET `State` = @state,
                    `NewRuntimeEntityId` = @newRuntimeEntityId,
                    `FailureCode` = @failureCode,
                    `CompletedAtUtc` = @completedAtUtc
                WHERE `RespawnId` = @respawnId;
                """;
            command.Parameters.AddWithValue("@state", result.State.ToString());
            command.Parameters.AddWithValue("@newRuntimeEntityId", result.NewRuntimeEntityId);
            command.Parameters.AddWithValue("@failureCode", result.FailureCode);
            command.Parameters.AddWithValue("@completedAtUtc", result.CompletedAtUtc.UtcDateTime);
            command.Parameters.AddWithValue("@respawnId", plan.RespawnId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<(string PayloadHash, string ResultJson)?> FindWithinTransactionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `CombatFingerprintSha256`, `ResultJson`
            FROM `combat_idempotency`
            WHERE `IdempotencyKeyHash` = @idempotencyKeyHash
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@idempotencyKeyHash", Hash(idempotencyKey));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString("CombatFingerprintSha256"), reader.GetString("ResultJson"))
            : null;
    }

    private static async Task<bool> UpdateMonsterAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        MonsterCombatRuntimeState before,
        MonsterCombatRuntimeState after,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                UPDATE `monster_combat_runtime_state`
                SET `LifecycleState` = @lifecycleState,
                    `CombatState` = @combatState,
                    `CurrentHp` = @currentHp,
                    `CurrentMp` = @currentMp,
                    `CurrentTargetRuntimeEntityId` = @currentTargetRuntimeEntityId,
                    `RuntimeVersion` = @runtimeVersionAfter,
                    `DirtyFlags` = @dirtyFlags,
                    `LastCombatAtUtc` = @lastCombatAtUtc,
                    `DiedAtUtc` = @diedAtUtc,
                    `RespawnDueAtUtc` = @respawnDueAtUtc,
                    `UpdatedAtUtc` = @updatedAtUtc
                WHERE `RuntimeEntityId` = @runtimeEntityId
                  AND `RuntimeVersion` = @runtimeVersionBefore;
                """;
            command.Parameters.AddWithValue("@lifecycleState", after.LifecycleState.ToString());
            command.Parameters.AddWithValue("@combatState", after.CombatState.ToString());
            command.Parameters.AddWithValue("@currentHp", after.CurrentHp);
            command.Parameters.AddWithValue("@currentMp", after.CurrentMp);
            command.Parameters.AddWithValue("@currentTargetRuntimeEntityId", after.CurrentTargetRuntimeEntityId);
            command.Parameters.AddWithValue("@runtimeVersionAfter", after.RuntimeVersion);
            command.Parameters.AddWithValue("@dirtyFlags", after.DirtyFlags);
            command.Parameters.AddWithValue("@lastCombatAtUtc", after.LastCombatAtUtc?.UtcDateTime);
            command.Parameters.AddWithValue("@diedAtUtc", after.DiedAtUtc?.UtcDateTime);
            command.Parameters.AddWithValue("@respawnDueAtUtc", after.RespawnDueAtUtc?.UtcDateTime);
            command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow);
            command.Parameters.AddWithValue("@runtimeEntityId", before.RuntimeEntityId);
            command.Parameters.AddWithValue("@runtimeVersionBefore", before.RuntimeVersion);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                return true;
            }
        }

        await using (var exists = connection.CreateCommand())
        {
            exists.Transaction = transaction;
            exists.CommandTimeout = CommandTimeoutSeconds;
            exists.CommandText = """
                SELECT `RuntimeVersion`
                FROM `monster_combat_runtime_state`
                WHERE `RuntimeEntityId` = @runtimeEntityId
                FOR UPDATE;
                """;
            exists.Parameters.AddWithValue("@runtimeEntityId", before.RuntimeEntityId);
            var persistedVersion = await exists.ExecuteScalarAsync(cancellationToken);
            if (persistedVersion is not null)
            {
                return false;
            }
        }

        await UpsertMonsterAsync(connection, transaction, after, cancellationToken);
        return true;
    }

    private static async Task UpsertMonsterAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        MonsterCombatRuntimeState state,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `monster_combat_runtime_state`
                (`RuntimeEntityId`, `MonsterTemplateId`, `SpawnDefinitionId`, `SpawnGroupId`,
                 `WorldInstanceId`, `MapId`, `PositionX`, `PositionY`, `PositionZ`, `Direction`,
                 `LifecycleState`, `CombatState`, `Level`, `MaximumHp`, `CurrentHp`, `MaximumMp`, `CurrentMp`,
                 `AttackPower`, `Defense`, `MagicAttackPower`, `MagicDefense`, `Metal`, `Wood`, `Water`, `Fire`, `Earth`,
                 `Accuracy`, `Evasion`, `AttackIntervalPolicy`, `AggroState`,
                 `CurrentTargetRuntimeEntityId`, `RuntimeVersion`, `DirtyFlags`, `SpawnedAtUtc`,
                 `LastCombatAtUtc`, `DiedAtUtc`, `RespawnDueAtUtc`, `StatPolicyStatus`,
                 `ContentVersion`, `RuntimeMetadataJson`, `UpdatedAtUtc`)
            VALUES
                (@runtimeEntityId, @monsterTemplateId, @spawnDefinitionId, @spawnGroupId,
                 @worldInstanceId, @mapId, @positionX, @positionY, @positionZ, @direction,
                 @lifecycleState, @combatState, @level, @maximumHp, @currentHp, @maximumMp, @currentMp,
                 @attackPower, @defense, @magicAttackPower, @magicDefense, @metal, @wood, @water, @fire, @earth,
                 @accuracy, @evasion, @attackIntervalPolicy, @aggroState,
                 @currentTargetRuntimeEntityId, @runtimeVersion, @dirtyFlags, @spawnedAtUtc,
                 @lastCombatAtUtc, @diedAtUtc, @respawnDueAtUtc, @statPolicyStatus,
                 @contentVersion, @runtimeMetadataJson, @updatedAtUtc)
            ON DUPLICATE KEY UPDATE
                `LifecycleState` = VALUES(`LifecycleState`),
                `CombatState` = VALUES(`CombatState`),
                `MaximumMp` = VALUES(`MaximumMp`),
                `CurrentHp` = VALUES(`CurrentHp`),
                `CurrentMp` = VALUES(`CurrentMp`),
                `AttackPower` = VALUES(`AttackPower`),
                `Defense` = VALUES(`Defense`),
                `MagicAttackPower` = VALUES(`MagicAttackPower`),
                `MagicDefense` = VALUES(`MagicDefense`),
                `Metal` = VALUES(`Metal`),
                `Wood` = VALUES(`Wood`),
                `Water` = VALUES(`Water`),
                `Fire` = VALUES(`Fire`),
                `Earth` = VALUES(`Earth`),
                `CurrentTargetRuntimeEntityId` = VALUES(`CurrentTargetRuntimeEntityId`),
                `RuntimeVersion` = VALUES(`RuntimeVersion`),
                `DirtyFlags` = VALUES(`DirtyFlags`),
                `LastCombatAtUtc` = VALUES(`LastCombatAtUtc`),
                `DiedAtUtc` = VALUES(`DiedAtUtc`),
                `RespawnDueAtUtc` = VALUES(`RespawnDueAtUtc`),
                `UpdatedAtUtc` = VALUES(`UpdatedAtUtc`);
            """;
        AddMonsterParameters(command, state);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddMonsterParameters(MySqlCommand command, MonsterCombatRuntimeState state)
    {
        command.Parameters.AddWithValue("@runtimeEntityId", state.RuntimeEntityId);
        command.Parameters.AddWithValue("@monsterTemplateId", state.MonsterTemplateId);
        command.Parameters.AddWithValue("@spawnDefinitionId", state.SpawnDefinitionId);
        command.Parameters.AddWithValue("@spawnGroupId", state.SpawnGroupId);
        command.Parameters.AddWithValue("@worldInstanceId", state.WorldInstanceId);
        command.Parameters.AddWithValue("@mapId", state.MapId);
        command.Parameters.AddWithValue("@positionX", state.RawPosition.X);
        command.Parameters.AddWithValue("@positionY", state.RawPosition.Y);
        command.Parameters.AddWithValue("@positionZ", state.RawPosition.Z);
        command.Parameters.AddWithValue("@direction", state.Direction.ToString());
        command.Parameters.AddWithValue("@lifecycleState", state.LifecycleState.ToString());
        command.Parameters.AddWithValue("@combatState", state.CombatState.ToString());
        command.Parameters.AddWithValue("@level", state.Level);
        command.Parameters.AddWithValue("@maximumHp", state.MaximumHp);
        command.Parameters.AddWithValue("@currentHp", state.CurrentHp);
        command.Parameters.AddWithValue("@maximumMp", state.MaximumMp);
        command.Parameters.AddWithValue("@currentMp", state.CurrentMp);
        command.Parameters.AddWithValue("@attackPower", state.AttackPower);
        command.Parameters.AddWithValue("@defense", state.Defense);
        command.Parameters.AddWithValue("@magicAttackPower", state.MagicAttackPower);
        command.Parameters.AddWithValue("@magicDefense", state.MagicDefense);
        command.Parameters.AddWithValue("@metal", state.Metal);
        command.Parameters.AddWithValue("@wood", state.Wood);
        command.Parameters.AddWithValue("@water", state.Water);
        command.Parameters.AddWithValue("@fire", state.Fire);
        command.Parameters.AddWithValue("@earth", state.Earth);
        command.Parameters.AddWithValue("@accuracy", state.Accuracy);
        command.Parameters.AddWithValue("@evasion", state.Evasion);
        command.Parameters.AddWithValue("@attackIntervalPolicy", state.AttackIntervalPolicy);
        command.Parameters.AddWithValue("@aggroState", state.AggroState);
        command.Parameters.AddWithValue("@currentTargetRuntimeEntityId", state.CurrentTargetRuntimeEntityId);
        command.Parameters.AddWithValue("@runtimeVersion", state.RuntimeVersion);
        command.Parameters.AddWithValue("@dirtyFlags", state.DirtyFlags);
        command.Parameters.AddWithValue("@spawnedAtUtc", state.SpawnedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@lastCombatAtUtc", state.LastCombatAtUtc?.UtcDateTime);
        command.Parameters.AddWithValue("@diedAtUtc", state.DiedAtUtc?.UtcDateTime);
        command.Parameters.AddWithValue("@respawnDueAtUtc", state.RespawnDueAtUtc?.UtcDateTime);
        command.Parameters.AddWithValue("@statPolicyStatus", state.StatPolicyStatus.ToString());
        command.Parameters.AddWithValue("@contentVersion", state.ContentVersion);
        command.Parameters.AddWithValue("@runtimeMetadataJson", state.RawMetadata);
        command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow);
    }

    private static async Task InsertDeathAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        MonsterDeathRecord death,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `monster_death_records`
                (`DeathId`, `CombatIntentId`, `MonsterRuntimeEntityId`, `MonsterTemplateId`,
                 `KillerRuntimeEntityId`, `KillerCharacterId`, `MapId`, `SpawnDefinitionId`,
                 `HpBefore`, `FinalDamage`, `DiedAtUtc`, `RewardPolicyStatus`, `DropPolicyStatus`,
                 `RespawnPolicyStatus`, `RuntimeVersionBefore`, `RuntimeVersionAfter`, `CorrelationId`)
            VALUES
                (@deathId, @combatIntentId, @monsterRuntimeEntityId, @monsterTemplateId,
                 @killerRuntimeEntityId, @killerCharacterId, @mapId, @spawnDefinitionId,
                 @hpBefore, @finalDamage, @diedAtUtc, @rewardPolicyStatus, @dropPolicyStatus,
                 @respawnPolicyStatus, @runtimeVersionBefore, @runtimeVersionAfter, @correlationId);
            """;
        command.Parameters.AddWithValue("@deathId", death.DeathId.ToString());
        command.Parameters.AddWithValue("@combatIntentId", death.CombatIntentId.ToString());
        command.Parameters.AddWithValue("@monsterRuntimeEntityId", death.MonsterRuntimeEntityId);
        command.Parameters.AddWithValue("@monsterTemplateId", death.MonsterTemplateId);
        command.Parameters.AddWithValue("@killerRuntimeEntityId", death.KillerRuntimeEntityId);
        command.Parameters.AddWithValue("@killerCharacterId", death.KillerCharacterId);
        command.Parameters.AddWithValue("@mapId", death.MapId);
        command.Parameters.AddWithValue("@spawnDefinitionId", death.SpawnDefinitionId);
        command.Parameters.AddWithValue("@hpBefore", death.HpBefore);
        command.Parameters.AddWithValue("@finalDamage", death.FinalDamage);
        command.Parameters.AddWithValue("@diedAtUtc", death.DiedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@rewardPolicyStatus", death.RewardPolicyStatus.ToString());
        command.Parameters.AddWithValue("@dropPolicyStatus", death.DropPolicyStatus.ToString());
        command.Parameters.AddWithValue("@respawnPolicyStatus", death.RespawnPolicyStatus.ToString());
        command.Parameters.AddWithValue("@runtimeVersionBefore", death.RuntimeVersionBefore);
        command.Parameters.AddWithValue("@runtimeVersionAfter", death.RuntimeVersionAfter);
        command.Parameters.AddWithValue("@correlationId", death.CorrelationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRespawnAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        MonsterRespawnPlan plan,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `monster_respawn_schedules`
                (`RespawnId`, `DeathId`, `MonsterTemplateId`, `SpawnDefinitionId`, `SpawnGroupId`,
                 `WorldInstanceId`, `MapId`, `PositionX`, `PositionY`, `PositionZ`, `Direction`,
                 `RespawnDueAtUtc`, `PolicyStatus`, `State`, `CreatedAtUtc`, `CorrelationId`,
                 `PreviousRuntimeEntityId`)
            VALUES
                (@respawnId, @deathId, @monsterTemplateId, @spawnDefinitionId, @spawnGroupId,
                 @worldInstanceId, @mapId, @positionX, @positionY, @positionZ, @direction,
                 @respawnDueAtUtc, @policyStatus, @state, @createdAtUtc, @correlationId,
                 @previousRuntimeEntityId);
            """;
        command.Parameters.AddWithValue("@respawnId", plan.RespawnId.ToString());
        command.Parameters.AddWithValue("@deathId", plan.DeathId.ToString());
        command.Parameters.AddWithValue("@monsterTemplateId", plan.MonsterTemplateId);
        command.Parameters.AddWithValue("@spawnDefinitionId", plan.SpawnDefinitionId);
        command.Parameters.AddWithValue("@spawnGroupId", plan.SpawnGroupId);
        command.Parameters.AddWithValue("@worldInstanceId", plan.WorldInstanceId);
        command.Parameters.AddWithValue("@mapId", plan.MapId);
        command.Parameters.AddWithValue("@positionX", plan.RawSpawnPosition.X);
        command.Parameters.AddWithValue("@positionY", plan.RawSpawnPosition.Y);
        command.Parameters.AddWithValue("@positionZ", plan.RawSpawnPosition.Z);
        command.Parameters.AddWithValue("@direction", plan.Direction.ToString());
        command.Parameters.AddWithValue("@respawnDueAtUtc", plan.RespawnDueAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@policyStatus", plan.PolicyStatus.ToString());
        command.Parameters.AddWithValue("@state", plan.State.ToString());
        command.Parameters.AddWithValue("@createdAtUtc", plan.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@correlationId", plan.CorrelationId);
        command.Parameters.AddWithValue("@previousRuntimeEntityId", plan.PreviousRuntimeEntityId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertIdempotencyAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CombatPersistenceCommit commit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `combat_idempotency`
                (`IdempotencyKeyHash`, `CombatIntentId`, `CharacterId`, `CombatFingerprintSha256`, `ResultJson`,
                 `PendingRewardPlanJson`, `CreatedAtUtc`, `CompletedAtUtc`)
            VALUES
                (@idempotencyKeyHash, @combatIntentId, @characterId, @payloadHash, @resultJson,
                 @pendingRewardPlanJson, @createdAtUtc, @completedAtUtc);
            """;
        command.Parameters.AddWithValue("@idempotencyKeyHash", Hash(commit.Intent.IdempotencyKey));
        command.Parameters.AddWithValue("@combatIntentId", commit.Intent.CombatIntentId.ToString());
        command.Parameters.AddWithValue("@characterId", commit.Intent.CharacterId);
        command.Parameters.AddWithValue("@payloadHash", commit.PayloadHash);
        command.Parameters.AddWithValue("@resultJson", JsonSerializer.Serialize(commit.Result, JsonOptions));
        command.Parameters.AddWithValue(
            "@pendingRewardPlanJson",
            commit.RewardPlan is null ? null : JsonSerializer.Serialize(commit.RewardPlan, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", commit.Intent.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@completedAtUtc", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CombatAuditRecord audit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `combat_audit`
                (`AuditId`, `CombatIntentId`, `DamagePlanId`, `DeathId`, `RewardPlanId`, `RespawnId`,
                 `IdempotencySafeId`, `CorrelationId`, `SessionId`, `CharacterId`,
                 `AttackerRuntimeEntityId`, `TargetRuntimeEntityId`, `AttackerTemplateId`,
                 `TargetTemplateId`, `ActionType`, `MapId`, `AttackerVersionBefore`,
                 `AttackerVersionAfter`, `TargetVersionBefore`, `TargetVersionAfter`, `HpBefore`,
                 `Damage`, `HpAfter`, `Result`, `FailureCode`, `DeathCommitted`, `RewardCommitted`,
                 `RespawnScheduled`, `PolicyStatus`, `CreatedAtUtc`, `CompletedAtUtc`)
            VALUES
                (@auditId, @combatIntentId, @damagePlanId, @deathId, @rewardPlanId, @respawnId,
                 @idempotencySafeId, @correlationId, @sessionId, @characterId,
                 @attackerRuntimeEntityId, @targetRuntimeEntityId, @attackerTemplateId,
                 @targetTemplateId, @actionType, @mapId, @attackerVersionBefore,
                 @attackerVersionAfter, @targetVersionBefore, @targetVersionAfter, @hpBefore,
                 @damage, @hpAfter, @result, @failureCode, @deathCommitted, @rewardCommitted,
                 @respawnScheduled, @policyStatus, @createdAtUtc, @completedAtUtc);
            """;
        command.Parameters.AddWithValue("@auditId", audit.AuditId.ToString());
        command.Parameters.AddWithValue("@combatIntentId", audit.CombatIntentId.ToString());
        command.Parameters.AddWithValue("@damagePlanId", audit.DamagePlanId?.ToString());
        command.Parameters.AddWithValue("@deathId", audit.DeathId?.ToString());
        command.Parameters.AddWithValue("@rewardPlanId", audit.RewardPlanId?.ToString());
        command.Parameters.AddWithValue("@respawnId", audit.RespawnId?.ToString());
        command.Parameters.AddWithValue("@idempotencySafeId", audit.IdempotencySafeId);
        command.Parameters.AddWithValue("@correlationId", audit.CorrelationId);
        command.Parameters.AddWithValue("@sessionId", audit.SessionId);
        command.Parameters.AddWithValue("@characterId", audit.CharacterId);
        command.Parameters.AddWithValue("@attackerRuntimeEntityId", audit.AttackerRuntimeEntityId);
        command.Parameters.AddWithValue("@targetRuntimeEntityId", audit.TargetRuntimeEntityId);
        command.Parameters.AddWithValue("@attackerTemplateId", audit.AttackerTemplateId);
        command.Parameters.AddWithValue("@targetTemplateId", audit.TargetTemplateId);
        command.Parameters.AddWithValue("@actionType", audit.ActionType.ToString());
        command.Parameters.AddWithValue("@mapId", audit.MapId);
        command.Parameters.AddWithValue("@attackerVersionBefore", audit.AttackerVersionBefore);
        command.Parameters.AddWithValue("@attackerVersionAfter", audit.AttackerVersionAfter);
        command.Parameters.AddWithValue("@targetVersionBefore", audit.TargetVersionBefore);
        command.Parameters.AddWithValue("@targetVersionAfter", audit.TargetVersionAfter);
        command.Parameters.AddWithValue("@hpBefore", audit.HpBefore);
        command.Parameters.AddWithValue("@damage", audit.Damage);
        command.Parameters.AddWithValue("@hpAfter", audit.HpAfter);
        command.Parameters.AddWithValue("@result", audit.Result.ToString());
        command.Parameters.AddWithValue("@failureCode", audit.FailureCode);
        command.Parameters.AddWithValue("@deathCommitted", audit.DeathCommitted);
        command.Parameters.AddWithValue("@rewardCommitted", audit.RewardCommitted);
        command.Parameters.AddWithValue("@respawnScheduled", audit.RespawnScheduled);
        command.Parameters.AddWithValue("@policyStatus", audit.PolicyStatus.ToString());
        command.Parameters.AddWithValue("@createdAtUtc", audit.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@completedAtUtc", audit.CompletedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MonsterCombatRuntimeState ReadMonster(MySqlDataReader reader)
    {
        var lifecycle = Enum.TryParse<MonsterLifecycleState>(reader.GetString("LifecycleState"), true, out var parsedLifecycle)
            ? parsedLifecycle
            : MonsterLifecycleState.Faulted;
        var combat = Enum.TryParse<MonsterCombatStateKind>(reader.GetString("CombatState"), true, out var parsedCombat)
            ? parsedCombat
            : MonsterCombatStateKind.Faulted;
        var direction = Enum.TryParse<WorldDirection>(reader.GetString("Direction"), true, out var parsedDirection)
            ? parsedDirection
            : WorldDirection.Unknown;
        var policy = Enum.TryParse<CombatPolicyStatus>(reader.GetString("StatPolicyStatus"), true, out var parsedPolicy)
            ? parsedPolicy
            : CombatPolicyStatus.EvidenceBlocked;
        return new MonsterCombatRuntimeState(
            reader.GetInt64("RuntimeEntityId"),
            reader.GetInt32("MonsterTemplateId"),
            reader.GetInt32("SpawnDefinitionId"),
            reader.GetString("SpawnGroupId"),
            reader.GetString("WorldInstanceId"),
            reader.GetInt32("MapId"),
            new WorldPosition3(reader.GetInt32("PositionX"), reader.GetInt32("PositionY"), reader.GetInt32("PositionZ")),
            direction,
            lifecycle,
            combat,
            reader.GetInt32("Level"),
            reader.GetInt64("MaximumHp"),
            reader.GetInt64("CurrentHp"),
            reader.GetInt64("AttackPower"),
            reader.GetInt64("Defense"),
            NullableInt64(reader, "Accuracy"),
            NullableInt64(reader, "Evasion"),
            reader.GetString("AttackIntervalPolicy"),
            reader.GetString("AggroState"),
            NullableInt64(reader, "CurrentTargetRuntimeEntityId"),
            reader.GetInt64("RuntimeVersion"),
            reader.GetString("DirtyFlags"),
            ReadDateTimeUtc(reader, "SpawnedAtUtc"),
            NullableUtc(reader, "LastCombatAtUtc"),
            NullableUtc(reader, "DiedAtUtc"),
            NullableUtc(reader, "RespawnDueAtUtc"),
            policy,
            reader.GetString("ContentVersion"),
            reader.GetString("RuntimeMetadataJson"),
            reader.GetInt64("MaximumMp"),
            reader.GetInt64("CurrentMp"),
            reader.GetInt64("MagicAttackPower"),
            reader.GetInt64("MagicDefense"),
            reader.GetInt32("Metal"),
            reader.GetInt32("Wood"),
            reader.GetInt32("Water"),
            reader.GetInt32("Fire"),
            reader.GetInt32("Earth"));
    }

    private static long? NullableInt64(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt64(name);

    private static DateTimeOffset? NullableUtc(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : ReadDateTimeUtc(reader, name);

    private static DateTimeOffset ReadDateTimeUtc(MySqlDataReader reader, string name) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(name), DateTimeKind.Utc));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
